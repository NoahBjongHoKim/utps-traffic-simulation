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
    """Filter events in a chunk by time and spatial domain."""
    valid_links, chunk, interval1, interval2 = args
    
    start1, end1 = map(time_to_seconds, interval1)
    start2, end2 = map(time_to_seconds, interval2)
    
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
            
            # Check time intervals
            in_interval = (
                (start1 <= time_enter <= end1 and start1 <= time <= end1) or
                (start2 <= time_enter <= end2 and start2 <= time <= end2)
            )
            
            # Check spatial domain
            if in_interval and link_id in valid_links:
                # Store both EnterLink and LeaveLink
                filtered_records.append({
                    'person': person,
                    'link_id': link_id,
                    'time_enter': time_enter,
                    'time_leave': time,
                    'event_type': 'trip'
                })
    
    if enter_events:
        logger.warning(f"{len(enter_events)} unmatched EnterLink events in chunk")

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


def write_to_parquet(output_path, pool, queue, filter_args):
    """Process chunks and write to Parquet file."""
    logger.info("Filtering events and writing to Parquet...")

    # Define schema
    schema = pa.schema([
        ('person', pa.string()),
        ('link_id', pa.string()),
        ('time_enter', pa.int32()),
        ('time_leave', pa.int32()),
        ('event_type', pa.string())
    ])

    writer = None
    total_filtered = 0
    batches_written = 0

    try:
        for records in pool.imap_unordered(
            filter_events_chunk,
            ((filter_args[0], chunk, filter_args[1], filter_args[2])
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
                            time_interval_1, time_interval_2,
                            num_workers, chunk_size, gpkg_network=None):
    """Main function to convert XML to filtered Parquet.

    Args:
        xml_input: Path to XML events file
        valid_links: Set of valid link IDs (strings) or None to load from gpkg_network
        parquet_output: Path to output Parquet file
        time_interval_1: First time interval as "hh:mm,hh:mm"
        time_interval_2: Second time interval as "hh:mm,hh:mm"
        num_workers: Number of worker processes
        chunk_size: Chunk size for processing
        gpkg_network: Path to GeoPackage (optional, for standalone use)
    """

    # Load valid link IDs if not provided (for standalone use)
    if valid_links is None:
        if gpkg_network is None:
            raise ValueError("Either valid_links or gpkg_network must be provided")
        valid_links = load_valid_link_ids(gpkg_network)
    
    # Parse time intervals
    interval1 = parse_time_interval(time_interval_1)
    interval2 = parse_time_interval(time_interval_2)
    
    # Setup multiprocessing
    queue = mp.Queue(maxsize=num_workers * 4)
    pool = mp.Pool(num_workers)
    
    # Start parser process
    parser = mp.Process(target=parse_xml_to_chunks, 
                       args=(xml_input, queue, chunk_size))
    parser.start()
    
    # Process and write to Parquet
    filter_args = (valid_links, interval1, interval2)
    write_to_parquet(parquet_output, pool, queue, filter_args)
    
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
    parser.add_argument("--time_interval_1", required=True)
    parser.add_argument("--time_interval_2", required=True)
    parser.add_argument("--num_workers", type=int, default=mp.cpu_count())
    parser.add_argument("--chunk_size", type=int, default=100000)
    
    args = parser.parse_args()

    xml_to_parquet_filtered(
        xml_input=args.xml_input,
        valid_links=None,  # Will be loaded from gpkg_network
        parquet_output=args.parquet_output,
        time_interval_1=args.time_interval_1,
        time_interval_2=args.time_interval_2,
        num_workers=args.num_workers,
        chunk_size=args.chunk_size,
        gpkg_network=args.gpkg_network
    )
