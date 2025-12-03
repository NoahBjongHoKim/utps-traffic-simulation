Module Reference
================

This section provides detailed API documentation for all modules in the Traffic Simulation Module package.

traffic_sim_module
------------------

.. automodule:: traffic_sim_module
   :members:
   :undoc-members:
   :show-inheritance:

Configuration
-------------

.. automodule:: traffic_sim_module.config
   :members:
   :undoc-members:
   :show-inheritance:

Module Overview
---------------

The ``traffic_sim_module`` package provides the following main components:

.. mermaid::

   graph TB
       A[traffic_sim_module] --> B[pipeline]
       A --> C[utils]
       A --> D[config]
       B --> E[xml_to_parquet]
       B --> F[parquet_to_animation]
       B --> G[parquet_to_heatmap]
       B --> H[main_pipeline]
       C --> I[logger]
       C --> J[network_cache]

See Also
--------

* :doc:`pipeline` - Pipeline processing modules
* :doc:`utils` - Utility functions and helpers