"""
Author: Joe Beck
Date: 25.03.25

Purpose:
- Filter a xml file regarding one or two time intervals and regarding a spatial domain.
- The spatial domain filtering is made by comparing the events in the xml files to the
  road network provided as input. Events that do not refer to road parts not present in the
  road network will get removed.
  In other words: The spatial domain of the road network determines the spatial domain of
  the output xml file.

Features:
- Code uses multiprocessing for using full cpu capacity.
- filter_events_in_chunk() prints a warning if a EnterLink statements comes without a LeaveLink statement.
 --> the statement gets ignored.
 --> means that either the events file is incomplete or the safe chunk generation failed.
- safe chunk generation: makes sure to keep EnterLink and LeaveLink statements together. Prints warning as well if
  EnterLink statement has no LeaveLink statement
- Combines the two filtering methods: Time AND spatial filtering

Required Inputs:
- gpkg_file_path, type=str, help = input path of the road network
- xml_input_path, type=str, help = input path of the xml file containing the events
- xml_output_path", type=str, help = output path of the filtered xml file
- time_interval_1", type=str, help="Format: "hh:mm,hh:mm", help = first time interval for which the file should be filtered
  Example: "07:30,09:00"
- time_interval_2", type=str,  help="Format: "hh:mm,hh:mm", help = second time interval for which the file should be filtered

Info:
- If code should filter just for one time interval, provide the same interval for interval 1 and interval 2 as argument.
- Depending on the used computer, for xml files up to 1 GB could be filtered locally.
  For larger input files, using the Euler Cluster Supercomputer of ETH is recommended.
"""

from lxml import etree
import multiprocessing as mp
import argparse
import geopandas as gpd
from collections import defaultdict
import xml.dom.minidom


def pretty_print_indent(input_file):
    # Read the whole raw XML file
    with open(input_file, "rb") as f:
        raw_xml = f.read()

    # Parse
    dom = xml.dom.minidom.parseString(raw_xml)

    # Clean up: remove extra whitespace-only text nodes
    def remove_whitespace_nodes(node):
        to_remove = []
        for child in node.childNodes:
            if child.nodeType == xml.dom.minidom.Node.TEXT_NODE and child.data.strip() == "":
                to_remove.append(child)
            elif child.hasChildNodes():
                remove_whitespace_nodes(child)
        for child in to_remove:
            node.removeChild(child)

    remove_whitespace_nodes(dom)

    # Pretty print
    pretty_xml_as_string = dom.toprettyxml(indent="  ", encoding="utf-8")

    # Overwrite the same file
    with open(input_file, "wb") as f:
        f.write(pretty_xml_as_string)

    print(f'Pretty-printed and saved file at {input_file}')


def load_gpkg_create_valid_ids(file, id_field_name):
    print("Loading GeoPackage...")
    gpkg = gpd.read_file(file)
    print('--> Geopackage loaded.')
    return set(gpkg[id_field_name])


def time_to_secs(time_interval):
    start, end = time_interval  # Unpack the tuple
    h1, m1 = map(int, start.split(":"))  # Convert start time to hours and minutes
    h2, m2 = map(int, end.split(":"))  # Convert end time to hours and minutes

    start_secs = h1 * 3600 + m1 * 60  # Convert start time to seconds
    end_secs = h2 * 3600 + m2 * 60  # Convert end time to seconds
    interval_secs = (start_secs, end_secs)
    return interval_secs


def parse_time_interval(interval_str):
    """Convert a comma-separated time interval string into a tuple."""
    return tuple(interval_str.split(","))


def element_to_dict(element):
    """ Convert a lxml element to a dictionary (picklable) """
    return {'tag': element.tag, 'attrib': dict(element.attrib)}


def dict_to_element(data):
    """ Convert a dictionary back to an lxml element """
    return etree.Element(data['tag'], attrib=data['attrib'])


def filter_events_in_chunk(args_tuple):
    valid_links, chunk, interval1, interval2 = args_tuple

    start_time_1, end_time_1 = time_to_secs(interval1)
    start_time_2, end_time_2 = time_to_secs(interval2)

    filtered_events = []
    enter_events = {}

    for event_dict in chunk:
        event = dict_to_element(event_dict)  # Convert back to lxml element
        event_type = event.get('type')
        person = event.get('person')
        time = event.get('time')

        if time is None:
            continue

        try:
            time = int(time)
        except ValueError:
            continue

        link_id = event.get('link')

        if event_type == "EnterLink":
            enter_events[(person, link_id)] = event_dict  # Store as dict
        elif event_type == "LeaveLink" and (person, link_id) in enter_events:
            enter_event_dict = enter_events.pop((person, link_id))
            enter_event = dict_to_element(enter_event_dict)  # Convert back to element
            time_enter = int(enter_event.get('time'))

            if ((start_time_1 <= time_enter <= end_time_1 and start_time_1 <= time <= end_time_1) or
                    (start_time_2 <= time_enter <= end_time_2 and start_time_2 <= time <= end_time_2)):
                if int(link_id) in valid_links:
                    filtered_events.append(enter_event_dict)  # Store as dict
                    filtered_events.append(event_dict)  # Store as dict

    if enter_events:
        print(f"\nWarning from filter_events_in_chunks():\n")
        print(f"{len(enter_events)} EnterLink events have no matching LeaveLink events!")

    print(f"Processed {len(chunk)} events, filtered {len(filtered_events)} events")  # Monitoring

    return filtered_events


def parse_and_queue_chunks(input_file, queue, chunk_size):
    """Parses XML file and ensures safe chunking for EnterLink and LeaveLink events."""
    context = etree.iterparse(input_file, events=("start", "end"))

    event_list = []
    pending_events = defaultdict(list)  # Stores EnterLink events waiting for their LeaveLink

    for event, elem in context:
        if event == "end" and elem.tag == "event":
            current_event_dict = element_to_dict(elem)  # Convert XML element to dict

            if not isinstance(current_event_dict, dict) or "attrib" not in current_event_dict:
                continue  # Skip processing if conversion failed or missing attributes

            event_type = current_event_dict["attrib"].get("type", "")
            person_id = current_event_dict["attrib"].get("person", "")
            link_id = current_event_dict["attrib"].get("link", "")

            key = (person_id, link_id)  # Match based on both person and link

            if event_type == "EnterLink":
                pending_events[key].append(current_event_dict)  # Store EnterLink event

            elif event_type == "LeaveLink":
                if key in pending_events:
                    event_list.extend(pending_events.pop(key))  # Move paired EnterLinks into chunk
                event_list.append(current_event_dict)  # Add LeaveLink itself

            # Free memory
            elem.clear()
            while elem.getprevious() is not None:
                del elem.getparent()[0]

            parent = elem.getparent()
            if parent is not None:
                parent.remove(elem)  # Explicitly remove the element itself


            # If chunk is full, send it—but keep unpaired EnterLinks in pending_events
            if len(event_list) >= chunk_size:
                queue.put(event_list)  # Send chunk
                event_list = []  # Reset list


    # Handle unmatched EnterLink events (file corruption detection)
    if pending_events:
        print("\nWARNING: Unmatched EnterLink events detected! Possible incomplete events file...\n")
        for key, events in pending_events.items():
            for event in events:
                print(f"Unmatched EnterLink event: {event}")  # Print full unmatched event


    # Send the remaining EnterLinks, carrying them over to the next proces
    if event_list:
        queue.put(event_list)  # Send any remaining events

    queue.put(None)  # Signal processing completion


def run_multiprocessing(valid_ids, input_file, output_file, interval1, interval2, num_workers, chunk_size=100000):
    queue = mp.Queue(maxsize=num_workers * 4)  # Larger queue for better parallelism
    pool = mp.Pool(num_workers)

    # divide xml in chunks
    parser_process = mp.Process(target=parse_and_queue_chunks, args=(input_file, queue, chunk_size))
    parser_process.start()

    # create output file and write heading
    with open(output_file, "wb") as f:
        #f.write(b"<?xml version='1.0' encoding='utf-8'?>\n<events>\n")
        f.write(b"<events>\n")

        # filter the events by comparing link_id of events to the ones in the geopackage
        for filtered_events in pool.imap_unordered(filter_events_in_chunk, ((valid_ids, chunk, interval1, interval2) for chunk in iter(queue.get, None))):
            for event_dict in filtered_events:
                event = dict_to_element(event_dict)  # Convert back to element
                f.write(etree.tostring(event, encoding="utf-8") + b"\n")

        f.write(b"</events>\n")
        print(f'Filtered events file successfully created.')

    # Cleanup
    pool.close()
    pool.join()
    parser_process.join()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="Filter events file for time intervals")
    parser.add_argument("--gpkg_file_path", type=str, required=True,)
    parser.add_argument("--xml_input_path", type=str, required=True)
    parser.add_argument("--xml_output_path", type=str, required=True)
    parser.add_argument("--time_interval_1", type=str, required=True, help="Format: hh:mm,hh:mm")
    parser.add_argument("--time_interval_2", type=str, required=True, help="Format: hh:mm,hh:mm")

    args = parser.parse_args()
    # Use 'id' for gpkg_v2 and 'linkId' for gpkg_v3 and v4
    valid_link_ids = load_gpkg_create_valid_ids(args.gpkg_file_path, 'id')

    # Convert the string to a tuple
    time_interval_1 = parse_time_interval(args.time_interval_1)
    time_interval_2 = parse_time_interval(args.time_interval_2)
    number_of_cpus = mp.cpu_count()
    run_multiprocessing(
        valid_link_ids,
        args.xml_input_path,
        args.xml_output_path,
        time_interval_1,
        time_interval_2,
        number_of_cpus,
        100000
    )

    pretty_print_indent(args.xml_output_path)
