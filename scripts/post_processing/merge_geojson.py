"""
Created on 26.04.2025
@author: Joe Beck
This code is written to merge two geojson files. There are two functions implemented
"""


def merge_large_geojson(output_path, file1_path, file2_path, file3_path=None):
    with open(file1_path, 'r', encoding='utf-8') as f1, open(file2_path, 'r', encoding='utf-8') as f2, open(output_path, 'w', encoding='utf-8') as out:
        # Write the start of the output GeoJSON
        out.write('{"type":"FeatureCollection","features":[\n')

        def extract_features(file):
            started = False
            for line in file:
                if '"features": [' in line:
                    started = True
                    continue
                if started and line.strip().startswith(']'):
                    break  # Stop at end of features
                yield line.strip() + '\n'


        # Read and write first file's features
        out.writelines(extract_features(f1))
        out.write(",")

        # Read and write second file's features
        out.writelines(extract_features(f2))

        # If a third file is provided, merge its features as well
        if file3_path:
            with open(file3_path, 'r', encoding='utf-8') as f3:
                out.write(",")  # Ensure proper separation
                out.writelines(extract_features(f3))

        # Close JSON object
        out.write(']}')

    print(f"Merged file saved to: {output_path}")



if __name__ == '__main__':
    input_path_csv_1 = 'your_geojson_input_path'
    input_path_csv_2 ='your_geojson_input_path'
    input_path_csv_3 ='your_geojson_input_path'

    output_path = 'your_geojson_output_path'

    merge_large_geojson(input_path_csv_1, input_path_csv_2, input_path_csv_3, output_path)
    print("Merging completed efficiently!")
