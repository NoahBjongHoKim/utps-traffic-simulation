"""
Trajectory interpolation functions.

Functions for interpolating agent positions between link enter/leave events.
Supports different time resolutions (1s, 20s) for various use cases.
"""

from typing import Tuple, List, Dict, Any, Optional
from .geometry import cal_arith_angle
from .transforms import time_to_timestamp


def interpolate_1s(
    link_id: str,
    start_time: int,
    end_time: int,
    travel_start: Tuple[float, float],
    travel_end: Tuple[float, float],
    person_id: str,
    freespeed: Optional[float] = None,
    link_length: Optional[float] = None
) -> List[Dict[str, Any]]:
    """
    Interpolate trajectory at 1-second intervals.

    Args:
        link_id: Link identifier
        start_time: Enter time (seconds since midnight)
        end_time: Leave time (seconds since midnight)
        travel_start: Starting coordinates (x, y)
        travel_end: Ending coordinates (x, y)
        person_id: Agent identifier
        freespeed: Free-flow speed of link (m/s), optional
        link_length: Length of link (m), optional

    Returns:
        List of GeoJSON features with 1-second interpolation
    """
    time_delta = end_time - start_time
    interpolated_features = []

    angle = cal_arith_angle(travel_start, travel_end)

    # Calculate speed fraction if freespeed and link_length provided
    if freespeed is not None and link_length is not None and time_delta > 0:
        speed_fraction = round(((link_length / time_delta) / freespeed), 1)
    else:
        speed_fraction = None

    # Interpolate every second
    for t in range(0, time_delta + 1):
        fraction = t / time_delta if time_delta > 0 else 0
        interpolated_x = round(
            travel_start[0] + fraction * (travel_end[0] - travel_start[0]), 12
        )
        interpolated_y = round(
            travel_start[1] + fraction * (travel_end[1] - travel_start[1]), 12
        )
        interpolated_time = start_time + t

        properties = {
            "t": time_to_timestamp(interpolated_time),
            "a": angle,
            "id": person_id,
        }

        # Add speed fraction if calculated
        if speed_fraction is not None:
            properties["s"] = speed_fraction

        feature = {
            "geometry": {"type": "Point", "coordinates": [interpolated_x, interpolated_y]},
            "properties": properties,
        }
        interpolated_features.append(feature)

    return interpolated_features


def interpolate_20s(
    link_id: str,
    start_time: int,
    end_time: int,
    travel_start: Tuple[float, float],
    travel_end: Tuple[float, float],
    person_id: str
) -> List[Dict[str, Any]]:
    """
    Interpolate trajectory at 20-second intervals (or start/end only for short trips).

    For trips <= 20 seconds: returns only start and end points.
    For trips > 20 seconds: interpolates at 20-second intervals plus end point.

    Args:
        link_id: Link identifier
        start_time: Enter time (seconds since midnight)
        end_time: Leave time (seconds since midnight)
        travel_start: Starting coordinates (x, y)
        travel_end: Ending coordinates (x, y)
        person_id: Agent identifier

    Returns:
        List of GeoJSON features with 20-second interpolation
    """
    time_delta = end_time - start_time
    interpolated_features = []

    angle = cal_arith_angle(travel_start, travel_end)

    # For short trips (<=20s), only return start and end
    if time_delta <= 20:
        for a, b in [(1, 0), (0, 1)]:  # (start, end) pairs
            x = a * travel_start[0] + b * travel_end[0]
            y = a * travel_start[1] + b * travel_end[1]
            time = a * start_time + b * end_time

            feature = {
                "geometry": {"type": "Point", "coordinates": [x, y]},
                "properties": {
                    "timestamp": time_to_timestamp(time),
                    "angle": angle,
                    "person_id": person_id,
                    "link_id": link_id,
                },
            }
            interpolated_features.append(feature)
    else:
        # For longer trips, interpolate in 20-second steps
        for t in range(0, time_delta, 20):
            fraction = t / time_delta
            interpolated_x = travel_start[0] + fraction * (travel_end[0] - travel_start[0])
            interpolated_y = travel_start[1] + fraction * (travel_end[1] - travel_start[1])
            interpolated_time = start_time + t

            feature = {
                "geometry": {"type": "Point", "coordinates": [interpolated_x, interpolated_y]},
                "properties": {
                    "timestamp": time_to_timestamp(interpolated_time),
                    "angle": angle,
                    "person_id": person_id,
                    "link_id": link_id,
                },
            }
            interpolated_features.append(feature)

        # Always add the final feature (end point)
        feature = {
            "geometry": {"type": "Point", "coordinates": [travel_end[0], travel_end[1]]},
            "properties": {
                "timestamp": time_to_timestamp(end_time),
                "angle": angle,
                "person_id": person_id,
                "link_id": link_id,
            },
        }
        interpolated_features.append(feature)

    return interpolated_features


# Alias for backward compatibility with events_to_geojson_008.py
interpolation = interpolate_1s
