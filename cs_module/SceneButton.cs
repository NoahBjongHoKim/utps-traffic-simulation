using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Time;
using System;
using System.IO;

namespace UTPS_Addin
{
    /// <summary>
    /// Switch the active map view to a 3D Local Scene and add the traffic data as true 3D cubes.
    ///
    /// What this button does:
    ///   1. Switches the current MapView to MapViewingMode.SceneLocal (3D Local Scene).
    ///      The World Elevation surface activates automatically (requires ArcGIS Online sign-in).
    ///   2. Adds Esri's World Topographic Map as a basemap for visual context.
    ///   3. Adds the traffic GDB Feature Class directly into the scene map as a new layer.
    ///   4. Symbolizes the points as white 3D cubes using Simple3DMarkerStyle.Cube.
    ///   5. Re-enables time on the scene layer (timestamp field).
    ///
    /// Buildings: No freely-available global 3D building dataset exists as an ArcGIS service.
    /// Add your own building footprints via the Catalog pane, or use
    /// Insert → Add Elevation Source / Building Layer for city-specific data.
    /// </summary>
    internal class SceneButton : Button
    {
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

                var mapView = MapView.Active;
                if (mapView == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No active map view found. Please open a map first.",
                        "No Active Map",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // ── 1. Switch to 3D Local Scene ───────────────────────────────────────
                bool switched = false;
                await QueuedTask.Run(async () =>
                {
                    if (mapView.CanSetViewingMode(MapViewingMode.SceneLocal))
                    {
                        await mapView.SetViewingModeAsync(MapViewingMode.SceneLocal);
                        switched = true;
                        System.Diagnostics.Debug.WriteLine("Switched to 3D Local Scene");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Cannot switch to SceneLocal from current view");
                    }
                });

                if (!switched)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Could not switch to 3D Local Scene.\n\n" +
                        "This may require restarting ArcGIS Pro or using a map that supports 3D viewing.",
                        "3D Not Available",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // ── 2. Add World Topographic basemap ─────────────────────────────────
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        var map = MapView.Active?.Map;
                        if (map == null) return;

                        var topoUri = new Uri(
                            "https://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer");
                        LayerFactory.Instance.CreateLayer(topoUri, map, layerName: "World Topographic Map");
                        System.Diagnostics.Debug.WriteLine("World Topographic basemap added");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add basemap: {ex.Message}");
                    }
                });

                // ── 3. Add the GDB Feature Class into the scene map ──────────────────
                // The 2D layer is in a different map; we add a fresh layer instance here
                // pointing at the same Feature Class path.
                string fcName     = AnimationState.TrafficFeatureClassName ?? "TrafficEvents";
                string fcFullPath = Path.Combine(AnimationState.OutputGdbPath, fcName);

                FeatureLayer sceneLayer = null;
                await QueuedTask.Run(() =>
                {
                    try
                    {
                        var map = MapView.Active?.Map;
                        if (map == null) return;

                        // Remove any stale copy of the same layer that may already be in the scene
                        var existing = map.FindLayers(fcName).Count > 0
                            ? map.FindLayers(fcName)[0]
                            : null;
                        if (existing != null)
                            map.RemoveLayer(existing);

                        sceneLayer = LayerFactory.Instance.CreateLayer(
                            new Uri(fcFullPath), map, layerName: fcName) as FeatureLayer;

                        System.Diagnostics.Debug.WriteLine($"Scene layer added: {sceneLayer != null}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not add scene layer: {ex.Message}");
                    }
                });

                // ── 4. Symbolize as 3D cubes ─────────────────────────────────────────
                if (sceneLayer != null)
                {
                    await QueuedTask.Run(() =>
                    {
                        try
                        {
                            Apply3DCubeRenderer(sceneLayer);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not apply 3D symbol: {ex.Message}");
                        }
                    });

                    // ── 5. Enable time on the scene layer ────────────────────────────
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

                    // Store the scene layer so SymbolizeButton can also target it
                    AnimationState.SceneTrafficLayer = sceneLayer;
                }

                // ── Result message ────────────────────────────────────────────────────
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "3D Local Scene ready.\n\n" +
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
                    $"Error switching to 3D scene:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Symbolize points as white 3D cubes using Simple3DMarkerStyle.Cube.
        /// Size is in points; adjust via Layer Properties → Symbology afterwards.
        /// </summary>
        private static void Apply3DCubeRenderer(FeatureLayer layer)
        {
            // ConstructPointSymbol overload: color, size, Simple3DMarkerStyle
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
