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
                string geojsonPath = config.OutputPath + ".geojson";

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

                    // 2. Add Event Points Layer (GeoJSON)
                    if (File.Exists(geojsonPath))
                    {
                        try
                        {
                            // Use Uri and LayerFactory.CreateLayer (standard pattern from ArcGIS docs)
                            Uri geojsonUri = new Uri(geojsonPath);
                            Layer eventsLayer = LayerFactory.Instance.CreateLayer(geojsonUri, map);

                            if (eventsLayer != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Event points layer added: {eventsLayer.Name}");
                                eventsAdded = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding event points layer: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"GeoJSON file not found at: {geojsonPath}");
                    }
                });

                // Show appropriate success/error message based on what was added
                if (networkAdded && eventsAdded)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Traffic data loaded successfully!\n\n" +
                        "Layers added:\n" +
                        "• Road Network\n" +
                        "• Event Points\n\n" +
                        $"Time range: {config.StartTime} - {config.EndTime}",
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                }
                else if (!networkAdded && !eventsAdded)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        $"Processing completed successfully, but layers could not be added automatically.\n\n" +
                        $"Please manually add the files:\n" +
                        $"• Road Network: {config.GpkgFilePath}\n" +
                        $"• Event Points: {geojsonPath}\n\n" +
                        $"Use the 'Add Data' button in ArcGIS Pro.",
                        "Manual Layer Addition Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                }
                else
                {
                    // Partial success
                    string addedLayers = "";
                    string failedLayers = "";

                    if (networkAdded) addedLayers += "• Road Network\n";
                    else failedLayers += $"• Road Network: {config.GpkgFilePath}\n";

                    if (eventsAdded) addedLayers += "• Event Points\n";
                    else failedLayers += $"• Event Points: {geojsonPath}\n";

                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        $"Processing completed!\n\n" +
                        $"Layers added:\n{addedLayers}\n" +
                        $"Please manually add:\n{failedLayers}\n" +
                        $"Time range: {config.StartTime} - {config.EndTime}",
                        "Partial Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddLayersToMap: {ex}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Processing completed, but error adding layers to map:\n{ex.Message}\n\n" +
                    $"You can manually add the output files:\n" +
                    $"• GeoJSON: {config.OutputPath}.geojson\n" +
                    $"• GPKG: {config.GpkgFilePath}",
                    "Layer Load Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
            }
        }
    }
}