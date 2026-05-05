using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using System;

namespace UTPS_Addin
{
    /// <summary>
    /// Toggle speed-based color coding on the traffic events layer.
    ///
    /// First click:  applies a graduated color renderer — red (speed_level 0, stopped)
    ///               through green (speed_level 15, fast) — using NaturalBreaks classification.
    /// Second click: reverts to a plain white circle symbol.
    ///
    /// The layer reference is read from AnimationState.TrafficLayer, which is set
    /// automatically by TrafficLoaderButton after the data is imported.
    /// </summary>
    internal class SymbolizeButton : Button
    {
        private bool _isColored = false;

        protected override void OnClick()
        {
            var layer = AnimationState.TrafficLayer;

            if (layer == null)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "No traffic layer found.\n\n" +
                    "Please run 'Load Traffic Data' first so the layer is registered.",
                    "No Layer",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            _ = QueuedTask.Run(() =>
            {
                try
                {
                    if (!_isColored)
                    {
                        ApplySpeedColorRenderer(layer);
                        _isColored = true;
                    }
                    else
                    {
                        ApplyWhiteRenderer(layer);
                        _isColored = false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SymbolizeButton error: {ex.Message}");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                            $"Error applying symbolization:\n{ex.Message}",
                            "Symbolization Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    });
                }
            });
        }

        /// <summary>
        /// Apply a graduated color renderer: red (slow) → green (fast) by speed_level.
        /// </summary>
        private static void ApplySpeedColorRenderer(FeatureLayer layer)
        {
            // Build a red→green color ramp
            var colorRamp = new CIMLinearContinuousColorRamp
            {
                FromColor = CIMColor.CreateRGBColor(220, 50, 50),   // slow = red
                ToColor   = CIMColor.CreateRGBColor(50, 200, 50),   // fast = green
            };

            var gcDef = new GraduatedColorsRendererDefinition
            {
                ClassificationField  = "speed_level",
                ClassificationMethod = ClassificationMethod.NaturalBreaks,
                BreakCount           = 5,
                ColorRamp            = colorRamp,
            };

            layer.SetRenderer(layer.CreateRenderer(gcDef));
            System.Diagnostics.Debug.WriteLine("Speed color renderer applied");
        }

        /// <summary>
        /// Revert to a simple white circle symbol.
        /// </summary>
        private static void ApplyWhiteRenderer(FeatureLayer layer)
        {
            var sym = SymbolFactory.Instance.ConstructPointSymbol(
                CIMColor.CreateRGBColor(255, 255, 255), 4, SimpleMarkerStyle.Circle);

            var simpleDef = new SimpleRendererDefinition
            {
                SymbolTemplate = sym.MakeSymbolReference()
            };

            layer.SetRenderer(layer.CreateRenderer(simpleDef));
            System.Diagnostics.Debug.WriteLine("White circle renderer applied");
        }
    }
}
