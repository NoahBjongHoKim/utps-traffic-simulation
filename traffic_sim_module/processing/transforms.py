"""
Coordinate and time transformation utilities.

Functions for converting between different time representations
and coordinate systems.
"""

from datetime import datetime, timedelta


def time_to_timestamp(seconds: int | float, base_date: datetime = None) -> str:
    """
    Convert seconds since midnight to a formatted timestamp string.

    Args:
        seconds: Seconds since midnight
        base_date: Base date to use (default: 2024-01-01)

    Returns:
        Formatted timestamp string (YYYY/MM/DD HH:MM:SS)
    """
    if base_date is None:
        base_date = datetime(2024, 1, 1)

    return (base_date + timedelta(seconds=int(seconds))).strftime('%Y/%m/%d %H:%M:%S')


def timestamp_to_time(stamp: str) -> int:
    """
    Convert a formatted timestamp string to seconds since midnight.

    Args:
        stamp: Timestamp string in format 'YYYY/MM/DD HH:MM:SS'

    Returns:
        Seconds since midnight
    """
    dt = datetime.strptime(stamp, '%Y/%m/%d %H:%M:%S')
    return dt.hour * 3600 + dt.minute * 60 + dt.second
