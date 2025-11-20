# Use Python 3.12 as specified in pyproject.toml
FROM python:3.12-slim

# Set working directory
WORKDIR /app

# Install system dependencies for geospatial libraries
# These are needed for geopandas, shapely, and other geo tools
RUN apt-get update && apt-get install -y \
    gdal-bin \
    libgdal-dev \
    libgeos-dev \
    libproj-dev \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

# Set GDAL environment variables
ENV GDAL_CONFIG=/usr/bin/gdal-config

# Copy requirements and metadata files first for better Docker layer caching
# This way, dependencies are only reinstalled when these files change
COPY requirements.txt .
COPY pyproject.toml .
COPY README.md .

# Copy the module code (needed for -e . installation in requirements.txt)
# Note: We'll also mount this as a volume so you can edit code on the fly
COPY traffic_sim_module/ ./traffic_sim_module/

# Install Python dependencies
# This includes installing the package itself in editable mode via -e .
RUN pip install --no-cache-dir -r requirements.txt

# Copy remaining application code
COPY scripts/ ./scripts/
COPY configs/ ./configs/
COPY docs/ ./docs/

# Create directories for logs and data
# These will be mounted as volumes so data persists
RUN mkdir -p logs data

# Expose port for Streamlit GUI
EXPOSE 8501

# Default command runs the Streamlit GUI
# You can override this to run the pipeline instead
CMD ["streamlit", "run", "scripts/pipeline_gui.py", "--server.address", "0.0.0.0"]