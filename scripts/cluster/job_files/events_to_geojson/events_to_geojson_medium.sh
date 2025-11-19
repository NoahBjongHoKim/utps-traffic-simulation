#!/bin/bash
#SBATCH -n 48
#SBATCH --time=4:00:00
#SBATCH --mem-per-cpu=2000
#SBATCH --tmp=80000
#SBATCH --job-name=medium_events_to_geojson
#SBATCH --output=medium_events_to_geojson_console.out
#SBATCH --error=medium_events_to_geojson_errors.err

# Activate Conda environment
conda activate simpleEnv

# Run the Python script
python events_to_geojson_007.py --input_path_xml xml_files/medium.xml --input_path_gpkg road_network_v4_clip_raster.gpkg --output_path_geojson medium.geojson --number_of_events 3796000