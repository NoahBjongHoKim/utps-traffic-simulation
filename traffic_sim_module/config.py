"""
Configuration module for traffic simulation pipeline.

This module sets up global configuration including logging with loguru.
"""

from pathlib import Path
from loguru import logger

# Project paths
PROJECT_ROOT = Path(__file__).parent.parent
LOGS_DIR = PROJECT_ROOT / "logs"
DATA_DIR = PROJECT_ROOT / "data"

# Ensure directories exist
LOGS_DIR.mkdir(exist_ok=True)
DATA_DIR.mkdir(exist_ok=True)

# Configure loguru with both file and console output
# Remove default handler (if it exists)
try:
    logger.remove(0)
except ValueError:
    pass  # Handler 0 doesn't exist, that's fine

# Console handler - with tqdm support if available
try:
    from tqdm import tqdm
    logger.add(lambda msg: tqdm.write(msg, end=""), colorize=True, level="INFO")
except ModuleNotFoundError:
    logger.add(lambda msg: print(msg, end=""), colorize=True, level="INFO")

# File handler - logs everything to a file with rotation
logger.add(
    LOGS_DIR / "pipeline_{time:YYYY-MM-DD}.log",
    rotation="00:00",  # New file at midnight
    retention="30 days",  # Keep logs for 30 days
    level="INFO",  # Log everything to file
    format="{time:YYYY-MM-DD HH:mm:ss} | {level: <8} | {name}:{function}:{line} - {message}",
    backtrace=True,
    diagnose=True,
)

logger.info("Logging initialized")
