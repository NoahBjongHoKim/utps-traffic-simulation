# Scripts Directory

This directory contains standalone utility scripts for various traffic simulation tasks.

## Directory Structure

### `filtering/`
XML event filtering and preprocessing scripts.

- **`time_filter_events_011.py`** - Filter events by time interval
- **`spatial_filtering_events_xml_003.py`** - Filter events by spatial boundary
- **`time_and_spatial_filter_events_001.py`** - Combined time and spatial filtering
- **`sort_xml_by_person.py`** - Sort events by person ID
- **`sort_xml_by_time.py`** - Sort events chronologically
- **`preprocess_events_xml_001.py`** - General event preprocessing

### `post_processing/`
GeoJSON output processing and refinement scripts.

- **`sort_geojson_001.py`** - Fast in-memory GeoJSON sorting (requires sufficient RAM)
- **`sort_geojson_002.py`** - Memory-efficient GeoJSON sorting (for large files)
- **`merge_geojson.py`** - Merge multiple GeoJSON files into one
- **`find_trips_in_geojson.py`** - Extract specific trips/persons from GeoJSON

### `network_tools/`
Road network analysis and manipulation tools.

- **`get_link_length.py`** - Calculate link lengths from network GeoPackage

### `cluster/`
Scripts for running jobs on the Euler cluster (ETH's HPC system).

Contains job submission scripts (`.sh` files) and templates for:
- Event filtering jobs
- GeoJSON generation jobs
- Post-processing jobs

## Usage Guidelines

### Standalone Scripts vs. Module Functions

- **Use scripts** when you need a one-off task or command-line tool
- **Use module functions** (`traffic_sim_module/`) when building pipelines or programmatic workflows

### Running Scripts

Most scripts accept command-line arguments:

```bash
# Example: Filter events by time
python scripts/filtering/time_filter_events_011.py \
    --input data/raw/v4/events.xml \
    --output data/interim/filtered_events.xml \
    --start-time 07:30 \
    --end-time 09:00

# Example: Sort GeoJSON
python scripts/post_processing/sort_geojson_001.py \
    --input data/processed/trajectories.geojson \
    --output data/processed/trajectories_sorted.geojson
```

Check each script's header or run with `--help` for specific usage.

### Cluster Usage

For processing large files (> 100MB), use the Euler cluster scripts:

```bash
cd scripts/cluster/job_files
# Edit the .sh file to set paths
sbatch events_to_geojson_medium.sh
```

## Integration with Pipeline

Many of these scripts' functionality has been integrated into the main pipeline (`traffic_sim_module/pipelines/`):

| Script Functionality | Pipeline Equivalent |
|---------------------|---------------------|
| Time filtering | `xml_to_parquet.py` with time interval config |
| Spatial filtering | `xml_to_parquet.py` with boundary config |
| Interpolation | `parquet_to_geojson.py` |
| GeoJSON sorting | Built into output generation |

**When to use scripts vs. pipeline:**
- **Pipeline**: For standard workflows, reproducible analysis, configuration-driven processing
- **Scripts**: For custom one-off tasks, debugging, or when you need fine-grained control

## Contributing New Scripts

When adding new scripts:

1. Place in the appropriate category directory
2. Add a docstring explaining purpose and usage
3. Use `argparse` for command-line arguments
4. Update this README with a description
5. Consider whether functionality should be in `traffic_sim_module/` instead
