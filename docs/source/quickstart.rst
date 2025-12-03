Quickstart
==========

This guide will help you get started with the Traffic Simulation Module quickly.

Basic Workflow
--------------

The typical workflow consists of three main steps:

1. **Convert XML to Parquet**: Transform raw XML simulation output
2. **Process Data**: Apply transformations and analysis
3. **Generate Visualizations**: Create animations or heatmaps

.. mermaid::

   sequenceDiagram
       participant User
       participant Pipeline
       participant XML2Parquet
       participant Visualizer

       User->>Pipeline: Start Processing
       Pipeline->>XML2Parquet: Convert Data
       XML2Parquet-->>Pipeline: Parquet Files
       Pipeline->>Visualizer: Generate Output
       Visualizer-->>User: Animations/Heatmaps

Running the Pipeline
--------------------

Using the GUI
~~~~~~~~~~~~~

The easiest way to run the pipeline is through the Streamlit GUI:

.. code-block:: bash

   streamlit run scripts/pipeline_gui.py

This opens a web interface where you can:

* Select input XML files
* Configure processing parameters
* Choose output formats
* Monitor progress in real-time

Using Python Code
~~~~~~~~~~~~~~~~~

For programmatic access:

.. code-block:: python

   from traffic_sim_module.pipeline.main_pipeline import run_pipeline

   # Configure and run the pipeline
   result = run_pipeline(
       xml_path="data/simulation_output.xml",
       output_dir="output/",
       generate_animation=True,
       generate_heatmap=True
   )

Configuration
-------------

The module uses configuration files in the ``configs/`` directory. You can customize:

* Processing parameters
* Visualization settings
* Logging preferences
* Caching behavior

See :doc:`usage` for detailed configuration options.

Next Steps
----------

* Learn about :doc:`usage` patterns
* Explore the :doc:`api/modules` API reference
* Read about :doc:`reference/caching` for performance optimization