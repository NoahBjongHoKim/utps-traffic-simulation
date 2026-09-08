using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Core.Data;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UTPS_Addin
{
    /// <summary>
    /// Fused "Load & Animate" button: replaces the previous 3-step workflow
    /// (Set Study Area → Load Traffic Data → Switch to 3D) with a single click.
    ///
    /// On click, in order:
    ///   1. Captures the current map view extent as the spatial filter (bbox), same
    ///      as the old "Set Study Area" button — mandatory, requires an active map view.
    ///   2. Opens the config dialog (XML/GPKG/stylx file pickers, time range,
    ///      video length + export fps).
    ///   3. Runs the Python pipeline with the computed interpolation fps.
    ///   4. Adds the resulting Feature Class to the 2D map, applies symbology
    ///      (stylx-based or default speed-color ramp), enables time.
    ///   5. Creates/reuses a "Traffic 3D Scene" Local Scene, adds a dark basemap
    ///      and 3D buildings, clones the 2D renderer onto the scene layer, enables time.
    /// </summary>
    internal class LoadAndAnimateButton : Button
    {
        private const string SceneName = "Traffic 3D Scene";

        protected override void OnClick()
        {
            try
            {
                // ── Step 1: Capture bbox from the current map view (mandatory) ──────
                var mapView = MapView.Active;
                if (mapView == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No active map found. Please open a map first.",
                        "No Active Map",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var extent = mapView.Extent;
                if (extent == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Could not read map extent. Please ensure the map has a valid view.",
                        "Extent Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var wgs84 = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(4326);
                var wgs84Extent = ArcGIS.Core.Geometry.GeometryEngine.Instance.Project(extent, wgs84)
                    as ArcGIS.Core.Geometry.Envelope;

                if (wgs84Extent == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Could not reproject map extent to WGS84.",
                        "Projection Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                AnimationState.BboxFilter = wgs84Extent;
                System.Diagnostics.Debug.WriteLine(
                    $"[BboxFilter SET] XMin={wgs84Extent.XMin:F6} YMin={wgs84Extent.YMin:F6} " +
                    $"XMax={wgs84Extent.XMax:F6} YMax={wgs84Extent.YMax:F6}");

                // ── Step 2: Validate Python environment, then open the config dialog ─
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

                var dialog = new TrafficConfigDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    var viewModel = dialog.DataContext as TrafficConfigViewModel;
                    if (viewModel != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"XML File: {viewModel.XmlFilePath}");
                        System.Diagnostics.Debug.WriteLine($"Start Time: {viewModel.StartTime}");
                        System.Diagnostics.Debug.WriteLine($"End Time: {viewModel.EndTime}");
                        System.Diagnostics.Debug.WriteLine($"GPKG File: {viewModel.GpkgFilePath}");
                        System.Diagnostics.Debug.WriteLine($"Output Path: {viewModel.OutputPath}");
                        System.Diagnostics.Debug.WriteLine($"Video Length: {viewModel.VideoLengthSeconds}s, Export FPS: {viewModel.ExportFps}");

                        StartProcessing(viewModel);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Load & Animate dialog cancelled by user");
                }
            }
            catch (Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error opening Load & Animate dialog:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
                System.Diagnostics.Debug.WriteLine($"Error in LoadAndAnimateButton: {ex}");
            }
        }

        /// <summary>
        /// Start Python processing with progress dialog. The computed interpolation fps
        /// (derived from video length + export fps + simulated time range) is read from
        /// the view model and stored in AnimationState for the Export Video button.
        /// </summary>
        private async void StartProcessing(TrafficConfigViewModel config)
        {
            try
            {
                AnimationState.Reset();

                double computedFps = config.ComputedInterpolationFps;
                AnimationState.VideoLengthSeconds = config.VideoLengthSeconds;
                AnimationState.ExportFps = config.ExportFps;
                AnimationState.InterpolationIntervalSeconds = 1.0 / computedFps;

                var runner = new PythonRunner();
                string extraArgs = BuildExtraArgs(config, computedFps);

                var progressDialog = new ProgressDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

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

                        int exitCode = runner.WaitForExit();

                        if (exitCode != 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Python process exited with code: {exitCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error running Python pipeline: {ex}");

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

                progressDialog.StartProcessing(runner);
                bool? dialogResult = progressDialog.ShowDialog();

                if (dialogResult == true)
                {
                    System.Diagnostics.Debug.WriteLine("Processing completed successfully");
                    await AddLayersToMap(config, computedFps);
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
        /// Appends --bbox (always set now, since Step 1 makes it mandatory) and --fps
        /// using the computed interpolation fps rather than a direct user-entered value.
        /// </summary>
        private static string BuildExtraArgs(TrafficConfigViewModel config, double computedFps)
        {
            var args = new System.Text.StringBuilder();

            if (AnimationState.BboxFilter != null)
            {
                var bb = AnimationState.BboxFilter;
                string bboxArg = FormattableString.Invariant(
                    $"--bbox {bb.XMin:F6} {bb.YMin:F6} {bb.XMax:F6} {bb.YMax:F6}");
                args.Append(bboxArg);
                System.Diagnostics.Debug.WriteLine($"[BboxFilter READ] {bboxArg}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[BboxFilter READ] null — no spatial filter will be applied");
            }

            if (args.Length > 0) args.Append(' ');
            args.Append(FormattableString.Invariant($"--fps {computedFps}"));
            System.Diagnostics.Debug.WriteLine($"[FPS] computed={computedFps}");

            return args.ToString();
        }

        /// <summary>
        /// Convert HH:MM or HH:MM:SS string to total seconds since midnight.
        /// </summary>
        private static double TimeToSeconds(string time)
        {
            var parts = time.Split(':');
            int h = int.Parse(parts[0]);
            int m = int.Parse(parts[1]);
            int s = parts.Length >= 3 ? int.Parse(parts[2]) : 0;
            return h * 3600 + m * 60 + s;
        }
    }
}
