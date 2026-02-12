using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Core.Geoprocessing;
using System;
using System.IO;
using System.Diagnostics;

namespace UTPS_Addin
{
    /// <summary>
    /// Test button to import a Parquet file as a table into the active map.
    /// Includes extensive logging for debugging.
    /// </summary>
    internal class TestParquetButton : Button
    {
        protected override void OnClick()
        {
            Debug.WriteLine("=== TEST PARQUET BUTTON CLICKED ===");

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

                // Ask user to select a Parquet file
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Parquet File",
                    Filter = "Parquet Files (*.parquet)|*.parquet|All Files (*.*)|*.*",
                    Multiselect = false
                };

                Debug.WriteLine("Opening file dialog...");
                bool? result = dialog.ShowDialog();

                if (result != true)
                {
                    Debug.WriteLine("User cancelled file selection");
                    return;
                }

                string parquetPath = dialog.FileName;
                Debug.WriteLine($"Selected file: {parquetPath}");
                Debug.WriteLine($"File exists: {File.Exists(parquetPath)}");
                Debug.WriteLine($"File size: {new FileInfo(parquetPath).Length} bytes");

                // Import using QueuedTask
                Debug.WriteLine("Starting QueuedTask to import Parquet...");

                QueuedTask.Run(() =>
                {
                    Debug.WriteLine("Inside QueuedTask");
                    Map map = MapView.Active.Map;
                    Debug.WriteLine($"Map reference obtained: {map.Name}");

                    try
                    {
                        // Method 1: Try using StandaloneTableFactory
                        Debug.WriteLine("METHOD 1: Trying StandaloneTableFactory...");
                        Debug.WriteLine($"Creating Uri from path: {parquetPath}");

                        Uri parquetUri = new Uri(parquetPath);
                        Debug.WriteLine($"Uri created: {parquetUri}");
                        Debug.WriteLine($"Uri scheme: {parquetUri.Scheme}");
                        Debug.WriteLine($"Uri absolute path: {parquetUri.AbsolutePath}");

                        StandaloneTable table = StandaloneTableFactory.Instance.CreateStandaloneTable(parquetUri, map);

                        if (table != null)
                        {
                            Debug.WriteLine($"SUCCESS! Table created: {table.Name}");
                            Debug.WriteLine($"Table URI: {table.URI}");

                            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                $"Parquet table added successfully!\n\nTable name: {table.Name}",
                                "Success",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information
                            );
                        }
                        else
                        {
                            Debug.WriteLine("ERROR: CreateStandaloneTable returned null");
                            throw new Exception("CreateStandaloneTable returned null");
                        }
                    }
                    catch (Exception ex1)
                    {
                        Debug.WriteLine($"METHOD 1 FAILED: {ex1.Message}");
                        Debug.WriteLine($"Exception type: {ex1.GetType().Name}");
                        Debug.WriteLine($"Stack trace: {ex1.StackTrace}");

                        // Method 2: Try using Geoprocessing tool
                        Debug.WriteLine("METHOD 2: Trying Geoprocessing.MakeTableView...");
                        try
                        {
                            var parameters = Geoprocessing.MakeValueArray(parquetPath, "ParquetTable");
                            Debug.WriteLine($"Parameters created: {string.Join(", ", parameters)}");

                            var gpResult = Geoprocessing.ExecuteToolAsync("management.MakeTableView", parameters).Result;

                            Debug.WriteLine($"Geoprocessing completed. IsFailed: {gpResult.IsFailed}");
                            Debug.WriteLine($"Return value: {gpResult.ReturnValue}");
                            Debug.WriteLine($"Messages: {string.Join("\n", gpResult.Messages)}");

                            if (gpResult.IsFailed)
                            {
                                Debug.WriteLine($"ERROR Messages: {string.Join("\n", gpResult.ErrorMessages)}");
                                throw new Exception($"Geoprocessing failed: {string.Join(", ", gpResult.ErrorMessages)}");
                            }
                            else
                            {
                                Debug.WriteLine("SUCCESS with geoprocessing!");
                                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                    "Parquet table added using geoprocessing!",
                                    "Success",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information
                                );
                            }
                        }
                        catch (Exception ex2)
                        {
                            Debug.WriteLine($"METHOD 2 FAILED: {ex2.Message}");
                            Debug.WriteLine($"Exception type: {ex2.GetType().Name}");
                            Debug.WriteLine($"Stack trace: {ex2.StackTrace}");
                            throw;
                        }
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
                    $"Error importing Parquet file:\n{ex.Message}\n\nCheck Output window for details.",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }

            Debug.WriteLine("=== TEST PARQUET BUTTON FINISHED ===");
        }
    }
}
