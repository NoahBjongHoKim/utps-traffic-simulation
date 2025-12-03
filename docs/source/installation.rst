Installation
============

Requirements
------------

* Python 3.12.0
* pip package manager

Basic Installation
------------------

Clone the repository and install dependencies:

.. code-block:: bash

   git clone <repository-url>
   cd utps-ts-repo
   pip install -r requirements.txt

The package will be installed in editable mode, making the ``traffic_sim_module``
importable throughout your Python environment.

Docker Installation
-------------------

For containerized deployment, see the :doc:`reference/docker` guide.

Dependencies
------------

The main dependencies include:

* **geopandas**: Geographic data handling
* **pandas**: Data manipulation and analysis
* **pyarrow**: Efficient columnar data storage
* **pydantic**: Data validation
* **streamlit**: Web-based GUI
* **loguru**: Advanced logging

For the complete list, see ``requirements.txt`` in the project root.

Verifying Installation
----------------------

To verify the installation, try importing the module:

.. code-block:: python

   import traffic_sim_module
   print(traffic_sim_module.__file__)