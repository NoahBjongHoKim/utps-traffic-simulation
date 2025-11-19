import geopandas as gpd

f_path = r"C:\Users\joe99\PycharmProjects\d_arch\Traffic_Simulation_SJJ\data\v4\road_network_v4\road_network_v4_clip_raster.gpkg"
geo = gpd.read_file(f_path)

id_value = 11932598

# Filter the GeoDataFrame by the given id
link = geo[geo["linkId"] == id_value]

# Calculate length (assuming geometries are in a projected coordinate system)
if not link.empty:
    link_length = link.geometry.length.iloc[0]
    print(f"Link Length: {link_length}")
else:
    print(f"No feature found with id {id_value}")
