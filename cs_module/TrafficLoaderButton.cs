using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Core.Data;
using System;
using System.IO;
using System.Threading.Tasks;

namespace UTPS_Addin
{
    /// <summary>
    /// Button to load traffic simulation data from XML files.
    /// Opens a configuration dialog for file selection and time interval parameters.
    /// </summary>
    internal class TrafficLoaderButton : Button
    {
        /// <summary>
        /// Called when the button is clicked.
        /// Opens the traffic configuration dialog on the UI thread.
        /// </summary>
        protected override void OnClick()
        {
            try
            {
                // Validate Python environment first
                if (!PythonRunner.ValidatePythonEnvironment(out string validationError))
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        validationError,
                        "Python Environment Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error
                    );
                    return;
                }

                // Create and show the configuration dialog
                var dialog = new TrafficConfigDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

                // Show dialog and wait for user input
                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    // User clicked OK - get the configuration
                    var viewModel = dialog.DataContext as TrafficConfigViewModel;

                    if (viewModel != null)
                    {
                        // Log the configuration (for debugging)
                        System.Diagnostics.Debug.WriteLine($"XML File: {viewModel.XmlFilePath}");
                        System.Diagnostics.Debug.WriteLine($"Start Time: {viewModel.StartTime}");
                        System.Diagnostics.Debug.WriteLine($"End Time: {viewModel.EndTime}");
                        System.Diagnostics.Debug.WriteLine($"GPKG File: {viewModel.GpkgFilePath}");
                        System.Diagnostics.Debug.WriteLine($"Output Path: {viewModel.OutputPath}");

                        // Start Python processing
                        StartProcessing(viewModel);
                    }
                }
                else
                {
                    // User clicked Cancel
                    System.Diagnostics.Debug.WriteLine("Traffic loader dialog cancelled by user");
                }
            }
            catch (Exception ex)
            {
                // Show error message to user
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error opening traffic loader dialog:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );

                // Log full exception for debugging
                System.Diagnostics.Debug.WriteLine($"Error in TrafficLoaderButton: {ex}");
            }
        }

        /// <summary>
        /// Start Python processing with progress dialog.
        /// </summary>
        private void StartProcessing(TrafficConfigViewModel config)
        {
            try
            {
                // Create Python runner
                var runner = new PythonRunner();

                // Create and show progress dialog
                var progressDialog = new ProgressDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

                // Start the pipeline in a background task
                Task.Run(() =>
                {
                    try
                    {
                        runner.RunPipeline(
                            config.XmlFilePath,
                            config.GpkgFilePath,
                            config.StartTime,
                            config.EndTime,
                            config.OutputPath
                        );

                        // Wait for completion
                        int exitCode = runner.WaitForExit();

                        if (exitCode != 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Python process exited with code: {exitCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error running Python pipeline: {ex}");

                        // Show error on UI thread
                        progressDialog.Dispatcher.Invoke(() =>
                        {
                            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                                $"Error running Python pipeline:\n{ex.Message}",
                                "Processing Error",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error
                            );
                        });
                    }
                });

                // Show progress dialog (blocks until processing completes or is cancelled)
                progressDialog.StartProcessing(runner);
                bool? dialogResult = progressDialog.ShowDialog();

                // If processing succeeded, load the result into ArcGIS Pro
                if (dialogResult == true)
                {
                    System.Diagnostics.Debug.WriteLine("Processing completed successfully");

                    // Add layers to map (method uses QueuedTask internally)
                    AddLayersToMap(config).Wait();
                }
            }
            catch (Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error starting processing:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
                System.Diagnostics.Debug.WriteLine($"Error in StartProcessing: {ex}");
            }
        }

        /// <summary>
        /// Add road network and event points layers to the active map.
        /// Uses QueuedTask.Run() internally for thread-safe ArcGIS operations.
        /// </summary>
        private async Task AddLayersToMap(TrafficConfigViewModel config)
        {
            try
            {
                // Check if there's an active map view
                if (MapView.Active == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No active map found. Please open a map to add layers.",
                        "No Active Map",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Adding layers to map...");

                // Track which layers were successfully added
                bool networkAdded = false;
                bool eventsAdded = false;
                string parquetPath = config.OutputPath + ".parquet";

                // Add layers within QueuedTask (required for ArcGIS Pro SDK)
                await QueuedTask.Run(() =>
                {
                    Map map = MapView.Active.Map;

                    // 1. Add Road Network Layer (GPKG)
                    if (File.Exists(config.GpkgFilePath))
                    {
                        try
                        {
                            // GPKG with specific feature class: path + "/" + feature_class_name
                            // For GPKG files, the feature class is typically "main.table_name"
                            string gpkgPath = config.GpkgFilePath;

                            // Try with the specific feature class name first
                            Uri gpkgUriWithLayer = new Uri(gpkgPath + "/main.clipped_single");

                            try
                            {
                                Layer networkLayer = LayerFactory.Instance.CreateLayer(gpkgUriWithLayer, map);
                                if (networkLayer != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Road network layer added: {networkLayer.Name}");
                                    networkAdded = true;
                                }
                            }
                            catch
                            {
                                // If that fails, try without specifying the layer (will use first available)
                                System.Diagnostics.Debug.WriteLine("Trying to add GPKG without specifying layer name...");
                                Uri gpkgUri = new Uri(gpkgPath);
                                Layer networkLayer = LayerFactory.Instance.CreateLayer(gpkgUri, map);

                                if (networkLayer != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Road network layer added: {networkLayer.Name}");
                                    networkAdded = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding road network layer: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        }
                    }

                    // 2. Add Event Points Layer from Parquet (using XY Event Layer)
                    if (File.Exists(parquetPath))
                    {
                        try
                        {
                            // APPROACH: Create XY Event Layer from Parquet table
                            // Step 1: Add Parquet as a standalone table
                            System.Diagnostics.Debug.WriteLine($"Adding Parquet table: {parquetPath}");
                            Uri parquetUri = new Uri(parquetPath);
                            StandaloneTable table = StandaloneTableFactory.Instance.CreateStandaloneTable(parquetUri, map);

                            if (table != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Parquet table added: {table.Name}");

                                // Step 2: Create XY Event Layer from the table
                                // The Parquet has columns: x, y, timestamp, angle, person_id, interval_id, travelling_speed, freespeed, s
                                var eventLayerParams = new XYEventLayerCreationParams(table)
                                {
                                    Name = "Traffic Events",
                                    XField = "x",
                                    YField = "y",
                                    SpatialReference = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(4326) // WGS84
                                };

                                Layer eventsLayer = LayerFactory.Instance.CreateLayer<FeatureLayer>(eventLayerParams, map);

                                if (eventsLayer != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"XY Event Layer created: {eventsLayer.Name}");
                                    eventsAdded = true;

                                    // Remove the standalone table since we now have the layer
                                    map.RemoveStandaloneTable(table);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding event points layer from Parquet: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Parquet file not found at: {parquetPath}");
                    }
                });

                // Show appropriate success/error message based on what was added
                if (networkAdded && eventsAdded)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Traffic data loaded successfully!\n\n" +
                        "Layers added:\n" +
                        "• Road Network\n" +
                        "• Traffic Events (XY Event Layer)\n\n" +
                        $"Time range: {config.StartTime} - {config.EndTime}",
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                }
                else if (!networkAdded && !eventsAdded)
                {
                    // Show detailed instructions for manual layer addition
                    string instructions =
                        "Processing completed successfully! ✓\n\n" +
                        "The output files have been created, but automatic layer addition failed.\n" +
                        "You can add them manually:\n\n" +
                        "HOW TO ADD LAYERS MANUALLY:\n" +
                        "──────────────────────────\n" +
                        "1. Road Network (GPKG):\n" +
                        "   • Map tab → Add Data\n" +
                        $"   • Browse to: {config.GpkgFilePath}\n\n" +
                        "2. Traffic Events (Parquet table → XY Event Layer):\n" +
                        "   • Map tab → Add Data\n" +
                        $"   • Browse to: {parquetPath}\n" +
                        "   • Right-click table → Display XY Data\n" +
                        "   • X Field: x, Y Field: y\n" +
                        "   • Coordinate System: WGS84 (EPSG:4326)\n\n" +
                        $"Time range processed: {config.StartTime} - {config.EndTime}\n\n" +
                        "TIP: Windows Explorer will open to the output folder.";

                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        instructions,
                        "Manual Layer Addition Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );

                    // Also open the output folder in Windows Explorer to help the user
                    try
                    {
                        string outputFolder = Path.GetDirectoryName(parquetPath);
                        if (Directory.Exists(outputFolder))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                        }
                    }
                    catch
                    {
                        // Ignore errors opening explorer
                    }
                }
                else
                {
                    // Partial success
                    string addedLayers = "";
                    string manualLayers = "";

                    if (networkAdded)
                        addedLayers += "• Road Network\n";
                    else
                        manualLayers += $"Road Network (GPKG):\n  {config.GpkgFilePath}\n\n";

                    if (eventsAdded)
                        addedLayers += "• Traffic Events\n";
                    else
                        manualLayers += $"Traffic Events (Parquet):\n  {parquetPath}\n  (Use Display XY Data: x field, y field, WGS84)\n\n";

                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        $"Processing completed!\n\n" +
                        $"AUTOMATICALLY ADDED:\n{addedLayers}\n" +
                        $"PLEASE ADD MANUALLY:\n{manualLayers}" +
                        $"Use Map → Add Data in ArcGIS Pro.\n\n" +
                        $"Time range: {config.StartTime} - {config.EndTime}",
                        "Partial Success - Manual Action Needed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );

                    // Open output folder
                    try
                    {
                        string outputFolder = Path.GetDirectoryName(parquetPath);
                        if (Directory.Exists(outputFolder))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddLayersToMap: {ex}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Processing completed, but error adding layers to map:\n{ex.Message}\n\n" +
                    $"You can manually add the output files:\n" +
                    $"• Parquet: {config.OutputPath}.parquet (use Display XY Data)\n" +
                    $"• GPKG: {config.GpkgFilePath}",
                    "Layer Load Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
            }
        }
    }
}