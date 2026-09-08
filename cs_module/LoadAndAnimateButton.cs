using ArcGIS.Desktop.Framework;
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
                        System.Diagnostics.Debug.WriteLine($"Speed Multiplier: {viewModel.SpeedMultiplier}x, Export FPS: {viewModel.ExportFps}");

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

        /// <summary>
        /// Add road network and event points layers to the 2D map, then create/open
        /// the 3D Local Scene with the same data. Merges the old TrafficLoaderButton
        /// (2D layer creation) and SceneButton (3D scene setup) logic into one flow.
        /// </summary>
        private async Task AddLayersToMap(TrafficConfigViewModel config, double computedFps)
        {
            string parquetPath = config.OutputPath + ".parquet";
            string outputDir   = Path.GetDirectoryName(config.OutputPath) ?? ".";
            string gdbName     = Path.GetFileNameWithoutExtension(config.OutputPath);
            string gdbPath     = Path.Combine(outputDir, gdbName + ".gdb");
            string fcName      = "TrafficEvents";
            string fcFullPath  = Path.Combine(gdbPath, fcName);

            bool networkAdded = false;
            bool eventsAdded  = false;
            string stylxFallbackReason = null;

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

                System.Diagnostics.Debug.WriteLine("Adding layers to 2D map...");

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

                        System.Diagnostics.Debug.WriteLine("Converting Parquet → XY feature class...");
                        string tempFc = @"memory\traffic_tmp";
                        var xyResult = await Geoprocessing.ExecuteToolAsync(
                            "management.XYTableToPoint",
                            Geoprocessing.MakeValueArray(
                                parquetPath, tempFc, "x", "y", "", "4326"));
                        if (xyResult.IsFailed)
                        {
                            System.Diagnostics.Debug.WriteLine($"XYTableToPoint failed: {string.Join(", ", xyResult.ErrorMessages)}");
                            return;
                        }

                        var tmpLayer = map.FindLayers("traffic_tmp").FirstOrDefault();
                        if (tmpLayer != null)
                            map.RemoveLayer(tmpLayer);

                        System.Diagnostics.Debug.WriteLine($"Copying to GDB: {fcFullPath}");
                        var copyResult = await Geoprocessing.ExecuteToolAsync(
                            "management.CopyFeatures",
                            Geoprocessing.MakeValueArray(tempFc, fcFullPath));
                        if (copyResult.IsFailed)
                        {
                            System.Diagnostics.Debug.WriteLine($"CopyFeatures failed: {string.Join(", ", copyResult.ErrorMessages)}");
                            return;
                        }

                        foreach (var name in new[] { "traffic_tmp", "TrafficEvents", fcName })
                        {
                            var autoLayer = map.FindLayers(name).FirstOrDefault();
                            if (autoLayer != null)
                                map.RemoveLayer(autoLayer);
                        }

                        await Geoprocessing.ExecuteToolAsync(
                            "management.Delete",
                            Geoprocessing.MakeValueArray(tempFc));

                        System.Diagnostics.Debug.WriteLine("Adding GDB Feature Class to map...");
                        var layer = LayerFactory.Instance.CreateLayer(
                            new Uri(fcFullPath), map, layerName: gdbName) as FeatureLayer;

                        eventsAdded = layer != null;

                        if (layer != null)
                        {
                            try
                            {
                                var cimLayer = layer.GetDefinition() as CIMFeatureLayer;
                                if (cimLayer?.FeatureTable != null)
                                {
                                    cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                    {
                                        StartTimeField = "timestamp_dt",
                                        EndTimeField   = "timestamp_dt",
                                    };
                                    cimLayer.FeatureTable.TimeDefinition = new CIMTimeDataDefinition
                                    {
                                        UseTime = true,
                                    };
                                    layer.SetDefinition(cimLayer);
                                    System.Diagnostics.Debug.WriteLine("Time enabled on timestamp_dt field");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Could not auto-enable time: {ex.Message}");
                            }

                            try
                            {
                                double spanSeconds = 1.0 / computedFps;
                                var baseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                                DateTime startDt = baseDate + TimeSpan.FromSeconds(TimeToSeconds(config.StartTime));
                                DateTime endDt   = baseDate + TimeSpan.FromSeconds(TimeToSeconds(config.EndTime));

                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    try
                                    {
                                        var mv = MapView.Active;
                                        if (mv == null) return;

                                        mv.Time = new TimeRange(
                                            startDt,
                                            startDt + TimeSpan.FromSeconds(spanSeconds));

                                        System.Diagnostics.Debug.WriteLine(
                                            $"MapView time set: {startDt:HH:mm:ss.fff} + {spanSeconds:F3}s span");
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Could not set MapView.Time: {ex.Message}");
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Could not set time extent: {ex.Message}");
                            }
                        }

                        AnimationState.OutputGdbPath = gdbPath;
                        AnimationState.TrafficFeatureClassName = fcName;
                        AnimationState.TrafficLayer = layer;

                        // Apply symbology: stylx-based if a style file was provided, otherwise
                        // the default red-to-green speed color ramp.
                        if (!string.IsNullOrWhiteSpace(config.StylxFilePath) && File.Exists(config.StylxFilePath))
                        {
                            stylxFallbackReason = RendererHelper.ApplyStylxRenderer(layer, config.StylxFilePath);
                        }
                        else
                        {
                            RendererHelper.ApplySpeedColorRenderer(layer);
                        }

                        System.Diagnostics.Debug.WriteLine($"Feature Class layer added: {eventsAdded}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error writing to GDB: {ex.Message}\n{ex.StackTrace}");
                    }
                });

                // ── 3D Scene setup (merged from the old SceneButton) ─────────────────
                bool sceneReady = false;
                FeatureLayer sceneLayer = null;
                if (eventsAdded)
                {
                    (sceneReady, sceneLayer) = await SetUpSceneAsync(fcName, fcFullPath);
                }

                // ── Result message ────────────────────────────────────────────────
                string stylxNote = stylxFallbackReason != null
                    ? $"\n\n⚠ Style file could not be applied: {stylxFallbackReason}\n   Using default color ramp instead."
                    : "";

                if (eventsAdded)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Traffic data loaded and animated!\n\n" +
                        "Layers added:\n" +
                        (networkAdded ? "• Road Network\n" : "") +
                        "• Traffic Events (Feature Class in GDB — Time Slider ready)\n" +
                        (sceneReady ? "• 3D Local Scene created/updated\n" : "• Could not set up 3D scene\n") +
                        $"\nGDB: {gdbPath}\n" +
                        $"Feature Class: {fcName}\n" +
                        $"Time range: {config.StartTime} – {config.EndTime}" +
                        stylxNote,
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Processing completed, but automatic layer addition failed.\n\n" +
                        "You can add layers manually:\n\n" +
                        "1. Traffic Events (GDB Feature Class):\n" +
                        $"   Catalog → {gdbPath} → {fcName}\n" +
                        "   Drag to map, then enable time on 'timestamp_dt' field.\n\n" +
                        "2. Road Network:\n" +
                        $"   Map → Add Data → {config.GpkgFilePath}\n\n" +
                        $"Time range: {config.StartTime} – {config.EndTime}" +
                        stylxNote,
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

        /// <summary>
        /// Create/reuse the "Traffic 3D Scene" Local Scene, add the dark basemap and
        /// 3D buildings, add the traffic Feature Class, clone the 2D renderer, and
        /// enable time. Merged from the old SceneButton.OnClick. Returns true if the
        /// scene layer was successfully added.
        /// </summary>
        private async Task<(bool Success, FeatureLayer Layer)> SetUpSceneAsync(string fcName, string fcFullPath)
        {
            FeatureLayer sceneLayer = null;
            try
            {
                Map sceneMap = null;
                await QueuedTask.Run(() =>
                {
                    foreach (var mapProjectItem in ArcGIS.Desktop.Core.Project.Current.GetItems<MapProjectItem>())
                    {
                        var m = mapProjectItem.GetMap();
                        if (m != null && m.Name == SceneName && m.MapType == MapType.Scene)
                        {
                            sceneMap = m;
                            break;
                        }
                    }

                    if (sceneMap == null)
                    {
                        sceneMap = MapFactory.Instance.CreateScene(SceneName, null, MapViewingMode.SceneLocal);
                        System.Diagnostics.Debug.WriteLine($"Created new Local Scene: {SceneName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Reusing existing scene: {SceneName}");
                    }
                });

                if (sceneMap == null)
                {
                    return (false, null);
                }

                IMapPane pane = await ProApp.Panes.CreateMapPaneAsync(sceneMap);
                System.Diagnostics.Debug.WriteLine($"Scene pane opened: {pane != null}");

                MapView scenePane = pane?.MapView;

                await Task.Delay(500);

                // ── Dark basemap (Human Geography Dark Base layer only) ──────────────
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        if (sceneMap.FindLayers("Human Geography Dark Base").Count == 0)
                        {
                            var basemapUri = new Uri(
                                "https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_v2/VectorTileServer");
                            LayerFactory.Instance.CreateLayer(basemapUri, sceneMap, layerName: "Human Geography Dark Base");
                            System.Diagnostics.Debug.WriteLine("Human Geography Dark basemap added");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add basemap (may require ArcGIS Online sign-in): {ex.Message}");
                    }
                });

                // ── Esri 3D Buildings at 50% transparency ─────────────────────────────
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        if (sceneMap.FindLayers("Esri 3D Buildings").Count == 0)
                        {
                            var buildingsUri = new Uri(
                                "https://basemaps3d.arcgis.com/arcgis/rest/services/Esri3D_Buildings_v1/SceneServer");
                            var buildingsLayer = LayerFactory.Instance.CreateLayer(
                                buildingsUri, sceneMap, layerName: "Esri 3D Buildings") as Layer;
                            if (buildingsLayer != null)
                            {
                                buildingsLayer.SetTransparency(50);
                                System.Diagnostics.Debug.WriteLine("Esri 3D Buildings added at 50% transparency");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add 3D buildings (may require ArcGIS Online sign-in): {ex.Message}");
                    }
                });

                // ── Add the GDB Feature Class into the scene ──────────────────────────
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        var existing = sceneMap.FindLayers(fcName).Count > 0
                            ? sceneMap.FindLayers(fcName)[0]
                            : null;
                        if (existing != null)
                            sceneMap.RemoveLayer(existing);

                        sceneLayer = LayerFactory.Instance.CreateLayer(
                            new Uri(fcFullPath), sceneMap, layerName: fcName) as FeatureLayer;

                        System.Diagnostics.Debug.WriteLine($"Scene layer added: {sceneLayer != null}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add scene layer: {ex.Message}");
                    }
                });

                if (sceneLayer != null)
                {
                    await QueuedTask.Run(() =>
                    {
                        try
                        {
                            var sourceLayer = AnimationState.TrafficLayer;
                            if (sourceLayer != null)
                            {
                                var rendererDef = sourceLayer.GetRenderer();
                                sceneLayer.SetRenderer(rendererDef);
                                System.Diagnostics.Debug.WriteLine("Cloned 2D renderer onto scene layer");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("No 2D traffic layer found to clone renderer from");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not clone renderer onto scene layer: {ex.Message}");
                        }
                    });

                    await QueuedTask.Run(() =>
                    {
                        try
                        {
                            var cimLayer = sceneLayer.GetDefinition() as CIMFeatureLayer;
                            if (cimLayer?.FeatureTable != null)
                            {
                                cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                {
                                    StartTimeField = "timestamp_dt",
                                    EndTimeField   = "timestamp_dt",
                                };
                                cimLayer.FeatureTable.TimeDefinition = new CIMTimeDataDefinition
                                {
                                    UseTime = true,
                                };
                                sceneLayer.SetDefinition(cimLayer);
                                System.Diagnostics.Debug.WriteLine("Time enabled on scene layer");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not enable time on scene layer: {ex.Message}");
                        }
                    });

                    AnimationState.SceneTrafficLayer = sceneLayer;
                }

                // ── Auto-zoom to the study area at a 45° oblique angle ────────────────
                // Deliberately NOT wrapped in QueuedTask.Run: ZoomToAsync is documented
                // by Esri as safe (and intended) to call directly from the calling thread,
                // unlike the CIM-mutation calls above (SetRenderer/SetDefinition/etc.)
                // which require QueuedTask.Run. This codebase has previously had a bug
                // from nesting QueuedTask.Run unnecessarily — do not reintroduce it here.
                if (scenePane != null && AnimationState.BboxFilter != null)
                {
                    try
                    {
                        // AnimationState.BboxFilter is already an Envelope in WGS84 (set
                        // in OnClick via GeometryEngine.Instance.Project), so it can be
                        // passed to ZoomToAsync directly with no reconstruction needed.
                        await scenePane.ZoomToAsync(AnimationState.BboxFilter, TimeSpan.FromSeconds(1));

                        // The camera read here reflects the post-zoom state: ZoomToAsync's
                        // returned Task only completes once the zoom is applied, and only
                        // Pitch/Heading (unconditionally overwritten below, never read back)
                        // are what this code actually relies on — the position/distance
                        // fields carried through from this read are exactly what the prior
                        // await settled, so there's no staleness risk in practice.
                        var camera = scenePane.Camera;
                        camera.Pitch = -45;
                        camera.Heading = 0;
                        await scenePane.ZoomToAsync(camera, TimeSpan.Zero);

                        System.Diagnostics.Debug.WriteLine("Scene camera auto-zoomed to study area at 45° pitch");
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal by design: a failed auto-zoom leaves the scene at
                        // ArcGIS's default camera rather than the study area — worth
                        // confirming visually during manual testing, but not worth
                        // failing the whole "Load & Animate" flow over.
                        System.Diagnostics.Debug.WriteLine($"Could not auto-zoom scene camera: {ex.Message}");
                    }
                }

                return (sceneLayer != null, sceneLayer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetUpSceneAsync error: {ex}");
                return (false, null);
            }
        }
    }
}
