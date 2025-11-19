"""
- Input: A xml file
- Output: Sorted XML file

- Made for large file since xml elements are not stored in memory but using a temporary file and
  using sequentially read in of the input file.
- No multiprocessing but running fast on local machine with a 400 MB xml file (3 minutes)

"""


import os
import heapq
import tempfile
from lxml import etree

def sort_trips_by_time(input_path, out_path, number_events, chunk_size=100000):
    temp_files = []

    # Phase 1: Chunked reading and sorting
    context = etree.iterparse(input_path, events=("end",), tag="event", encoding="utf-8")
    chunk = []
    count = 0

    print("Reading and sorting chunks...")
    for i, (_, elem) in enumerate(context):
        time = float(elem.get("time"))
        chunk.append((time, etree.tostring(elem)))

        elem.clear()
        while elem.getprevious() is not None:
            del elem.getparent()[0]

        if len(chunk) >= chunk_size:
            chunk.sort()
            temp = tempfile.NamedTemporaryFile(delete=False, mode='wb')
            for _, xml in chunk:
                temp.write(xml + b"\n")
            temp.close()
            temp_files.append(temp.name)
            chunk = []
            print(f"  Written chunk {len(temp_files)} with {chunk_size} events")

        count += 1
        print(f"\rProgress: {(count / number_events) * 100:.2f}%", end="", flush=True)

    # Write any remaining events in the final chunk
    if chunk:
        chunk.sort()
        temp = tempfile.NamedTemporaryFile(delete=False, mode='wb')
        for _, xml in chunk:
            temp.write(xml + b"\n")
        temp.close()
        temp_files.append(temp.name)
        print(f"\n  Written final chunk with {len(chunk)} events")

    del context

    # Phase 2: Merge sorted chunks
    print("\nMerging sorted chunks...")

    def generate_events_from_file(file_name):
        with open(file_name, "rb") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue  # Skip empty lines
                try:
                    elem = etree.fromstring(line)
                    yield (float(elem.get("time")), line)
                except etree.XMLSyntaxError:
                    print(f"Skipping invalid XML line in {file_name}: {line[:50]}")
                    continue

    generators = [generate_events_from_file(f) for f in temp_files]
    merged = heapq.merge(*generators)

    with open(out_path, "wb") as out:
        out.write(b"<events>\n")
        for _, xml in merged:
            #indented_xml = b"    " + xml.strip() + b"\n"
            #out.write(indented_xml)
            out.write(xml + b"\n")
        out.write(b"</events>\n")

    # Clean up temp files
    for f in temp_files:
        os.remove(f)

    print(f"Time sorted events file successfully created at {out_path}")





if __name__ == '__main__':
    in_path = r"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\data\v4\events_v4\smaller_files\mini_sorted.xml"
    out_path = r"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\data\v4\events_v4\smaller_files\mini_sorted_time_sorted.xml"
    num_events = 5785560

    sort_trips_by_time(in_path, out_path, num_events)
