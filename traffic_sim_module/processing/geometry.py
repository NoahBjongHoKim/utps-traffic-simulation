"""
Geometric calculations for trajectory processing.

Functions for distance, bearing, and coordinate manipulation.
"""

import math
from typing import Tuple, Dict, Any, Optional


def distance(point1: Tuple[float, float], point2: Tuple[float, float]) -> float:
    """
    Calculate the Euclidean distance between two points.

    Args:
        point1: First point (x, y)
        point2: Second point (x, y)

    Returns:
        Euclidean distance between points
    """
    return ((point2[0] - point1[0]) ** 2 + (point2[1] - point1[1]) ** 2) ** 0.5


def cal_arith_angle(start_coords: Tuple[float, float],
                    end_coords: Tuple[float, float]) -> int:
    """
    Calculate arithmetic bearing angle between two coordinates.

    Args:
        start_coords: Starting coordinates (lat, lon)
        end_coords: Ending coordinates (lat, lon)

    Returns:
        Bearing in degrees (0° = east, counterclockwise, rounded to nearest degree)
    """
    lat1, lon1 = map(math.radians, start_coords)
    lat2, lon2 = map(math.radians, end_coords)

    delta_lon = lon2 - lon1

    x = math.cos(lat2) * math.sin(delta_lon)
    y = math.cos(lat1) * math.sin(lat2) - math.sin(lat1) * math.cos(lat2) * math.cos(delta_lon)

    angle = math.atan2(x, y)
    angle_degrees = math.degrees(angle)

    # Convert to arithmetic angle (0° = east, counterclockwise)
    bearing = round((angle_degrees + 360) % 360)

    return bearing


def get_neighboring_link_ids(
    from_node_current_link: str,
    to_node_current_link: str,
    link_attributes: Dict[str, Dict[str, Any]]
) -> Tuple[Optional[str], Optional[str]]:
    """
    Find the IDs of neighboring (previous and preceding) links.

    Args:
        from_node_current_link: From-node of current link
        to_node_current_link: To-node of current link
        link_attributes: Dictionary mapping link IDs to their attributes

    Returns:
        Tuple of (previous_link_id, preceding_link_id), either can be None
    """
    previous_link = None
    preceding_link = None

    for other_link_id, data in link_attributes.items():
        if data["to"] == from_node_current_link and data["from"] != to_node_current_link:
            if previous_link is None:
                previous_link = other_link_id

        elif data["from"] == to_node_current_link and data["to"] != from_node_current_link:
            if preceding_link is None:
                preceding_link = other_link_id

    return previous_link, preceding_link


def extract_neighboring_edge_coords(
    link_id: Optional[str],
    link_attributes: Dict[str, Dict[str, Any]],
    fallback: Tuple[float, float]
) -> Tuple[Tuple[float, float], Tuple[float, float]]:
    """
    Extract edge coordinates of a neighboring link.

    Args:
        link_id: Link ID (can be None for terminal links)
        link_attributes: Dictionary mapping link IDs to their attributes
        fallback: Coordinates to use if link_id is None

    Returns:
        Tuple of (start_coord, end_coord) for the link
    """
    if link_id is not None:
        link_geom = link_attributes[link_id]["geometry"]
        edge_coord_1 = link_geom.coords[0]
        edge_coord_2 = link_geom.coords[-1]
    else:
        # If no neighboring link (end or start of road), use fallback
        edge_coord_1 = edge_coord_2 = fallback

    return edge_coord_1, edge_coord_2


def get_travel_start_end_coords(
    link_id: str,
    link_attributes: Dict[str, Dict[str, Any]]
) -> Tuple[Tuple[float, float], Tuple[float, float]]:
    """
    Determine the start and end coordinates for travel on a link.

    Takes into account neighboring links to provide smooth trajectories
    across link boundaries.

    Args:
        link_id: ID of the current link
        link_attributes: Dictionary mapping link IDs to their attributes

    Returns:
        Tuple of (travel_start_coords, travel_end_coords)
    """
    # Read out nodes of the current link
    from_node_current_link = link_attributes[link_id]["from"]
    to_node_current_link = link_attributes[link_id]["to"]

    previous_link, preceding_link = get_neighboring_link_ids(
        from_node_current_link, to_node_current_link, link_attributes
    )

    # Extract edge coordinates of the current link
    current_link_geom = link_attributes[link_id]["geometry"]
    ec1 = current_link_geom.coords[0]
    ec2 = current_link_geom.coords[-1]

    # Extract edge coordinates of neighboring links (handle None cases)
    ef1, ef2 = extract_neighboring_edge_coords(previous_link, link_attributes, ec1)
    et1, et2 = extract_neighboring_edge_coords(preceding_link, link_attributes, ec2)

    # Determine edge points and direction of travel
    travel_start = None
    travel_end = None

    if ec1 in {ef1, ef2}:
        travel_start = ef1 if ec1 == ef1 else ef2
    elif ec1 in {et1, et2}:
        travel_end = et1 if ec1 == et1 else et2
    else:
        travel_start = ec1  # Fallback to ec1

    if ec2 in {ef1, ef2}:
        travel_start = ef1 if ec2 == ef1 else ef2
    elif ec2 in {et1, et2}:
        travel_end = et1 if ec2 == et1 else et2
    else:
        travel_end = ec2  # Fallback to ec2

    return travel_start, travel_end
