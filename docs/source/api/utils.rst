Utilities Module
================

The utils module provides utility functions and helper classes.

Logger
------

.. automodule:: traffic_sim_module.utils.logger
   :members:
   :undoc-members:
   :show-inheritance:

Network Cache
-------------

.. automodule:: traffic_sim_module.utils.network_cache
   :members:
   :undoc-members:
   :show-inheritance:

Logging Configuration
---------------------

The module uses `loguru` for advanced logging capabilities. Configure logging levels
and output formats:

.. code-block:: python

   from traffic_sim_module.utils.logger import setup_logger

   # Basic setup
   logger = setup_logger(log_level="INFO")

   # With file output
   logger = setup_logger(
       log_level="DEBUG",
       log_file="logs/debug.log",
       rotation="500 MB"
   )

Network Caching
---------------

The network cache provides efficient storage and retrieval of network data:

.. mermaid::

   flowchart LR
       A[Request Data] --> B{Cache Hit?}
       B -->|Yes| C[Return Cached Data]
       B -->|No| D[Load from Source]
       D --> E[Store in Cache]
       E --> F[Return Data]
       C --> G[Use Data]
       F --> G

Usage Example:

.. code-block:: python

   from traffic_sim_module.utils.network_cache import NetworkCache

   cache = NetworkCache(cache_dir="cache/networks")

   # Get or load network data
   network = cache.get_or_load(
       network_id="downtown_grid",
       loader_func=load_network_from_file,
       network_path="networks/downtown.xml"
   )

   # Clear old cache entries
   cache.clear_expired(max_age_days=30)

Best Practices
--------------

**Logging**

* Use appropriate log levels (DEBUG, INFO, WARNING, ERROR)
* Configure log rotation to prevent disk space issues
* Include context information in log messages
* Use structured logging for better analysis

**Caching**

* Set reasonable cache expiration times
* Monitor cache size and implement cleanup strategies
* Use cache versioning for schema changes
* Document cache key formats

See Also
--------

* :doc:`../userguide` - Usage patterns and examples
* :doc:`modules` - Main module reference