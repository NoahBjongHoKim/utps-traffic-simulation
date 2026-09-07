using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UTPS_Addin
{
    /// <summary>
    /// Shared symbology helpers for the traffic events layer.
    /// Used by both TrafficLoaderButton (2D layer, at import time) and
    /// SceneButton (3D scene layer, cloning the 2D renderer).
    /// </summary>
    internal static class RendererHelper
    {
        /// <summary>
        /// Apply a graduated color renderer: red (slow) → green (fast) by speed_level.
        /// This is the default coloring when no .stylx style file is provided.
        /// </summary>
        public static void ApplySpeedColorRenderer(FeatureLayer layer)
        {
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
        /// Apply a Unique Values renderer using named point symbols ("1" through "15")
        /// looked up from a user-provided .stylx style file, keyed on the style_id field.
        /// </summary>
        /// <param name="layer">The feature layer to symbolize.</param>
        /// <param name="stylxPath">Full path to the .stylx file.</param>
        public static async System.Threading.Tasks.Task ApplyStylxRendererAsync(FeatureLayer layer, string stylxPath)
        {
            await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                // Register the style file with the current project (idempotent if already added)
                StyleHelper.AddStyle(ArcGIS.Desktop.Core.Project.Current, stylxPath);

                string styleFileName = System.IO.Path.GetFileNameWithoutExtension(stylxPath);
                var styleItem = ArcGIS.Desktop.Core.Project.Current
                    .GetItems<StyleProjectItem>()
                    .FirstOrDefault(s => string.Equals(s.Name, styleFileName, StringComparison.OrdinalIgnoreCase));

                if (styleItem == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not find registered style project item for: {stylxPath}");
                    ApplySpeedColorRenderer(layer);
                    return;
                }

                var classes = new List<CIMUniqueValueClass>();
                for (int i = 1; i <= 15; i++)
                {
                    string name = i.ToString();
                    var symbolItem = styleItem.LookupItem(StyleItemType.PointSymbol, name) as SymbolStyleItem
                                     ?? styleItem.SearchSymbols(StyleItemType.PointSymbol, name).FirstOrDefault();

                    if (symbolItem?.Symbol is CIMPointSymbol pointSymbol)
                    {
                        classes.Add(new CIMUniqueValueClass
                        {
                            Values = new CIMUniqueValue[]
                            {
                                new CIMUniqueValue { FieldValues = new string[] { name } }
                            },
                            Label = name,
                            Visible = true,
                            Editable = true,
                            Symbol = pointSymbol.MakeSymbolReference()
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Style '{styleFileName}' has no point symbol named '{name}'");
                    }
                }

                if (classes.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No matching symbols found in stylx file — falling back to speed color renderer");
                    ApplySpeedColorRenderer(layer);
                    return;
                }

                var fallbackSymbol = SymbolFactory.Instance.ConstructPointSymbol(
                    CIMColor.CreateRGBColor(255, 255, 255), 4, SimpleMarkerStyle.Circle);

                var renderer = new CIMUniqueValueRenderer
                {
                    Fields = new string[] { "style_id" },
                    Groups = new CIMUniqueValueGroup[]
                    {
                        new CIMUniqueValueGroup { Classes = classes.ToArray() }
                    },
                    UseDefaultSymbol = true,
                    DefaultLabel = "<other>",
                    DefaultSymbol = fallbackSymbol.MakeSymbolReference()
                };

                layer.SetRenderer(renderer);
                System.Diagnostics.Debug.WriteLine($"Stylx renderer applied: {classes.Count} symbols matched from {styleFileName}");
            });
        }
    }
}
