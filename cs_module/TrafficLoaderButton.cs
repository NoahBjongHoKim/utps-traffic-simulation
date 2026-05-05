using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Core.Geoprocessing;
using System;
using System.IO;
using System.Linq;
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
        private async void StartProcessing(TrafficConfigViewModel config)
        {
            try
            {
                // Reset animation state for this new run
                AnimationState.Reset();

                // Create Python runner
                var runner = new PythonRunner();

                // Build optional bbox argument from study area if one was set
                string extraArgs = BuildExtraArgs();

                // Create and show progress dialog
                var progressDialog = new ProgressDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

                // Start the pipeline in a background task (intentional fire-and-forget;
                // the progress dialog waits for the runner process directly)
                _ = Task.Run(() =>
                {
                    try
                    {
                        runner.RunPipeline(
                            config.XmlFilePath,
                            config.GpkgFilePath,
                            config.StartTime,
                            config.EndTime,
                            config.OutputPath,
                            extraArgs
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
                    await AddLayersToMap(config);
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
        /// Build optional extra CLI arguments for the Python wrapper.
        /// Appends --bbox if a study area was set in AnimationState.
        /// </summary>
        private static string BuildExtraArgs()
        {
            var args = new System.Text.StringBuilder();

            if (AnimationState.BboxFilter != null)
            {
                var bb = AnimationState.BboxFilter;
                // Format with invariant culture to avoid locale-specific decimal separators
                args.Append(FormattableString.Invariant(
                    $"--bbox {bb.XMin:F6} {bb.YMin:F6} {bb.XMax:F6} {bb.YMax:F6}"));
            }

            return args.ToString();
        }

        /// <summary>
        /// Add road network and event points layers to the active map.
        /// After Python outputs a Parquet file, this method chains geoprocessing calls to:
        ///   1. Create a File Geodatabase (if it doesn't exist)
        ///   2. Convert Parquet → in-memory XY feature class
        ///   3. Copy into the GDB as a permanent Feature Class (enables Time Slider)
        ///   4. Add the Feature Class layer to the active map
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

                string parquetPath = config.OutputPath + ".parquet";
                string outputDir   = Path.GetDirectoryName(config.OutputPath) ?? ".";
                // GDB named after the output (e.g. "export_1.gdb") so each run gets its own GDB
                string gdbName     = Path.GetFileNameWithoutExtension(config.OutputPath);
                string gdbPath     = Path.Combine(outputDir, gdbName + ".gdb");
                string fcName      = "TrafficEvents";
                string fcFullPath  = Path.Combine(gdbPath, fcName);

                bool networkAdded = false;
                bool eventsAdded  = false;

                await QueuedTask.Run(async () =>
                {
                    Map map = MapView.Active.Map;

                    // ── 1. Road Network (GPKG) ───────────────────────────────────────
                    if (File.Exists(config.GpkgFilePath))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"Adding GPKG: {config.GpkgFilePath}");
                            var gpkgResult = await Geoprocessing.ExecuteToolAsync(
                                "management.MakeFeatureLayer",
                                Geoprocessing.MakeValueArray(config.GpkgFilePath));
                            networkAdded = !gpkgResult.IsFailed;
                            if (!networkAdded)
                                System.Diagnostics.Debug.WriteLine($"Failed to add GPKG: {string.Join(", ", gpkgResult.ErrorMessages)}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding road network layer: {ex.Message}");
                        }
                    }

                    // ── 2. Traffic Events → File Geodatabase Feature Class ───────────
                    if (!File.Exists(parquetPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Parquet file not found: {parquetPath}");
                        return;
                    }

                    try
                    {
                        // Step A: Create GDB if it doesn't exist
                        if (!Directory.Exists(gdbPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"Creating File Geodatabase: {gdbPath}");
                            var createGdbResult = await Geoprocessing.ExecuteToolAsync(
                                "management.CreateFileGDB",
                                Geoprocessing.MakeValueArray(outputDir, gdbName));
                            if (createGdbResult.IsFailed)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to create GDB: {string.Join(", ", createGdbResult.ErrorMessages)}");
                                return;
                            }
                        }

                        // Step B: Parquet → in-memory XY feature class (temporary)
                        System.Diagnostics.Debug.WriteLine("Converting Parquet → XY feature class...");
                        string tempFc = @"memory\traffic_tmp";
                        var xyResult = await Geoprocessing.ExecuteToolAsync(
                            "management.XYTableToPoint",
                            Geoprocessing.MakeValueArray(
                                parquetPath,   // Input table
                                tempFc,        // Output feature class
                                "x",           // X field
                                "y",           // Y field
                                "",            // Z field (none)
                                "4326"));      // Coordinate system WKID as string
                        if (xyResult.IsFailed)
                        {
                            System.Diagnostics.Debug.WriteLine($"XYTableToPoint failed: {string.Join(", ", xyResult.ErrorMessages)}");
                            return;
                        }

                        // Remove temp layer from map if it was auto-added
                        var tmpLayer = map.FindLayers("traffic_tmp").FirstOrDefault();
                        if (tmpLayer != null)
                            map.RemoveLayer(tmpLayer);

                        // Step C: Copy from memory → GDB (creates a proper permanent Feature Class)
                        System.Diagnostics.Debug.WriteLine($"Copying to GDB: {fcFullPath}");
                        var copyResult = await Geoprocessing.ExecuteToolAsync(
                            "management.CopyFeatures",
                            Geoprocessing.MakeValueArray(tempFc, fcFullPath));
                        if (copyResult.IsFailed)
                        {
                            System.Diagnostics.Debug.WriteLine($"CopyFeatures failed: {string.Join(", ", copyResult.ErrorMessages)}");
                            return;
                        }

                        // Clean up the in-memory temporary FC
                        await Geoprocessing.ExecuteToolAsync(
                            "management.Delete",
                            Geoprocessing.MakeValueArray(tempFc));

                        // Step D: Add only the final GDB Feature Class to map
                        System.Diagnostics.Debug.WriteLine("Adding GDB Feature Class to map...");
                        var layer = LayerFactory.Instance.CreateLayer(
                            new Uri(fcFullPath), map, layerName: gdbName) as FeatureLayer;

                        eventsAdded = layer != null;

                        // Store in AnimationState for downstream buttons
                        AnimationState.OutputGdbPath = gdbPath;
                        AnimationState.TrafficFeatureClassName = fcName;
                        AnimationState.TrafficLayer = layer;

                        System.Diagnostics.Debug.WriteLine($"Feature Class layer added: {eventsAdded}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error writing to GDB: {ex.Message}\n{ex.StackTrace}");
                    }
                });

                // ── Result message ────────────────────────────────────────────────
                string studyAreaNote = AnimationState.BboxFilter != null
                    ? $"\nStudy area filter was applied (bbox)."
                    : "";

                if (eventsAdded)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Traffic data loaded successfully!\n\n" +
                        "Layers added:\n" +
                        (networkAdded ? "• Road Network\n" : "") +
                        "• Traffic Events (Feature Class in GDB — Time Slider ready)\n\n" +
                        $"GDB: {gdbPath}\n" +
                        $"Feature Class: {fcName}\n" +
                        $"Time range: {config.StartTime} – {config.EndTime}" +
                        studyAreaNote,
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    // Fallback instructions
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Processing completed, but automatic layer addition failed.\n\n" +
                        "You can add layers manually:\n\n" +
                        "1. Traffic Events (GDB Feature Class):\n" +
                        $"   Catalog → {gdbPath} → {fcName}\n" +
                        "   Drag to map, then enable time on 'timestamp_dt' field.\n\n" +
                        "2. Road Network:\n" +
                        $"   Map → Add Data → {config.GpkgFilePath}\n\n" +
                        $"Time range: {config.StartTime} – {config.EndTime}" +
                        studyAreaNote,
                        "Manual Steps Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    try
                    {
                        if (Directory.Exists(outputDir))
                            System.Diagnostics.Process.Start("explorer.exe", outputDir);
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