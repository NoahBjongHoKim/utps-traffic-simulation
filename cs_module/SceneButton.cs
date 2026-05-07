using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using System;
using System.IO;
using System.Threading.Tasks;

namespace UTPS_Addin
{
    /// <summary>
    /// Open (or reuse) a Local Scene in the project and add the traffic data as true 3D cubes.
    ///
    /// What this button does:
    ///   1. Looks for an existing Local Scene named "Traffic 3D Scene" in the project.
    ///      If none exists, creates a new one.
    ///   2. Opens/activates that scene's map view.
    ///   3. Adds Esri's World Topographic Map as a basemap for visual context.
    ///   4. Adds the traffic GDB Feature Class into the scene map as a new layer.
    ///   5. Symbolizes the points as white 3D cubes using Simple3DMarkerStyle.Cube.
    ///   6. Enables time on the scene layer (timestamp field).
    ///
    /// Note: ArcGIS Pro does not support converting a 2D map view to 3D in-place via the SDK.
    /// This button creates a dedicated Local Scene instead.
    /// </summary>
    internal class SceneButton : Button
    {
        private const string SceneName = "Traffic 3D Scene";

        protected override async void OnClick()
        {
            try
            {
                // Guard: need traffic data loaded first
                if (string.IsNullOrEmpty(AnimationState.OutputGdbPath))
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No traffic data found.\n\nPlease run 'Load Traffic Data' first.",
                        "No Traffic Data",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // ── 1. Get or create the Local Scene ─────────────────────────────────
                Map sceneMap = null;
                await QueuedTask.Run(() =>
                {
                    // Check if a scene with this name already exists in the project
                    foreach (var mapProjectItem in ArcGIS.Desktop.Core.Project.Current.GetItems<ArcGIS.Desktop.Core.MapProjectItem>())
                    {
                        var m = mapProjectItem.GetMap();
                        if (m != null && m.Name == SceneName && m.MapType == MapType.Scene)
                        {
                            sceneMap = m;
                            break;
                        }
                    }

                    // Create a new Local Scene if not found
                    if (sceneMap == null)
                    {
                        sceneMap = MapFactory.Instance.CreateScene(SceneName, MapViewingMode.SceneLocal);
                        System.Diagnostics.Debug.WriteLine($"Created new Local Scene: {SceneName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Reusing existing scene: {SceneName}");
                    }
                });

                if (sceneMap == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Could not create or find the 3D Local Scene.",
                        "Scene Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                // ── 2. Activate (open) the scene view ────────────────────────────────
                // This must run on the UI thread
                await ArcGIS.Desktop.Framework.FrameworkApplication.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var pane = await ProApp.Panes.CreateMapPaneAsync(sceneMap);
                    System.Diagnostics.Debug.WriteLine($"Scene pane opened: {pane != null}");
                });

                // Give the pane a moment to become active
                await Task.Delay(500);

                // ── 3. Add World Topographic basemap ─────────────────────────────────
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        // Only add if not already present
                        if (sceneMap.FindLayers("World Topographic Map").Count == 0)
                        {
                            var topoUri = new Uri(
                                "https://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer");
                            LayerFactory.Instance.CreateLayer(topoUri, sceneMap, layerName: "World Topographic Map");
                            System.Diagnostics.Debug.WriteLine("World Topographic basemap added");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add basemap: {ex.Message}");
                    }
                });

                // ── 4. Add the GDB Feature Class into the scene ──────────────────────
                string fcName     = AnimationState.TrafficFeatureClassName ?? "TrafficEvents";
                string fcFullPath = Path.Combine(AnimationState.OutputGdbPath, fcName);

                FeatureLayer sceneLayer = null;
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        // Remove any stale copy
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

                // ── 5 & 6. Symbolize + enable time ───────────────────────────────────
                if (sceneLayer != null)
                {
                    await QueuedTask.Run(() =>
                    {
                        try { Apply3DCubeRenderer(sceneLayer); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not apply 3D symbol: {ex.Message}");
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
                                    StartTimeField = "timestamp",
                                    EndTimeField   = "timestamp",
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

                // ── Result message ────────────────────────────────────────────────────
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"3D Local Scene '{SceneName}' is ready.\n\n" +
                    "• World Topographic Map added as basemap\n" +
                    (sceneLayer != null
                        ? $"• Traffic layer '{fcName}' added with white 3D cube symbols\n"
                        : $"• Could not add traffic layer — check path: {fcFullPath}\n") +
                    "• Terrain surface activates automatically (requires ArcGIS Online sign-in)\n\n" +
                    "To add buildings:\n" +
                    "  • Add your own building footprints via the Catalog pane\n" +
                    "  • Or use Insert → Add Elevation Source / Building Layer",
                    "3D Scene Ready",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SceneButton error: {ex}");
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error setting up 3D scene:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Symbolize points as white 3D cubes using Simple3DMarkerStyle.Cube.
        /// </summary>
        private static void Apply3DCubeRenderer(FeatureLayer layer)
        {
            var sym = SymbolFactory.Instance.ConstructPointSymbol(
                CIMColor.CreateRGBColor(255, 255, 255),
                10,
                Simple3DMarkerStyle.Cube);

            sym.UseRealWorldSymbolSizes = false;

            var renderer = new SimpleRendererDefinition
            {
                SymbolTemplate = sym.MakeSymbolReference()
            };

            layer.SetRenderer(layer.CreateRenderer(renderer));
            System.Diagnostics.Debug.WriteLine("3D cube renderer applied");
        }
    }
}
