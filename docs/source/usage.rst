Usage Guide
===========

This guide covers common usage patterns and advanced features.

Pipeline Components
-------------------

XML to Parquet Conversion
~~~~~~~~~~~~~~~~~~~~~~~~~~

Convert XML simulation output to Parquet format:

.. code-block:: python

   from traffic_sim_module.pipeline.xml_to_parquet import xml_to_parquet

   xml_to_parquet(
       input_path="simulation.xml",
       output_path="output.parquet"
   )

Animation Generation
~~~~~~~~~~~~~~~~~~~~

Create interactive animations from Parquet data:

.. code-block:: python

   from traffic_sim_module.pipeline.parquet_to_animation import generate_animation

   generate_animation(
       parquet_path="data.parquet",
       output_path="animation.html"
   )

Heatmap Generation
~~~~~~~~~~~~~~~~~~

Generate heatmaps for traffic analysis:

.. code-block:: python

   from traffic_sim_module.pipeline.parquet_to_heatmap import generate_heatmap

   generate_heatmap(
       parquet_path="data.parquet",
       output_path="heatmap.html",
       time_interval="1h"
   )

Configuration Management
------------------------

The module uses configuration files for flexible setup:

.. code-block:: python

   from traffic_sim_module.config import load_config

   config = load_config("configs/default.yaml")
   # Use config settings

Network Caching
---------------

For improved performance, enable network caching:

.. code-block:: python

   from traffic_sim_module.utils.network_cache import NetworkCache

   cache = NetworkCache(cache_dir="cache/")
   network_data = cache.get_or_load("network_id")

See :doc:`reference/caching` for detailed information.

Logging
-------

Configure logging behavior:

.. code-block:: python

   from traffic_sim_module.utils.logger import setup_logger

   logger = setup_logger(
       log_level="INFO",
       log_file="logs/app.log"
   )

Docker Deployment
-----------------

Run the module in a Docker container:

.. code-block:: bash

   docker-compose up

For detailed Docker setup, see :doc:`reference/docker`.

Best Practices
--------------

1. **Use Caching**: Enable network caching for repeated analyses
2. **Configure Logging**: Set appropriate log levels for debugging
3. **Batch Processing**: Process multiple simulations in parallel when possible
4. **Resource Management**: Monitor memory usage for large datasets
5. **Version Control**: Track configuration changes in version control

Troubleshooting
---------------

Common issues and solutions:

**Import Errors**
   Ensure the package is installed in editable mode: ``pip install -e .``

**Memory Issues**
   Process data in chunks for large XML files

**Slow Performance**
   Enable network caching and use Parquet format for intermediate data

For more help, see :doc:`reference/quick`.