"""
Created on 26.04.2025
@author: Joe Beck
- sorts geojson features by person
- removes duplicates because the xml-to-geojson converter creates a feature for the end of current node
  and start of next node even though they have same timestamp and coordinates
- fast but memory intensive since the whole geojson is loaded into memory and the outputfile is first created
  and saved afterwards. (needs about 2 times input file size memory)
"""

import json

def load_geojson(file_path):
    """Loads GeoJSON data from a file."""
    print('loading geojson...')
    with open(file_path, 'r') as f:
        return json.load(f)


def compact_save_geojson(data, output_path, num_features):
    """Saves GeoJSON with one feature per line (compact format)."""

    with open(output_path, 'w') as f:
        f.write('{"type": "FeatureCollection", "features": [\n')

        features = data["features"]
        for i, feature in enumerate(features):
            print(f'\rsaving geojson features: ({(i / num_features) * 100:.2f}%)', end='', flush=True)
            json_str = json.dumps(feature)  # compact, one-line
            f.write(json_str)
            if i < len(features) - 1:
                f.write(',\n')  # comma between features
            else:
                f.write('\n')  # no comma after last feature

        f.write(']}\n')
    print()
    print(f"Processed data saved to {output_path}")


def sort_geo(geojson_data, num_trips):
    # Step 1: Split by person_id
    person_trips = {}

    for k, feature in enumerate(geojson_data['features'], 1):
        print(f'\rcreating dict for fast lookup: ({(k / num_trips) * 100:.2f}%)', end='', flush=True)
        person_id = feature['properties'].get('person_id')
        if person_id:
            person_trips.setdefault(person_id, []).append(feature)
    print()
    print('creating dict done')

    # Step 2: Process each person's trips
    sorted_features = []

    number_of_people = len(person_trips)
    j = 0
    for person_id, features in person_trips.items():
        print(f"\rProcessing trips of person {person_id}. People done: ({(j / number_of_people) * 100:.2f}%)", end='', flush=True)

        # Sort features by timestamp
        features.sort(key=lambda x: x['properties'].get('timestamp'))

        last_feature = None
        for i, feature in enumerate(features):
            current_coords = feature['geometry'].get('coordinates')
            current_timestamp = feature['properties'].get('timestamp')
            current_person_id = feature['properties'].get('person_id')

            if last_feature:
                last_coords = last_feature['geometry'].get('coordinates')
                last_timestamp = last_feature['properties'].get('timestamp')
                last_person_id = last_feature['properties'].get('person_id')

                if (current_coords == last_coords and
                    current_timestamp == last_timestamp and
                    current_person_id == last_person_id):
                    continue  # skip duplicate

            sorted_features.append(feature)
            last_feature = feature

        j += 1

    print()
    final_geojson = {"type": "FeatureCollection", "features": sorted_features}
    print('processing finished')
    return final_geojson



def start_postprocessing(in_path, out_path, length):
    geojson_file = load_geojson(in_path)
    print('loading geojson done.')
    processed_geojson = sort_geo(geojson_file, length)

    # Export
    compact_save_geojson(processed_geojson, out_path, length)


if __name__ == '__main__':
    input_path = 'your_geojson_input_path'
    out_path = 'your_geojson_output_path'
    num_features = 'nuber_of_features_for_progress'

    start_postprocessing(input_path, out_path, num_features)




