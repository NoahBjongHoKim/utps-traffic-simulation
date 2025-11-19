#!/bin/bash
#SBATCH -n 48
#SBATCH --time=4:00:00
#SBATCH --mem-per-cpu=2000
#SBATCH --tmp=80000
#SBATCH --job-name=filter_events_xml
#SBATCH --output=filter_events_xml.out
#SBATCH --error=filter_events_xml.err

# Activate Conda environment
conda activate simpleEnv

# Run the Python script
python time_and_spatial_filter_events_001.py --gpkg_file_path ../data/road_network_v4_clip_raster.gpkg --xml_input_path ../data/xml_files/xml_parts/int1_p2.xml --xml_output_path ../data/xml_files/xml_parts/20_min.xml --time_interval_1 "08:30,08:50" --time_interval_2 "08:30,08:50"

