Pipeline Module
===============

The pipeline module contains the core data processing components.

Main Pipeline
-------------

.. automodule:: traffic_sim_module.pipeline.main_pipeline
   :members:
   :undoc-members:
   :show-inheritance:

XML to Parquet
--------------

.. automodule:: traffic_sim_module.pipeline.xml_to_parquet
   :members:
   :undoc-members:
   :show-inheritance:

Parquet to Animation
--------------------

.. automodule:: traffic_sim_module.pipeline.parquet_to_animation
   :members:
   :undoc-members:
   :show-inheritance:

Parquet to Heatmap
------------------

.. automodule:: traffic_sim_module.pipeline.parquet_to_heatmap
   :members:
   :undoc-members:
   :show-inheritance:

Pipeline Flow
-------------

The pipeline follows this general flow:

.. mermaid::

   flowchart TD
       A[Start Pipeline] --> B{Input Type}
       B -->|XML| C[XML to Parquet]
       B -->|Parquet| D[Load Data]
       C --> D
       D --> E{Output Type}
       E -->|Animation| F[Generate Animation]
       E -->|Heatmap| G[Generate Heatmap]
       E -->|Both| H[Generate Both]
       F --> I[Save Output]
       G --> I
       H --> I
       I --> J[End Pipeline]

Examples
--------

Basic Pipeline Usage
~~~~~~~~~~~~~~~~~~~~

.. code-block:: python

   from traffic_sim_module.pipeline.main_pipeline import run_pipeline

   # Run complete pipeline
   run_pipeline(
       input_path="data/simulation.xml",
       output_dir="output/",
       generate_animation=True,
       generate_heatmap=True
   )

Individual Components
~~~~~~~~~~~~~~~~~~~~~

.. code-block:: python

   from traffic_sim_module.pipeline.xml_to_parquet import xml_to_parquet
   from traffic_sim_module.pipeline.parquet_to_animation import generate_animation

   # Step 1: Convert XML
   parquet_file = xml_to_parquet("simulation.xml", "data.parquet")

   # Step 2: Generate animation
   generate_animation(parquet_file, "animation.html")