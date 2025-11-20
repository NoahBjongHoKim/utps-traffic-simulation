"""
Main Pipeline Coordinator
Author: Noah Kim
Date: 2025

Coordinates the complete pipeline:
1. XML -> Parquet (with time/spatial filtering)
2. Parquet -> export (with interpolation)

Configuration via YAML file with Pydantic validation.


Run pipeline with config file:
python main_pipeline.py config.yaml

Generate JSON schema for IDE support:
python generate_config_schema.py > config_schema.json

"""

import multiprocessing as mp
from pathlib import Path
import time
import yaml
from pydantic import BaseModel, Field, field_validator, ConfigDict
from typing import Optional

from .xml_to_parquet import xml_to_parquet_filtered
from .parquet_to_animation import parquet_to_export
from .parquet_to_heatmap import parquet_to_heatmap
from ..config import logger
from ..utils.network_cache import load_network_cached, build_link_attributes_dict




class PathConfig(BaseModel):
    """File paths configuration."""
    model_config = ConfigDict(str_strip_whitespace=True)

    xml_input: Path = Field(..., description="Input XML file with events")
    gpkg_network: Path = Field(..., description="Input GeoPackage with road network")
    parquet_intermediate: Path = Field(..., description="Intermediate Parquet file")
    output_base: Path = Field(..., description="Base path for output files (without extension)")

    @field_validator('xml_input', 'gpkg_network')
    @classmethod
    def validate_input_exists(cls, v: Path) -> Path:
        """Check that input files exist."""
        if not v.exists():
            raise ValueError(f"Input file does not exist: {v}")
        return v

    @field_validator('parquet_intermediate', 'output_base')
    @classmethod
    def validate_output_dir(cls, v: Optional[Path]) -> Optional[Path]:
        """Check that output directory exists."""
        if v is not None and not v.parent.exists():
            raise ValueError(f"Output directory does not exist: {v.parent}")
        return v


class FilterConfig(BaseModel):
    """Filtering parameters configuration."""
    start_time: str = Field(..., pattern=r'^\d{2}:\d{2}$', description="Start time for snapshots (hh:mm)")
    end_time: str = Field(..., pattern=r'^\d{2}:\d{2}$', description="End time for snapshots (hh:mm)")
    frequency_seconds: int = Field(..., ge=1, description="Frequency between snapshots (seconds)")
    duration_seconds: int = Field(..., ge=1, description="Duration of each snapshot (seconds)")

    @field_validator('start_time', 'end_time')
    @classmethod
    def validate_time_format(cls, v: str) -> str:
        """Validate time is within 24-hour format."""
        hours, minutes = map(int, v.split(':'))
        if not (0 <= hours <= 23 and 0 <= minutes <= 59):
            raise ValueError(f"Invalid time: {v}. Hours must be 0-23, minutes 0-59.")
        return v


class ProcessingConfig(BaseModel):
    """Processing parameters configuration."""
    num_workers: Optional[int] = Field(None, ge=1, description="Number of worker processes")
    chunk_size: int = Field(100000, ge=1000, description="Chunk size for processing")
    output_formats: list[str] = Field(
        default=["geojson"],
        description="Output formats: geojson, csv, parquet, geoparquet"
    )
    heatmap_enabled: bool = Field(False, description="Enable heatmap export with vehicle counts")
    heatmap_time_interval: int = Field(300, ge=60, description="Time interval for heatmap sampling (seconds)")
    heatmap_output_formats: list[str] = Field(
        default=["csv"],
        description="Heatmap output formats: geojson, csv, parquet, geoparquet"
    )
    heatmap_output_base: str = Field("data/processed/heatmap", description="Base path for heatmap outputs")

    @field_validator('num_workers')
    @classmethod
    def set_default_workers(cls, v: Optional[int]) -> int:
        """Set default to CPU count if not specified."""
        return v if v is not None else mp.cpu_count()

    @field_validator('output_formats', 'heatmap_output_formats')
    @classmethod
    def validate_output_formats(cls, v: list[str]) -> list[str]:
        """Validate output formats."""
        valid_formats = {'geojson', 'csv', 'parquet', 'geoparquet'}
        for fmt in v:
            if fmt not in valid_formats:
                raise ValueError(f"Invalid output format: {fmt}. Must be one of {valid_formats}")
        return v


class PipelineConfig(BaseModel):
    """Complete pipeline configuration."""
    paths: PathConfig
    filters: FilterConfig
    processing: ProcessingConfig = Field(default_factory=ProcessingConfig)
    skip_xml_to_parquet: bool = Field(False, description="Skip step 1 if Parquet exists")
    skip_parquet_to_export: bool = Field(False, description="Skip step 2 if export exists")


def load_config(config_path: str) -> PipelineConfig:
    """Load and validate configuration from YAML file."""
    with open(config_path, 'r') as f:
        config_dict = yaml.safe_load(f)

    return PipelineConfig(**config_dict)


def generate_snapshot_intervals(start_time: str, end_time: str,
                                frequency_seconds: int, duration_seconds: int) -> list[tuple[int, int]]:
    """
    Generate list of (start, end) time intervals in seconds from snapshot config.

    Args:
        start_time: Start time as "hh:mm"
        end_time: End time as "hh:mm"
        frequency_seconds: Seconds between snapshots
        duration_seconds: Duration of each snapshot

    Returns:
        List of (start_seconds, end_seconds) tuples

    Example:
        generate_snapshot_intervals("12:00", "12:15", 300, 5)
        # Returns: [(43200, 43205), (43500, 43505), (43800, 43805)]
        # That's 12:00:00-12:00:05, 12:05:00-12:05:05, 12:10:00-12:10:05
    """
    # Convert times to seconds
    h_start, m_start = map(int, start_time.split(':'))
    h_end, m_end = map(int, end_time.split(':'))

    start_seconds = h_start * 3600 + m_start * 60
    end_seconds = h_end * 3600 + m_end * 60

    intervals = []
    current = start_seconds

    while current + duration_seconds <= end_seconds:
        intervals.append((current, current + duration_seconds))
        current += frequency_seconds

    logger.info(f"Generated {len(intervals)} snapshot intervals ({duration_seconds}s duration, every {frequency_seconds}s)")

    return intervals


def print_config_summary(config: PipelineConfig):
    """Print configuration summary."""
    logger.info("="*80)
    logger.info("PIPELINE CONFIGURATION")
    logger.info("="*80)

    logger.info("Input Files:")
    if config.paths.xml_input.exists():
        size_mb = config.paths.xml_input.stat().st_size / (1024 * 1024)
        logger.info(f"  XML Events: {config.paths.xml_input} ({size_mb:.2f} MB)")
    else:
        logger.warning(f"  XML Events: {config.paths.xml_input} (NOT FOUND)")

    if config.paths.gpkg_network.exists():
        size_mb = config.paths.gpkg_network.stat().st_size / (1024 * 1024)
        logger.info(f"  Network GPKG: {config.paths.gpkg_network} ({size_mb:.2f} MB)")
    else:
        logger.warning(f"  Network GPKG: {config.paths.gpkg_network} (NOT FOUND)")

    logger.info("Output Files:")
    logger.info(f"  Intermediate Parquet: {config.paths.parquet_intermediate}")
    logger.info(f"  Output base:          {config.paths.output_base}")
    logger.info(f"  Output formats:       {', '.join(config.processing.output_formats)}")

    logger.info("Filters:")
    logger.info(f"  Snapshot mode:")
    logger.info(f"    Period: {config.filters.start_time} - {config.filters.end_time}")
    logger.info(f"    Frequency: every {config.filters.frequency_seconds}s")
    logger.info(f"    Duration: {config.filters.duration_seconds}s per snapshot")

    # Calculate how many intervals
    intervals = generate_snapshot_intervals(
        config.filters.start_time,
        config.filters.end_time,
        config.filters.frequency_seconds,
        config.filters.duration_seconds
    )
    logger.info(f"    Total snapshots: {len(intervals)}")

    logger.info("Processing:")
    logger.info(f"  Workers:     {config.processing.num_workers}")
    logger.info(f"  Chunk size:  {config.processing.chunk_size:,}")

    if config.processing.heatmap_enabled:
        logger.info("Heatmap Export:")
        logger.info(f"  Enabled:           True")
        logger.info(f"  Time interval:     {config.processing.heatmap_time_interval}s")
        logger.info(f"  Output base:       {config.processing.heatmap_output_base}")
        logger.info(f"  Output formats:    {', '.join(config.processing.heatmap_output_formats)}")

    logger.info("Pipeline Steps:")
    logger.info(f"  Skip XML->Parquet:       {config.skip_xml_to_parquet}")
    logger.info(f"  Skip Parquet->export:   {config.skip_parquet_to_export}")
    logger.info("="*80)


def main(config_path: str):
    """Run the complete pipeline."""
    logger.info(f"Starting pipeline with config: {config_path}")

    # Load and validate configuration
    try:
        config = load_config(config_path)
        logger.success("Configuration loaded and validated")
    except Exception as e:
        logger.error(f"Configuration validation failed: {e}")
        return 1

    print_config_summary(config)

    # Load network once (used by both steps)
    logger.info("="*80)
    logger.info("Loading road network (will be used by both pipeline steps)")
    logger.info("="*80)
    start = time.time()

    try:
        network_df = load_network_cached(config.paths.gpkg_network)
        link_attrs = build_link_attributes_dict(network_df, link_id_col='linkId', precompute_endpoints=True)
        valid_links = set(link_attrs.keys())  # All link IDs as strings

        elapsed = time.time() - start
        logger.success(f"Network loaded: {len(link_attrs):,} links in {elapsed:.2f} seconds")
    except Exception as e:
        logger.error(f"Failed to load network: {e}")
        logger.exception("Full traceback:")
        return 1

    # Step 1: XML -> Parquet (with filtering)
    if not config.skip_xml_to_parquet:
        logger.info("="*80)
        logger.info("STAGE 1: XML -> Filtered Parquet")
        logger.info("="*80)
        start = time.time()

        # Generate time intervals from snapshot config
        time_intervals = generate_snapshot_intervals(
            config.filters.start_time,
            config.filters.end_time,
            config.filters.frequency_seconds,
            config.filters.duration_seconds
        )

        try:
            xml_to_parquet_filtered(
                xml_input=str(config.paths.xml_input),
                valid_links=valid_links,
                parquet_output=str(config.paths.parquet_intermediate),
                time_intervals=time_intervals,
                num_workers=config.processing.num_workers,
                chunk_size=config.processing.chunk_size
            )
        except Exception as e:
            logger.error(f"Error in Step 1: {e}")
            logger.exception("Full traceback:")
            return 1

        elapsed = time.time() - start
        logger.success(f"Step 1 completed in {elapsed:.2f} seconds ({elapsed/60:.1f} minutes)")
        if config.paths.parquet_intermediate.exists():
            size_mb = config.paths.parquet_intermediate.stat().st_size / (1024 * 1024)
            logger.info(f"Output Parquet: {config.paths.parquet_intermediate} ({size_mb:.2f} MB)")
    else:
        logger.info("STEP 1: Skipped (using existing Parquet)")
        if not config.paths.parquet_intermediate.exists():
            logger.error(f"Parquet file does not exist: {config.paths.parquet_intermediate}")
            return 1
        if config.paths.parquet_intermediate.exists():
            size_mb = config.paths.parquet_intermediate.stat().st_size / (1024 * 1024)
            logger.info(f"Existing Parquet: {config.paths.parquet_intermediate} ({size_mb:.2f} MB)")

    # Step 2: Parquet -> export
    if not config.skip_parquet_to_export:
        logger.info("="*80)
        logger.info("STAGE 2: Parquet -> Export")
        logger.info("="*80)
        start = time.time()

        try:
            parquet_to_export(
                parquet_input=str(config.paths.parquet_intermediate),
                link_attrs=link_attrs,
                output_base=str(config.paths.output_base),
                output_formats=config.processing.output_formats,
                num_workers=config.processing.num_workers,
                chunk_size=config.processing.chunk_size
            )
        except Exception as e:
            logger.error(f"Error in Step 2: {e}")
            logger.exception("Full traceback:")
            return 1

        elapsed = time.time() - start
        logger.success(f"Step 2 completed in {elapsed:.2f} seconds ({elapsed/60:.1f} minutes)")
        if config.paths.output_base.exists():
            size_mb = config.paths.output_base.stat().st_size / (1024 * 1024)
            logger.info(f"Output : {config.paths.output_base} ({size_mb:.2f} MB)")
    else:
        logger.info("STEP 2: Skipped (using existing export)")
        if not config.paths.output_base.exists():
            logger.error(f"export file does not exist: {config.paths.output_base}")
            return 1
        if config.paths.output_base.exists():
            size_mb = config.paths.output_base.stat().st_size / (1024 * 1024)
            logger.info(f"Existing export: {config.paths.output_base} ({size_mb:.2f} MB)")

    # Step 3: Parquet -> Heatmap (optional)
    if config.processing.heatmap_enabled:
        logger.info("="*80)
        logger.info("STAGE 3: Parquet -> Heatmap Export")
        logger.info("="*80)
        start = time.time()

        # Calculate start and end times in seconds
        h_start, m_start = map(int, config.filters.start_time.split(':'))
        h_end, m_end = map(int, config.filters.end_time.split(':'))
        start_sec = h_start * 3600 + m_start * 60
        end_sec = h_end * 3600 + m_end * 60

        try:
            parquet_to_heatmap(
                parquet_input=str(config.paths.parquet_intermediate),
                link_attrs=link_attrs,
                output_base=config.processing.heatmap_output_base,
                output_formats=config.processing.heatmap_output_formats,
                time_interval_seconds=config.processing.heatmap_time_interval,
                start_time=start_sec,
                end_time=end_sec,
                num_workers=config.processing.num_workers
            )
        except Exception as e:
            logger.error(f"Error in Step 3 (Heatmap): {e}")
            logger.exception("Full traceback:")
            return 1

        elapsed = time.time() - start
        logger.success(f"Step 3 (Heatmap) completed in {elapsed:.2f} seconds ({elapsed/60:.1f} minutes)")
    else:
        logger.info("STEP 3: Skipped (heatmap export not enabled)")

    logger.info("="*80)
    logger.info("PIPELINE COMPLETED SUCCESSFULLY")
    logger.info("="*80)
    logger.info(f"Final output: {config.paths.output_base}")
    if config.processing.heatmap_enabled:
        logger.info(f"Heatmap output: {config.processing.heatmap_output_base}")

    return 0

if __name__ == "__main__":
    import sys

    if len(sys.argv) != 2:
        print("Usage: python main_pipeline.py <config.yaml>")
        sys.exit(1)

    config_path = sys.argv[1]

    if not Path(config_path).exists():
        print(f"ERROR: Config file not found: {config_path}")
        sys.exit(1)

    exit_code = main(config_path)
    sys.exit(exit_code)
