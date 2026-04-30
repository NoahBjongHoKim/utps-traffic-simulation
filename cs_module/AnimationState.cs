using System;
using System.Collections.Generic;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;

namespace UTPS_Addin
{
    /// <summary>
    /// Shared static state for the UTPS animation workflow.
    /// Each animation workflow button reads and writes state here so later steps
    /// can access context set by earlier ones without re-querying the map.
    /// </summary>
    public static class AnimationState
    {
        // ── Study area (set by DrawBboxButton) ────────────────────────────────
        /// <summary>
        /// Bounding box of the study area in the map's spatial reference.
        /// Null = no spatial filter (process all links).
        /// Set by "Set Study Area" button from the current map view extent.
        /// </summary>
        public static Envelope BboxFilter { get; set; }

        // ── Output paths (set by TrafficLoaderButton after processing) ─────────
        /// <summary>
        /// Full path to the File Geodatabase created during "Load Traffic Data".
        /// Example: C:\Users\Noah\Documents\UTPS\output\traffic_output.gdb
        /// </summary>
        public static string OutputGdbPath { get; set; }

        /// <summary>
        /// Name of the Feature Class inside the GDB that holds traffic events.
        /// Default: "TrafficEvents"
        /// </summary>
        public static string TrafficFeatureClassName { get; set; } = "TrafficEvents";

        // ── Active layer reference (set by SymbolizeButton) ────────────────────
        /// <summary>
        /// The traffic events Feature Layer currently loaded in the active map.
        /// Set after symbolization so subsequent buttons target the correct layer.
        /// </summary>
        public static FeatureLayer TrafficLayer { get; set; }

        // ── Time extent (set by EnableTimeButton) ──────────────────────────────
        /// <summary>Start of the data time range (from layer time extent).</summary>
        public static DateTime DataStartTime { get; set; }

        /// <summary>End of the data time range (from layer time extent).</summary>
        public static DateTime DataEndTime { get; set; }

        // ── Animation settings (set by AnimationDurationButton) ────────────────
        /// <summary>Target duration of the exported animation in seconds. Default: 60.</summary>
        public static double AnimationDurationSeconds { get; set; } = 60.0;

        // ── Split layers (set by SplitLayerButton) ─────────────────────────────
        /// <summary>
        /// Sub-layers created when the dataset is split for ArcGIS performance.
        /// Empty until SplitLayerButton is used.
        /// </summary>
        public static List<FeatureLayer> SplitLayers { get; set; } = new List<FeatureLayer>();

        // ── Reset ───────────────────────────────────────────────────────────────
        /// <summary>
        /// Clear all state. Call at the start of a new "Load Traffic Data" run
        /// to avoid stale references from a previous session.
        /// </summary>
        public static void Reset()
        {
            BboxFilter = null;
            OutputGdbPath = null;
            TrafficFeatureClassName = "TrafficEvents";
            TrafficLayer = null;
            DataStartTime = default;
            DataEndTime = default;
            AnimationDurationSeconds = 60.0;
            SplitLayers = new List<FeatureLayer>();
        }
    }
}
