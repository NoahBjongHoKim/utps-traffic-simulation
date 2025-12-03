"""XML to Parquet converter with time and spatial filtering.

This module provides efficient conversion of large XML event files to Parquet format
with simultaneous filtering by time intervals and spatial domains. Uses multiprocessing
for parallel processing and streaming writes to handle files larger than available RAM.

The converter supports:
    - Multiple configurable time intervals for snapshot analysis
    - Spatial filtering using road network from GeoPackage
    - Automatic time clipping for events extending beyond interval boundaries
    - Memory-efficient streaming XML parsing
    - Parallel chunk processing with multiprocessing

Authors: Noah Kim & Joe Beck
Date: 14.11.2025

Example:
    >>> from traffic_sim_module.pipeline.xml_to_parquet import xml_to_parquet_filtered
    >>> time_intervals = [(28800, 32400), (61200, 64800)]  # 8-9am and 5-6pm
    >>> xml_to_parquet_filtered(
    ...     xml_input="events.xml",
    ...     valid_links=None,
    ...     parquet_output="filtered.parquet",
    ...     time_intervals=time_intervals,
    ...     num_workers=8,
    ...     chunk_size=100000,
    ...     gpkg_network="network.gpkg"
    ... )
"""

from collections import defaultdict
import multiprocessing as mp

import geopandas as gpd
from lxml import etree
import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq

from ..config import logger


def load_valid_link_ids(gpkg_path, id_field='linkId'):
    """Load GeoPackage and extract valid link IDs for spatial filtering.

    Args:
        gpkg_path: Path to the GeoPackage file containing road network
        id_field: Name of the column containing link IDs (default: 'linkId')

    Returns:
        Set of valid link IDs as strings

    Example:
        >>> valid_links = load_valid_link_ids("network.gpkg")
        >>> len(valid_links)
        45032
    """
    logger.info("Loading road network GeoPackage...")
    gpkg = gpd.read_file(gpkg_path)
    logger.info(f"Road network loaded: {len(gpkg):,} links")
    return set(gpkg[id_field].astype(str))


def parse_time_interval(interval_str):
    """Convert time interval string to tuple.

    Args:
        interval_str: Time interval in format 'hh:mm,hh:mm'

    Returns:
        Tuple of (start_time, end_time) as strings

    Example:
        >>> parse_time_interval("08:00,09:00")
        ('08:00', '09:00')
    """
    return tuple(interval_str.split(","))


def time_to_seconds(time_str):
    """Convert time string to seconds since midnight.

    Args:
        time_str: Time in format 'hh:mm'

    Returns:
        Integer seconds since midnight

    Example:
        >>> time_to_seconds("08:30")
        30600
        >>> time_to_seconds("14:15")
        51300
    """
    h, m = map(int, time_str.split(":"))
    return h * 3600 + m * 60


def filter_events_chunk(args):
    """Filter events chunk by time and spatial domain with automatic time clipping.

    Processes a chunk of XML events and filters them based on time intervals and
    spatial domain (valid link IDs). For snapshot-based analysis, if an EnterLink
    event falls within an interval but its corresponding LeaveLink extends beyond
    the interval boundary, the LeaveLink time is automatically clipped to the
    interval end. This ensures trajectories remain within the snapshot window for
    proper interpolation.

    Args:
        args: Tuple of (valid_links, chunk, time_intervals) where:
            - valid_links (set): Set of valid link IDs as strings
            - chunk (list): List of event dictionaries from XML
            - time_intervals (list): List of (start_seconds, end_seconds) tuples

    Returns:
        List of filtered event dictionaries with keys:
            - person (str): Person/vehicle ID
            - link_id (str): Link ID
            - time_enter (int): Enter time in seconds
            - time_leave (int): Leave time in seconds (possibly clipped)
            - interval_id (int): Index of the time interval this event belongs to
            - event_type (str): Always 'trip' for matched EnterLink/LeaveLink pairs

    Note:
        Unmatched EnterLink events are expected in snapshot mode and logged at
        debug level. Only complete EnterLink/LeaveLink pairs are included in output.
    """
    valid_links, chunk, time_intervals = args

    filtered_records = []
    enter_events = {}

    for event in chunk:
        event_type = event.get('type')
        person = event.get('person')
        time_str = event.get('time')

        if not time_str:
            continue

        try:
            time = int(time_str)
        except ValueError:
            continue

        link_id = event.get('link')

        if event_type == "EnterLink":
            enter_events[(person, link_id)] = event
        elif event_type == "LeaveLink" and (person, link_id) in enter_events:
            enter_event = enter_events.pop((person, link_id))
            time_enter = int(enter_event.get('time'))
            time_leave = time

            # Check which interval the EnterLink falls into
            matched_interval = None
            interval_id = None
            for idx, (interval_start, interval_end) in enumerate(time_intervals):
                if interval_start <= time_enter <= interval_end:
                    matched_interval = (interval_start, interval_end)
                    interval_id = idx  # Track which interval this belongs to
                    break

            # If EnterLink is in an interval and link is in spatial domain
            if matched_interval and link_id in valid_links:
                interval_start, interval_end = matched_interval

                # Clip LeaveLink time to interval end if it extends beyond
                if time_leave > interval_end:
                    time_leave = interval_end
                    # Note: time_leave might now equal time_enter, which is fine
                    # It just means the vehicle was at a point in the snapshot

                # Store the filtered record with potentially clipped time_leave
                filtered_records.append({
                    'person': person,
                    'link_id': link_id,
                    'time_enter': time_enter,
                    'time_leave': time_leave,
                    'interval_id': interval_id,  # NEW: Track which interval
                    'event_type': 'trip'
                })

    if enter_events:
        logger.debug(f"{len(enter_events)} unmatched EnterLink events in chunk (expected for snapshot mode)")

    return filtered_records


def parse_xml_to_chunks(xml_path, queue, chunk_size):
    """Parse XML event file and send chunks to processing queue.

    Uses streaming XML parsing (iterparse) to handle large files efficiently without
    loading the entire file into memory. Ensures proper EnterLink/LeaveLink pairing
    by buffering pending EnterLink events until their matching LeaveLink is found.

    Args:
        xml_path: Path to the XML events file
        queue: Multiprocessing queue to send event chunks
        chunk_size: Number of events per chunk

    Note:
        - Sends None to queue when parsing is complete (sentinel value)
        - Clears parsed elements from memory to prevent accumulation
        - Logs progress every 10 chunks
        - Warns about unmatched EnterLink events at end of file
    """
    logger.info("Starting XML parsing...")
    context = etree.iterparse(xml_path, events=("start", "end"))

    event_list = []
    pending_events = defaultdict(list)
    total_events = 0
    chunks_sent = 0

    for event, elem in context:
        if event == "end" and elem.tag == "event":
            event_dict = dict(elem.attrib)

            event_type = event_dict.get("type", "")
            person_id = event_dict.get("person", "")
            link_id = event_dict.get("link", "")

            key = (person_id, link_id)

            if event_type == "EnterLink":
                pending_events[key].append(event_dict)
            elif event_type == "LeaveLink":
                if key in pending_events:
                    event_list.extend(pending_events.pop(key))
                event_list.append(event_dict)

            # Memory cleanup
            elem.clear()
            while elem.getprevious() is not None:
                del elem.getparent()[0]

            # Send chunk when full
            if len(event_list) >= chunk_size:
                queue.put(event_list)
                total_events += len(event_list)
                chunks_sent += 1
                if chunks_sent % 10 == 0:  # Log every 10 chunks
                    logger.info(f"Parsed {total_events:,} events ({chunks_sent} chunks sent)")
                event_list = []

    # Handle remaining events
    if pending_events:
        logger.warning(f"{len(pending_events)} unmatched EnterLink events at end of file")

    if event_list:
        queue.put(event_list)
        total_events += len(event_list)

    queue.put(None)
    logger.success(f"XML parsing complete: {total_events:,} total events processed")


def write_to_parquet(output_path, pool, queue, valid_links, time_intervals):
    """Process event chunks in parallel and write results to Parquet file.

    Coordinates parallel filtering of event chunks using a multiprocessing pool,
    then writes the filtered results to a Parquet file with streaming writes.
    Uses PyArrow for efficient columnar storage.

    Args:
        output_path: Path for the output Parquet file
        pool: Multiprocessing pool for parallel processing
        queue: Queue containing event chunks to process
        valid_links: Set of valid link IDs for spatial filtering
        time_intervals: List of (start_seconds, end_seconds) tuples

    Note:
        - Schema is predefined with appropriate types for all columns
        - Writes are streaming to handle large datasets
        - Logs progress every 50 batches
        - Ensures writer is properly closed even if errors occur
    """
    logger.info("Filtering events and writing to Parquet...")
    logger.info(f"Using {len(time_intervals)} time intervals")

    # Define schema
    schema = pa.schema([
        ('person', pa.string()),
        ('link_id', pa.string()),
        ('time_enter', pa.int32()),
        ('time_leave', pa.int32()),
        ('interval_id', pa.int32()),
        ('event_type', pa.string())
    ])

    writer = None
    total_filtered = 0
    batches_written = 0

    try:
        for records in pool.imap_unordered(
            filter_events_chunk,
            ((valid_links, chunk, time_intervals)
             for chunk in iter(queue.get, None))
        ):
            if records:
                df = pd.DataFrame(records)
                table = pa.Table.from_pandas(df, schema=schema)

                if writer is None:
                    writer = pq.ParquetWriter(output_path, schema)
                    logger.debug(f"Parquet writer initialized: {output_path}")

                writer.write_table(table)
                total_filtered += len(records)
                batches_written += 1

                if batches_written % 50 == 0:  # Log every 50 batches
                    logger.info(f"Filtered events written: {total_filtered:,}")

    finally:
        if writer:
            writer.close()

    logger.success(f"Parquet file created: {total_filtered:,} filtered events")


def xml_to_parquet_filtered(xml_input, valid_links, parquet_output,
                            time_intervals, num_workers, chunk_size, gpkg_network=None):
    """Convert XML events file to filtered Parquet format with parallel processing.

    Main entry point for the XML to Parquet conversion pipeline. Orchestrates
    multiprocessing-based parsing, filtering, and writing of event data. Supports
    both time-based and spatial filtering with automatic time clipping for
    snapshot-based analysis.

    Args:
        xml_input: Path to input XML events file
        valid_links: Set of valid link IDs as strings, or None to load from gpkg_network
        parquet_output: Path for output Parquet file
        time_intervals: List of (start_seconds, end_seconds) tuples defining time windows
        num_workers: Number of parallel worker processes for filtering
        chunk_size: Number of events to process per chunk
        gpkg_network: Optional path to GeoPackage for loading valid link IDs

    Raises:
        ValueError: If both valid_links and gpkg_network are None

    Note:
        For snapshot-based filtering, LeaveLink times are automatically clipped to
        the interval end if they extend beyond it. This ensures trajectories stay
        within the snapshot window for proper interpolation.

    Example:
        >>> time_intervals = [(28800, 32400), (61200, 64800)]  # 8-9am, 5-6pm
        >>> xml_to_parquet_filtered(
        ...     xml_input="events.xml",
        ...     valid_links=None,
        ...     parquet_output="filtered.parquet",
        ...     time_intervals=time_intervals,
        ...     num_workers=8,
        ...     chunk_size=100000,
        ...     gpkg_network="network.gpkg"
        ... )
    """

    # Load valid link IDs if not provided (for standalone use)
    if valid_links is None:
        if gpkg_network is None:
            raise ValueError("Either valid_links or gpkg_network must be provided")
        valid_links = load_valid_link_ids(gpkg_network)

    # Setup multiprocessing
    queue = mp.Queue(maxsize=num_workers * 4)
    pool = mp.Pool(num_workers)

    # Start parser process
    parser = mp.Process(target=parse_xml_to_chunks,
                       args=(xml_input, queue, chunk_size))
    parser.start()

    # Process and write to Parquet
    write_to_parquet(parquet_output, pool, queue, valid_links, time_intervals)
    
    # Cleanup
    pool.close()
    pool.join()
    parser.join()
    
    print(f"Output saved to: {parquet_output}")


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("--xml_input", required=True)
    parser.add_argument("--gpkg_network", required=True)
    parser.add_argument("--parquet_output", required=True)
    parser.add_argument("--time_interval_1", required=True, help="First interval as 'hh:mm,hh:mm'")
    parser.add_argument("--time_interval_2", required=True, help="Second interval as 'hh:mm,hh:mm'")
    parser.add_argument("--num_workers", type=int, default=mp.cpu_count())
    parser.add_argument("--chunk_size", type=int, default=100000)

    args = parser.parse_args()

    # Convert legacy interval format to new format
    def parse_legacy_interval(interval_str):
        """Convert 'hh:mm,hh:mm' to (start_seconds, end_seconds)."""
        start, end = interval_str.split(',')
        h_start, m_start = map(int, start.split(':'))
        h_end, m_end = map(int, end.split(':'))
        return (h_start * 3600 + m_start * 60, h_end * 3600 + m_end * 60)

    time_intervals = [
        parse_legacy_interval(args.time_interval_1),
        parse_legacy_interval(args.time_interval_2)
    ]

    xml_to_parquet_filtered(
        xml_input=args.xml_input,
        valid_links=None,  # Will be loaded from gpkg_network
        parquet_output=args.parquet_output,
        time_intervals=time_intervals,
        num_workers=args.num_workers,
        chunk_size=args.chunk_size,
        gpkg_network=args.gpkg_network
    )
