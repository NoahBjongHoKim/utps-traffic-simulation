using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Geoprocessing;
using System;
using System.Diagnostics;

namespace UTPS_Addin
{
    /// <summary>
    /// Test button to create a simple point feature layer on the active map.
    /// Includes extensive logging for debugging.
    /// </summary>
    internal class TestPointButton : Button
    {
        protected override void OnClick()
        {
            Debug.WriteLine("=== TEST POINT BUTTON CLICKED ===");

            try
            {
                // Check if there's an active map view
                if (MapView.Active == null)
                {
                    Debug.WriteLine("ERROR: No active map found");
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No active map found. Please open a map first.",
                        "No Active Map",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                    return;
                }

                Debug.WriteLine($"Active map: {MapView.Active.Map.Name}");

                // Create a point layer using QueuedTask
                Debug.WriteLine("Starting QueuedTask to create point layer...");

                QueuedTask.Run(() =>
                {
                    Debug.WriteLine("Inside QueuedTask");
                    Map map = MapView.Active.Map;
                    Debug.WriteLine($"Map reference obtained: {map.Name}");
                    Debug.WriteLine($"Map spatial reference: {map.SpatialReference?.Name ?? "null"}");

                    try
                    {
                        // Method 1: Try using CreateUniqueName to make an in-memory feature layer
                        Debug.WriteLine("METHOD 1: Trying to create in-memory feature class...");

                        // Create a simple point at a test location (San Francisco)
                        double x = -122.4194;
                        double y = 37.7749;
                        Debug.WriteLine($"Test point coordinates: ({x}, {y})");

                        // Create spatial reference (WGS84)
                        var spatialRef = SpatialReferenceBuilder.CreateSpatialReference(4326);
                        Debug.WriteLine($"Spatial reference created: {spatialRef.Name}");

                        // Use CreateFeatures geoprocessing tool to create a simple point layer
                        Debug.WriteLine("METHOD 2: Using CreateFeatures geoprocessing tool...");

                        // First, create the feature class in memory
                        var createParams = Geoprocessing.MakeValueArray(
                            "memory\\TestPoint",  // Output location (in-memory)
                            "POINT",              // Geometry type
                            "",                   // Template (none)
                            "",                   // Has M (no)
                            "",                   // Has Z (no)
                            spatialRef            // Spatial reference
                        );

                        Debug.WriteLine("Calling CreateFeatureclass...");
                        var createResult = Geoprocessing.ExecuteToolAsync(
                            "management.CreateFeatureclass",
                            createParams
                        ).Result;

                        Debug.WriteLine($"CreateFeatureclass completed. IsFailed: {createResult.IsFailed}");
                        Debug.WriteLine($"Return value: {createResult.ReturnValue}");
                        Debug.WriteLine($"Messages: {string.Join("\n", createResult.Messages)}");

                        if (createResult.IsFailed)
                        {
                            Debug.WriteLine($"ERROR Messages: {string.Join("\n", createResult.ErrorMessages)}");
                            throw new Exception($"CreateFeatureclass failed: {string.Join(", ", createResult.ErrorMessages)}");
                        }

                        // Add the layer to the map
                        Debug.WriteLine("Adding layer to map...");
                        string featureClassPath = createResult.ReturnValue;
                        Debug.WriteLine($"Feature class path: {featureClassPath}");

                        var addLayerParams = Geoprocessing.MakeValueArray(featureClassPath);
                        var addLayerResult = Geoprocessing.ExecuteToolAsync(
                            "management.MakeFeatureLayer",
                            addLayerParams
                        ).Result;

                        Debug.WriteLine($"MakeFeatureLayer completed. IsFailed: {addLayerResult.IsFailed}");
                        Debug.WriteLine($"Messages: {string.Join("\n", addLayerResult.Messages)}");

                        if (addLayerResult.IsFailed)
                        {
                            Debug.WriteLine($"ERROR Messages: {string.Join("\n", addLayerResult.ErrorMessages)}");
                        }

                        // Now add a point to it
                        Debug.WriteLine("Adding point feature...");
                        MapPoint point = MapPointBuilderEx.CreateMapPoint(x, y, spatialRef);
                        Debug.WriteLine($"MapPoint created: {point.X}, {point.Y}");

                        // Use Insert Cursor via geoprocessing
                        string geometryWKT = $"POINT ({x} {y})";
                        Debug.WriteLine($"Geometry WKT: {geometryWKT}");

                        // Alternative: Use XY To Point tool
                        Debug.WriteLine("METHOD 3: Using XY Table To Point...");

                        // Create a simple CSV in memory
                        string tempCsv = System.IO.Path.GetTempFileName() + ".csv";
                        Debug.WriteLine($"Creating temp CSV: {tempCsv}");

                        System.IO.File.WriteAllText(tempCsv, $"x,y,id\n{x},{y},1");
                        Debug.WriteLine($"CSV created with content:\n{System.IO.File.ReadAllText(tempCsv)}");

                        // Convert to points
                        string outputPoints = "memory\\TestPoints";
                        var xyParams = Geoprocessing.MakeValueArray(
                            tempCsv,        // Input table
                            outputPoints,   // Output feature class
                            "x",            // X field
                            "y",            // Y field
                            "",             // Z field (none)
                            spatialRef      // Coordinate system
                        );

                        Debug.WriteLine("Calling XYTableToPoint...");
                        var xyResult = Geoprocessing.ExecuteToolAsync(
                            "management.XYTableToPoint",
                            xyParams
                        ).Result;

                        Debug.WriteLine($"XYTableToPoint completed. IsFailed: {xyResult.IsFailed}");
                        Debug.WriteLine($"Return value: {xyResult.ReturnValue}");
                        Debug.WriteLine($"Messages: {string.Join("\n", xyResult.Messages)}");

                        if (xyResult.IsFailed)
                        {
                            Debug.WriteLine($"ERROR Messages: {string.Join("\n", xyResult.ErrorMessages)}");
                            throw new Exception($"XYTableToPoint failed: {string.Join(", ", xyResult.ErrorMessages)}");
                        }

                        // Add to map
                        Debug.WriteLine("Adding point layer to map...");
                        var finalParams = Geoprocessing.MakeValueArray(xyResult.ReturnValue, "Test Point Layer");
                        var finalResult = Geoprocessing.ExecuteToolAsync(
                            "management.MakeFeatureLayer",
                            finalParams
                        ).Result;

                        Debug.WriteLine($"Final MakeFeatureLayer completed. IsFailed: {finalResult.IsFailed}");
                        Debug.WriteLine($"Messages: {string.Join("\n", finalResult.Messages)}");

                        if (!finalResult.IsFailed)
                        {
                            Debug.WriteLine("SUCCESS! Point layer created");
                            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                $"Test point layer created successfully!\n\n" +
                                $"Location: San Francisco ({x}, {y})\n" +
                                $"Layer: {finalResult.ReturnValue}",
                                "Success",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information
                            );
                        }
                        else
                        {
                            Debug.WriteLine($"ERROR: {string.Join(", ", finalResult.ErrorMessages)}");
                            throw new Exception($"Failed to add layer: {string.Join(", ", finalResult.ErrorMessages)}");
                        }

                        // Clean up temp file
                        try
                        {
                            System.IO.File.Delete(tempCsv);
                            Debug.WriteLine("Temp CSV deleted");
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"EXCEPTION IN QUEUEDTASK: {ex.Message}");
                        Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        throw;
                    }
                }).Wait();

                Debug.WriteLine("QueuedTask completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OUTER EXCEPTION: {ex.Message}");
                Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error creating point layer:\n{ex.Message}\n\nCheck Output window for details.",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }

            Debug.WriteLine("=== TEST POINT BUTTON FINISHED ===");
        }
    }
}
