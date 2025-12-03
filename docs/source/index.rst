.. Traffic Simulation Module documentation master file

Traffic Simulation Module
==========================

Welcome to the Traffic Simulation Module documentation! This package processes simulation data
results from Swiss AI traffic simulations, providing tools for data transformation, analysis,
and visualization.

.. mermaid::

   graph LR
       A[XML Input] --> B[XML to Parquet]
       B --> C[Parquet Data]
       C --> D[Animation Generator]
       C --> E[Heatmap Generator]
       D --> F[Interactive Animation]
       E --> G[Heatmap Visualization]

Features
--------

* **Data Processing**: Convert XML simulation output to efficient Parquet format
* **Visualization**: Generate animations and heatmaps from simulation data
* **Network Caching**: Efficient caching system for network data
* **Pipeline Integration**: Streamlined data processing pipeline
* **GUI Interface**: Streamlit-based graphical interface for easy interaction

Documentation
-------------

.. toctree::
   :maxdepth: 2
   :caption: User Guide

   userguide

.. toctree::
   :maxdepth: 2
   :caption: API Reference

   api/modules
   api/pipeline
   api/utils

