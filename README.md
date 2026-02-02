# UTPS Traffic Simulation

Process and visualize traffic simulation data from MATSim simulations.

## Documentation

**Full Documentation**: Build and view the complete documentation with installation guides, usage examples, and API reference.

```bash
# Build documentation
cd docs
make html

# View documentation
open build/html/index.html  # macOS
xdg-open build/html/index.html  # Linux
start build/html/index.html  # Windows
```

The documentation includes:
- **User Guide**: Installation, setup, and usage instructions
- **API Reference**: Complete API documentation for all modules

---

## Quick Setup Guide

## Prerequisites

1. **Python 3.12 or higher**
   - Download from [python.org](https://www.python.org/downloads/)

2. **Verify Python is installed**:
   ```bash
   python --version
   pip --version
   ```

## Installation

**Step 1: Clone or extract the repository**
```bash
# If using Git:
git clone <repository-url>
cd utps-ts-repo

# Or extract the zip file and navigate to the directory
unzip utps-traffic-sim.zip
cd utps-ts-repo
```

**Step 2: Install dependencies**
```bash
# Create a virtual environment (recommended)
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install required packages
pip install -r requirements.txt
```

**Step 3: Add your data**
```bash
# Create the data directory structure
mkdir -p data/raw

# Copy your data files into data/raw
# You need:
#   - events.xml (or events.xml.gz)
#   - network.gpkg (or similar network file)
```

---

## Data Requirements

Your data should be organized like this:

```
data/
├── raw/
│   └── v4/
│       ├── events.xml.gz          # MATSim events file
│       └── road_network_v4_clipped.gpkg  # Road network
├── interim/      # Created automatically
├── processed/    # Output goes here
└── external/     # Optional
```

**Supported formats:**
- Events: `.xml` or `.xml.gz` (compressed)
- Network: `.gpkg` (GeoPackage) or `.shp` (Shapefile)

---

## Using the Application

### Via Command Line

Run a pipeline directly using a configuration file:
```bash
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

### Creating Configuration Files

Configuration files are YAML files that specify:
- Input data paths
- Time windows to analyze
- Output formats and settings
- Processing parameters

Example configuration:
```yaml
# configs/example.yaml
input:
  events_file: data/raw/v4/events.xml.gz
  network_file: data/raw/v4/road_network_v4_clipped.gpkg

time_windows:
  - start: "07:00:00"
    end: "09:00:00"
    name: "morning_rush"

output:
  format: "parquet"
  directory: "data/processed"
```

---

## Common Tasks

### Run a pipeline
```bash
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

### Process multiple time windows
```bash
# Edit your config file to include multiple time_windows, then run:
python -m traffic_sim_module.pipeline.main_pipeline configs/multi_window.yaml
```

### View logs
Application logs are saved in the `logs/` directory.

---

## Troubleshooting

### Missing dependencies
```bash
# Reinstall all dependencies
pip install -r requirements.txt
```

### Can't find my data
1. Check that data is in the correct location: `data/raw/v4/`
2. Verify file paths in your configuration file
3. Verify file permissions (should be readable)

### Memory errors with large datasets
- Process smaller time windows
- Increase available system memory
- Process data in batches

---

## Getting Help

**Check logs:**
Application logs are saved in the `logs/` directory on your computer.

**Full documentation:**
See the complete documentation by building the Sphinx docs (see Documentation section above).

---

## Sharing Your Results

Your processed data will be in:
```
data/processed/
```

These files can be:
- Opened in GIS software (QGIS, ArcGIS)
- Analyzed with pandas/geopandas
- Shared with others

---

## Updating the Application

```bash
# Pull latest changes (if using Git)
git pull

# Reinstall dependencies if requirements.txt changed
pip install -r requirements.txt
```

---

## System Requirements

**Minimum:**
- 4 GB RAM
- 10 GB free disk space
- Python 3.12 or higher

**Recommended:**
- 8 GB RAM or more
- 50 GB free disk space (for large datasets)
- SSD for better performance

---

## Need More Help?

Check the full documentation by building the Sphinx docs (see Documentation section above).
