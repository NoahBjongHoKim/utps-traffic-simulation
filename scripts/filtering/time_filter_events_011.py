"""
Author: Joe Beck
Date: 21.03.25

This version is based on 005 which was upgraded to 010 version since it ran successfully.
Updates:
- time intervals as input. In the following format: "hh:mm", "hh:mm". Example: "16:30", "18:00"
- Prints a warning if a EnterLink statements comes without a LeaveLink statement.
 --> the statement gets ignored.

Info:
If code should filter just for one time interval, provide the same interval for interval 1 and interval 2 as argument.
"""

from lxml import etree
import multiprocessing as mp
import argparse


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

def process_chunk(args_tuple):
    chunk, interval1, interval2 = args_tuple

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
                filtered_events.append(enter_event_dict)  # Store as dict
                filtered_events.append(event_dict)  # Store as dict

    if enter_events:
        print(f"Warning: {len(enter_events)} EnterLink events have no matching LeaveLink events!")

    print(f"Processed {len(chunk)} events, filtered {len(filtered_events)} events")  # Debugging

    return filtered_events


def parse_xml_producer(input_file, queue, chunk_size=100000):
    """ Parses XML file using lxml and sends chunks to the queue for parallel processing """
    context = etree.iterparse(input_file, events=("start", "end"))
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

    if event_list:
        queue.put(event_list)

    queue.put(None)  # Signal processing completion

def parse_and_filter_large_xml(input_file, output_file, interval1, interval2, num_workers, chunk_size=100000):
    queue = mp.Queue(maxsize=num_workers * 4)  # Larger queue for better parallelism
    pool = mp.Pool(num_workers)

    # Start XML parsing process
    parser_process = mp.Process(target=parse_xml_producer, args=(input_file, queue, chunk_size))
    parser_process.start()

    with open(output_file, "wb") as f:
        #f.write(b"<?xml version='1.0' encoding='utf-8'?>\n<events>\n")
        f.write(b"<events>\n")

        # ✅ Using lxml with multiprocessing
        for filtered_events in pool.imap_unordered(process_chunk, ((chunk, interval1, interval2) for chunk in iter(queue.get, None))):
            for event_dict in filtered_events:
                event = dict_to_element(event_dict)  # Convert back to element
                f.write(etree.tostring(event, encoding="utf-8") + b"\n")  # ✅ Use etree.tostring()

        f.write(b"</events>\n")

    # Cleanup
    pool.close()
    pool.join()
    parser_process.join()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="Filter events file for time intervals")
    parser.add_argument("--xml_input_path", type=str, required=True)
    parser.add_argument("--xml_output_path", type=str, required=True)
    parser.add_argument("--time_interval_1", type=str, required=True, help="Format: hh:mm,hh:mm")
    parser.add_argument("--time_interval_2", type=str, required=True, help="Format: hh:mm,hh:mm")

    args = parser.parse_args()

    # Convert the string to a tuple
    time_interval_1 = parse_time_interval(args.time_interval_1)
    time_interval_2 = parse_time_interval(args.time_interval_2)
    number_of_cpus = mp.cpu_count()
    parse_and_filter_large_xml(
        args.xml_input_path, args.xml_output_path, time_interval_1, time_interval_2, number_of_cpus, 100000
    )
