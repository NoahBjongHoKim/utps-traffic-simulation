# UTPS Traffic Simulation

<a target="_blank" href="https://cookiecutter-data-science.drivendata.org/">
    <img src="https://img.shields.io/badge/CCDS-Project%20template-328F97?logo=cookiecutter" />
</a>

Processing and analysis of MATSim traffic simulation data for the Urban Traffic Pattern Synthesis project.

## Overview

This project provides tools and pipelines for processing large-scale traffic simulation data:
- Convert MATSim event XML files to trajectory GeoJSON
- Filter events by time and spatial boundaries
- Interpolate agent positions at configurable time resolutions (1s, 20s)
- Visualize traffic patterns in GIS applications

## Quick Start

### Installation

```bash
# Clone or navigate to the repository
cd utps-ts-repo

# Create and activate conda environment
make create_environment
conda activate utps-ts-repo

# Install dependencies
make requirements
```

### Running the Pipeline

1. Place your data in the appropriate directory:
   ```
   data/raw/v4/
   ├── events.xml      # MATSim events file
   └── network.gpkg    # Road network GeoPackage
   ```

2. Choose or create a configuration file (see `configs/`)

3. Run the pipeline:
   ```bash
   python -m traffic_sim_module.pipelines.main_pipeline configs/v4_morning_rush.yaml
   ```

### Example: Morning Rush Hour Analysis

```bash
# Process morning rush hour (7:30 AM - 9:00 AM)
python -m traffic_sim_module.pipelines.main_pipeline configs/v4_morning_rush.yaml
```

Output will be in `data/processed/v4_morning_rush_trajectories.geojson`

## Documentation

- **[MIGRATION.md](MIGRATION.md)** - Migration guide from old structure
- **[configs/README.md](configs/README.md)** - Configuration file documentation
- **[scripts/README.md](docs/SCRIPTS.md)** - Utility scripts documentation
- **[docs/](docs/)** - Detailed documentation (MkDocs)

## Project Organization
```
├── LICENSE            <- Open-source license if one is chosen
├── Makefile           <- Makefile with convenience commands like `make data` or `make train`
├── README.md          <- The top-level README for developers using this project.
├── data
│   ├── external       <- Data from third party sources.
│   ├── interim        <- Intermediate data that has been transformed.
│   ├── processed      <- The final, canonical data sets for modeling.
│   └── raw            <- The original, immutable data dump.
│
├── docs               <- A default mkdocs project; see www.mkdocs.org for details
│
├── models             <- Trained and serialized models, model predictions, or model summaries
│
├── notebooks          <- Jupyter notebooks. Naming convention is a number (for ordering),
│                         the creator's initials, and a short `-` delimited description, e.g.
│                         `1.0-jqp-initial-data-exploration`.
│
├── pyproject.toml     <- Project configuration file with package metadata for 
│                         traffic_sim_module and configuration for tools like black
│
├── references         <- Data dictionaries, manuals, and all other explanatory materials.
│
├── reports            <- Generated analysis as HTML, PDF, LaTeX, etc.
│   └── figures        <- Generated graphics and figures to be used in reporting
│
├── requirements.txt   <- The requirements file for reproducing the analysis environment, e.g.
│                         generated with `pip freeze > requirements.txt`
│
├── setup.cfg          <- Configuration file for flake8
│
└── traffic_sim_module   <- Source code for use in this project.
    │
    ├── __init__.py             <- Makes traffic_sim_module a Python module
    │
    ├── config.py               <- Store useful variables and configuration
    │
    ├── dataset.py              <- Scripts to download or generate data
    │
    ├── features.py             <- Code to create features for modeling
    │
    ├── modeling                
    │   ├── __init__.py 
    │   ├── predict.py          <- Code to run model inference with trained models          
    │   └── train.py            <- Code to train models
    │
    └── plots.py                <- Code to create visualizations
```

--------

