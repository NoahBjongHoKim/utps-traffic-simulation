#!/bin/bash
#SBATCH -n 1
#SBATCH --time=5:00:00
#SBATCH --mem-per-cpu=96000
#SBATCH --tmp=80000
#SBATCH --job-name=refine_geojson
#SBATCH --output=refine_geojson.out
#SBATCH --error=refine_geojson.err

# Activate Conda environment
conda activate simpleEnv

# Run the Python script
python refine_geojson_002.py
