"""
Logging configuration using loguru.

Provides structured logging with automatic file rotation and progress tracking.
"""

import sys
from pathlib import Path
from datetime import datetime
from loguru import logger


def setup_logger(log_dir: str | Path = "logs", log_name: str = None) -> None:
    """
    Configure loguru logger with file and console output.

    Args:
        log_dir: Directory to store log files (default: "logs")
        log_name: Name for the log file (default: timestamp-based)
    """
    log_dir = Path(log_dir)
    log_dir.mkdir(exist_ok=True)

    # Remove default handler
    logger.remove()

    # Console handler with nice formatting
    logger.add(
        sys.stdout,
        format="<green>{time:HH:mm:ss}</green> | <level>{level: <8}</level> | <level>{message}</level>",
        level="INFO",
        colorize=True,
    )

    # File handler with detailed formatting
    if log_name is None:
        log_name = f"pipeline_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

    log_file = log_dir / log_name

    logger.add(
        log_file,
        format="{time:YYYY-MM-DD HH:mm:ss} | {level: <8} | {name}:{function}:{line} | {message}",
        level="DEBUG",
        rotation="100 MB",  # Rotate when file reaches 100MB
        retention="30 days",  # Keep logs for 30 days
        compression="zip",  # Compress rotated logs
    )

    logger.info(f"Logging initialized. Log file: {log_file}")


def log_progress(
    current: int,
    total: int,
    step_name: str = "Processing",
    interval: int = 10000
) -> None:
    """
    Log progress at regular intervals.

    Args:
        current: Current item count
        total: Total item count
        step_name: Name of the processing step
        interval: Log every N items
    """
    if current % interval == 0 or current == total:
        percentage = (current / total * 100) if total > 0 else 0
        logger.info(f"{step_name}: {current:,} / {total:,} ({percentage:.1f}%)")


def log_pipeline_stage(stage_name: str, stage_number: int = None) -> None:
    """
    Log the start of a pipeline stage with emphasis.

    Args:
        stage_name: Name of the pipeline stage
        stage_number: Optional stage number
    """
    separator = "=" * 80
    if stage_number is not None:
        message = f"STAGE {stage_number}: {stage_name}"
    else:
        message = stage_name

    logger.info(separator)
    logger.info(message)
    logger.info(separator)


def log_file_info(file_path: str | Path, description: str = "File") -> None:
    """
    Log information about a file (path, size, existence).

    Args:
        file_path: Path to the file
        description: Description of the file's purpose
    """
    file_path = Path(file_path)

    if file_path.exists():
        size_mb = file_path.stat().st_size / (1024 * 1024)
        logger.info(f"{description}: {file_path} ({size_mb:.2f} MB)")
    else:
        logger.warning(f"{description} does not exist: {file_path}")


def log_config(config_dict: dict) -> None:
    """
    Log configuration parameters in a readable format.

    Args:
        config_dict: Configuration dictionary to log
    """
    logger.info("Configuration:")
    for key, value in config_dict.items():
        if isinstance(value, dict):
            logger.info(f"  {key}:")
            for sub_key, sub_value in value.items():
                logger.info(f"    {sub_key}: {sub_value}")
        else:
            logger.info(f"  {key}: {value}")


# Export logger instance for direct use
__all__ = ["logger", "setup_logger", "log_progress", "log_pipeline_stage", "log_file_info", "log_config"]
