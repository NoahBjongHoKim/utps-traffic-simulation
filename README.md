# UTPS Traffic Simulation - Quick Setup Guide

This guide is for setting up the UTPS Traffic Simulation application using Docker.

## Prerequisites

1. **Install Docker Desktop**
   - **Mac**: Download from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop)
   - **Windows**: Download from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop)
   - Make sure Docker is running (whale icon in menu bar/system tray)

2. **Verify Docker is installed**:
   ```bash
   docker --version
   docker-compose --version
   ```

## Setup Instructions

### If You Received the Source Code

**Step 1: Extract the files**
```bash
# Extract the zip/bundle you received
unzip utps-traffic-sim.zip
cd utps-ts-repo
```

**Step 2: Add your data**
```bash
# Create the data directory structure
mkdir -p data/raw

# Copy your data files into data/raw
# You need:
#   - events.xml (or events.xml.gz)
#   - network.gpkg (or similar network file)
```

**Step 3: Build and run**
```bash
docker-compose up --build
```

**Step 4: Open the GUI**

Open your browser and go to: **http://localhost:8501**

---

### If You Received a Docker Hub Image Link

**Step 1: Create project directory**
```bash
mkdir utps-traffic-sim
cd utps-traffic-sim
```

**Step 2: Download the docker-compose file**

Create a file named `docker-compose.yml` with the following content:

```yaml
version: '3.8'

services:
  traffic-sim:
    image: noahbjonghokim/utps-traffic-sim:latest  # Replace with actual image name

    container_name: utps-traffic-sim

    ports:
      - "8501:8501"

    volumes:
      - ./data:/app/data
      - ./configs:/app/configs
      - ./logs:/app/logs

    environment:
      - PYTHONUNBUFFERED=1

    restart: unless-stopped
```

**Step 3: Create directories and add data**
```bash
# Create directories
mkdir -p data/raw
mkdir -p configs
mkdir -p logs

# Copy your data files into data/raw/v4/
```

**Step 4: Download the application config files**

You'll need to get the `configs` folder from your friend. Copy all `.yaml` files into the `configs/` directory.

**Step 5: Run the application**
```bash
docker-compose up
```

**Step 6: Open the GUI**

Open your browser and go to: **http://localhost:8501**

---

## Data Requirements

Your data should be organized like this:

```
data/
├── raw/
│   └── v4/
│       ├── events.xml.gz          # MATSim events file
│       └── road_network_v4_clipped.gpkg  # Road network
├── interim/      # Created automatically
├── processed/    # Output goes here
└── external/     # Optional
```

**Supported formats:**
- Events: `.xml` or `.xml.gz` (compressed)
- Network: `.gpkg` (GeoPackage) or `.shp` (Shapefile)

---

## Using the Application

### Via Web GUI (Recommended)

1. **Access GUI**: http://localhost:8501
2. **Select mode**:
   - "Create/Edit Config" - Build new configurations
   - "Run Pipeline" - Run existing configs
   - "Manage Presets" - Browse available configs

3. **Run a pipeline**:
   - Go to "Run Pipeline" mode
   - Select a config (e.g., "v4_morning_rush.yaml")
   - Click "Run Pipeline"
   - Watch the logs in real-time

### Via Command Line

Run a pipeline directly:
```bash
docker-compose run --rm traffic-sim \
  python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

---

## Common Tasks

### Start the application
```bash
docker-compose up
```

### Start in background
```bash
docker-compose up -d
```

### Stop the application
```bash
docker-compose down
```

### View logs
```bash
docker-compose logs -f
```

### Restart after making changes
```bash
docker-compose restart
```

### Update to latest version (if using Docker Hub)
```bash
docker-compose pull
docker-compose up
```

---

## Troubleshooting

### Port 8501 already in use
```bash
# Option 1: Stop the other application using port 8501

# Option 2: Change the port in docker-compose.yml:
ports:
  - "8502:8501"  # Use 8502 instead
```

### Can't see my data
1. Make sure Docker Desktop is running
2. Check that data is in the correct location: `data/raw/v4/`
3. Verify file permissions (should be readable)

### Docker daemon not running
- Start Docker Desktop
- Wait for the whale icon to appear in menu bar/system tray

### Permission errors (Windows)
1. Open Docker Desktop
2. Go to Settings → Resources → File Sharing
3. Add your project directory
4. Restart Docker Desktop

---

## Getting Help

**Check logs:**
```bash
# Container logs
docker-compose logs

# Application logs
# Check the logs/ directory on your computer
```

**Access container shell:**
```bash
docker-compose exec traffic-sim bash
```

**Full documentation:**
- See DOCKER_SETUP.md (if included) for detailed Docker guide
- See README.md for application documentation

---

## Sharing Your Results

Your processed data will be in:
```
data/processed/
```

These are regular files on your computer (not inside Docker), so you can:
- Copy them normally
- Open them in GIS software (QGIS, ArcGIS)
- Share them with others

---

## Updating the Application

### If you have source code:
```bash
# Pull latest changes (if using Git)
git pull

# Rebuild
docker-compose up --build
```

### If using Docker Hub image:
```bash
# Pull latest image
docker-compose pull

# Restart
docker-compose up
```

---

## System Requirements

**Minimum:**
- 4 GB RAM
- 10 GB free disk space
- Docker Desktop installed

**Recommended:**
- 8 GB RAM or more
- 50 GB free disk space (for large datasets)
- SSD for better performance

---

## Need More Help?

Contact the person who shared this with you, or check:
- Docker documentation: https://docs.docker.com/
- Streamlit documentation: https://docs.streamlit.io/