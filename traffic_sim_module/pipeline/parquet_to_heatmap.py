"""Parquet to heatmap converter for traffic density visualization.

This module generates time-series heatmap data from filtered Parquet event files
by sampling vehicle counts at regular time intervals. The output shows traffic
density on each road link over time, suitable for animated heatmap visualization.

Key Features:
    - Regular time interval sampling (configurable resolution)
    - Aggregation of vehicle counts per road link
    - Multiple export formats (GeoJSON, CSV, Parquet, GeoParquet)
    - Parallel processing of time points for performance
    - Precomputed link center coordinates for fast lookups
    - Vectorized pandas operations for efficiency

The heatmap generation process:
    1. Loads filtered trajectory events from Parquet
    2. Samples vehicle presence at regular time intervals
    3. Counts active vehicles on each link at each timepoint
    4. Outputs time-stamped vehicle counts with coordinates

Authors: Noah Kim & Claude
Date: 20.11.2025

Example:
    >>> from traffic_sim_module.pipeline.parquet_to_heatmap import parquet_to_heatmap
    >>> parquet_to_heatmap(
    ...     parquet_input="filtered.parquet",
    ...     gpkg_network="network.gpkg",
    ...     output_base="heatmap",
    ...     time_interval_seconds=60,  # 1-minute resolution
    ...     num_workers=8
    ... )
"""

import csv as csv_module
from datetime import datetime, timedelta
from functools import partial
import json
import multiprocessing as mp
from pathlib import Path

import geopandas as gpd
import numpy as np
import pandas as pd
from shapely.geometry import Point

# Handle both module import and direct script execution
try:
    from ..config import logger
    from ..utils.network_cache import build_link_attributes_dict, load_network_cached
except ImportError:
    # Running as standalone script - setup minimal logging
    from pathlib import Path
    import sys

    from loguru import logger

    # Configure logger for standalone execution
    logger.configure(handlers=[{"sink": sys.stderr, "level": "INFO"}])

    # Import from absolute path
    repo_root = Path(__file__).parent.parent.parent
    sys.path.insert(0, str(repo_root))
    from traffic_sim_module.utils.network_cache import (
        build_link_attributes_dict,
        load_network_cached,
    )


def time_to_timestamp(seconds):
    """Convert seconds since midnight to formatted timestamp string.

    Args:
        seconds: Seconds since midnight (e.g., 28800 for 8:00 AM)

    Returns:
        Formatted timestamp string in 'YYYY/MM/DD HH:MM:SS' format

    Example:
        >>> time_to_timestamp(28800)
        '2024/01/01 08:00:00'
        >>> time_to_timestamp(64800)
        '2024/01/01 18:00:00'
    """
    base = datetime(2024, 1, 1)
    return (base + timedelta(seconds=int(seconds))).strftime('%Y/%m/%d %H:%M:%S')


def process_timepoint_batch(timepoints, df, link_attrs):
    """Process a batch of time points to generate heatmap records.

    For each timepoint in the batch, counts active vehicles on each link and
    creates heatmap records with vehicle counts and geographic coordinates.
    Uses vectorized pandas operations for efficient processing.

    Args:
        timepoints: List of integer timestamps (seconds since midnight) to sample
        df: DataFrame with columns link_id, time_enter, time_leave, and interval_id
        link_attrs: Dictionary mapping link_id to attributes including 'center' coords

    Returns:
        List of heatmap record dictionaries with keys:
            - timestamp: Formatted timestamp string
            - lon: Longitude of link center
            - lat: Latitude of link center
            - vehicle_count: Number of active vehicles
            - link_id: Link identifier

    Note:
        A vehicle is considered active on a link if time_enter <= timepoint < time_leave.
        Links without center coordinates are skipped.
    """
    records = []

    for timepoint in timepoints:
        # Vectorized filtering: count vehicles where time_enter <= timepoint < time_leave
        mask = (df['time_enter'] <= timepoint) & (df['time_leave'] > timepoint)
        active_vehicles = df[mask]

        # Group by link_id and count
        counts = active_vehicles.groupby('link_id', as_index=False).size()
        counts.columns = ['link_id', 'vehicle_count']

        # Convert to records with coordinates
        timestamp = time_to_timestamp(timepoint)

        for _, row in counts.iterrows():
            link_id = row['link_id']
            vehicle_count = row['vehicle_count']

            # Skip if link not in network
            if link_id not in link_attrs:
                continue

            # Get precomputed center coordinates
            center = link_attrs[link_id].get('center')
            if center is None:
                continue

            lon, lat = center

            # Create record
            record = {
                'link_id': link_id,
                'x': lon,
                'y': lat,
                'timestamp': timestamp,
                'timepoint_seconds': int(timepoint),
                'vehicle_count': int(vehicle_count)
            }
            records.append(record)

    return records


def parquet_to_heatmap(parquet_input, link_attrs, output_base,
                       output_formats, time_interval_seconds,
                       start_time=None, end_time=None,
                       num_workers=None, gpkg_network=None):
    """
    Convert Parquet events to heatmap data with time-interval sampling.

    Args:
        parquet_input: Path to input Parquet file
        link_attrs: Pre-loaded link attributes dictionary or None to load from gpkg_network
        output_base: Base path for output files (without extension)
        output_formats: List of formats to generate (geojson, csv, parquet, geoparquet)
        time_interval_seconds: Time interval for sampling (e.g., 300 for 5 minutes)
        start_time: Optional start time in seconds (if None, use min from data)
        end_time: Optional end time in seconds (if None, use max from data)
        num_workers: Number of parallel workers (default: CPU count)
        gpkg_network: Path to GeoPackage (optional, for standalone use)
    """

    # Load network if not provided (for standalone use)
    if link_attrs is None:
        if gpkg_network is None:
            raise ValueError("Either link_attrs or gpkg_network must be provided")
        logger.info("Loading road network...")
        network_df = load_network_cached(gpkg_network)
        link_attrs = build_link_attributes_dict(network_df, link_id_col='linkId', precompute_endpoints=True)

    # Set default workers
    if num_workers is None:
        num_workers = mp.cpu_count()

    # Read Parquet file
    logger.info(f"Reading Parquet file: {parquet_input}")
    df = pd.read_parquet(parquet_input)
    total_rows = len(df)
    logger.info(f"Total events loaded: {total_rows:,}")

    # Ensure link_id is string for consistency
    df['link_id'] = df['link_id'].astype(str)

    # Determine time range
    if start_time is None:
        start_time = int(df['time_enter'].min())
    if end_time is None:
        end_time = int(df['time_leave'].max())

    logger.info(f"Time range: {start_time} to {end_time} seconds ({time_to_timestamp(start_time)} to {time_to_timestamp(end_time)})")

    # Generate timepoints
    timepoints = np.arange(start_time, end_time + time_interval_seconds, time_interval_seconds)
    logger.info(f"Generating {len(timepoints)} timepoints at {time_interval_seconds}s intervals")

    # Split timepoints into batches for parallel processing
    # Each worker processes ~10 timepoints
    batch_size = max(1, len(timepoints) // (num_workers * 2))
    timepoint_batches = [timepoints[i:i + batch_size] for i in range(0, len(timepoints), batch_size)]

    logger.info(f"Processing {len(timepoints)} timepoints in {len(timepoint_batches)} batches using {num_workers} workers...")

    # Create worker function with fixed df and link_attrs
    worker_func = partial(process_timepoint_batch, df=df, link_attrs=link_attrs)

    # Process batches in parallel
    heatmap_records = []
    with mp.Pool(num_workers) as pool:
        batch_num = 0
        for batch_records in pool.imap_unordered(worker_func, timepoint_batches):
            heatmap_records.extend(batch_records)
            batch_num += 1

            # Log progress every few batches
            if batch_num % 10 == 0 or batch_num == len(timepoint_batches):
                progress = (batch_num / len(timepoint_batches)) * 100
                logger.info(f"Progress: {batch_num}/{len(timepoint_batches)} batches ({progress:.1f}%)")

    logger.info(f"Total heatmap records generated: {len(heatmap_records):,}")

    # Setup output files
    output_paths = {}
    for fmt in output_formats:
        output_paths[fmt] = f"{output_base}.{fmt}"

    logger.info("Output files:")
    for fmt, path in output_paths.items():
        logger.info(f"  {fmt}: {path}")

    # Export to formats
    if 'geojson' in output_formats:
        logger.info("Writing GeoJSON...")
        features = []
        for rec in heatmap_records:
            feature = {
                "type": "Feature",
                "geometry": {
                    "type": "Point",
                    "coordinates": [rec['x'], rec['y']]
                },
                "properties": {
                    "link_id": rec['link_id'],
                    "timestamp": rec['timestamp'],
                    "timepoint_seconds": rec['timepoint_seconds'],
                    "vehicle_count": rec['vehicle_count']
                }
            }
            features.append(feature)

        geojson = {
            "type": "FeatureCollection",
            "features": features
        }

        with open(output_paths['geojson'], 'w') as f:
            json.dump(geojson, f)
        logger.success(f"GeoJSON created: {output_paths['geojson']}")

    if 'csv' in output_formats:
        logger.info("Writing CSV...")
        with open(output_paths['csv'], 'w', newline='') as f:
            writer = csv_module.writer(f)
            writer.writerow(['link_id', 'x', 'y', 'timestamp', 'timepoint_seconds', 'vehicle_count'])
            for rec in heatmap_records:
                writer.writerow([rec['link_id'], rec['x'], rec['y'], rec['timestamp'],
                               rec['timepoint_seconds'], rec['vehicle_count']])
        logger.success(f"CSV created: {output_paths['csv']}")

    if 'parquet' in output_formats:
        logger.info("Writing Parquet...")
        df_out = pd.DataFrame(heatmap_records)
        df_out.to_parquet(output_paths['parquet'], index=False)
        logger.success(f"Parquet created: {output_paths['parquet']}")

    if 'geoparquet' in output_formats:
        logger.info("Writing GeoParquet...")
        df_out = pd.DataFrame(heatmap_records)
        # Create geometry from x, y
        geometry = [Point(row['x'], row['y']) for _, row in df_out.iterrows()]
        gdf_out = gpd.GeoDataFrame(df_out.drop(columns=['x', 'y']), geometry=geometry, crs='EPSG:4326')
        gdf_out.to_parquet(output_paths['geoparquet'])
        logger.success(f"GeoParquet created: {output_paths['geoparquet']}")

    logger.success("Heatmap export completed!")


if __name__ == "__main__":
    import argparse
    import multiprocessing as mp

    parser = argparse.ArgumentParser()
    parser.add_argument("--parquet_input", required=True)
    parser.add_argument("--gpkg_network", required=True)
    parser.add_argument("--output_base", required=True, help="Base path for output (without extension)")
    parser.add_argument("--output_formats", nargs='+', default=['csv'],
                       help="Output formats: geojson, csv, parquet, geoparquet")
    parser.add_argument("--time_interval", type=int, default=300,
                       help="Time interval in seconds (default: 300 = 5 minutes)")
    parser.add_argument("--start_time", type=int, default=None,
                       help="Start time in seconds (optional)")
    parser.add_argument("--end_time", type=int, default=None,
                       help="End time in seconds (optional)")

    args = parser.parse_args()

    parquet_to_heatmap(
        parquet_input=args.parquet_input,
        link_attrs=None,  # Will be loaded from gpkg_network
        output_base=args.output_base,
        output_formats=args.output_formats,
        time_interval_seconds=args.time_interval,
        start_time=args.start_time,
        end_time=args.end_time,
        gpkg_network=args.gpkg_network
    )