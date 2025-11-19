from lxml import etree
from collections import defaultdict

def sort_trips_by_person(input_path, number_events):
    context = etree.iterparse(input_path, events=("start", "end"), encoding="utf-8")

    events_dict = defaultdict(list)  # Stores events grouped by person ID

    j = 0
    for event, elem in context:
        print(f"\rSorting trips by person... ({(j / (2*number_events)) * 100:.2f}%)", end='', flush=True)
        if event == "end" and elem.tag == "event":
            person_id = elem.get("person")  # ✅ Get the person ID attribute from the event

            if person_id:
                events_dict[person_id].append(etree.tostring(elem))  # ✅ Store XML string of the event

            elem.clear()
        j += 1

    return events_dict


def dump_events(events_dict, output_path, number_events):
    with open(output_path, "wb") as f:
        f.write(b"<events>\n")

        j = 0
        for person_id, events in events_dict.items():
            for event_xml in events:
                print(f"\rWriting sorted events to file... ({(j / number_events) * 100:.2f}%)", end='', flush=True)
                # f.write(event_xml + b"\n")  # ✅ Write each event's XML to file
                f.write(event_xml)  # ✅ Write each event's XML to file
                j += 1

        f.write(b"</events>\n")
        print()
        print(f'Filtered events file successfully created at {output_path}')



if __name__ == '__main__':
    in_path = r"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\events_to_json\results\1s\v4\geojson_v4\evening_rush_hour\v4_geo_int2_merged.geojson"
    out_path = r"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\events_to_json\results\1s\v4\geojson_v4\evening_rush_hour\v4_geo_int2_merged_sorted.geojson"
    #num_events = 12677
    num_events = 109784000

    event_dict = sort_trips_by_person(in_path, num_events)
    dump_events(event_dict, out_path, num_events)
