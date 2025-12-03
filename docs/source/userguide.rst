User Guide
==========

This guide covers installation, setup, and running the Traffic Simulation Module.

Installation
------------

**Requirements**

* Python 3.12.0
* Docker Desktop (for containerized deployment)

**Option 1: Local Installation**

.. code-block:: bash

   git clone <repository-url>
   cd utps-ts-repo
   pip install -r requirements.txt

The package installs in editable mode, making ``traffic_sim_module`` available system-wide.

**Option 2: Docker Installation**

.. code-block:: bash

   # Build and start
   docker-compose up --build

   # Or pull from Docker Hub
   docker-compose pull
   docker-compose up

Setup
-----

**Data Structure**

Organize your data as follows:

.. code-block:: text

   data/
   ├── raw/          # Place XML and network files here
   ├── interim/      # Auto-generated temporary files
   ├── processed/    # Pipeline outputs
   └── external/     # Optional additional data

**Supported Formats**

* Events: ``.xml`` or ``.xml.gz``
* Network: ``.gpkg`` (GeoPackage) or ``.shp`` (Shapefile)

Running the Application
-----------------------

**Using the GUI (Recommended)**

Local:

.. code-block:: bash

   streamlit run scripts/pipeline_gui.py

Docker:

.. code-block:: bash

   docker-compose up

Then open http://localhost:8501 in your browser.

The GUI provides three modes:

* **Create/Edit Config**: Build new configuration files
* **Run Pipeline**: Execute existing configurations
* **Manage Presets**: Browse available configurations

**Using Python**

.. code-block:: python

   from traffic_sim_module.pipeline.main_pipeline import run_pipeline

   run_pipeline(
       xml_path="data/simulation.xml",
       output_dir="output/",
       generate_animation=True,
       generate_heatmap=True
   )

**Using Configuration Files**

.. code-block:: bash

   # Local
   python -m traffic_sim_module.pipeline.main_pipeline configs/your_config.yaml

   # Docker
   docker-compose run --rm traffic-sim \
     python -m traffic_sim_module.pipeline.main_pipeline configs/your_config.yaml

Pipeline Components
-------------------

**XML to Parquet Conversion**

.. code-block:: python

   from traffic_sim_module.pipeline.xml_to_parquet import xml_to_parquet

   xml_to_parquet(
       input_path="simulation.xml",
       output_path="output.parquet"
   )

**Generate Animation**

.. code-block:: python

   from traffic_sim_module.pipeline.parquet_to_animation import generate_animation

   generate_animation(
       parquet_path="data.parquet",
       output_path="animation.html"
   )

**Generate Heatmap**

.. code-block:: python

   from traffic_sim_module.pipeline.parquet_to_heatmap import generate_heatmap

   generate_heatmap(
       parquet_path="data.parquet",
       output_path="heatmap.html",
       time_interval="1h"
   )

Configuration
-------------

**Loading Configurations**

.. code-block:: python

   from traffic_sim_module.config import load_config

   config = load_config("configs/default.yaml")

**Logging**

.. code-block:: python

   from traffic_sim_module.utils.logger import setup_logger

   logger = setup_logger(
       log_level="INFO",
       log_file="logs/app.log"
   )

**Network Caching**

Enable caching for better performance:

.. code-block:: python

   from traffic_sim_module.utils.network_cache import NetworkCache

   cache = NetworkCache(cache_dir="cache/")
   network_data = cache.get_or_load("network_id")

Docker Commands
---------------

.. code-block:: bash

   # Start
   docker-compose up

   # Start in background
   docker-compose up -d

   # Stop
   docker-compose down

   # View logs
   docker-compose logs -f

   # Rebuild after changes
   docker-compose up --build

   # Update to latest
   docker-compose pull && docker-compose up

Troubleshooting
---------------

**Import Errors**

Ensure package is installed: ``pip install -e .``

**Memory Issues**

Process large XML files in chunks or use Parquet format.

**Slow Performance**

Enable network caching and use Parquet for intermediate data.

**Port 8501 Already in Use**

Change port in docker-compose.yml:

.. code-block:: yaml

   ports:
     - "8502:8501"

**Docker Daemon Not Running**

Start Docker Desktop and wait for it to initialize.