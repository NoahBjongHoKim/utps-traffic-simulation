# Logging Setup Summary

## What Was Added

### 1. Loguru Dependency
Added `loguru` to `requirements.txt` for advanced logging capabilities.

### 2. Logger Module
Created `traffic_sim_module/utils/logger.py` with:
- **`setup_logger()`** - Initialize logging with file and console output
- **`log_progress()`** - Log progress at regular intervals
- **`log_pipeline_stage()`** - Log pipeline stage transitions
- **`log_file_info()`** - Log file paths and sizes
- **`log_config()`** - Log configuration in readable format

### 3. Pipeline Integration
Updated pipeline files to use logging:
- `main_pipeline.py` - Overall pipeline coordination
- `xml_to_parquet.py` - XML parsing and filtering progress

### 4. Log Directory
Created `logs/` directory where all log files are stored.

## Features

### Dual Output
- **Console**: Colored, simplified format for real-time monitoring
- **File**: Detailed format with timestamps, line numbers, and full context

### Automatic Rotation
- Logs rotate at 100 MB
- Rotated logs are compressed (.zip)
- Logs kept for 30 days

### Progress Tracking
The pipeline now logs:
- **XML Parsing**: Every 10 chunks (~1M events)
- **Filtering**: Every 50 batches
- **File Operations**: Sizes and paths
- **Timing**: Duration of each stage

### Log Levels
- **DEBUG**: Detailed diagnostic information
- **INFO**: Progress updates and general information
- **SUCCESS**: Successful completion of stages
- **WARNING**: Issues that don't stop execution
- **ERROR**: Failures with full tracebacks

## Usage Example

### Running the Pipeline
```bash
# Run pipeline - logging is automatic
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml

# Log file created at: logs/pipeline_v4_morning_rush_YYYYMMDD_HHMMSS.log
```

### Console Output
```
14:30:52 | INFO     | Starting pipeline with config: configs/v4_morning_rush.yaml
14:30:53 | SUCCESS  | Configuration loaded and validated
14:30:53 | INFO     | ================================================================================
14:30:53 | INFO     | PIPELINE CONFIGURATION
14:30:53 | INFO     | ================================================================================
14:30:53 | INFO     | Input Files:
14:30:53 | INFO     |   XML Events: data/raw/v4/events.xml (2456.32 MB)
14:30:54 | INFO     | Loading road network GeoPackage...
14:30:54 | INFO     | Road network loaded: 45,823 links
14:31:12 | INFO     | Parsed 1,000,000 events (10 chunks sent)
14:31:30 | INFO     | Parsed 2,000,000 events (20 chunks sent)
14:32:45 | SUCCESS  | XML parsing complete: 5,234,567 total events processed
14:32:45 | INFO     | Filtering events and writing to Parquet...
14:33:10 | INFO     | Filtered events written: 150,000
14:34:12 | SUCCESS  | Parquet file created: 423,891 filtered events
14:34:12 | SUCCESS  | Step 1 completed in 198.45 seconds (3.3 minutes)
```

### File Log Format
```
2025-11-18 14:30:52 | INFO     | traffic_sim_module.pipelines.main_pipeline:main:150 | Starting pipeline with config: configs/v4_morning_rush.yaml
2025-11-18 14:30:53 | SUCCESS  | traffic_sim_module.pipelines.main_pipeline:main:155 | Configuration loaded and validated
2025-11-18 14:30:54 | INFO     | traffic_sim_module.pipelines.xml_to_parquet:load_valid_link_ids:26 | Loading road network GeoPackage...
```

## Monitoring Logs

### Watch Log in Real-Time
```bash
# Follow the most recent log
tail -f logs/pipeline_*.log

# Filter to INFO and above (no DEBUG)
tail -f logs/pipeline_*.log | grep -v "DEBUG"
```

### Search for Errors
```bash
# Find all errors
grep "ERROR" logs/*.log

# Find warnings
grep "WARNING" logs/*.log
```

### Check Progress
```bash
# See how many events have been processed
tail -f logs/pipeline_*.log | grep "Parsed"

# See filtering progress
tail -f logs/pipeline_*.log | grep "Filtered"
```

### View Specific Log
```bash
# View specific log file
less logs/pipeline_v4_morning_rush_20251118_143052.log

# Search within less: press '/' then type search term
```

## Using Logger in Custom Scripts

If you want to add logging to your own scripts:

```python
from traffic_sim_module.utils.logger import setup_logger, logger, log_progress

# Initialize logger (usually in main function)
setup_logger(log_name="my_script.log")

# Log messages
logger.info("Starting processing...")
logger.debug("Detailed diagnostic info")
logger.warning("Something might be wrong")
logger.error("Something went wrong!")
logger.success("Task completed successfully!")

# Log progress in a loop
total = 10000
for i in range(total):
    # Process item
    log_progress(i, total, step_name="Processing items", interval=1000)
```

## Log File Naming

Log files are named automatically based on the config file:
- Config: `configs/v4_morning_rush.yaml`
- Log: `logs/pipeline_v4_morning_rush_YYYYMMDD_HHMMSS.log`

Example:
```
logs/
├── pipeline_v4_morning_rush_20251118_143052.log
├── pipeline_v4_morning_rush_20251118_143052.log.zip  # Rotated
├── pipeline_v4_evening_rush_20251118_150234.log
└── pipeline_v4_full_day_20251118_163045.log
```

## Customization

To change logging behavior, edit `traffic_sim_module/utils/logger.py`:

```python
# Change log directory
setup_logger(log_dir="my_custom_logs")

# Change rotation size
logger.add(..., rotation="500 MB")  # Rotate at 500 MB

# Change retention period
logger.add(..., retention="60 days")  # Keep for 60 days

# Change console log level
logger.add(sys.stdout, level="DEBUG")  # Show DEBUG in console
```

## Troubleshooting

### No log files created
```bash
# Check logs directory exists
ls -ld logs/

# Create if missing
mkdir -p logs/
```

### Permission denied
```bash
# Fix permissions
chmod 755 logs/
```

### Loguru not installed
```bash
# Install dependencies
pip install -r requirements.txt

# Or install just loguru
pip install loguru
```

## Benefits

1. **Track Progress**: Know exactly where the pipeline is and how much is left
2. **Debug Issues**: Full tracebacks and detailed logging help identify problems
3. **Performance Analysis**: Timing information for each stage
4. **Audit Trail**: Complete record of what was processed and when
5. **File Management**: Easy to find and manage log files by date/config

## Next Steps

The logging is already integrated! Just run your pipeline as normal:

```bash
python -m traffic_sim_module.pipeline.main_pipeline configs/v4_morning_rush.yaml
```

Check `logs/` directory for the log files.
