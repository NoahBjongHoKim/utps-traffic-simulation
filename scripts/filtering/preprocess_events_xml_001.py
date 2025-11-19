"""
date: 20.03.25
This version is the original preprocessing file which has all the functions implemented.
However: No multiprocessing, loading the whole xml into ram.
So not suitable for executing locally.


General Inputs:
- Two geopackages, one with deleted links and one that contains all links used in the original
  events_v4.xml file.
- The xml events file

function: delete_links_with_zero_length()
This function deletes links with zero length in the input geopackage and safes it into a new
geopackage and returns it as well.

function: find_deleted_link_ids()
This function compares the two input geopackages and safes the link_ids that where deleted in a
csv file.

function: delete_trips_with_deleted_links()
loads the csv file created by find_deleted_link_ids() as well as the events_v4.xml file and deletes
all events corresponding to deleted links.

function: compare_trips_and_gpkg(xml, gpkg)
Finds and removes events in xml file with link id not in geopackage. This function is the generalized form of the
delete_trips_with_deleted_links() which needs no csv file but directly checks which link ids are missing in the geopackage.


"""

import geopandas as gpd
import pandas as pd
import xml.etree.ElementTree as ET

def delete_links_with_zero_length(geo, output_path):
    # Ensure geometry column is valid
    if geo.empty or 'geometry' not in geo.columns:
        print("Invalid GeoDataFrame.")
        return

    # Filter out zero-length geometries
    print('filtering out zero length links...')
    geo_filtered = geo[geo.geometry.length > 0].copy()

    # Save to a new GeoPackage
    geo_filtered.to_file(output_path, driver="GPKG")

    print(f"Deleted {len(geo) - len(geo_filtered)} links with zero length.")
    print(f"Filtered data saved to {output_path}")

    return geo_filtered  # Still returning in case further processing is needed


# find the link ids which were deleted
def find_deleted_link_ids(backup_gdf, modified_gdf, output_csv_path):

    # Extract feature IDs (assuming "link" is the unique identifier)
    backup_ids = set(backup_gdf["id"])
    modified_ids = set(modified_gdf["id"])

    # Find deleted feature IDs
    deleted_ids = backup_ids - modified_ids

    # Save deleted IDs to CSV
    pd.DataFrame(deleted_ids, columns=["Deleted_IDs"]).to_csv(output_csv_path, index=False)

    print(f"Deleted train route IDs saved to: {output_csv_path}")

def load_xml(path_xml):
    tree = ET.parse(path_xml)
    xml_root = tree.getroot()
    print('--> XML loaded')
    return tree, xml_root


def safe_xml(tree, xml, output_path):
    tree.write(xml)
    print(f"--> Updated XML saved to: {output_path}")


# delete the trips traveling deleted links
def delete_trips_with_deleted_links(xml, deleted_links_csv):

    # Load deleted link IDs from the CSV file
    deleted_links_df = pd.read_csv(deleted_links_csv)
    deleted_ids = set(deleted_links_df['Deleted_IDs'].astype(str))  # Ensure IDs are strings
    print(f"--> Loaded {len(deleted_ids)} deleted link IDs")

    # Find and remove events with deleted links
    events_to_remove = []
    for event in xml.findall("event"):
        link_id = event.get("link")
        if link_id and link_id in deleted_ids:
            events_to_remove.append(event)

    for event in events_to_remove:
        xml.remove(event)

    print(f"--> Removed {len(events_to_remove)} events containing deleted link IDs")
    return xml


def compare_trips_and_gpkg(xml, gpkg):
    # Find and remove events with link id not in geopackage
    print('start with editing xml file')
    deletion_counter = 0
    number_of_events = len(xml.findall("event"))
    progress_counter = 1

    valid_link_ids = set(gpkg["linkId"])

    for event in xml.findall("event"):
        link_id = event.get("link")

        if not link_id or link_id not in valid_link_ids:
            xml.remove(event)
            deletion_counter += 1

        percentage_complete = (progress_counter / number_of_events) * 100
        print(f"\rProcessed {percentage_complete:.1f}% of events", end='', flush=True)
        progress_counter += 1

    print(f"--> Removed {deletion_counter} events containing deleted links")
    return xml


if __name__ == '__main__':

    # geopackage part ----------------------------------------------------------------------------------

    # Geopackage input paths
    backup_gpkg_path = r"/\original_data\v2\Road_network\original_data\network_stats.gpkg"
    modified_gpkg_path = r"/\original_data\v2\Road_network\No_public_transport\network_stats_no_public_trans.gpkg"


    # Geopackage output path for new geopackage
    modified_gpkg_no_zero_length_out_path = r"/\original_data\v2\Road_network\No_public_transport\manually_deleted_public_trans_network_no_zero_links.gpkg"
    deleted_links_csv_path = r"/\original_data\v2\Road_network\No_public_transport\deleted_links.csv"

    # Load both GeoPackage files
    print('Loading Geopackages...')
    #backup_gpkg = gpd.read_file(backup_gpkg_path)
    modified_gpkg = gpd.read_file(modified_gpkg_path)


    # modify GeoPackages
    #no_zero_length_gdf = delete_links_with_zero_length(modified_gpkg, modified_gpkg_no_zero_length_out_path)
    #find_deleted_link_ids(backup_gpkg, no_zero_length_gdf, deleted_links_csv_path)


    # xml part ----------------------------------------------------------------------------------

    # In and output paths for the xml events file
    xml_path = r"/\original_data\v3\events_v3.xml"
    output_path_updated_xml = r"/\original_data\v3\events_v3_edited.xml"
    """
    # load the xml events file
    xml_tree, xml_file = load_xml(xml_path)

    # edit the xml. ATTENTION: choose the right geopackage as input in the compare_trips function depending on the
    # chosen geopackage editing steps above!
    xml_time_filtered = filter_events_by_time(xml_file, (27000, 32400), (59400, 64800))
    #xml_edited = compare_trips_and_gpkg(xml_file, modified_gpkg)
    #xml_links_deleted = delete_trips_with_deleted_links(xml_time_filtered, deleted_links_csv_path)

    # Save the updated XML file
    safe_xml(xml_tree, xml_time_filtered, output_path_updated_xml)

    """