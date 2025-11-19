"""
Main Pipeline Coordinator
Author: Noah Kim
Date: 2025

Coordinates the complete pipeline:
1. XML -> Parquet (with time/spatial filtering)
2. Parquet -> GeoJSON (with interpolation)

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
from .parquet_to_geojson import parquet_to_geojson
from ..config import logger
from ..utils.network_cache import load_network_cached, build_link_attributes_dict


class TimeInterval(BaseModel):
    """Time interval with format validation."""
    start: str = Field(..., pattern=r'^\d{2}:\d{2}$', description="Start time (hh:mm)")
    end: str = Field(..., pattern=r'^\d{2}:\d{2}$', description="End time (hh:mm)")

    @field_validator('start', 'end')
    @classmethod
    def validate_time_format(cls, v: str) -> str:
        """Validate time is within 24-hour format."""
        hours, minutes = map(int, v.split(':'))
        if not (0 <= hours <= 23 and 0 <= minutes <= 59):
            raise ValueError(f"Invalid time: {v}. Hours must be 0-23, minutes 0-59.")
        return v

    def to_string(self) -> str:
        """Convert to 'hh:mm,hh:mm' format."""
        return f"{self.start},{self.end}"


class PathConfig(BaseModel):
    """File paths configuration."""
    model_config = ConfigDict(str_strip_whitespace=True)

    xml_input: Path = Field(..., description="Input XML file with events")
    gpkg_network: Path = Field(..., description="Input GeoPackage with road network")
    parquet_intermediate: Path = Field(..., description="Intermediate Parquet file")
    geojson_output: Path = Field(..., description="Final GeoJSON output")

    @field_validator('xml_input', 'gpkg_network')
    @classmethod
    def validate_input_exists(cls, v: Path) -> Path:
        """Check that input files exist."""
        if not v.exists():
            raise ValueError(f"Input file does not exist: {v}")
        return v

    @field_validator('parquet_intermediate', 'geojson_output')
    @classmethod
    def validate_output_dir(cls, v: Path) -> Path:
        """Check that output directory exists."""
        if not v.parent.exists():
            raise ValueError(f"Output directory does not exist: {v.parent}")
        return v


class FilterConfig(BaseModel):
    """Filtering parameters configuration."""
    time_interval_1: TimeInterval = Field(..., description="First time interval")
    time_interval_2: TimeInterval = Field(..., description="Second time interval")

    @field_validator('time_interval_2')
    @classmethod
    def validate_intervals(cls, v: TimeInterval, info) -> TimeInterval:
        """Ensure interval_2 is provided (can be same as interval_1 for single interval)."""
        return v


class ProcessingConfig(BaseModel):
    """Processing parameters configuration."""
    num_workers: Optional[int] = Field(None, ge=1, description="Number of worker processes")
    chunk_size: int = Field(100000, ge=1000, description="Chunk size for processing")

    @field_validator('num_workers')
    @classmethod
    def set_default_workers(cls, v: Optional[int]) -> int:
        """Set default to CPU count if not specified."""
        return v if v is not None else mp.cpu_count()


class PipelineConfig(BaseModel):
    """Complete pipeline configuration."""
    paths: PathConfig
    filters: FilterConfig
    processing: ProcessingConfig = Field(default_factory=ProcessingConfig)
    skip_xml_to_parquet: bool = Field(False, description="Skip step 1 if Parquet exists")
    skip_parquet_to_geojson: bool = Field(False, description="Skip step 2 if GeoJSON exists")


def load_config(config_path: str) -> PipelineConfig:
    """Load and validate configuration from YAML file."""
    with open(config_path, 'r') as f:
        config_dict = yaml.safe_load(f)

    return PipelineConfig(**config_dict)


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
    logger.info(f"  Final GeoJSON:        {config.paths.geojson_output}")

    logger.info("Filters:")
    logger.info(f"  Time Interval 1: {config.filters.time_interval_1.start} - {config.filters.time_interval_1.end}")
    logger.info(f"  Time Interval 2: {config.filters.time_interval_2.start} - {config.filters.time_interval_2.end}")

    logger.info("Processing:")
    logger.info(f"  Workers:     {config.processing.num_workers}")
    logger.info(f"  Chunk size:  {config.processing.chunk_size:,}")

    logger.info("Pipeline Steps:")
    logger.info(f"  Skip XML->Parquet:       {config.skip_xml_to_parquet}")
    logger.info(f"  Skip Parquet->GeoJSON:   {config.skip_parquet_to_geojson}")
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

        try:
            xml_to_parquet_filtered(
                xml_input=str(config.paths.xml_input),
                valid_links=valid_links,
                parquet_output=str(config.paths.parquet_intermediate),
                time_interval_1=config.filters.time_interval_1.to_string(),
                time_interval_2=config.filters.time_interval_2.to_string(),
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

    # Step 2: Parquet -> GeoJSON
    if not config.skip_parquet_to_geojson:
        logger.info("="*80)
        logger.info("STAGE 2: Parquet -> GeoJSON")
        logger.info("="*80)
        start = time.time()

        try:
            parquet_to_geojson(
                parquet_input=str(config.paths.parquet_intermediate),
                link_attrs=link_attrs,
                geojson_output=str(config.paths.geojson_output),
                num_workers=config.processing.num_workers,
                chunk_size=config.processing.chunk_size
            )
        except Exception as e:
            logger.error(f"Error in Step 2: {e}")
            logger.exception("Full traceback:")
            return 1

        elapsed = time.time() - start
        logger.success(f"Step 2 completed in {elapsed:.2f} seconds ({elapsed/60:.1f} minutes)")
        if config.paths.geojson_output.exists():
            size_mb = config.paths.geojson_output.stat().st_size / (1024 * 1024)
            logger.info(f"Output GeoJSON: {config.paths.geojson_output} ({size_mb:.2f} MB)")
    else:
        logger.info("STEP 2: Skipped (using existing GeoJSON)")
        if not config.paths.geojson_output.exists():
            logger.error(f"GeoJSON file does not exist: {config.paths.geojson_output}")
            return 1
        if config.paths.geojson_output.exists():
            size_mb = config.paths.geojson_output.stat().st_size / (1024 * 1024)
            logger.info(f"Existing GeoJSON: {config.paths.geojson_output} ({size_mb:.2f} MB)")

    logger.info("="*80)
    logger.info("PIPELINE COMPLETED SUCCESSFULLY")
    logger.info("="*80)
    logger.info(f"Final output: {config.paths.geojson_output}")

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
