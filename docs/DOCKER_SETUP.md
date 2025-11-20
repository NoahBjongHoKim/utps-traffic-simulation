# Docker Setup Guide for UTPS Traffic Simulation

A complete beginner-friendly guide to running this application in Docker on Mac and Windows.

## Table of Contents
- [What is Docker?](#what-is-docker)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Understanding the Setup](#understanding-the-setup)
- [Common Tasks](#common-tasks)
- [Troubleshooting](#troubleshooting)
- [Development Workflow](#development-workflow)

---

## What is Docker?

Think of Docker as a **portable container** for your application:

- **Image**: Like a recipe - contains all the instructions to build your app environment
- **Container**: Like a running instance of that recipe - your actual application running
- **Volume**: Like a shared folder between your computer and the container
- **Docker Compose**: Like a manager that handles multiple containers easily

**Why use Docker?**
- ✅ Works the same on Mac, Windows, and Linux
- ✅ No "works on my machine" problems
- ✅ Easy to share and deploy
- ✅ Isolated from your system (won't mess up your Python installation)
- ✅ Easy to start fresh (just rebuild the container)

---

## Prerequisites

### 1. Install Docker

**For Mac:**
1. Download [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop)
2. Install and start Docker Desktop
3. You'll see a whale icon in your menu bar when it's running

**For Windows:**
1. Download [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop)
2. Install and start Docker Desktop
3. Make sure WSL 2 is enabled (Docker will prompt you)
4. You'll see a whale icon in your system tray when it's running

**Verify installation:**
```bash
docker --version
docker-compose --version
```

### 2. Prepare Your Data Directory

Docker will **mount** (share) your data directory, not copy it into the container. This is important because:
- Your data is too large to include in the Docker image
- You want to access the same data from outside Docker
- Processing results should be saved on your actual computer

Make sure your data is organized like this:
```
utps-ts-repo/
├── data/
│   ├── raw/
│   │   ├── v4/
│   │   │   ├── events.xml
│   │   │   └── network.gpkg
│   ├── interim/
│   ├── processed/
│   └── external/
```

---

## Quick Start

### Option 1: Run with Docker Compose (Recommended)

This is the easiest way! Docker Compose handles everything for you.

```bash
# Navigate to the project directory
cd /path/to/utps-ts-repo

# Build and start the application
docker-compose up --build

# The Streamlit GUI will be available at:
# http://localhost:8501
```

**What just happened?**
1. Docker built an image from your Dockerfile (this includes Python, all libraries, etc.)
2. Docker created a container from that image
3. Docker mounted your local folders (data, configs, logs, code) into the container
4. Docker started the Streamlit GUI inside the container
5. Docker mapped port 8501 so you can access it from your browser

**To stop:**
```bash
# Press Ctrl+C in the terminal, then:
docker-compose down
```

### Option 2: Run with Docker Commands (Manual)

If you want more control or don't want to use Docker Compose:

```bash
# Build the image
docker build -t utps-traffic-sim .

# Run the container
docker run -d \
  --name traffic-sim \
  -p 8501:8501 \
  -v "$(pwd)/data:/app/data" \
  -v "$(pwd)/configs:/app/configs" \
  -v "$(pwd)/logs:/app/logs" \
  -v "$(pwd)/traffic_sim_module:/app/traffic_sim_module" \
  -v "$(pwd)/scripts:/app/scripts" \
  utps-traffic-sim

# View logs
docker logs -f traffic-sim

# Stop the container
docker stop traffic-sim

# Remove the container
docker rm traffic-sim
```

---

## Understanding the Setup

### File Overview

**Dockerfile**
- The "recipe" for building your application environment
- Specifies Python 3.12, installs system dependencies, copies code
- Like a script that sets up a fresh computer with everything you need

**docker-compose.yml**
- A configuration file that makes running Docker easier
- Defines services (like your app), ports, volumes, and environment variables
- One command (`docker-compose up`) does everything

**.dockerignore**
- Tells Docker what NOT to copy into the image
- Keeps the image small and builds fast
- Like `.gitignore` but for Docker

### Volume Mounts Explained

The most important concept for your workflow! Volumes connect your computer's folders to folders inside the container:

```yaml
volumes:
  - ./data:/app/data          # Your data → Container's data
  - ./configs:/app/configs    # Your configs → Container's configs
  - ./logs:/app/logs          # Your logs → Container's logs
  - ./traffic_sim_module:/app/traffic_sim_module  # Your code → Container's code
```

**What this means:**
- ✅ Edit Python files on your Mac/Windows → Changes appear instantly in the container
- ✅ Add new configs → Available immediately in the container
- ✅ Container writes logs → You see them in your logs folder
- ✅ Process data → Results saved to your data folder

**No need to rebuild the container when you edit code!**

### Ports Explained

```yaml
ports:
  - "8501:8501"
```

This means:
- Left side (8501): Port on **your computer** (Mac/Windows)
- Right side (8501): Port **inside the container**
- When you visit `http://localhost:8501` in your browser, it connects to port 8501 in the container

---

## Common Tasks

### Running the Streamlit GUI

**Start:**
```bash
docker-compose up
```

**Access:**
Open your browser and go to `http://localhost:8501`

**Stop:**
Press `Ctrl+C` in the terminal

### Running the Pipeline Directly (Without GUI)

You can override the default command to run the pipeline directly:

```bash
docker-compose run --rm traffic-sim \
  python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

Explanation:
- `docker-compose run` - Run a one-off command in a new container
- `--rm` - Remove the container when done
- `traffic-sim` - The service name from docker-compose.yml
- The rest is the Python command you want to run

### Running Python Scripts

**Interactive Python shell:**
```bash
docker-compose run --rm traffic-sim python
```

**Run a specific script:**
```bash
docker-compose run --rm traffic-sim python scripts/my_script.py
```

**Run a Jupyter notebook:**
```bash
docker-compose --profile jupyter up jupyter

# Then open: http://localhost:8888
```

### Viewing Logs

**Real-time logs:**
```bash
docker-compose logs -f
```

**Just show the last 100 lines:**
```bash
docker-compose logs --tail=100
```

**Check container logs folder:**
Your `logs/` directory is mounted, so just check it normally on your computer!

### Rebuilding After Changes

**When to rebuild:**
- After changing `Dockerfile`
- After changing `requirements.txt`
- After installing new system dependencies

**Don't need to rebuild for:**
- ✅ Editing Python code (volumes handle this)
- ✅ Changing configs
- ✅ Adding new data files

**How to rebuild:**
```bash
docker-compose up --build
```

Or manually:
```bash
docker-compose down
docker-compose build --no-cache
docker-compose up
```

### Editing Code and Seeing Changes

**For most code changes (in mounted volumes):**
1. Edit the Python file on your computer (Mac/Windows)
2. Save the file
3. Restart the container: `docker-compose restart`
4. Changes are live!

For Streamlit specifically, it auto-reloads in many cases, so you might not even need to restart!

### Accessing the Container Shell

Sometimes you want to "go inside" the container:

```bash
docker-compose exec traffic-sim bash
```

Now you're inside the container! You can:
- Run commands directly
- Inspect files
- Debug issues
- Run Python interactively

Exit with `exit` or `Ctrl+D`.

---

## Troubleshooting

### Docker Daemon Not Running

**Error:** `Cannot connect to the Docker daemon`

**Solution:** Make sure Docker Desktop is running. You should see the whale icon in your menu bar (Mac) or system tray (Windows).

### Port Already in Use

**Error:** `port is already allocated`

**Solution:**
```bash
# Find what's using the port
# Mac/Linux:
lsof -i :8501

# Windows (PowerShell):
Get-Process -Id (Get-NetTCPConnection -LocalPort 8501).OwningProcess

# Then either stop that process or change the port in docker-compose.yml:
ports:
  - "8502:8501"  # Use 8502 on your computer instead
```

### Permission Denied (Especially on Windows)

**Error:** Permission errors when accessing volumes

**Solution:**
1. Make sure Docker Desktop has permission to access your drives
2. In Docker Desktop → Settings → Resources → File Sharing
3. Add your project directory

### Container Exits Immediately

**Check logs:**
```bash
docker-compose logs
```

Common causes:
- Python error in your code
- Missing dependency
- Wrong path in config

### Volume Data Not Showing Up

**Issue:** Container can't see your data files

**Check:**
1. Are you running `docker-compose up` from the correct directory?
2. Is your data in the right place? (`./data` relative to docker-compose.yml)
3. Try absolute paths in docker-compose.yml:
   ```yaml
   volumes:
     - /Users/yourname/Documents/UTPS/Traffic_Sim/utps-ts-repo/data:/app/data
   ```

### Slow Performance on Mac

**Issue:** File access is slow

**Solution:** This is a known Docker Desktop for Mac issue. Options:
1. Use Docker's new VirtioFS (Docker Desktop → Preferences → Experimental Features)
2. Keep large data operations inside the container (don't mount huge files)
3. Consider Docker alternatives like Colima

### Rebuilds Take Too Long

**Solution:**
```bash
# Use Docker's build cache:
docker-compose build

# If cache is causing issues, clear it:
docker-compose build --no-cache

# Remove old images to free space:
docker system prune -a
```

### Out of Disk Space

**Check Docker disk usage:**
```bash
docker system df
```

**Clean up:**
```bash
# Remove stopped containers
docker container prune

# Remove unused images
docker image prune -a

# Remove unused volumes
docker volume prune

# Nuclear option (removes everything):
docker system prune -a --volumes
```

---

## Development Workflow

### Daily Workflow Example

1. **Start your day:**
   ```bash
   cd utps-ts-repo
   docker-compose up
   ```

2. **Open the GUI in your browser:**
   `http://localhost:8501`

3. **Edit code on your computer:**
   - Open `traffic_sim_module/pipelines/main_pipeline.py` in your favorite editor
   - Make changes
   - Save

4. **See changes:**
   - For most changes: `docker-compose restart`
   - For Streamlit: Usually auto-reloads!

5. **Test with different data:**
   - Add new data to `data/raw/`
   - It's immediately available in the container

6. **Check logs:**
   - View in `logs/` folder on your computer
   - Or: `docker-compose logs -f`

7. **End of day:**
   ```bash
   Ctrl+C
   docker-compose down
   ```

### Adding New Python Dependencies

1. **Edit `requirements.txt`** on your computer:
   ```txt
   # Add your new library
   my-new-library==1.0.0
   ```

2. **Rebuild the container:**
   ```bash
   docker-compose down
   docker-compose up --build
   ```

### Adding New Data Preprocessing

Let's say you want to add preprocessing for a new data type:

1. **Edit the code** (e.g., `traffic_sim_module/utils/preprocessing.py`) on your computer

2. **Restart the container:**
   ```bash
   docker-compose restart
   ```

3. **Test immediately** - your changes are live!

4. **No need to rebuild** - the code is mounted as a volume

### Testing Different Configurations

1. **Create a new config** in `configs/my_new_test.yaml` on your computer

2. **Run it:**
   - Via GUI: Refresh the page, select your config
   - Via command:
     ```bash
     docker-compose run --rm traffic-sim \
       python -m traffic_sim_module.pipeline.main_pipeline configs/my_new_test.yaml
     ```

### Switching Between Mac and Windows

Your Docker setup is identical on both! Just:

1. **On Mac:**
   ```bash
   cd /Users/you/Documents/UTPS/Traffic_Sim/utps-ts-repo
   docker-compose up
   ```

2. **On Windows:**
   ```bash
   cd C:\Users\You\Documents\UTPS\Traffic_Sim\utps-ts-repo
   docker-compose up
   ```

The same Docker image works on both!

---

## Advanced Tips

### Running in Background (Detached Mode)

```bash
docker-compose up -d
```

The container runs in the background. Check logs with:
```bash
docker-compose logs -f
```

### Resource Limits

If you want to limit CPU/memory:

Edit `docker-compose.yml`:
```yaml
deploy:
  resources:
    limits:
      cpus: '2'
      memory: 4G
```

### Multiple Environments

You can have different docker-compose files:

```bash
# Development
docker-compose -f docker-compose.dev.yml up

# Production
docker-compose -f docker-compose.prod.yml up
```

### Jupyter Lab Access

Start Jupyter Lab:
```bash
docker-compose --profile jupyter up jupyter
```

Access at: `http://localhost:8888`

---

## Quick Reference

| Task | Command |
|------|---------|
| Start application | `docker-compose up` |
| Start in background | `docker-compose up -d` |
| Stop application | `docker-compose down` |
| Restart after code change | `docker-compose restart` |
| Rebuild after dependency change | `docker-compose up --build` |
| View logs | `docker-compose logs -f` |
| Access container shell | `docker-compose exec traffic-sim bash` |
| Run pipeline directly | `docker-compose run --rm traffic-sim python -m traffic_sim_module.pipelines.main_pipeline CONFIG_PATH` |
| Clean up everything | `docker-compose down -v` |
| Remove old images | `docker system prune -a` |

---

## Summary

**What you've set up:**
- ✅ A portable, reproducible environment that works on Mac and Windows
- ✅ Easy code editing - just edit files on your computer, changes appear in Docker
- ✅ Data mounting - your large data files stay on your computer but are accessible in Docker
- ✅ GUI access via browser
- ✅ Command-line access for running pipelines

**Key points to remember:**
1. Your **data** is never copied into Docker - it's mounted
2. Your **code** is mounted - edit on your computer, see changes immediately
3. You only need to **rebuild** when you change dependencies or the Dockerfile
4. **Works identically** on Mac and Windows

**Need help?**
- Docker docs: https://docs.docker.com/
- Docker Compose docs: https://docs.docker.com/compose/
- Streamlit docs: https://docs.streamlit.io/

Happy Dockering! 🐳