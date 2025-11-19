"""
Author: Joe Beck
Date: 25.03.25

Based on the multiprocessing set up from "time_filter_events_011.py"

Attention: Safe chunk generation not implemented here!
--> replace parse_and_queue_chunks with the corresponding function form "time_and_spatial_filer_events.py"
"""

import geopandas as gpd
from lxml import etree
import multiprocessing as mp
import argparse

def load_gpkg_create_valid_ids(file):
    print("Loading GeoPackage...")
    gpkg = gpd.read_file(file)
    print('--> Geopackage loaded.')
    return set(gpkg["id"])


def element_to_dict(element):
    """ Convert a lxml element to a dictionary (picklable) """
    return {'tag': element.tag, 'attrib': dict(element.attrib)}


def dict_to_element(data):
    """ Convert a dictionary back to a lxml element """
    return etree.Element(data['tag'], attrib=data['attrib'])


def filter_events(args_tuple):
    chunk, valid_ids = args_tuple
    filtered_events = []

    for event_dict in chunk:
        event = dict_to_element(event_dict)  # Convert back to lxml element
        time = event.get('time')

        if time is None:
            continue

        link_id = event.get('link')
        if link_id:
            link_id = int(link_id)
            if link_id in valid_ids:
                filtered_events.append(event_dict)  # Store dictionary instead of element

    print(f"Processed {len(chunk)} events, filtered {len(filtered_events)} events")  # Monitoring

    return filtered_events


def parse_and_queue_chunks(xml_path, queue, chunk_size):
    """ Parses XML file using lxml and sends chunks to the queue for parallel processing """
    context = etree.iterparse(xml_path, events=("start", "end"))
    event_list = []

    for event, elem in context:
        if event == "end" and elem.tag == "event":
            event_dict = element_to_dict(elem)  # ✅ Convert to dict
            event_list.append(event_dict)
            elem.clear()  # ✅ Free memory
            while elem.getprevious() is not None:
                del elem.getparent()[0]  # ✅ Remove references to processed elements

            if len(event_list) >= chunk_size:
                queue.put(event_list)  # ✅ Send list of dicts, not elements
                event_list = []  # Reset list to reduce memory usage

    #sends the last chunk of size <= chunk_size to the queue
    if event_list:
        queue.put(event_list)

    queue.put(None)  # Signal processing completion


def run_multiprocessing(input_file, output_file, valid_ids, num_workers, chunk_size=100000):
    """This function creates two multiprocessing processes. One to divide the xml input file into chunks
    and another to filter the events in each chunk"""

    print("Start processing XML file with multiprocessing...")

    pool = mp.Pool(num_workers)


    # set up queue where chunks getting fed in later
    queue = mp.Queue(maxsize=num_workers * 4)  # Larger queue for better parallelism

    # divide xml in chunks
    parser_process = mp.Process(target=parse_and_queue_chunks, args=(input_file, queue, chunk_size))
    parser_process.start()

    # create output file and write heading
    with open(output_file, "wb") as f:
        #f.write(b"<?xml version='1.0' encoding='utf-8'?>\n<events>\n")
        f.write(b"<events>\n")

        # filter the events by comparing link_id of events to the ones in the geopackage
        for filtered_events in pool.imap_unordered(
                filter_events, ((chunk, valid_ids) for chunk in iter(queue.get, None))
        ):
            for event_dict in filtered_events:
                event = dict_to_element(event_dict)  # Convert back to element
                f.write(etree.tostring(event, encoding="utf-8") + b"\n")

        f.write(b"</events>\n")
        print(f'Filtered events file successfully created at {output_file}')

    # Cleanup
    pool.close()
    pool.join()
    parser_process.join()


if __name__ == '__main__':

    parser = argparse.ArgumentParser(description="Filter events file for time intervals")
    parser.add_argument("--gpkg_file_path", type=str, required=True)
    parser.add_argument("--xml_input_path", type=str, required=True)
    parser.add_argument("--xml_output_path", type=str, required=True)
    args = parser.parse_args()

    valid_link_ids = load_gpkg_create_valid_ids(args.gpkg_file_path)

    number_of_cpus = mp.cpu_count()
    run_multiprocessing(args.xml_input_path, args.xml_output_path, valid_link_ids, number_of_cpus, 100000)
