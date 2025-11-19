"""
XML to Parquet Converter with Time/Spatial Filtering
Author: Noah Kim & Joe Beck
Date: 14.11.2025

Reads XML events file and creates filtered Parquet output.
Filters by:
- Time intervals (2 configurable intervals)
- Spatial domain (using road network from GeoPackage)
"""

from lxml import etree
import multiprocessing as mp
import geopandas as gpd
import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq
from collections import defaultdict
import sys

from ..config import logger


def load_valid_link_ids(gpkg_path, id_field='linkId'):
    """Load GeoPackage and extract valid link IDs."""
    logger.info("Loading road network GeoPackage...")
    gpkg = gpd.read_file(gpkg_path)
    logger.info(f"Road network loaded: {len(gpkg):,} links")
    return set(gpkg[id_field].astype(str))


def parse_time_interval(interval_str):
    """Convert 'hh:mm,hh:mm' to tuple of (start, end)."""
    return tuple(interval_str.split(","))


def time_to_seconds(time_str):
    """Convert 'hh:mm' to seconds."""
    h, m = map(int, time_str.split(":"))
    return h * 3600 + m * 60


def filter_events_chunk(args):
    """Filter events in a chunk by time and spatial domain with LeaveLink time clipping.

    For snapshot-based filtering, if EnterLink is within an interval but LeaveLink extends
    beyond it, the LeaveLink time is clipped to the interval end. This ensures trajectories
    stay within the snapshot window for proper interpolation.
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
    """Parse XML and send chunks to queue, ensuring EnterLink/LeaveLink pairing."""
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
    """Process chunks and write to Parquet file."""
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
    """Main function to convert XML to filtered Parquet.

    Args:
        xml_input: Path to XML events file
        valid_links: Set of valid link IDs (strings) or None to load from gpkg_network
        parquet_output: Path to output Parquet file
        time_intervals: List of (start_seconds, end_seconds) tuples
        num_workers: Number of worker processes
        chunk_size: Chunk size for processing
        gpkg_network: Path to GeoPackage (optional, for standalone use)

    Note:
        For snapshot-based filtering, LeaveLink times are automatically clipped to the
        interval end if they extend beyond it. This ensures trajectories stay within
        the snapshot window.
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
