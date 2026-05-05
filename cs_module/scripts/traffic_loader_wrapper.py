"""
Traffic Loader Wrapper Script for ArcGIS Add-In

This wrapper provides a command-line interface for the traffic simulation pipeline,
designed to be called from C# with structured progress reporting.

Usage:
    python traffic_loader_wrapper.py --xml events.xml --gpkg network.gpkg
           --start-time 08:00 --end-time 09:00 --output result

Progress Output Format:
    PROGRESS: <stage> | <percent> | <message>
    ERROR: <error_message>
    SUCCESS: <output_path>
"""

import argparse
import sys
import os
from pathlib import Path
import traceback

# Force UTF-8 output on Windows to avoid CP1252 encoding errors in log messages
if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if sys.stderr.encoding != 'utf-8':
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')


def print_progress(stage, percent, message):
    """Print structured progress message for C# to parse."""
    print(f"PROGRESS: {stage} | {percent} | {message}", flush=True)


def print_error(message):
    """Print structured error message for C# to parse."""
    print(f"ERROR: {message}", flush=True)


def print_success(output_path):
    """Print structured success message for C# to parse."""
    print(f"SUCCESS: {output_path}", flush=True)


def validate_inputs(args):
    """Validate input arguments before processing."""
    errors = []

    # Check XML file — only required if no pre-computed raw parquet exists alongside it
    xml_dir = os.path.dirname(os.path.abspath(args.xml))
    xml_stem = os.path.splitext(os.path.basename(args.xml))[0]
    raw_parquet_candidate = os.path.join(xml_dir, xml_stem + ".parquet")
    if not os.path.exists(args.xml) and not os.path.exists(raw_parquet_candidate):
        errors.append(f"XML file not found and no pre-computed parquet exists: {args.xml}")

    # Check GPKG file
    if not os.path.exists(args.gpkg):
        errors.append(f"GPKG file not found: {args.gpkg}")

    # Validate time format — accepts HH:MM or HH:MM:SS
    import re
    time_pattern = re.compile(r'^([0-1][0-9]|2[0-3]):([0-5][0-9])(?::([0-5][0-9]))?$')

    if not time_pattern.match(args.start_time):
        errors.append(f"Invalid start time format: {args.start_time} (use HH:MM or HH:MM:SS)")

    if not time_pattern.match(args.end_time):
        errors.append(f"Invalid end time format: {args.end_time} (use HH:MM or HH:MM:SS)")

    # Validate time range
    if time_pattern.match(args.start_time) and time_pattern.match(args.end_time):
        start_seconds = time_to_seconds(args.start_time)
        end_seconds = time_to_seconds(args.end_time)

        if end_seconds <= start_seconds:
            errors.append(f"End time must be after start time")

    # Check output directory
    output_dir = os.path.dirname(args.output)
    if output_dir and not os.path.exists(output_dir):
        try:
            os.makedirs(output_dir, exist_ok=True)
        except Exception as e:
            errors.append(f"Cannot create output directory: {e}")

    return errors


def time_to_seconds(time_str):
    """Convert HH:MM or HH:MM:SS to seconds since midnight."""
    parts = time_str.split(':')
    h, m = int(parts[0]), int(parts[1])
    s = int(parts[2]) if len(parts) == 3 else 0
    return h * 3600 + m * 60 + s


def run_pipeline(args):
    """Execute the traffic simulation pipeline."""

    # Import pipeline modules (after sys.path is set up)
    try:
        print_progress("INIT", 0, "Importing pipeline modules...")

        # Add parent directory to path to import traffic_sim_module
        repo_root = Path(__file__).parent.parent.parent
        sys.path.insert(0, str(repo_root))

        from python_module.pipeline.xml_to_parquet import (
            xml_to_parquet_filtered,
            xml_to_parquet_full,
            filter_parquet_to_intermediate,
        )
        from python_module.pipeline.parquet_to_animation import parquet_to_export
        from python_module.utils.network_cache import build_link_attributes_dict, load_network_cached

        print_progress("INIT", 10, "Modules loaded successfully")

    except ImportError as e:
        print_error(f"Failed to import pipeline modules: {e}")
        print_error(f"Python path: {sys.path}")
        return 1

    # Convert time strings to seconds
    start_seconds = time_to_seconds(args.start_time)
    end_seconds = time_to_seconds(args.end_time)
    time_intervals = [(start_seconds, end_seconds)]

    # Optional bbox
    bbox = tuple(args.bbox) if args.bbox else None

    # Resolve paths
    output_dir = os.path.dirname(os.path.abspath(args.output))
    xml_dir = os.path.dirname(os.path.abspath(args.xml))

    # Look for a pre-computed raw parquet next to the XML file (same stem, .parquet extension).
    # If found, skip the 30+ minute XML parse entirely and filter from parquet instead.
    xml_stem = os.path.splitext(os.path.basename(args.xml))[0]
    raw_parquet_candidate = os.path.join(xml_dir, xml_stem + ".parquet")
    if os.path.exists(raw_parquet_candidate):
        raw_parquet = raw_parquet_candidate
    else:
        raw_parquet = None

    # Intermediate (filtered) parquet goes in an "interim" subfolder of the output dir
    interim_dir = os.path.join(output_dir, "interim")
    os.makedirs(interim_dir, exist_ok=True)
    intermediate_parquet = os.path.join(interim_dir, "filtered_events.parquet")

    try:
        # Stage 1: Load network
        print_progress("NETWORK", 15, "Loading road network...")
        network_df = load_network_cached(args.gpkg)
        link_attrs = build_link_attributes_dict(network_df, link_id_col='linkId', precompute_endpoints=True)

        # Always read the network CRS — needed to reproject output coordinates to WGS84
        import geopandas as gpd
        network_gdf_meta = gpd.read_file(args.gpkg, rows=1)
        network_crs = network_gdf_meta.crs
        print_progress("NETWORK", 18, f"Network CRS: {network_crs}")

        # Apply bbox spatial filter if provided — restrict valid links to study area
        if bbox:
            from shapely.geometry import box as shapely_box
            from shapely.ops import transform as shapely_transform
            import pyproj

            # bbox is in WGS84 — reproject to match the network CRS
            clip_geom_wgs84 = shapely_box(*bbox)
            if network_crs and not network_crs.equals("EPSG:4326"):
                transformer = pyproj.Transformer.from_crs(
                    "EPSG:4326", network_crs, always_xy=True
                )
                clip_geom = shapely_transform(transformer.transform, clip_geom_wgs84)
            else:
                clip_geom = clip_geom_wgs84

            valid_links = set(
                lid for lid, attrs in link_attrs.items()
                if attrs.get('geometry') is not None and attrs['geometry'].intersects(clip_geom)
            )
            print_progress("NETWORK", 25, f"Network loaded: {len(link_attrs):,} total links, "
                                          f"{len(valid_links):,} within bbox")
        else:
            valid_links = set(link_attrs.keys())
            print_progress("NETWORK", 25, f"Network loaded: {len(link_attrs):,} links")

        # Stage 2: Build filtered intermediate parquet
        if raw_parquet:
            # Fast path — filter the pre-computed raw parquet (seconds, not minutes)
            print_progress("XML_PARSE", 30, f"Raw parquet found, skipping XML parse: {os.path.basename(raw_parquet)}")
            filter_parquet_to_intermediate(
                raw_parquet=raw_parquet,
                parquet_output=intermediate_parquet,
                valid_links=valid_links,
                time_intervals=time_intervals,
            )
            print_progress("XML_PARSE", 60, "Filtered from raw parquet")
        else:
            # Slow path — parse full XML once and save as permanent raw parquet,
            # then filter from it. Next run will find the raw parquet and skip XML entirely.
            print_progress("XML_PARSE", 30, "No raw parquet found — parsing full XML (one-time, this may take a while)...")
            xml_to_parquet_full(
                xml_input=args.xml,
                parquet_output=raw_parquet_candidate,
                num_workers=args.workers,
                chunk_size=args.chunk_size,
            )
            print_progress("XML_PARSE", 50, f"Raw parquet saved: {os.path.basename(raw_parquet_candidate)}")
            filter_parquet_to_intermediate(
                raw_parquet=raw_parquet_candidate,
                parquet_output=intermediate_parquet,
                valid_links=valid_links,
                time_intervals=time_intervals,
            )
            print_progress("XML_PARSE", 60, "XML parsing complete")

        # Stage 3: Export to Parquet and GeoJSON (snapshot mode - simple event points)
        print_progress("EXPORT", 65, "Creating event point layers...")
        parquet_to_export(
            parquet_input=intermediate_parquet,
            link_attrs=link_attrs,
            output_base=args.output,
            output_formats=['parquet'],  # Parquet only — C# converts to GDB Feature Class
            num_workers=args.workers,
            chunk_size=args.chunk_size,
            snapshot_mode=True,  # Output simple event points, not interpolated trajectories
            fps=args.fps,
            num_chunks=args.num_chunks,
            network_crs=network_crs,  # Reproject UTM → WGS84 for ArcGIS
        )
        print_progress("EXPORT", 95, "Event point layers created")

        # Cleanup intermediate file if requested
        if not args.keep_intermediate:
            try:
                os.remove(intermediate_parquet)
                print_progress("CLEANUP", 98, "Cleaned up intermediate files")
            except:
                pass  # Don't fail if cleanup fails

        # Success!
        output_geojson = args.output + ".geojson"
        output_parquet = args.output + ".parquet"
        print_progress("COMPLETE", 100, "Pipeline complete")
        print_success(output_geojson)  # Return GeoJSON path for ArcGIS layer loading

        return 0

    except Exception as e:
        print_error(f"Pipeline failed: {str(e)}")
        print_error(traceback.format_exc())
        return 1


def main():
    parser = argparse.ArgumentParser(
        description='Traffic Simulation Data Loader for ArcGIS',
        formatter_class=argparse.RawDescriptionHelpFormatter
    )

    # Required arguments
    parser.add_argument('--xml', required=True,
                        help='Path to XML events file')
    parser.add_argument('--gpkg', required=True,
                        help='Path to GeoPackage road network file')
    parser.add_argument('--start-time', required=True,
                        help='Start time in HH:MM format (e.g., 08:00)')
    parser.add_argument('--end-time', required=True,
                        help='End time in HH:MM format (e.g., 09:00)')
    parser.add_argument('--output', required=True,
                        help='Output file base path (without extension)')

    # Optional arguments
    parser.add_argument('--workers', type=int, default=None,
                        help='Number of worker processes (default: CPU count)')
    parser.add_argument('--chunk-size', type=int, default=100000,
                        help='Chunk size for processing (default: 100000)')
    parser.add_argument('--keep-intermediate', action='store_true',
                        help='Keep intermediate Parquet file')
    parser.add_argument('--bbox', type=float, nargs=4,
                        metavar=('XMIN', 'YMIN', 'XMAX', 'YMAX'),
                        help='Spatial bounding box filter in WGS84 decimal degrees')
    parser.add_argument('--fps', type=int, default=1,
                        help='Frames per second for sub-second interpolation (default: 1)')
    parser.add_argument('--num-chunks', type=int, default=1,
                        help='Split output into N time-based files for ArcGIS performance (default: 1)')

    args = parser.parse_args()

    # Set default workers to CPU count
    if args.workers is None:
        import multiprocessing
        args.workers = multiprocessing.cpu_count()

    # Validate inputs
    print_progress("VALIDATE", 0, "Validating inputs...")
    validation_errors = validate_inputs(args)

    if validation_errors:
        for error in validation_errors:
            print_error(error)
        return 1

    print_progress("VALIDATE", 5, "Input validation passed")

    # Run pipeline
    return run_pipeline(args)


if __name__ == '__main__':
    try:
        exit_code = main()
        sys.exit(exit_code)
    except KeyboardInterrupt:
        print_error("Process interrupted by user")
        sys.exit(130)
    except Exception as e:
        print_error(f"Unexpected error: {str(e)}")
        print_error(traceback.format_exc())
        sys.exit(1)