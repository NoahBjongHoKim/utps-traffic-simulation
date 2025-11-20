# Quick Reference Guide

## Essential Commands

### Setup
```bash
# Install dependencies
pip install -r requirements.txt

# Format code
make format

# Clean compiled files
make clean
```

### Running the Pipeline

```bash
# Basic usage
python -m traffic_sim_module.pipeline.main_pipeline <config.yaml>

# Examples
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_evening_rush.yaml
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_full_day.yaml
```

### Using Individual Scripts

```bash
# Filter events by time
python scripts/filtering/time_filter_events_011.py \
    --input data/raw/v4/events.xml \
    --output data/interim/filtered.xml

# Sort GeoJSON (fast, requires RAM)
python scripts/post_processing/sort_geojson_001.py \
    --input data/processed/trajectories.geojson \
    --output data/processed/sorted.geojson

# Sort GeoJSON (memory-efficient)
python scripts/post_processing/sort_geojson_002.py \
    --input data/processed/trajectories.geojson \
    --output data/processed/sorted.geojson

# Merge GeoJSON files
python scripts/post_processing/merge_geojson.py \
    --inputs file1.geojson file2.geojson \
    --output merged.geojson
```

## Project Structure Quick Reference

```
utps-ts-repo/
│
├── traffic_sim_module/          ← Importable Python package
│   ├── io/                       ← I/O functions (XML, GeoPackage, Parquet)
│   ├── processing/               ← Core processing (geometry, interpolation, transforms)
│   ├── pipelines/                ← Main pipeline code
│   └── config.py                 ← Pydantic configuration schemas
│
├── scripts/                      ← Standalone CLI tools
│   ├── filtering/                ← Event filtering scripts
│   ├── post_processing/          ← GeoJSON processing
│   ├── network_tools/            ← Network analysis tools
│   └── cluster/                  ← Euler cluster job scripts
│
├── configs/                      ← YAML configuration files
│   ├── v4_morning_rush.yaml
│   ├── v4_evening_rush.yaml
│   └── v4_full_day.yaml
│
├── gis/                          ← GIS tools and projects (ArcGIS, QGIS)
│
└── data/                         ← Data directory (populate manually)
    ├── raw/v4/                   ← Original data
    ├── interim/                  ← Intermediate files (Parquet)
    └── processed/                ← Final outputs (GeoJSON)
```

## Import Patterns

### Using Module Functions in Python

```python
# I/O operations
from traffic_sim_module.io.gpkg_reader import load_gpkg
from traffic_sim_module.io.xml_reader import element_to_dict

# Processing functions
from traffic_sim_module.processing.geometry import cal_arith_angle, distance
from traffic_sim_module.processing.interpolation import interpolate_1s, interpolate_20s
from traffic_sim_module.processing.transforms import time_to_timestamp

# Pipeline functions
from traffic_sim_module.pipeline.xml_to_parquet import xml_to_parquet_filtered
from traffic_sim_module.pipeline.parquet_to_animation import parquet_to_geojson

# Configuration
from traffic_sim_module.config import PipelineConfig
```

## Common Workflows

### Workflow 1: Quick Analysis of Rush Hour
```bash
# 1. Ensure data is in place
ls data/raw/v4/events.xml data/raw/v4/network.gpkg

# 2. Run pipeline
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml

# 3. Output is in data/processed/
ls -lh data/processed/v4_morning_rush_trajectories.geojson
```

### Workflow 2: Custom Time Window
```bash
# 1. Copy example config
cp configs/v4_morning_rush.yaml configs/my_custom.yaml

# 2. Edit my_custom.yaml to change time intervals

# 3. Run pipeline
python -m traffic_sim_module.pipeline.main_pipeline configs/my_custom.yaml
```

### Workflow 3: Post-Process Existing GeoJSON
```bash
# Sort by timestamp and person ID
python scripts/post_processing/sort_geojson_001.py \
    --input data/processed/trajectories.geojson \
    --output data/processed/trajectories_sorted.geojson

# Extract specific person's trips
python scripts/post_processing/find_trips_in_geojson.py \
    --input data/processed/trajectories_sorted.geojson \
    --person-id 12345 \
    --output data/processed/person_12345.geojson
```

### Workflow 4: Large File Processing on Euler
```bash
# 1. Copy data to Euler cluster
scp data/raw/v4/events.xml username@euler.ethz.ch:~/data/

# 2. Edit cluster job script
vim scripts/cluster/job_files/events_to_geojson_medium.sh

# 3. Submit job
cd scripts/cluster/job_files
sbatch events_to_geojson_medium.sh

# 4. Monitor job
squeue -u $USER

# 5. Download results
scp username@euler.ethz.ch:~/results/*.geojson data/processed/
```

## Configuration File Template

```yaml
paths:
  xml_input: data/raw/v4/events.xml
  gpkg_network: data/raw/v4/network.gpkg
  parquet_intermediate: data/interim/filtered_events.parquet
  geojson_output: data/processed/trajectories.geojson

filters:
  time_interval_1:
    start: "HH:MM"  # 24-hour format
    end: "HH:MM"
  time_interval_2:
    start: "HH:MM"
    end: "HH:MM"

processing:
  num_workers: 8      # Optional, defaults to CPU count
  chunk_size: 100000  # Larger = more memory but faster

skip_xml_to_parquet: false
skip_parquet_to_geojson: false
```

## Troubleshooting

### "Module not found" errors
```bash
# Ensure package is installed in editable mode
pip install -e .
```

### Memory errors during processing
```yaml
# Reduce chunk_size and num_workers in config
processing:
  num_workers: 2
  chunk_size: 50000
```

### Large file processing takes too long
```bash
# Use Euler cluster scripts in scripts/cluster/
# Or split processing by time intervals
```

## File Size Estimates

| Input Size | Processing Time (Local) | Recommended Method |
|-----------|-------------------------|-------------------|
| < 100 MB | Minutes | Local pipeline |
| 100 MB - 1 GB | Hours | Local with reduced workers |
| > 1 GB | Many hours | Euler cluster |

## Getting Help

- Read detailed docs: `docs/`
- Check migration guide: `MIGRATION.md`
- Review example configs: `configs/`
- Examine script headers for usage info
