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

    # Check XML file
    if not os.path.exists(args.xml):
        errors.append(f"XML file not found: {args.xml}")

    # Check GPKG file
    if not os.path.exists(args.gpkg):
        errors.append(f"GPKG file not found: {args.gpkg}")

    # Validate time format (HH:MM)
    import re
    time_pattern = re.compile(r'^([0-1][0-9]|2[0-3]):([0-5][0-9])$')

    if not time_pattern.match(args.start_time):
        errors.append(f"Invalid start time format: {args.start_time} (use HH:MM)")

    if not time_pattern.match(args.end_time):
        errors.append(f"Invalid end time format: {args.end_time} (use HH:MM)")

    # Validate time range
    if time_pattern.match(args.start_time) and time_pattern.match(args.end_time):
        start_h, start_m = map(int, args.start_time.split(':'))
        end_h, end_m = map(int, args.end_time.split(':'))
        start_minutes = start_h * 60 + start_m
        end_minutes = end_h * 60 + end_m

        if end_minutes <= start_minutes:
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
    """Convert HH:MM to seconds since midnight."""
    h, m = map(int, time_str.split(':'))
    return h * 3600 + m * 60


def run_pipeline(args):
    """Execute the traffic simulation pipeline."""

    # Import pipeline modules (after sys.path is set up)
    try:
        print_progress("INIT", 0, "Importing pipeline modules...")

        # Add parent directory to path to import traffic_sim_module
        repo_root = Path(__file__).parent.parent.parent
        sys.path.insert(0, str(repo_root))

        from python_module.pipeline.xml_to_parquet import xml_to_parquet_filtered
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

    # Setup intermediate file path
    intermediate_parquet = args.output + "_intermediate.parquet"

    try:
        # Stage 1: Load network
        print_progress("NETWORK", 15, "Loading road network...")
        network_df = load_network_cached(args.gpkg)
        link_attrs = build_link_attributes_dict(network_df, link_id_col='linkId', precompute_endpoints=True)

        # Apply bbox spatial filter if provided — restrict valid links to study area
        if bbox:
            from shapely.geometry import box as shapely_box
            from shapely.ops import transform as shapely_transform
            import pyproj
            import geopandas as gpd

            # bbox is in WGS84 — reproject to match the network CRS
            network_gdf = gpd.read_file(args.gpkg, rows=1)  # read one row just to get CRS
            network_crs = network_gdf.crs
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

        # Stage 2: XML to Parquet
        print_progress("XML_PARSE", 30, "Starting XML parsing and filtering...")
        xml_to_parquet_filtered(
            xml_input=args.xml,
            valid_links=valid_links,
            parquet_output=intermediate_parquet,
            time_intervals=time_intervals,
            num_workers=args.workers,
            chunk_size=args.chunk_size,
            bbox=bbox,
        )
        print_progress("XML_PARSE", 60, "XML parsing complete")

        # Stage 3: Export to Parquet and GeoJSON (snapshot mode - simple event points)
        print_progress("EXPORT", 65, "Creating event point layers...")
        parquet_to_export(
            parquet_input=intermediate_parquet,
            link_attrs=link_attrs,
            output_base=args.output,
            output_formats=['parquet', 'geojson'],  # Parquet for internal use, GeoJSON for ArcGIS
            num_workers=args.workers,
            chunk_size=args.chunk_size,
            snapshot_mode=True,  # Output simple event points, not interpolated trajectories
            fps=args.fps,
            num_chunks=args.num_chunks,
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