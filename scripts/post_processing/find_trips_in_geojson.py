"""
Created on 26.04.2025
@author: Joe Beck
find all the trips of a specified person id
export it into a geojson
"""



# Define the target person_id
target_person_id = '478'

geojson_file = r"/events_to_json/results/1s/V2/clip_raster/trips_v2_clip_raster_morning_rh.geojson"
output_folder = rf"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\events_to_json\results\1s\V2\clip_raster\filtered_trips_person_{target_person_id}"


#----------------------------------------------------------------------------------------------------------------------

import json
from datetime import datetime, timedelta
import os



def load_geojson(file_path):
    """Loads GeoJSON data from a file."""
    with open(file_path, 'r') as f:
        return json.load(f)

def save_geojson(data, output_path):
    """Saves data to a GeoJSON file."""
    with open(output_path, 'w') as f:
        json.dump(data, f, indent=4)




# Ensure the output folder exists
os.makedirs(output_folder, exist_ok=True)

geojson_data = load_geojson(geojson_file)

# Find all features for the given person_id
matching_features = [feature for feature in geojson_data['features'] if feature['properties'].get('person_id') == target_person_id]

# Sort features by timestamp (ensuring chronological order)
matching_features.sort(key=lambda x: x['properties'].get('timestamp'))

# Group features into separate trips based on time gaps
trips = []
current_trip = []

for i, feature in enumerate(matching_features):
    if i == 0:
        current_trip.append(feature)
        continue

    prev_time = datetime.strptime(matching_features[i - 1]['properties']['timestamp'], "%Y/%m/%d %H:%M:%S")
    curr_time = datetime.strptime(feature['properties']['timestamp'], "%Y/%m/%d %H:%M:%S")

    # If time gap is greater than 1 second, start a new trip
    if (curr_time - prev_time) > timedelta(seconds=1):
        trips.append(current_trip)  # Save the current trip
        current_trip = []  # Start a new trip

    current_trip.append(feature)

# Append the last trip if not empty
if current_trip:
    trips.append(current_trip)

# Save each trip as a separate GeoJSON file
for trip_index, trip in enumerate(trips):
    trip_output_path = os.path.join(output_folder, f"trip_{trip_index + 1}.geojson")
    trip_geojson = {"type": "FeatureCollection", "features": trip}
    save_geojson(trip_geojson, trip_output_path)
    print(f"Trip {trip_index + 1} saved to {trip_output_path}")

print(f"Total trips found: {len(trips)}")


