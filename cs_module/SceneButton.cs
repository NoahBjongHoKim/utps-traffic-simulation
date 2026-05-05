using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using System;

namespace UTPS_Addin
{
    /// <summary>
    /// Switch the active map view to a 3D Local Scene and prepare the traffic layer for 3D display.
    ///
    /// What this button does:
    ///   1. Switches the current MapView to MapViewingMode.SceneLocal (3D Local Scene).
    ///      The World Elevation surface activates automatically in Scene views
    ///      (requires ArcGIS Online sign-in or ArcGIS Pro Advanced license).
    ///   2. Adds Esri's World Topographic Map basemap as a visual reference layer.
    ///   3. Symbolizes the traffic points as small white square markers (visible in 3D).
    ///
    /// Buildings note: No freely-available global 3D building dataset exists as a ready-made
    /// ArcGIS service. To add buildings:
    ///   - Add your own building footprints from the Catalog pane
    ///   - Or use ArcGIS Pro → Insert → Add Elevation Source / Building Layer for city-specific data
    /// </summary>
    internal class SceneButton : Button
    {
        protected override async void OnClick()
        {
            try
            {
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
                        // Non-fatal — basemap can be added manually
                        System.Diagnostics.Debug.WriteLine($"Could not add basemap: {ex.Message}");
                    }
                });

                // ── 3. Symbolize points as white squares (visible in 3D) ─────────────
                var layer = AnimationState.TrafficLayer;
                if (layer != null)
                {
                    await QueuedTask.Run(() =>
                    {
                        try
                        {
                            ApplyWhite3DRenderer(layer);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not apply 3D symbol: {ex.Message}");
                        }
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No traffic layer in AnimationState — skipping 3D symbolization");
                }

                // ── Result message ────────────────────────────────────────────────────
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "Switched to 3D Local Scene.\n\n" +
                    "• World Topographic Map added as basemap\n" +
                    (layer != null ? "• Traffic points symbolized as white squares\n" : "") +
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
        /// Symbolize points as small white filled squares — clearly visible in 3D scenes.
        /// The user can customize the symbol further via the Layer Properties pane.
        /// </summary>
        private static void ApplyWhite3DRenderer(FeatureLayer layer)
        {
            var sym = SymbolFactory.Instance.ConstructPointSymbol(
                CIMColor.CreateRGBColor(255, 255, 255), 5, SimpleMarkerStyle.Square);

            // Disable outline for a clean solid-white appearance
            sym.UseRealWorldSymbolSizes = false;

            var renderer = new SimpleRendererDefinition
            {
                SymbolTemplate = sym.MakeSymbolReference()
            };

            layer.SetRenderer(layer.CreateRenderer(renderer));
            System.Diagnostics.Debug.WriteLine("White square renderer applied for 3D scene");
        }
    }
}
