#!/bin/bash
#SBATCH -n 48
#SBATCH --time=3:30:00
#SBATCH --mem-per-cpu=2000
#SBATCH --tmp=80000
#SBATCH --job-name=preprocess_events_xml
#SBATCH --output=preprocess_events_xml.out
#SBATCH --error=preprocess_events_xml.err

# Activate Conda environment
conda activate simpleEnv

# Run the Python script
python time_filter_events_001.py --xml_input_path events_v3.xml --xml_output_path events_v3_time_filtered_005.xml

