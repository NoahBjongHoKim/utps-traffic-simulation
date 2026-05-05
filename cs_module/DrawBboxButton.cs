using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using System;

namespace UTPS_Addin
{
    /// <summary>
    /// Step 1 of the animation workflow: capture the current map view extent as the
    /// study area bounding box. Only events on road links within this area will be
    /// loaded and processed by the "Load Traffic Data" button.
    ///
    /// Usage: Zoom / pan the map to your area of interest, then click this button.
    /// The current view extent is stored in AnimationState.BboxFilter and passed to
    /// the Python pipeline as --bbox xmin ymin xmax ymax (WGS84).
    /// </summary>
    internal class DrawBboxButton : Button
    {
        protected override void OnClick()
        {
            try
            {
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

                // Reproject extent to WGS84 so Python receives decimal degrees.
                // The map CRS may be anything (e.g. EPSG:32632 UTM in metres).
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

                // Store the WGS84 extent for use by TrafficLoaderButton
                AnimationState.BboxFilter = wgs84Extent;

                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Study area set to current map view:\n\n" +
                    $"  X (Longitude):  {wgs84Extent.XMin:F4}° → {wgs84Extent.XMax:F4}°\n" +
                    $"  Y (Latitude):   {wgs84Extent.YMin:F4}° → {wgs84Extent.YMax:F4}°\n\n" +
                    "Only road links within this area will be processed.\n" +
                    "Now click 'Load Traffic Data' to start the pipeline.",
                    "Study Area Set",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error setting study area:\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Error in DrawBboxButton: {ex}");
            }
        }
    }
}
