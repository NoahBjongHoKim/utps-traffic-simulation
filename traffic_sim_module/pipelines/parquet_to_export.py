"""
Parquet to GeoJSON Converter with Interpolation
Author: Noah Kim & Joe Beck
Date: 14.11.2025

Reads filtered Parquet events and creates GeoJSON with:
- Coordinate interpolation along road segments
- Speed and bearing calculations
- Temporal attributes
"""

import pandas as pd
import pyarrow.parquet as pq
import pyarrow as pa
import json
import math
import multiprocessing as mp
from datetime import datetime, timedelta
from pathlib import Path
import csv as csv_module
import geopandas as gpd
from shapely.geometry import Point
from shapely import wkb

# Handle both module import and direct script execution
try:
    from ..config import logger
    from ..utils.network_cache import load_network_cached, build_link_attributes_dict
except ImportError:
    # Running as standalone script - setup minimal logging
    import sys
    from pathlib import Path
    from loguru import logger

    # Configure logger for standalone execution
    logger.configure(handlers=[{"sink": sys.stderr, "level": "INFO"}])

    # Import from absolute path
    repo_root = Path(__file__).parent.parent.parent
    sys.path.insert(0, str(repo_root))
    from traffic_sim_module.utils.network_cache import load_network_cached, build_link_attributes_dict


def load_network_with_cache(gpkg_path):
    """
    Load road network with automatic Parquet caching.

    First run: Converts GeoPackage to Parquet cache (slow)
    Subsequent runs: Loads from Parquet cache (10-50x faster!)

    Args:
        gpkg_path: Path to the GeoPackage file

    Returns:
        DataFrame with network data
    """
    return load_network_cached(gpkg_path)


def time_to_timestamp(seconds):
    """Convert seconds to timestamp string."""
    base = datetime(2024, 1, 1)
    return (base + timedelta(seconds=int(seconds))).strftime('%Y/%m/%d %H:%M:%S')


def calculate_bearing(start_coords, end_coords):
    """Calculate bearing from start to end coordinates."""
    lat1, lon1 = map(math.radians, start_coords)
    lat2, lon2 = map(math.radians, end_coords)
    
    delta_lon = lon2 - lon1
    
    x = math.cos(lat2) * math.sin(delta_lon)
    y = (math.cos(lat1) * math.sin(lat2) - 
         math.sin(lat1) * math.cos(lat2) * math.cos(delta_lon))
    
    angle = math.atan2(x, y)
    bearing = (math.degrees(angle) + 360) % 360
    
    return round(bearing)


def get_neighboring_links(from_node, to_node, link_attrs):
    """Find previous and next links based on node connections."""
    previous = None
    next_link = None

    for link_id, attrs in link_attrs.items():
        # Access dict attributes directly (not DataFrame columns)
        if attrs.get('to') == from_node and attrs.get('from') != to_node:
            if previous is None:
                previous = link_id
        elif attrs.get('from') == to_node and attrs.get('to') != from_node:
            if next_link is None:
                next_link = link_id

    return previous, next_link


def get_edge_coords(link_id, link_attrs, fallback):
    """Get edge coordinates of a link (handles both LineString and MultiLineString)."""
    if link_id is not None and link_id in link_attrs:
        geom = link_attrs[link_id].get('geometry')
        if geom is not None:
            try:
                if geom.geom_type == 'LineString':
                    return geom.coords[0], geom.coords[-1]
                elif geom.geom_type == 'MultiLineString':
                    return geom.geoms[0].coords[0], geom.geoms[-1].coords[-1]
            except Exception:
                pass
    return fallback, fallback


def get_travel_endpoints(link_id, link_attrs):
    """Determine actual travel start and end points considering neighboring links."""
    attrs = link_attrs[link_id]
    from_node = attrs.get('from')
    to_node = attrs.get('to')

    prev_link, next_link = get_neighboring_links(from_node, to_node, link_attrs)

    current_geom = attrs.get('geometry')

    # Handle both LineString and MultiLineString
    if current_geom.geom_type == 'LineString':
        ec1 = current_geom.coords[0]
        ec2 = current_geom.coords[-1]
    elif current_geom.geom_type == 'MultiLineString':
        ec1 = current_geom.geoms[0].coords[0]
        ec2 = current_geom.geoms[-1].coords[-1]
    else:
        raise ValueError(f"Unsupported geometry type: {current_geom.geom_type}")

    ef1, ef2 = get_edge_coords(prev_link, link_attrs, ec1)
    et1, et2 = get_edge_coords(next_link, link_attrs, ec2)

    # Determine travel direction
    travel_start = None
    travel_end = None

    if ec1 in {ef1, ef2}:
        travel_start = ef1 if ec1 == ef1 else ef2
    elif ec1 in {et1, et2}:
        travel_end = et1 if ec1 == et1 else et2
    else:
        travel_start = ec1

    if ec2 in {ef1, ef2}:
        travel_start = ef1 if ec2 == ef1 else ef2
    elif ec2 in {et1, et2}:
        travel_end = et1 if ec2 == et1 else et2
    else:
        travel_end = ec2

    return travel_start, travel_end


def interpolate_trajectory(link_id, time_enter, time_leave,
                          start_coords, end_coords, person_id,
                          freespeed, link_length, bearing, interval_id):
    """Interpolate points along trajectory with 1-second resolution."""
    time_delta = time_leave - time_enter

    if time_delta <= 0:
        return []

    features = []
    for t in range(time_delta + 1):
        fraction = t / time_delta
        x = round(start_coords[0] + fraction * (end_coords[0] - start_coords[0]), 12)
        y = round(start_coords[1] + fraction * (end_coords[1] - start_coords[1]), 12)

        feature = {
            "geometry": {
                "type": "Point",
                "coordinates": [x, y]
            },
            "properties": {
                "timestamp": time_to_timestamp(time_enter + t),
                "angle": bearing,
                "person_id": person_id,
                "interval_id": interval_id
            }
        }
        features.append(feature)

    return features


def process_parquet_chunk(args):
    """Process a chunk of Parquet data and create GeoJSON features."""
    chunk_df, link_attrs = args

    all_features = []
    links_not_found = set()

    for _, row in chunk_df.iterrows():
        # Ensure link_id is string for lookup consistency
        link_id = str(row['link_id'])

        if link_id not in link_attrs:
            links_not_found.add(link_id)
            continue

        try:
            attrs = link_attrs[link_id]

            # Use precomputed values (massive speedup!)
            start_coords = attrs.get('travel_start')
            end_coords = attrs.get('travel_end')
            bearing = attrs.get('bearing')

            # Fallback if not precomputed (shouldn't happen with default settings)
            if start_coords is None or end_coords is None:
                start_coords, end_coords = get_travel_endpoints(link_id, link_attrs)
                bearing = calculate_bearing(start_coords, end_coords)

            features = interpolate_trajectory(
                link_id,
                row['time_enter'],
                row['time_leave'],
                start_coords,
                end_coords,
                row['person'],
                attrs.get('freespeed'),
                attrs.get('length'),
                bearing,
                row['interval_id']  # Pass interval_id through
            )

            all_features.extend(features)

        except Exception as e:
            logger.warning(f"Error processing link {link_id}: {e}")
            continue

    if links_not_found:
        logger.warning(f"Chunk had {len(links_not_found)} links not found in network. Sample: {list(links_not_found)[:5]}")

    return all_features


def parquet_to_export(parquet_input, link_attrs, output_base,
                       output_formats, num_workers, chunk_size,
                       gpkg_network=None):
    """Main function to convert Parquet to multiple output formats with interpolation.

    Args:
        parquet_input: Path to input Parquet file
        link_attrs: Pre-loaded link attributes dictionary or None to load from gpkg_network
        output_base: Base path for output files (without extension)
        output_formats: List of formats to generate (geojson, csv, parquet, geoparquet)
        num_workers: Number of worker processes
        chunk_size: Chunk size for processing
        gpkg_network: Path to GeoPackage (optional, for standalone use)
        geojson_output: Legacy parameter for backward compatibility
    """

    # Load network if not provided (for standalone use)
    if link_attrs is None:
        if gpkg_network is None:
            raise ValueError("Either link_attrs or gpkg_network must be provided")
        logger.info("Loading road network...")
        network_df = load_network_with_cache(gpkg_network)
        link_attrs = build_link_attributes_dict(network_df, link_id_col='linkId', precompute_endpoints=True)

    # Read Parquet file
    logger.info(f"Reading Parquet file: {parquet_input}")
    parquet_file = pq.ParquetFile(parquet_input)
    total_rows = parquet_file.metadata.num_rows
    logger.info(f"Total events to process: {total_rows:,}")

    # Setup output files
    output_paths = {}
    for fmt in output_formats:
        output_paths[fmt] = f"{output_base}.{fmt}"

    logger.info(f"Output files:")
    for fmt, path in output_paths.items():
        logger.info(f"  {fmt}: {path}")

    # Setup multiprocessing
    logger.info(f"Initializing multiprocessing pool with {num_workers} workers")
    pool = mp.Pool(num_workers)

    # Process in chunks using multiprocessing
    logger.info("Creating trajectory features with interpolation...")

    # Open all output writers
    writers = {}

    try:
        # GeoJSON writer
        if 'geojson' in output_formats:
            writers['geojson'] = open(output_paths['geojson'], 'w')
            writers['geojson'].write('{"type": "FeatureCollection", "features": [\n')
            writers['geojson_first'] = True

        # CSV writer
        if 'csv' in output_formats:
            csv_file = open(output_paths['csv'], 'w', newline='')
            csv_writer = csv_module.writer(csv_file)
            csv_writer.writerow(['x', 'y', 'timestamp', 'angle', 'person_id', 'interval_id'])  # Header
            writers['csv'] = csv_file
            writers['csv_writer'] = csv_writer

        # Parquet/GeoParquet - collect all features first
        if 'parquet' in output_formats or 'geoparquet' in output_formats:
            writers['features_list'] = []

        processed = 0
        batches_processed = 0

        # Create iterator of (df, link_attrs) tuples for all batches
        def batch_generator():
            for batch in parquet_file.iter_batches(batch_size=chunk_size):
                df = batch.to_pandas()
                yield (df, link_attrs)

        # Process batches in parallel using the pool
        for features in pool.imap_unordered(process_parquet_chunk, batch_generator()):
            # Write features to all formats
            for feature in features:
                props = feature['properties']
                coords = feature['geometry']['coordinates']

                # GeoJSON
                if 'geojson' in output_formats:
                    if not writers['geojson_first']:
                        writers['geojson'].write(',\n')
                    json.dump(feature, writers['geojson'])
                    writers['geojson_first'] = False

                # CSV
                if 'csv' in output_formats:
                    writers['csv_writer'].writerow([
                        coords[0], coords[1],  # x, y
                        props['timestamp'], props['angle'], props['person_id'],
                        props['interval_id']
                    ])

                # Collect for Parquet/GeoParquet
                if 'parquet' in output_formats or 'geoparquet' in output_formats:
                    writers['features_list'].append({
                        'x': coords[0],
                        'y': coords[1],
                        'timestamp': props['timestamp'],
                        'angle': props['angle'],
                        'person_id': props['person_id'],
                        'interval_id': props['interval_id']
                    })

            processed += chunk_size  # Approximate (last batch may be smaller)
            batches_processed += 1

            # Log progress every 10 batches
            if batches_processed % 10 == 0:
                progress = min(100, (processed / total_rows) * 100)
                logger.info(f"Progress: {min(processed, total_rows):,}/{total_rows:,} events ({progress:.1f}%)")

        # Close GeoJSON
        if 'geojson' in output_formats:
            writers['geojson'].write('\n]}')
            writers['geojson'].close()
            logger.success(f"GeoJSON created: {output_paths['geojson']}")

        # Close CSV
        if 'csv' in output_formats:
            writers['csv'].close()
            logger.success(f"CSV created: {output_paths['csv']}")

        # Write Parquet
        if 'parquet' in output_formats:
            df_out = pd.DataFrame(writers['features_list'])
            df_out.to_parquet(output_paths['parquet'], index=False)
            logger.success(f"Parquet created: {output_paths['parquet']}")

        # Write GeoParquet
        if 'geoparquet' in output_formats:
            df_out = pd.DataFrame(writers['features_list'])
            # Create geometry from x, y
            geometry = [Point(row['x'], row['y']) for _, row in df_out.iterrows()]
            gdf_out = gpd.GeoDataFrame(df_out.drop(columns=['x', 'y']), geometry=geometry, crs='EPSG:4326')
            gdf_out.to_parquet(output_paths['geoparquet'])
            logger.success(f"GeoParquet created: {output_paths['geoparquet']}")

    finally:
        # Clean up any open file handles
        for key, val in writers.items():
            if hasattr(val, 'close'):
                try:
                    val.close()
                except:
                    pass
        pool.close()
        pool.join()


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("--parquet_input", required=True)
    parser.add_argument("--gpkg_network", required=True)
    parser.add_argument("--output_base", required=True, help="Base path for output (without extension)")
    parser.add_argument("--output_formats", nargs='+', default=['geojson'],
                       help="Output formats: geojson, csv, parquet, geoparquet")
    parser.add_argument("--num_workers", type=int, default=mp.cpu_count())
    parser.add_argument("--chunk_size", type=int, default=10000)

    args = parser.parse_args()

    parquet_to_export(
        parquet_input=args.parquet_input,
        link_attrs=None,  # Will be loaded from gpkg_network
        output_base=args.output_base,
        output_formats=args.output_formats,
        num_workers=args.num_workers,
        chunk_size=args.chunk_size,
        gpkg_network=args.gpkg_network
    )
    # parquet_to_export(
    #     "/Users/noahkim/Documents/UTPS/Traffic_Sim/utps-ts-repo/data/interim/filtered_events_test.parquet",
    #     None,
    #     "/Users/noahkim/Documents/UTPS/Traffic_Sim/utps-ts-repo/data/interim/filtered_events_test.geojson",
    #     ["geojson", "csv", "parquet", "geoparquet"],
    #     6,
    #     30000,
    #     "/Users/noahkim/Documents/UTPS/Traffic_Sim/utps-ts-repo/data/raw/road_network_v4_clipped_single.gpkg"
    # )
