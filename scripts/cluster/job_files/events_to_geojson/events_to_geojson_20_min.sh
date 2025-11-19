#!/bin/bash
#SBATCH -n 48
#SBATCH --time=4:00:00
#SBATCH --mem-per-cpu=2000
#SBATCH --tmp=80000
#SBATCH --job-name=events_to_geojson
#SBATCH --output=events_to_geojson_console.out
#SBATCH --error=events_to_geojson_errors.err

# Run the Python script
python events_to_geojson_008.py --input_path_xml ../data/xml_files/20_min.xml --input_path_gpkg ../data/road_network_v4_clip_raster.gpkg --output_path_geojson 20_min.geojson --number_of_events 5785560