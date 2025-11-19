"""
XML I/O utilities for MATSim events processing.

Functions for parsing and converting XML event elements.
"""

from lxml import etree
from typing import Dict, Any


def element_to_dict(element: etree.Element) -> Dict[str, Any]:
    """
    Convert an lxml element to a dictionary (picklable for multiprocessing).

    Args:
        element: lxml Element to convert

    Returns:
        Dictionary with 'tag' and 'attrib' keys
    """
    return {'tag': element.tag, 'attrib': dict(element.attrib)}


def dict_to_element(data: Dict[str, Any]) -> etree.Element:
    """
    Convert a dictionary back to an lxml element.

    Args:
        data: Dictionary with 'tag' and 'attrib' keys

    Returns:
        lxml Element reconstructed from dictionary
    """
    return etree.Element(data['tag'], attrib=data['attrib'])
