# Migration Summary

This document describes the migration from `Traffic_Simulation_SJJ` to the structured `utps-ts-repo` project.

## Migration Date
November 18, 2025

## What Was Migrated

### ✅ Code Pipeline (Source: `Code_Pipeline/`)
**Destination**: `traffic_sim_module/pipelines/`

- `main_pipeline.py` - Main pipeline coordinator
- `xml_to_parquet.py` - XML to Parquet conversion with filtering
- `parquet_to_geojson.py` - Parquet to GeoJSON conversion with interpolation
- `config.py` - Pydantic configuration schemas (formerly config_schema.py)

### ✅ Core Utility Functions (Source: `events_to_json/events_to_geojson_008.py`)
**Destination**: Modularized into `traffic_sim_module/`

#### I/O Modules (`traffic_sim_module/io/`)
- `gpkg_reader.py` - GeoPackage loading and CRS transformation
- `xml_reader.py` - XML element serialization for multiprocessing

#### Processing Modules (`traffic_sim_module/processing/`)
- `geometry.py` - Distance, bearing, and coordinate calculations
- `transforms.py` - Time/timestamp conversions
- `interpolation.py` - Trajectory interpolation (1s and 20s variants)

### ✅ Interpolation Functions (Source: `events_to_json/interpolation_functions/`)
**Destination**: `traffic_sim_module/processing/interpolation.py`

- Consolidated 1s and 20s interpolation functions
- Added type hints and documentation
- Maintained backward compatibility

### ✅ Tool Scripts (Source: `events_to_json/Tools/`)
**Destination**: `scripts/` (categorized by function)

#### Post-Processing (`scripts/post_processing/`)
- `sort_geojson_001.py` - Fast in-memory GeoJSON sorting
- `sort_geojson_002.py` - Memory-efficient GeoJSON sorting
- `merge_geojson.py` - Merge multiple GeoJSON files
- `find_trips_in_geojson.py` - Extract specific trips

#### Filtering (`scripts/filtering/`)
- `time_filter_events_011.py` - Time-based event filtering
- `spatial_filtering_events_xml_003.py` - Spatial event filtering
- `time_and_spatial_filter_events_001.py` - Combined filtering
- `sort_xml_by_person.py` - Sort events by person ID
- `sort_xml_by_time.py` - Sort events chronologically
- `preprocess_events_xml_001.py` - Event preprocessing

#### Network Tools (`scripts/network_tools/`)
- `get_link_length.py` - Calculate link lengths from network

### ✅ Euler Cluster Scripts (Source: `events_to_json/euler_cluster/`)
**Destination**: `scripts/cluster/`

- Job submission scripts (.sh files)
- Job templates for different processing tasks
- Output and results directories

### ✅ GIS Tools (Source: `GIS/`)
**Destination**: `gis/` (top-level)

- `ArcGIS_for_Traffic_Sim_SJJ/` - ArcGIS workflows
- `Arcgis/` - Additional ArcGIS files
- `edit_road_network/` - Network editing tools
- `qgis_visualization.qgz` - QGIS project file
- `qgis commands.txt` - QGIS command reference

### ✅ Configuration Files
**Destination**: `configs/`

Created YAML configuration presets:
- `v4_morning_rush.yaml` - Morning rush hour (07:30-09:00)
- `v4_evening_rush.yaml` - Evening rush hour (16:30-18:30)
- `v4_full_day.yaml` - Full simulation day
- `README.md` - Configuration documentation

## What Was NOT Migrated

### ❌ Data Files
- `data/v2/` - v2 is deprecated, focus on v4
- `data/v4/` - Large data files (you'll move these manually to `data/raw/v4/`)
- `events_to_json/results/` - Output files (regenerate as needed)

### ❌ Archived Scripts
- All files in `*/archive/` directories were excluded
- Old script versions (e.g., `events_to_geojson_006.py`)

### ❌ IDE Configuration
- `.idea/` - PyCharm settings (keep your existing IDE setup)

## New Project Structure

```
utps-ts-repo/
├── traffic_sim_module/          # Main Python package
│   ├── io/                       # I/O utilities
│   │   ├── gpkg_reader.py
│   │   └── xml_reader.py
│   ├── processing/               # Core processing functions
│   │   ├── geometry.py
│   │   ├── interpolation.py
│   │   └── transforms.py
│   ├── pipelines/                # Pipeline orchestration
│   │   ├── main_pipeline.py
│   │   ├── xml_to_parquet.py
│   │   └── parquet_to_geojson.py
│   ├── utils/                    # General utilities
│   └── config.py                 # Pydantic schemas
│
├── scripts/                      # Standalone scripts
│   ├── filtering/                # Event filtering tools
│   ├── post_processing/          # GeoJSON post-processing
│   ├── network_tools/            # Network utilities
│   └── cluster/                  # Euler cluster jobs
│
├── gis/                          # GIS tools and workflows
│   ├── ArcGIS_for_Traffic_Sim_SJJ/
│   ├── edit_road_network/
│   └── qgis_visualization.qgz
│
├── configs/                      # Pipeline configurations
│   ├── v4_morning_rush.yaml
│   ├── v4_evening_rush.yaml
│   └── v4_full_day.yaml
│
├── data/                         # Data directory (populate manually)
│   ├── raw/                      # Original data
│   │   └── v4/
│   │       ├── events.xml        # <- Move manually
│   │       └── network.gpkg      # <- Move manually
│   ├── interim/                  # Intermediate files
│   └── processed/                # Final outputs
│
├── notebooks/                    # Jupyter notebooks
├── docs/                         # Documentation
├── reports/                      # Analysis reports
└── references/                   # Reference materials
```

## Next Steps

### 1. Manual Data Migration
Move your v4 data files to the appropriate locations:
```bash
# Example (adjust paths as needed)
mv /path/to/old/data/v4/events.xml utps-ts-repo/data/raw/v4/
mv /path/to/old/data/v4/network.gpkg utps-ts-repo/data/raw/v4/
```

### 2. Install Dependencies
```bash
cd utps-ts-repo
pip install -r requirements.txt
```

### 3. Format Code
```bash
make format
# or manually: ruff format traffic_sim_module/ scripts/
```

### 4. Run the Pipeline
```bash
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

### 5. Update Import Statements (if needed)
If you have existing scripts that import from the old structure, update them:

**Old:**
```python
from events_to_json.interpolation_functions.1s_interpolation import interpolation
```

**New:**
```python
from traffic_sim_module.processing.interpolation import interpolate_1s
```

## Benefits of New Structure

1. **Modular Code**: Reusable functions separated into logical modules
2. **Type Safety**: Pydantic validation for configurations
3. **Clear Separation**: Code vs scripts vs data vs documentation
4. **Version Control Friendly**: No large data files in code directories
5. **Standard Structure**: Follows Cookiecutter Data Science best practices
6. **Easier Testing**: Modular functions are easier to unit test
7. **Better Documentation**: Clear organization makes onboarding easier

## Rollback Plan

If needed, the original `Traffic_Simulation_SJJ` folder remains unchanged.
You can continue using it while transitioning to the new structure.
