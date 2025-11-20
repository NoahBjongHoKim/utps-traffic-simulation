"""
Network cache utilities for fast loading of road network data.

Converts GeoPackage to Parquet format for 10-50x faster loading.
"""
from pathlib import Path
import pandas as pd
import geopandas as gpd
from shapely import wkb
from ..config import logger


def get_cache_path(gpkg_path: str | Path) -> Path:
    """
    Generate cache file path from GeoPackage path.

    Args:
        gpkg_path: Path to the GeoPackage file

    Returns:
        Path to the corresponding cache file

    Example:
        data/raw/v4/network.gpkg → data/interim/network_cache.parquet
    """
    gpkg_path = Path(gpkg_path)
    cache_dir = gpkg_path.parent.parent / "interim"
    cache_dir.mkdir(parents=True, exist_ok=True)

    cache_name = gpkg_path.stem + "_cache.parquet"
    return cache_dir / cache_name


def is_cache_valid(gpkg_path: str | Path, cache_path: str | Path) -> bool:
    """
    Check if cache is valid (exists and newer than source).

    Args:
        gpkg_path: Path to the GeoPackage file
        cache_path: Path to the cache file

    Returns:
        True if cache is valid, False otherwise
    """
    gpkg_path = Path(gpkg_path)
    cache_path = Path(cache_path)

    if not cache_path.exists():
        return False

    # Check if cache is newer than source
    gpkg_mtime = gpkg_path.stat().st_mtime
    cache_mtime = cache_path.stat().st_mtime

    return cache_mtime >= gpkg_mtime


def create_network_cache(gpkg_path: str | Path, cache_path: str | Path = None) -> Path:
    """
    Convert GeoPackage to Parquet cache for fast loading.

    This function reads the GeoPackage, converts geometries to WKB format,
    and saves as Parquet. Subsequent loads will be 10-50x faster.

    Args:
        gpkg_path: Path to the GeoPackage file
        cache_path: Optional path for cache file (auto-generated if None)

    Returns:
        Path to the created cache file
    """
    gpkg_path = Path(gpkg_path)

    if cache_path is None:
        cache_path = get_cache_path(gpkg_path)
    else:
        cache_path = Path(cache_path)

    logger.info(f"Creating network cache from {gpkg_path.name}...")

    # Load GeoPackage
    gdf = gpd.read_file(gpkg_path)
    logger.debug(f"Loaded GeoDataFrame: {len(gdf)} features")

    # Convert CRS to EPSG:4326 if needed
    if gdf.crs and gdf.crs.to_epsg() != 4326:
        logger.info(f"Transforming CRS from {gdf.crs} to EPSG:4326")
        gdf = gdf.to_crs(epsg=4326)
    else:
        logger.debug("Already in EPSG:4326")

    # Convert geometry to WKB for Parquet storage
    logger.debug("Converting geometries to WKB format")
    gdf['geometry_wkb'] = gdf.geometry.to_wkb()

    # Drop original geometry column and convert to DataFrame
    df = pd.DataFrame(gdf.drop(columns='geometry'))

    # Save to Parquet with compression
    logger.debug(f"Writing cache to {cache_path}")
    df.to_parquet(cache_path, compression='snappy', index=False)

    cache_size_mb = cache_path.stat().st_size / (1024 * 1024)
    logger.success(f"Network cache created: {cache_path.name} ({cache_size_mb:.2f} MB)")

    return cache_path


def load_network_from_cache(cache_path: str | Path) -> pd.DataFrame:
    """
    Load network from Parquet cache (fast).

    Args:
        cache_path: Path to the cache file

    Returns:
        DataFrame with network data and reconstructed geometries
    """
    cache_path = Path(cache_path)

    logger.info(f"Loading network from cache: {cache_path.name}")

    # Load Parquet file (very fast)
    df = pd.read_parquet(cache_path)
    logger.debug(f"Loaded {len(df):,} network links from cache")

    # Reconstruct geometries from WKB
    logger.debug("Reconstructing geometries from WKB")
    df['geometry'] = df['geometry_wkb'].apply(wkb.loads)

    # Drop WKB column (no longer needed)
    df = df.drop(columns='geometry_wkb')

    logger.success(f"Network loaded: {len(df):,} links")

    return df


def load_network_cached(gpkg_path: str | Path, force_refresh: bool = False) -> pd.DataFrame:
    """
    Load network with automatic caching.

    This function automatically:
    1. Checks if a valid cache exists
    2. Creates cache if missing or outdated
    3. Loads from cache (or creates and loads)

    Args:
        gpkg_path: Path to the GeoPackage file
        force_refresh: Force recreation of cache even if valid

    Returns:
        DataFrame with network data
    """
    gpkg_path = Path(gpkg_path)
    cache_path = get_cache_path(gpkg_path)

    # Check if cache needs to be created/refreshed
    if force_refresh or not is_cache_valid(gpkg_path, cache_path):
        if force_refresh:
            logger.info("Force refresh requested - recreating cache")
        else:
            logger.info("Cache missing or outdated - creating new cache")

        create_network_cache(gpkg_path, cache_path)
    else:
        logger.debug("Valid cache found")

    # Load from cache
    return load_network_from_cache(cache_path)


def build_link_attributes_dict(network_df: pd.DataFrame,
                               link_id_col: str = 'linkId',
                               precompute_endpoints: bool = True) -> dict:
    """
    Convert network DataFrame to dictionary for fast lookups.

    Optionally precomputes travel endpoints and bearing for each link,
    which provides massive speedup (10-100x) during trajectory processing.

    Args:
        network_df: DataFrame with network data
        link_id_col: Column name for link IDs
        precompute_endpoints: If True, precomputes travel start/end coords and bearing

    Returns:
        Dictionary mapping link_id (str) → attributes
        If precompute_endpoints=True, includes 'travel_start', 'travel_end', 'bearing'
    """
    import math

    logger.info("Building link attributes dictionary...")

    # IMPORTANT: Convert link IDs to strings for consistency with parquet data
    network_df = network_df.copy()
    network_df[link_id_col] = network_df[link_id_col].astype(str)

    # Also convert from/to to strings
    if 'from' in network_df.columns:
        network_df['from'] = network_df['from'].astype(str)
    if 'to' in network_df.columns:
        network_df['to'] = network_df['to'].astype(str)

    # Convert to dict using to_dict('index') - much faster than iterrows()
    df_indexed = network_df.set_index(link_id_col)
    link_attrs = df_indexed.to_dict('index')

    if precompute_endpoints:
        logger.info("Precomputing travel endpoints and bearings for all links...")

        # Build node→links lookup (much faster than repeated searches)
        from_node_links = {}
        to_node_links = {}

        for link_id, attrs in link_attrs.items():
            from_node = attrs.get('from')
            to_node = attrs.get('to')

            if from_node not in from_node_links:
                from_node_links[from_node] = []
            from_node_links[from_node].append(link_id)

            if to_node not in to_node_links:
                to_node_links[to_node] = []
            to_node_links[to_node].append(link_id)

        # Precompute for each link
        for link_id, attrs in link_attrs.items():
            geom = attrs.get('geometry')
            if geom is None:
                continue

            from_node = attrs.get('from')
            to_node = attrs.get('to')

            # Get edge coordinates (handle both LINESTRING and MULTILINESTRING)
            try:
                if geom.geom_type == 'LineString':
                    ec1 = geom.coords[0]
                    ec2 = geom.coords[-1]
                elif geom.geom_type == 'MultiLineString':
                    # Use first coord of first segment and last coord of last segment
                    ec1 = geom.geoms[0].coords[0]
                    ec2 = geom.geoms[-1].coords[-1]
                else:
                    logger.warning(f"Link {link_id}: Unexpected geometry type {geom.geom_type}, skipping")
                    continue
            except Exception as e:
                logger.warning(f"Link {link_id}: Error extracting coordinates: {e}, skipping")
                continue

            # Find previous link (connects to from_node)
            # Previous link: ends at current link's from_node
            previous_link = None
            if from_node in to_node_links:
                for other_link_id in to_node_links[from_node]:
                    if other_link_id != link_id and link_attrs[other_link_id].get('from') != to_node:
                        previous_link = other_link_id
                        break

            # Find next link (connects to to_node)
            # Next link: starts at current link's to_node
            next_link = None
            if to_node in from_node_links:
                for other_link_id in from_node_links[to_node]:
                    if other_link_id != link_id and link_attrs[other_link_id].get('to') != from_node:
                        next_link = other_link_id
                        break

            # Determine travel endpoints
            if previous_link and previous_link in link_attrs:
                prev_geom = link_attrs[previous_link].get('geometry')
                if prev_geom:
                    ef1, ef2 = prev_geom.coords[0], prev_geom.coords[-1]
                else:
                    ef1, ef2 = ec1, ec1
            else:
                ef1, ef2 = ec1, ec1

            if next_link and next_link in link_attrs:
                next_geom = link_attrs[next_link].get('geometry')
                if next_geom:
                    et1, et2 = next_geom.coords[0], next_geom.coords[-1]
                else:
                    et1, et2 = ec2, ec2
            else:
                et1, et2 = ec2, ec2

            # Determine actual travel start/end
            travel_start = ef1 if ec1 == ef1 else (ef2 if ec1 == ef2 else ec1)
            travel_end = et1 if ec2 == et1 else (et2 if ec2 == et2 else ec2)

            # Calculate bearing
            lat1, lon1 = map(math.radians, travel_start)
            lat2, lon2 = map(math.radians, travel_end)
            delta_lon = lon2 - lon1
            x = math.cos(lat2) * math.sin(delta_lon)
            y = math.cos(lat1) * math.sin(lat2) - math.sin(lat1) * math.cos(lat2) * math.cos(delta_lon)
            bearing = round((math.degrees(math.atan2(x, y)) + 360) % 360)

            # Calculate center point (for heatmaps)
            try:
                center_point = geom.interpolate(0.5, normalized=True)
                link_center = (center_point.x, center_point.y)
            except Exception as e:
                logger.warning(f"Link {link_id}: Error calculating center: {e}, using midpoint")
                link_center = ((travel_start[0] + travel_end[0]) / 2, (travel_start[1] + travel_end[1]) / 2)

            # Store precomputed values
            attrs['travel_start'] = travel_start
            attrs['travel_end'] = travel_end
            attrs['bearing'] = bearing
            attrs['center'] = link_center

        logger.success(f"Precomputed endpoints, bearings, and centers for {len(link_attrs):,} links")

    logger.success(f"Dictionary created: {len(link_attrs):,} links")
    logger.debug(f"Sample link IDs (first 5): {list(link_attrs.keys())[:5]}")

    return link_attrs
