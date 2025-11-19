# Pipeline Configuration Files

This directory contains YAML configuration files for the traffic simulation pipeline.

## Available Configurations

### v4 Configurations (Current/Active Data Version)

- **`v4_morning_rush.yaml`** - Morning rush hour analysis (07:30 - 09:00)
- **`v4_evening_rush.yaml`** - Evening rush hour analysis (16:30 - 18:30)
- **`v4_full_day.yaml`** - Full simulation day (00:00 - 23:59)

## Usage

Run the pipeline with a config file:

```bash
python -m traffic_sim_module.pipelines.main_pipeline configs/v4_morning_rush.yaml
```

## Configuration Structure

```yaml
paths:
  xml_input: path/to/events.xml          # Input MATSim events file
  gpkg_network: path/to/network.gpkg     # Road network GeoPackage
  parquet_intermediate: path/to/out.parquet  # Intermediate filtered data
  geojson_output: path/to/trajectories.geojson  # Final output

filters:
  time_interval_1:                       # First time window
    start: "HH:MM"
    end: "HH:MM"
  time_interval_2:                       # Second time window (or same)
    start: "HH:MM"
    end: "HH:MM"

processing:
  num_workers: 8                         # Parallel workers (optional)
  chunk_size: 100000                     # Events per chunk

skip_xml_to_parquet: false               # Skip step 1 if file exists
skip_parquet_to_geojson: false           # Skip step 2 if file exists
```

## Creating Custom Configurations

1. Copy an existing config file
2. Update the `paths` to point to your data
3. Adjust `filters` for your desired time window
4. Tune `processing` parameters based on your system resources

## Data Organization

Ensure your data follows the Cookiecutter Data Science structure:

```
data/
├── raw/           # Original, immutable data
│   └── v4/
│       ├── events.xml
│       └── network.gpkg
├── interim/       # Intermediate transformations
│   └── *.parquet
└── processed/     # Final analysis-ready data
    └── *.geojson
```

## Notes

- **Time Format**: Use 24-hour format (HH:MM) for time intervals
- **Workers**: Omit `num_workers` to use all available CPU cores
- **Memory**: Reduce `chunk_size` or `num_workers` if you encounter memory issues
- **Large Files**: For files > 100MB, consider using the Euler cluster scripts in `scripts/cluster/`
