"""
GeoPackage I/O utilities for traffic simulation.

Functions for loading and processing road network GeoPackage files.
"""

import geopandas as gpd
from pathlib import Path


def load_gpkg(path: str | Path, target_crs: int = 4326) -> gpd.GeoDataFrame:
    """
    Load a GeoPackage file and ensure it's in the target CRS.

    Args:
        path: Path to the GeoPackage file
        target_crs: Target EPSG code (default: 4326 for WGS84)

    Returns:
        GeoDataFrame with road network in target CRS
    """
    print('--> Loading Geopackage')
    network_gdf = gpd.read_file(path)

    if network_gdf.crs and network_gdf.crs.to_epsg() == target_crs:
        print(f"--> The GeoPackage is already in EPSG:{target_crs}.")
    else:
        print(f"--> The current CRS is {network_gdf.crs}. Transforming to EPSG:{target_crs}...")
        network_gdf = network_gdf.to_crs(epsg=target_crs)
        print("--> Transformation complete, Geopackage loaded.")

    return network_gdf
