"""
Created on 26.04.2025
@author: Joe Beck
- sorts geojson features by person
- removes duplicates because the xml-to-geojson converter creates a feature for the end of current node
  and start of next node even though they have same timestamp and coordinates
- Memory efficient, since input file is read sequentially and output features are saved after the processing
  for a single agent is finished. This results in very low memory usage.
- Slow because hard drive is slower than memory.
"""

import json
import ijson


def create_dict(file_path, num_trips = 100000):
    with open(file_path, 'r', encoding='utf-8') as file:
        features = ijson.items(file, 'features.item')

        # Step 1: Split by person_id
        person_trips = {}

        number_of_lines = 0
        for k, feature in enumerate(features, 1):
            print(f'\rcreating dict for fast lookup: ({(k / num_trips) * 100:.2f}%)', end='', flush=True)
            person_id = feature['properties'].get('id')
            if person_id:
                person_trips.setdefault(person_id, []).append(feature)
                number_of_lines += 1
        print()
        print('creating dict done')
        return person_trips, number_of_lines


def sort_geo(file_path, out_file_path, num_trips):
    # Step 1: Create trips dict for fast lookup
    person_trips, total_lines = create_dict(file_path, num_trips)

    # Step 2: Process each person's trips
    sorted_features = []

    number_of_people = len(person_trips)
    last_counter = 0
    lines_done = 0
    j = 0

    with open(out_file_path, 'w') as f:
        f.write('{"type": "FeatureCollection", "features": [\n')

        for person_id, features in person_trips.items():
            print(f"\rProcessing trips of person {person_id}. People done: ({(j / number_of_people) * 100:.2f}%)", end='', flush=True)

            # Sort features by timestamp
            features.sort(key=lambda x: x['properties'].get('t'))

            last_feature = None

            for i, feature in enumerate(features):
                lines_done += 1
                current_coords = feature['geometry'].get('coordinates')
                current_timestamp = feature['properties'].get('t')
                current_person_id = feature['properties'].get('id')

                if last_feature:
                    last_coords = last_feature['geometry'].get('coordinates')
                    last_timestamp = last_feature['properties'].get('t')
                    last_person_id = last_feature['properties'].get('id')

                    if (current_coords == last_coords and
                        current_timestamp == last_timestamp and
                        current_person_id == last_person_id):
                        continue  # skip duplicate


                sorted_features.append(feature)
                last_feature = feature

            for geo_feature in sorted_features:

                json_str = json.dumps(geo_feature, default=float)  # compact, one-line
                f.write(json_str)
                if lines_done != total_lines:
                    f.write(',\n')  # comma between features
                else:
                    last_counter += 1
                    if last_counter == len(sorted_features):
                        f.write('\n')  # no comma after last feature
                    else:
                        f.write(',\n')

            sorted_features.clear()  # free memory after writing
            j += 1

        print()
        print(f'lines done: {lines_done}')
        f.write(']}\n')

    print()
    print(f"Processed data saved to {out_file_path}")



if __name__ == '__main__':
    input_path = 'your_geojson_input_path'
    out_path = 'your_geojson_output_path'
    num_features = 'nuber_of_features_for_progress'

    sort_geo(input_path, out_path, num_features)




