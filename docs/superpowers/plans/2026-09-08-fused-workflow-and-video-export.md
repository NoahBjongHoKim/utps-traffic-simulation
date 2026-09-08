# Fused Workflow and Video Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fuse the 3-step Set Study Area / Load Traffic Data / Switch to 3D workflow into one "Load & Animate" button, replace the manual FPS input with a video-length/export-fps derived calculation, fix a silent stylx symbology fallback, enrich the 3D scene with a dark basemap and 3D buildings, and add a new "Export Video" button with an indeterminate progress dialog.

**Architecture:** C#/.NET 8 WPF ArcGIS Pro add-in (`cs_module/`). Three existing button classes (`DrawBboxButton`, `TrafficLoaderButton`, `SceneButton`) are deleted and their logic merged sequentially into one new `LoadAndAnimateButton`. The config dialog's FPS field is replaced with two new fields whose live-computed derived value flows into the same pipeline call the old FPS field fed. A new `ExportVideoButton` + dialog pair uses the ArcGIS Pro SDK's `Animation`/`TimeTrack`/`BeginExport` API to export a video from the current view.

**Tech Stack:** C# / .NET 8 / WPF / ArcGIS Pro SDK (`ArcGIS.Desktop.Mapping`, `ArcGIS.Desktop.Mapping.Animations`, `ArcGIS.Core.CIM`). No compiler available in this environment (macOS) — every C# task is verified by careful manual code review against the exact API signatures researched for this spec, not an actual build. This must be built and manually tested in ArcGIS Pro on Windows before being trusted.

---

## Task 1: Add new `AnimationState` fields

**Files:**
- Modify: `cs_module/AnimationState.cs`

- [ ] **Step 1: Add three new fields for the video-export chain**

Current code (lines 56-59, the "Animation settings" section):
```csharp
        // ── Animation settings (set by AnimationDurationButton) ────────────────
        /// <summary>Target duration of the exported animation in seconds. Default: 60.</summary>
        public static double AnimationDurationSeconds { get; set; } = 60.0;
```

Change to:
```csharp
        // ── Animation settings (set by LoadAndAnimateButton, read by ExportVideoButton) ──
        /// <summary>Target duration of the exported animation in seconds. Default: 60.</summary>
        public static double AnimationDurationSeconds { get; set; } = 60.0;

        /// <summary>
        /// Video length (seconds) chosen in the Load &amp; Animate dialog. 0 = unset
        /// (Load &amp; Animate has not run this session). Read by ExportVideoButton to
        /// bound the allowed export start/end second range.
        /// </summary>
        public static double VideoLengthSeconds { get; set; } = 0.0;

        /// <summary>
        /// Export frame rate (frames per second) chosen in the Load &amp; Animate dialog.
        /// Reused directly by ExportVideoButton — not asked for again at export time.
        /// </summary>
        public static double ExportFps { get; set; } = 0.0;

        /// <summary>
        /// Computed interpolation interval in seconds (= 1.0 / computed interpolation fps),
        /// used both for the Time Slider span at import time and for aligning the exported
        /// animation's time-step span so no frame ever falls in a gap between data points.
        /// </summary>
        public static double InterpolationIntervalSeconds { get; set; } = 0.0;
```

- [ ] **Step 2: Reset the new fields in `Reset()`**

Current code (lines 72-85):
```csharp
        public static void Reset()
        {
            // NOTE: BboxFilter is intentionally NOT reset here — it is set by "Set Study Area"
            // and should persist across multiple "Load Traffic Data" runs until the user
            // explicitly sets a new study area.
            OutputGdbPath = null;
            TrafficFeatureClassName = "TrafficEvents";
            TrafficLayer = null;
            SceneTrafficLayer = null;
            DataStartTime = default;
            DataEndTime = default;
            AnimationDurationSeconds = 60.0;
            SplitLayers = new List<FeatureLayer>();
        }
```

Change to:
```csharp
        public static void Reset()
        {
            // NOTE: BboxFilter is intentionally NOT reset here — it is set by "Set Study Area"
            // and should persist across multiple "Load Traffic Data" runs until the user
            // explicitly sets a new study area.
            OutputGdbPath = null;
            TrafficFeatureClassName = "TrafficEvents";
            TrafficLayer = null;
            SceneTrafficLayer = null;
            DataStartTime = default;
            DataEndTime = default;
            AnimationDurationSeconds = 60.0;
            SplitLayers = new List<FeatureLayer>();
            VideoLengthSeconds = 0.0;
            ExportFps = 0.0;
            InterpolationIntervalSeconds = 0.0;
        }
```

- [ ] **Step 3: Verify by code review**

```bash
grep -n "VideoLengthSeconds\|ExportFps\|InterpolationIntervalSeconds" "cs_module/AnimationState.cs"
```
Expected: 3 new properties declared, all 3 also reset in `Reset()` — 6 total occurrences.

- [ ] **Step 4: Commit**

```bash
git add cs_module/AnimationState.cs
git commit -m "$(cat <<'EOF'
Add VideoLengthSeconds, ExportFps, InterpolationIntervalSeconds to AnimationState

These carry the video-export configuration computed by the fused
Load & Animate button through to the new Export Video button, and
the computed interpolation interval used to keep the Time Slider
span aligned with actual data density (preventing flashing).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Replace the FPS field with Video Length + Export FPS in the config dialog

**Files:**
- Modify: `cs_module/TrafficConfigViewModel.cs`
- Modify: `cs_module/TrafficConfigDialog.xaml`

- [ ] **Step 1: Replace the `_fps` field and `Fps` property with `_videoLengthSeconds`/`_exportFps` and computed-value properties**

Current code (line 25):
```csharp
        private double _fps = 5.0;
```

Change to:
```csharp
        private double _videoLengthSeconds = 60.0;
        private double _exportFps = 24.0;
```

Current code (lines 129-141, the `Fps` property):
```csharp
        public double Fps
        {
            get => _fps;
            set
            {
                if (_fps != value)
                {
                    _fps = value;
                    OnPropertyChanged(nameof(Fps));
                    ClearValidation();
                }
            }
        }
```

Change to:
```csharp
        public double VideoLengthSeconds
        {
            get => _videoLengthSeconds;
            set
            {
                if (_videoLengthSeconds != value)
                {
                    _videoLengthSeconds = value;
                    OnPropertyChanged(nameof(VideoLengthSeconds));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    ClearValidation();
                }
            }
        }

        public double ExportFps
        {
            get => _exportFps;
            set
            {
                if (_exportFps != value)
                {
                    _exportFps = value;
                    OnPropertyChanged(nameof(ExportFps));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    ClearValidation();
                }
            }
        }

        /// <summary>
        /// Computed interpolation fps, derived from VideoLengthSeconds, ExportFps, and the
        /// simulated time range (EndTime - StartTime), clamped to the pipeline's supported
        /// 0.1-60 fps range. Read by OnOk() / the caller after a successful dialog close.
        /// </summary>
        public double ComputedInterpolationFps
        {
            get
            {
                double totalSimSeconds = 0;
                if (IsValidTimeFormat(StartTime) && IsValidTimeFormat(EndTime))
                {
                    totalSimSeconds = TimeToSeconds(EndTime) - TimeToSeconds(StartTime);
                }

                if (totalSimSeconds <= 0 || VideoLengthSeconds <= 0 || ExportFps <= 0)
                {
                    return 1.0; // sane default while inputs are incomplete/invalid
                }

                double totalFrames = VideoLengthSeconds * ExportFps;
                double rawIntervalSeconds = totalSimSeconds / totalFrames;
                double rawFps = 1.0 / rawIntervalSeconds;

                return Math.Min(60.0, Math.Max(0.1, rawFps));
            }
        }

        /// <summary>
        /// Live-updating text shown in the dialog describing the computed interpolation
        /// interval, including a note if the raw computed value was clamped.
        /// </summary>
        public string ComputedIntervalText
        {
            get
            {
                double totalSimSeconds = 0;
                if (IsValidTimeFormat(StartTime) && IsValidTimeFormat(EndTime))
                {
                    totalSimSeconds = TimeToSeconds(EndTime) - TimeToSeconds(StartTime);
                }

                if (totalSimSeconds <= 0 || VideoLengthSeconds <= 0 || ExportFps <= 0)
                {
                    return "Computed: enter a valid time range, video length, and export fps above.";
                }

                double totalFrames = VideoLengthSeconds * ExportFps;
                double rawIntervalSeconds = totalSimSeconds / totalFrames;
                double rawFps = 1.0 / rawIntervalSeconds;
                double clampedFps = Math.Min(60.0, Math.Max(0.1, rawFps));
                double clampedIntervalSeconds = 1.0 / clampedFps;

                if (Math.Abs(clampedFps - rawFps) < 0.0001)
                {
                    return $"Computed: 1 point every {clampedIntervalSeconds:F2}s";
                }

                string reason = rawFps > 60.0 ? "60 fps interpolation limit" : "0.1 fps interpolation limit";
                return $"Requested 1 point every {rawIntervalSeconds:F2}s, clamped to {clampedIntervalSeconds:F2}s ({reason})";
            }
        }
```

- [ ] **Step 2: Update `StartTime`/`EndTime` setters to also notify `ComputedIntervalText`**

Current code (lines 87-99, `StartTime` property):
```csharp
        public string StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged(nameof(StartTime));
                    ClearValidation();
                }
            }
        }
```

Change to:
```csharp
        public string StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged(nameof(StartTime));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    ClearValidation();
                }
            }
        }
```

Current code (lines 101-113, `EndTime` property):
```csharp
        public string EndTime
        {
            get => _endTime;
            set
            {
                if (_endTime != value)
                {
                    _endTime = value;
                    OnPropertyChanged(nameof(EndTime));
                    ClearValidation();
                }
            }
        }
```

Change to:
```csharp
        public string EndTime
        {
            get => _endTime;
            set
            {
                if (_endTime != value)
                {
                    _endTime = value;
                    OnPropertyChanged(nameof(EndTime));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    ClearValidation();
                }
            }
        }
```

- [ ] **Step 3: Change `IsValidTimeFormat` and `TimeToSeconds` from `private` to `internal` so the new computed properties (already in this same class, so this step is actually a no-op — they're already accessible since `ComputedInterpolationFps`/`ComputedIntervalText` are members of the same class)**

Re-read the class: `IsValidTimeFormat` (line 352) and `TimeToSeconds` (line 359) are already `private` instance methods on `TrafficConfigViewModel`, and the new `ComputedInterpolationFps`/`ComputedIntervalText` properties added in Step 1 are also instance members of the same class — so they can already call `IsValidTimeFormat`/`TimeToSeconds` directly with no accessibility change needed. This step is a verification checkpoint only, not an edit: confirm after Step 1 that the new properties compile-review cleanly against these existing private methods (no `this.` prefix issues, no shadowing).

- [ ] **Step 4: Update FPS validation to validate Video Length and Export FPS instead**

Current code (lines 314-318):
```csharp
            // Validate FPS
            if (Fps < 0.1 || Fps > 60)
            {
                errors.AppendLine("• Interpolation FPS must be between 0.1 and 60");
            }
```

Change to:
```csharp
            // Validate video length and export fps (the computed interpolation fps is
            // always clamped to a valid range internally, so no need to validate that)
            if (VideoLengthSeconds <= 0)
            {
                errors.AppendLine("• Video Length must be greater than 0 seconds");
            }

            if (ExportFps <= 0)
            {
                errors.AppendLine("• Export FPS must be greater than 0");
            }
```

- [ ] **Step 5: Update the XAML — replace the Interpolation FPS row with Video Length + Export FPS rows and a computed-value readout**

Current code (`cs_module/TrafficConfigDialog.xaml` lines 166-183):
```xml
                <!-- Interpolation FPS -->
                <TextBlock Text="Interpolation FPS" Style="{StaticResource FieldLabel}" Margin="0,12,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="120"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0"
                             Text="{Binding Fps, UpdateSourceTrigger=PropertyChanged}"
                             Style="{StaticResource FieldTextBox}"
                             ToolTip="Frames per second for trajectory interpolation (e.g. 5 = one point every 0.2 s, 0.2 = one point every 5 s)"/>
                    <TextBlock Grid.Column="1"
                               Text="frames per second (0.1–60)"
                               VerticalAlignment="Center"
                               Margin="8,0,0,0"
                               FontSize="10"
                               Foreground="#888"/>
                </Grid>
```

Change to:
```xml
                <!-- Video Length + Export FPS -->
                <TextBlock Text="Target Video" Style="{StaticResource FieldLabel}" Margin="0,12,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="20"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0">
                        <TextBlock Text="Video Length (seconds)" FontSize="11" Margin="0,0,0,4"/>
                        <TextBox Text="{Binding VideoLengthSeconds, UpdateSourceTrigger=PropertyChanged}"
                                 Style="{StaticResource FieldTextBox}"
                                 ToolTip="How long the exported video should be, in seconds (e.g. 60)"/>
                    </StackPanel>

                    <TextBlock Grid.Column="1" Text="×"
                               VerticalAlignment="Bottom"
                               HorizontalAlignment="Center"
                               Margin="0,0,0,8"
                               FontSize="16"/>

                    <StackPanel Grid.Column="2">
                        <TextBlock Text="Export FPS" FontSize="11" Margin="0,0,0,4"/>
                        <TextBox Text="{Binding ExportFps, UpdateSourceTrigger=PropertyChanged}"
                                 Style="{StaticResource FieldTextBox}"
                                 ToolTip="Frames per second of the exported video (e.g. 24 or 30)"/>
                    </StackPanel>
                </Grid>

                <TextBlock Text="{Binding ComputedIntervalText}"
                           FontSize="10"
                           Foreground="#666"
                           Margin="0,6,0,0"
                           TextWrapping="Wrap"/>
```

- [ ] **Step 6: Verify by code review**

```bash
grep -n "VideoLengthSeconds\|ExportFps\|ComputedInterpolationFps\|ComputedIntervalText\|\bFps\b" "cs_module/TrafficConfigViewModel.cs" "cs_module/TrafficConfigDialog.xaml"
```
Expected: no remaining bare `Fps` property/binding anywhere (the old property is fully gone); `VideoLengthSeconds`, `ExportFps`, `ComputedInterpolationFps`, `ComputedIntervalText` all present in the ViewModel; XAML binds `VideoLengthSeconds`, `ExportFps`, `ComputedIntervalText`.

- [ ] **Step 7: Commit**

```bash
git add cs_module/TrafficConfigViewModel.cs cs_module/TrafficConfigDialog.xaml
git commit -m "$(cat <<'EOF'
Replace direct FPS input with video length + export FPS

Users now specify how long the exported video should be and at what
FPS, instead of the interpolation FPS directly. The dialog computes
and displays the derived interpolation interval live, clamped to the
pipeline's supported 0.1-60 fps range with a visible note when
clamping occurs.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Fix stylx renderer to surface its fallback reason

**Files:**
- Modify: `cs_module/RendererHelper.cs`

- [ ] **Step 1: Change `ApplyStylxRenderer`'s return type from `void` to `string`, returning the failure reason (or `null` on success)**

Current code (lines 41-131):
```csharp
        /// <summary>
        /// Apply a Unique Values renderer using named point symbols ("1" through "15")
        /// looked up from a user-provided .stylx style file, keyed on the style_id field.
        /// </summary>
        /// <param name="layer">The feature layer to symbolize.</param>
        /// <param name="stylxPath">Full path to the .stylx file.</param>
        /// <remarks>
        /// Must be called from within an existing <c>QueuedTask.Run</c> context (same
        /// requirement as <see cref="ApplySpeedColorRenderer"/>, which directly calls
        /// <c>layer.SetRenderer</c>/<c>layer.CreateRenderer</c> — CIM-thread-affine
        /// operations). This method does not schedule its own worker-thread callback;
        /// its only caller (<c>TrafficLoaderButton.AddLayersToMap</c>) already runs on
        /// the CIM worker thread inside its own <c>QueuedTask.Run</c>.
        /// </remarks>
        public static void ApplyStylxRenderer(FeatureLayer layer, string stylxPath)
        {
            try
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying stylx renderer from '{stylxPath}': {ex.Message}. Falling back to speed color renderer.");
                ApplySpeedColorRenderer(layer);
            }
        }
```

Change to:
```csharp
        /// <summary>
        /// Apply a Unique Values renderer using named point symbols ("1" through "15")
        /// looked up from a user-provided .stylx style file, keyed on the style_id field.
        /// </summary>
        /// <param name="layer">The feature layer to symbolize.</param>
        /// <param name="stylxPath">Full path to the .stylx file.</param>
        /// <returns>
        /// <c>null</c> if the stylx-based unique values renderer was applied successfully.
        /// Otherwise, a human-readable reason the fallback speed-color renderer was used
        /// instead — one of: style file not registered/found, no matching named symbols,
        /// or an exception message. Callers should surface this to the user rather than
        /// silently showing a plain success message when a stylx path was provided but
        /// the fallback fired.
        /// </returns>
        /// <remarks>
        /// Must be called from within an existing <c>QueuedTask.Run</c> context (same
        /// requirement as <see cref="ApplySpeedColorRenderer"/>, which directly calls
        /// <c>layer.SetRenderer</c>/<c>layer.CreateRenderer</c> — CIM-thread-affine
        /// operations). This method does not schedule its own worker-thread callback;
        /// its callers already run on the CIM worker thread inside their own
        /// <c>QueuedTask.Run</c>.
        /// </remarks>
        public static string ApplyStylxRenderer(FeatureLayer layer, string stylxPath)
        {
            try
            {
                // Register the style file with the current project (idempotent if already added)
                StyleHelper.AddStyle(ArcGIS.Desktop.Core.Project.Current, stylxPath);

                string styleFileName = System.IO.Path.GetFileNameWithoutExtension(stylxPath);
                var styleItem = ArcGIS.Desktop.Core.Project.Current
                    .GetItems<StyleProjectItem>()
                    .FirstOrDefault(s => string.Equals(s.Name, styleFileName, StringComparison.OrdinalIgnoreCase));

                if (styleItem == null)
                {
                    string reason = "Style file not found in project after registration";
                    System.Diagnostics.Debug.WriteLine($"Could not find registered style project item for: {stylxPath}");
                    ApplySpeedColorRenderer(layer);
                    return reason;
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
                    string reason = "No symbols named 1-15 found in style file";
                    System.Diagnostics.Debug.WriteLine("No matching symbols found in stylx file — falling back to speed color renderer");
                    ApplySpeedColorRenderer(layer);
                    return reason;
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
                return null;
            }
            catch (Exception ex)
            {
                string reason = $"Error loading style file: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error applying stylx renderer from '{stylxPath}': {ex.Message}. Falling back to speed color renderer.");
                ApplySpeedColorRenderer(layer);
                return reason;
            }
        }
```

- [ ] **Step 2: Verify by code review**

```bash
grep -n "public static string ApplyStylxRenderer\|return null\|return reason" "cs_module/RendererHelper.cs"
```
Expected: method signature now returns `string`; exactly 1 `return null` (success path) and 3 `return reason` (the 3 fallback paths).

- [ ] **Step 3: Commit**

```bash
git add cs_module/RendererHelper.cs
git commit -m "$(cat <<'EOF'
Surface ApplyStylxRenderer's fallback reason instead of failing silently

Previously all 3 fallback paths (style file not found, no matching
symbols, exception) silently applied the default speed-color ramp
with no indication why — this was the direct cause of a bug where
graduated colors appeared instead of expected unique-value stylx
symbols. Now returns null on success or a human-readable reason on
fallback, so callers can surface it to the user.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Create `LoadAndAnimateButton` (fused button) — Part A: bbox capture + config dialog + pipeline run

**Files:**
- Create: `cs_module/LoadAndAnimateButton.cs`

This task creates the new file with the bbox-capture and pipeline-run logic (mirroring `DrawBboxButton` + the pipeline-invocation half of `TrafficLoaderButton`). The layer-adding/symbology/3D-scene logic is added in Task 5 (same file, continued) to keep this task's diff reviewable.

- [ ] **Step 1: Create `cs_module/LoadAndAnimateButton.cs` with the click handler, bbox capture, and pipeline-run logic**

```csharp
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
```

Note: this file is intentionally incomplete after this task — `AddLayersToMap` is referenced but not yet defined. Task 5 adds it as a continuation of this same class in this same file. This is expected and will be resolved by Task 5; do not attempt to make this file self-contained/compilable at the end of Task 4 alone.

- [ ] **Step 2: Verify by code review**

```bash
grep -n "class LoadAndAnimateButton\|private async void StartProcessing\|private static string BuildExtraArgs\|private static double TimeToSeconds\|AddLayersToMap" "cs_module/LoadAndAnimateButton.cs"
```
Expected: class declared, `StartProcessing`, `BuildExtraArgs`, `TimeToSeconds` all present; one reference to `AddLayersToMap` (called but not yet defined — expected at this point, resolved in Task 5).

- [ ] **Step 3: Commit**

```bash
git add cs_module/LoadAndAnimateButton.cs
git commit -m "$(cat <<'EOF'
Add LoadAndAnimateButton: bbox capture + dialog + pipeline run (part A)

First half of the fused Load & Animate button: captures the current
map extent as the study area (mandatory, was previously a separate
"Set Study Area" click), opens the config dialog, and runs the
pipeline using the dialog's computed interpolation fps. The
layer-adding/symbology/3D-scene logic (AddLayersToMap) is added in
the next commit to keep this diff reviewable — this file is not
compilable in isolation until then.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `LoadAndAnimateButton` — Part B: AddLayersToMap (2D layer + symbology + 3D scene)

**Files:**
- Modify: `cs_module/LoadAndAnimateButton.cs`

This task completes the file started in Task 4 by adding `AddLayersToMap`, which merges the 2D-layer-adding logic from the old `TrafficLoaderButton.AddLayersToMap` with the entire 3D-scene setup from the old `SceneButton.OnClick`, plus the new dark basemap/3D buildings (section 4 of the design spec) and the stylx-fallback-reason surfacing (Task 3's return value).

- [ ] **Step 1: Add the `AddLayersToMap` method to the end of the `LoadAndAnimateButton` class, before its closing brace**

Current end of file (from Task 4, the closing braces of `TimeToSeconds` and the class):
```csharp
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
```

Change to (inserting `AddLayersToMap` before the final two closing braces):
```csharp
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
                    sceneReady = await SetUpSceneAsync(fcName, fcFullPath, out sceneLayer);
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
        private async Task<bool> SetUpSceneAsync(string fcName, string fcFullPath, out FeatureLayer sceneLayerOut)
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
                    sceneLayerOut = null;
                    return false;
                }

                IMapPane pane = await ProApp.Panes.CreateMapPaneAsync(sceneMap);
                System.Diagnostics.Debug.WriteLine($"Scene pane opened: {pane != null}");

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

                sceneLayerOut = sceneLayer;
                return sceneLayer != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetUpSceneAsync error: {ex}");
                sceneLayerOut = null;
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: Verify by code review**

```bash
grep -n "private async Task AddLayersToMap\|private async Task<bool> SetUpSceneAsync\|Human Geography Dark Base\|Esri 3D Buildings\|ApplyStylxRenderer\|stylxFallbackReason" "cs_module/LoadAndAnimateButton.cs"
```
Expected: both methods present; both new layer URIs present; `ApplyStylxRenderer`'s return value captured into `stylxFallbackReason` and used in the result message.

Confirm the file is now a complete, single well-formed class (read the whole file back and check brace balance):
```bash
python3 -c "
content = open('cs_module/LoadAndAnimateButton.cs').read()
opens = content.count('{')
closes = content.count('}')
print(f'braces: {opens} open, {closes} close, balanced={opens == closes}')
"
```
Expected: `balanced=True`.

- [ ] **Step 3: Commit**

```bash
git add cs_module/LoadAndAnimateButton.cs
git commit -m "$(cat <<'EOF'
Add AddLayersToMap + SetUpSceneAsync to LoadAndAnimateButton (part B)

Completes the fused button: 2D layer creation/symbology (merged from
TrafficLoaderButton) followed automatically by 3D scene setup (merged
from SceneButton), now with a dark basemap (Human Geography Dark
Base) and Esri 3D Buildings at 50% transparency instead of the plain
World Topographic Map. Surfaces the stylx fallback reason (from the
prior RendererHelper change) in the final result dialog when a style
file was provided but couldn't be applied.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Delete the three superseded button files

**Files:**
- Delete: `cs_module/DrawBboxButton.cs`
- Delete: `cs_module/TrafficLoaderButton.cs`
- Delete: `cs_module/SceneButton.cs`

- [ ] **Step 1: Delete all three files**

```bash
git rm cs_module/DrawBboxButton.cs cs_module/TrafficLoaderButton.cs cs_module/SceneButton.cs
```

- [ ] **Step 2: Verify no other file references the deleted classes**

```bash
grep -rn "DrawBboxButton\|TrafficLoaderButton\|SceneButton" cs_module/ --include="*.cs" --include="*.daml"
```
Expected: matches ONLY in `cs_module/Config.daml` (the `className="DrawBboxButton"` etc. attributes — these are fixed in Task 7, not this task) — no matches in any `.cs` file (confirming `LoadAndAnimateButton.cs` and every other remaining `.cs` file no longer reference the deleted classes).

- [ ] **Step 3: Commit**

```bash
git commit -m "$(cat <<'EOF'
Delete DrawBboxButton, TrafficLoaderButton, SceneButton

Superseded by LoadAndAnimateButton, which merges all three into one
fused workflow. Config.daml is updated in the next commit to remove
the corresponding ribbon button entries.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Update `Config.daml` — fused button + placeholder for Export Video

**Files:**
- Modify: `cs_module/Config.daml`

- [ ] **Step 1: Replace the 3-button group and controls with the fused "Load & Animate" button**

Current code (lines 29-81, the full `<groups>` and `<controls>` sections):
```xml
      <groups>
        <!-- Animation Workflow group — sequential steps for loading and animating traffic data -->
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_SetStudyArea_Button" size="large"/>
          <button refID="UTPS_Addin_TrafficLoader_Button" size="large"/>
          <button refID="UTPS_Anim_Scene_Button" size="large"/>
        </group>
      </groups>

      <controls>
        <!-- Set Study Area button -->
        <button id="UTPS_Anim_SetStudyArea_Button" caption="1. Set Study Area"
                className="DrawBboxButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen32.png">
          <tooltip heading="Set Study Area">
            Captures the current map view extent as the spatial filter for data loading.
            Zoom and pan the map to your area of interest, then click this button.
            Only road links within this area will be processed — dramatically reducing
            processing time for large simulation datasets.
            <disabledText />
          </tooltip>
        </button>

        <!-- Load Traffic Data button -->
        <button id="UTPS_Addin_TrafficLoader_Button" caption="2. Load Traffic Data"
                className="TrafficLoaderButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonBlue16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonBlue32.png">
          <tooltip heading="Import Traffic Simulation">
            Load traffic simulation data from XML events file.
            Configure time range, FPS, and output settings.
            If a study area was set, only events within that area are processed.
            Output is written to a File Geodatabase with time automatically enabled.
            <disabledText />
          </tooltip>
        </button>

        <!-- Switch to 3D Scene button -->
        <button id="UTPS_Anim_Scene_Button" caption="3. Switch to 3D"
                className="SceneButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen32.png">
          <tooltip heading="Switch to 3D Local Scene">
            Switches the active map view to a 3D Local Scene.
            Adds a topographic basemap for visual context.
            Symbolizes traffic points as white 3D boxes.
            Terrain surface activates automatically (requires ArcGIS Online sign-in).
            For buildings: add your own layer via Catalog pane.
            <disabledText />
          </tooltip>
        </button>
      </controls> 
```

Change to:
```xml
      <groups>
        <!-- Animation Workflow group — load & animate traffic data, then export video -->
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_LoadAndAnimate_Button" size="large"/>
          <button refID="UTPS_Anim_ExportVideo_Button" size="large"/>
        </group>
      </groups>

      <controls>
        <!-- Load & Animate button (fused: study area + load data + 3D scene) -->
        <button id="UTPS_Anim_LoadAndAnimate_Button" caption="1. Load &amp; Animate"
                className="LoadAndAnimateButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonBlue16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonBlue32.png">
          <tooltip heading="Load &amp; Animate Traffic Simulation">
            Captures the current map view as the study area, loads traffic simulation
            data from an XML events file, applies speed-based symbology, enables the
            Time Slider, and sets up a 3D Local Scene — all in one click.
            Zoom/pan the map to your area of interest first, then click this button.
            <disabledText />
          </tooltip>
        </button>

        <!-- Export Video button -->
        <button id="UTPS_Anim_ExportVideo_Button" caption="2. Export Video"
                className="ExportVideoButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonGreen32.png">
          <tooltip heading="Export Animation Video">
            Exports the current map/scene view's Time Slider animation to an MP4 video.
            Choose an output path, resolution, and the start/end seconds of the video
            to export. Requires 'Load &amp; Animate' to have run first this session.
            <disabledText />
          </tooltip>
        </button>
      </controls> 
```

- [ ] **Step 2: Verify by code review**

```bash
grep -n "className=\|<button " "cs_module/Config.daml"
```
Expected: exactly 2 buttons, `className="LoadAndAnimateButton"` and `className="ExportVideoButton"` (the latter doesn't exist yet — created in Task 9 — this is expected; `Config.daml` referencing a not-yet-created class by name is fine since XAML/DAML has no compile-time class reference checking the way C# does, but flag this in your report so it's tracked until Task 9 lands).

- [ ] **Step 3: Commit**

```bash
git add cs_module/Config.daml
git commit -m "$(cat <<'EOF'
Update ribbon to 2 buttons: Load & Animate, Export Video

Replaces the 3-button Set Study Area / Load Traffic Data / Switch to
3D sequence with the fused LoadAndAnimateButton, and adds a new
Export Video button entry (ExportVideoButton class created in a
later commit).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Create `ExportProgressDialog` (indeterminate progress dialog)

**Files:**
- Create: `cs_module/ExportProgressDialog.xaml`
- Create: `cs_module/ExportProgressDialog.xaml.cs`

This is a minimal, purpose-built dialog distinct from the existing pipeline `ProgressDialog` (which shows percent-based stage progress parsed from Python stdout). This one shows only an indeterminate marquee bar, since the ArcGIS Pro SDK's video export API provides no percent-complete signal.

- [ ] **Step 1: Create `cs_module/ExportProgressDialog.xaml`**

```xml
<Window x:Class="UTPS_Addin.ExportProgressDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Exporting Video"
        Height="140"
        Width="400"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        WindowStyle="ToolWindow"
        Background="#F0F0F0">

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="Exporting video, please wait..."
                   FontSize="13"
                   Margin="0,0,0,15"/>

        <ProgressBar Grid.Row="1"
                     IsIndeterminate="True"
                     Height="20"
                     Margin="0,0,0,10"/>

        <TextBlock Grid.Row="2"
                   FontSize="10"
                   Foreground="#888"
                   TextWrapping="Wrap">
            Export progress cannot be shown as a percentage — ArcGIS Pro does not report
            per-frame export progress. This may take several minutes depending on video
            length, resolution, and your computer's performance.
        </TextBlock>
    </Grid>
</Window>
```

- [ ] **Step 2: Create `cs_module/ExportProgressDialog.xaml.cs`**

```csharp
using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Minimal indeterminate-progress dialog shown while a video export runs via
    /// ArcGIS Pro's ViewAnimation.BeginExport, which provides no percent-complete
    /// signal — only a start bool and a completion event.
    /// </summary>
    public partial class ExportProgressDialog : Window
    {
        public ExportProgressDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Close this dialog with the given DialogResult. Safe to call from any
        /// thread — marshals to the UI thread via Dispatcher if needed.
        /// </summary>
        public void CloseWithResult(bool result)
        {
            if (Dispatcher.CheckAccess())
            {
                DialogResult = result;
                Close();
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DialogResult = result;
                    Close();
                });
            }
        }
    }
}
```

- [ ] **Step 3: Verify by code review**

```bash
grep -n "class ExportProgressDialog\|IsIndeterminate\|CloseWithResult" "cs_module/ExportProgressDialog.xaml" "cs_module/ExportProgressDialog.xaml.cs"
```
Expected: `x:Class="UTPS_Addin.ExportProgressDialog"` in the XAML, `IsIndeterminate="True"` on the ProgressBar, `class ExportProgressDialog : Window` and `CloseWithResult` in the code-behind.

Confirm the `.csproj` doesn't need a manual entry for this new XAML+code-behind pair, following the same pattern already used for `TrafficConfigDialog.xaml`/`ProgressDialog.xaml` (both are picked up automatically since the project is SDK-style with WPF enabled — check the `.csproj`'s `<UseWPF>true</UseWPF>` setting, already present, means `.xaml` files are compiled automatically alongside their `.xaml.cs` code-behind with no explicit item list needed):
```bash
grep -n "UseWPF" "cs_module/UTPS_Addin.csproj"
```
Expected: `<UseWPF>true</UseWPF>` present, confirming no manual project-file changes are needed for the new XAML file.

- [ ] **Step 4: Commit**

```bash
git add cs_module/ExportProgressDialog.xaml cs_module/ExportProgressDialog.xaml.cs
git commit -m "$(cat <<'EOF'
Add ExportProgressDialog: indeterminate progress during video export

ArcGIS Pro's BeginExport API provides no percent-complete signal, so
this shows a marquee/indeterminate progress bar rather than a
determinate one, with an explanatory note about why no percentage
is shown.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Create `ExportVideoViewModel` + `ExportVideoDialog`

**Files:**
- Create: `cs_module/ExportVideoViewModel.cs`
- Create: `cs_module/ExportVideoDialog.xaml`
- Create: `cs_module/ExportVideoDialog.xaml.cs`

- [ ] **Step 1: Create `cs_module/ExportVideoViewModel.cs`**

```csharp
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace UTPS_Addin
{
    /// <summary>
    /// Resolution presets for video export.
    /// </summary>
    public enum VideoResolution
    {
        Res720p,
        Res1080p,
        Res4K
    }

    /// <summary>
    /// ViewModel for ExportVideoDialog. Handles data binding, validation, and the
    /// output file picker command. Start/End second bounds are validated against
    /// AnimationState.VideoLengthSeconds (set by the most recent Load & Animate run).
    /// </summary>
    public class ExportVideoViewModel : INotifyPropertyChanged
    {
        private readonly Window _parentWindow;

        private string _outputFilePath;
        private VideoResolution _resolution = VideoResolution.Res1080p;
        private double _startSecond = 0.0;
        private double _endSecond;
        private string _validationMessage;
        private bool _hasValidationErrors;

        public ICommand BrowseOutputCommand { get; }
        public ICommand OkCommand { get; }

        public ExportVideoViewModel(Window parentWindow)
        {
            _parentWindow = parentWindow;

            BrowseOutputCommand = new RelayCommand(BrowseOutputFile);
            OkCommand = new RelayCommand(OnOk);

            _endSecond = AnimationState.VideoLengthSeconds;

            _outputFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UTPS",
                "output",
                "traffic_video.mp4"
            );
        }

        #region Properties

        public string OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                if (_outputFilePath != value)
                {
                    _outputFilePath = value;
                    OnPropertyChanged(nameof(OutputFilePath));
                    ClearValidation();
                }
            }
        }

        public VideoResolution Resolution
        {
            get => _resolution;
            set
            {
                if (_resolution != value)
                {
                    _resolution = value;
                    OnPropertyChanged(nameof(Resolution));
                    ClearValidation();
                }
            }
        }

        public double StartSecond
        {
            get => _startSecond;
            set
            {
                if (_startSecond != value)
                {
                    _startSecond = value;
                    OnPropertyChanged(nameof(StartSecond));
                    ClearValidation();
                }
            }
        }

        public double EndSecond
        {
            get => _endSecond;
            set
            {
                if (_endSecond != value)
                {
                    _endSecond = value;
                    OnPropertyChanged(nameof(EndSecond));
                    ClearValidation();
                }
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            set
            {
                if (_validationMessage != value)
                {
                    _validationMessage = value;
                    OnPropertyChanged(nameof(ValidationMessage));
                }
            }
        }

        public bool HasValidationErrors
        {
            get => _hasValidationErrors;
            set
            {
                if (_hasValidationErrors != value)
                {
                    _hasValidationErrors = value;
                    OnPropertyChanged(nameof(HasValidationErrors));
                }
            }
        }

        #endregion

        #region File Browser

        private void BrowseOutputFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select Video Output Location",
                Filter = "MP4 Video (*.mp4)|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                FileName = Path.GetFileName(OutputFilePath) ?? "traffic_video.mp4"
            };

            string directory = Path.GetDirectoryName(OutputFilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFilePath = dialog.FileName;
            }
        }

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            var errors = new System.Text.StringBuilder();

            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                errors.AppendLine("• Output file path is required");
            }
            else
            {
                try
                {
                    string directory = Path.GetDirectoryName(OutputFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        errors.AppendLine($"• Output directory does not exist: {directory}");
                    }
                }
                catch (Exception ex)
                {
                    errors.AppendLine($"• Invalid output path: {ex.Message}");
                }
            }

            if (StartSecond < 0)
            {
                errors.AppendLine("• Start second must be 0 or greater");
            }

            if (EndSecond <= StartSecond)
            {
                errors.AppendLine("• End second must be greater than start second");
            }

            if (EndSecond > AnimationState.VideoLengthSeconds)
            {
                errors.AppendLine($"• End second cannot exceed the video length from Load & Animate ({AnimationState.VideoLengthSeconds:F0}s)");
            }

            if (errors.Length > 0)
            {
                ValidationMessage = errors.ToString().TrimEnd();
                HasValidationErrors = true;
                return false;
            }

            return true;
        }

        private void ClearValidation()
        {
            if (HasValidationErrors)
            {
                HasValidationErrors = false;
                ValidationMessage = string.Empty;
            }
        }

        #endregion

        #region Commands

        private void OnOk()
        {
            if (ValidateInputs())
            {
                _parentWindow.DialogResult = true;
                _parentWindow.Close();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        /// <summary>
        /// Convert the selected resolution preset to pixel dimensions.
        /// </summary>
        public (int Width, int Height) GetResolutionPixels()
        {
            switch (Resolution)
            {
                case VideoResolution.Res720p:
                    return (1280, 720);
                case VideoResolution.Res4K:
                    return (3840, 2160);
                case VideoResolution.Res1080p:
                default:
                    return (1920, 1080);
            }
        }
    }
}
```

- [ ] **Step 2: Create `cs_module/ExportVideoDialog.xaml`**

```xml
<Window x:Class="UTPS_Addin.ExportVideoDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:UTPS_Addin"
        Title="Export Video"
        Height="420"
        Width="550"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        Background="#F0F0F0">

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>

        <Style x:Key="FieldLabel" TargetType="TextBlock">
            <Setter Property="Margin" Value="0,8,0,4"/>
            <Setter Property="FontSize" Value="12"/>
        </Style>

        <Style x:Key="FieldTextBox" TargetType="TextBox">
            <Setter Property="Padding" Value="5"/>
            <Setter Property="Height" Value="28"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
        </Style>

        <Style x:Key="BrowseButton" TargetType="Button">
            <Setter Property="Width" Value="80"/>
            <Setter Property="Height" Value="28"/>
            <Setter Property="Margin" Value="8,0,0,0"/>
        </Style>
    </Window.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" TextWrapping="Wrap" Margin="0,0,0,10">
            Export the current view's Time Slider animation to an MP4 video.
        </TextBlock>

        <StackPanel Grid.Row="1">

            <TextBlock Text="Output File Path *" Style="{StaticResource FieldLabel}"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBox Grid.Column="0"
                         Text="{Binding OutputFilePath, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource FieldTextBox}"
                         ToolTip="Where to save the exported MP4 video"/>
                <Button Grid.Column="1"
                        Content="Browse..."
                        Style="{StaticResource BrowseButton}"
                        Command="{Binding BrowseOutputCommand}"/>
            </Grid>

            <TextBlock Text="Resolution" Style="{StaticResource FieldLabel}"/>
            <ComboBox SelectedValue="{Binding Resolution}"
                      SelectedValuePath="Tag"
                      Height="28"
                      VerticalContentAlignment="Center">
                <ComboBoxItem Content="720p (1280x720)" Tag="{x:Static local:VideoResolution.Res720p}"/>
                <ComboBoxItem Content="1080p (1920x1080)" Tag="{x:Static local:VideoResolution.Res1080p}" IsSelected="True"/>
                <ComboBoxItem Content="4K (3840x2160)" Tag="{x:Static local:VideoResolution.Res4K}"/>
            </ComboBox>

            <TextBlock Text="Export Range (seconds)" Style="{StaticResource FieldLabel}" Margin="0,12,0,4"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="20"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0">
                    <TextBlock Text="Start Second" FontSize="11" Margin="0,0,0,4"/>
                    <TextBox Text="{Binding StartSecond, UpdateSourceTrigger=PropertyChanged}"
                             Style="{StaticResource FieldTextBox}"
                             ToolTip="Video second to start exporting from (minimum 0)"/>
                </StackPanel>

                <TextBlock Grid.Column="1" Text="—"
                           VerticalAlignment="Bottom"
                           HorizontalAlignment="Center"
                           Margin="0,0,0,8"
                           FontSize="16"/>

                <StackPanel Grid.Column="2">
                    <TextBlock Text="End Second" FontSize="11" Margin="0,0,0,4"/>
                    <TextBox Text="{Binding EndSecond, UpdateSourceTrigger=PropertyChanged}"
                             Style="{StaticResource FieldTextBox}"
                             ToolTip="Video second to stop exporting at (maximum = video length from Load &amp; Animate)"/>
                </StackPanel>
            </Grid>

            <TextBlock FontSize="10" Foreground="#888" Margin="0,4,0,0" TextWrapping="Wrap">
                Export range refers to seconds within the exported VIDEO's own timeline
                (0 to the video length chosen in Load &amp; Animate), not the simulated data's
                time range.
            </TextBlock>

            <!-- Validation Messages -->
            <Border Background="#FFF3CD"
                    BorderBrush="#FFC107"
                    BorderThickness="1"
                    CornerRadius="4"
                    Padding="10"
                    Margin="0,15,0,0"
                    Visibility="{Binding HasValidationErrors, Converter={StaticResource BoolToVisibilityConverter}}">
                <StackPanel>
                    <TextBlock Text="⚠ Please correct the following:" FontWeight="Bold" Foreground="#856404"/>
                    <TextBlock Text="{Binding ValidationMessage}"
                               TextWrapping="Wrap"
                               Margin="0,5,0,0"
                               Foreground="#856404"/>
                </StackPanel>
            </Border>

        </StackPanel>

        <Grid Grid.Row="2" Margin="0,15,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0"
                       Text="* Required fields"
                       VerticalAlignment="Center"
                       FontSize="10"
                       Foreground="#666"/>

            <Button Grid.Column="1"
                    Content="Export"
                    Width="90"
                    Height="32"
                    Margin="0,0,10,0"
                    IsDefault="True"
                    Command="{Binding OkCommand}"/>

            <Button Grid.Column="2"
                    Content="Cancel"
                    Width="90"
                    Height="32"
                    IsCancel="True"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 3: Create `cs_module/ExportVideoDialog.xaml.cs`**

```csharp
using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Interaction logic for ExportVideoDialog.xaml
    /// Dialog for configuring video export parameters (output path, resolution,
    /// start/end second within the video's own timeline).
    /// </summary>
    public partial class ExportVideoDialog : Window
    {
        public ExportVideoDialog()
        {
            InitializeComponent();
            DataContext = new ExportVideoViewModel(this);
        }
    }
}
```

- [ ] **Step 4: Verify by code review**

```bash
grep -n "class ExportVideoViewModel\|enum VideoResolution\|GetResolutionPixels\|VideoLengthSeconds" "cs_module/ExportVideoViewModel.cs"
grep -n "x:Class=\"UTPS_Addin.ExportVideoDialog\"\|Binding StartSecond\|Binding EndSecond\|Binding Resolution" "cs_module/ExportVideoDialog.xaml"
```
Expected: `VideoResolution` enum with 3 values, `ExportVideoViewModel` class with `GetResolutionPixels()`, reads `AnimationState.VideoLengthSeconds` in the constructor for `_endSecond` default and in validation; XAML binds `StartSecond`, `EndSecond`, `Resolution`.

- [ ] **Step 5: Commit**

```bash
git add cs_module/ExportVideoViewModel.cs cs_module/ExportVideoDialog.xaml cs_module/ExportVideoDialog.xaml.cs
git commit -m "$(cat <<'EOF'
Add ExportVideoDialog + ExportVideoViewModel

Dialog for configuring video export: output file path, resolution
preset (720p/1080p/4K), and start/end second within the exported
video's own timeline (bounded by AnimationState.VideoLengthSeconds
set during the most recent Load & Animate run). No FPS field — reuses
AnimationState.ExportFps from Load & Animate. No time estimate shown,
since resolution affects export duration unpredictably.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Create `ExportVideoButton`

**Files:**
- Create: `cs_module/ExportVideoButton.cs`

- [ ] **Step 1: Create `cs_module/ExportVideoButton.cs`**

```csharp
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Animations;
using ArcGIS.Desktop.Mapping.Events;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UTPS_Addin
{
    /// <summary>
    /// Export the current view's Time Slider animation to an MP4 video, using the
    /// ArcGIS Pro SDK's Animation/TimeTrack/BeginExport API. Requires "Load &amp;
    /// Animate" to have run first this session (reads AnimationState.VideoLengthSeconds
    /// and AnimationState.ExportFps, both unset/0 until that button has run).
    /// </summary>
    internal class ExportVideoButton : Button
    {
        private const string AnimationName = "TrafficAnimation";

        protected override async void OnClick()
        {
            try
            {
                // Guard: Load & Animate must have run first this session
                if (AnimationState.VideoLengthSeconds <= 0 || AnimationState.ExportFps <= 0)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "Please run 'Load & Animate' first.",
                        "No Animation Data",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var mapView = MapView.Active;
                if (mapView == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "No active map or scene view found. Please open a view first.",
                        "No Active View",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (mapView.Time == null)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "The active view has no time range set. Please run 'Load & Animate' " +
                        "and ensure the Time Slider is enabled before exporting.",
                        "No Time Range",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var dialog = new ExportVideoDialog
                {
                    Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
                };

                bool? result = dialog.ShowDialog();
                if (result != true)
                {
                    System.Diagnostics.Debug.WriteLine("Export Video dialog cancelled by user");
                    return;
                }

                var viewModel = dialog.DataContext as ExportVideoViewModel;
                if (viewModel == null) return;

                await RunExportAsync(mapView, viewModel);
            }
            catch (Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"Error exporting video:\n{ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Error in ExportVideoButton: {ex}");
            }
        }

        /// <summary>
        /// Build animation keyframes spanning the view's time range over
        /// AnimationState.VideoLengthSeconds, align the time span to
        /// AnimationState.InterpolationIntervalSeconds (preventing flashing at any
        /// export resolution/range), then call BeginExport on the UI thread and show
        /// an indeterminate progress dialog until the export finishes.
        /// </summary>
        private async Task RunExportAsync(MapView mapView, ExportVideoViewModel viewModel)
        {
            // ── Build keyframes inside QueuedTask.Run (CIM worker thread) ────────────
            await QueuedTask.Run(() =>
            {
                var animation = mapView.Map.Animation;
                var timeTrack = animation.Tracks.OfType<TimeTrack>().First();

                // Clear any pre-existing keyframes from a prior export so re-running
                // this button doesn't accumulate stale keyframes.
                foreach (var kf in timeTrack.Keyframes.ToArray())
                {
                    timeTrack.RemoveKeyframe(kf);
                }

                var startExtent = new TimeExtent(mapView.Time.Start);
                var endExtent = new TimeExtent(mapView.Time.End);

                timeTrack.CreateKeyframe(startExtent, TimeSpan.Zero, AnimationTransition.Linear);
                timeTrack.CreateKeyframe(
                    endExtent,
                    TimeSpan.FromSeconds(AnimationState.VideoLengthSeconds),
                    AnimationTransition.Linear);

                // Align the view's time span to the same interpolation interval used
                // at import time, so every exported frame's sampled instant falls
                // inside a window containing a data point — regardless of the
                // start/end second range or resolution chosen for this export.
                double spanSeconds = AnimationState.InterpolationIntervalSeconds;
                if (spanSeconds > 0)
                {
                    mapView.Time = new TimeRange(mapView.Time.Start, mapView.Time.Start + TimeSpan.FromSeconds(spanSeconds));
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Animation keyframes built: {mapView.Time.Start:HH:mm:ss} -> " +
                    $"{mapView.Time.End:HH:mm:ss} over {AnimationState.VideoLengthSeconds}s video, " +
                    $"span={spanSeconds:F3}s");
            });

            // ── Export must be started on the UI thread, NOT inside QueuedTask.Run ───
            var (width, height) = viewModel.GetResolutionPixels();
            var exportParams = new AnimationExportParameters
            {
                FilePath = viewModel.OutputFilePath,
                ResolutionWidth = width,
                ResolutionHeight = height,
                FrameRate = AnimationState.ExportFps,
                Quality = 0.9,
                StartFrame = (int)(viewModel.StartSecond * AnimationState.ExportFps),
                EndFrame = (int)(viewModel.EndSecond * AnimationState.ExportFps),
            };

            var progressDialog = new ExportProgressDialog
            {
                Owner = ArcGIS.Desktop.Framework.FrameworkApplication.Current.MainWindow
            };

            EventHandler<AnimationExportFinishedEventArgs> onFinished = null;
            onFinished = (sender, args) =>
            {
                AnimationExportFinishedEvent.Unsubscribe(onFinished);

                bool success = string.IsNullOrEmpty(args.ErrorMessage);
                progressDialog.CloseWithResult(success);

                if (success)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        $"Video exported successfully!\n\n{args.Path}",
                        "Export Complete",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        $"Video export failed:\n{args.ErrorMessage}",
                        "Export Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            };
            AnimationExportFinishedEvent.Subscribe(onFinished);

            bool started = mapView.Animation.BeginExport(AnimationName, exportParams);

            if (!started)
            {
                AnimationExportFinishedEvent.Unsubscribe(onFinished);
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    "Video export could not be started. Check the output path and try again.",
                    "Export Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            // Show the indeterminate progress dialog; it's closed by onFinished above
            // when AnimationExportFinishedEvent fires.
            progressDialog.ShowDialog();
        }
    }
}
```

- [ ] **Step 2: Verify by code review**

```bash
grep -n "class ExportVideoButton\|AnimationExportParameters\|BeginExport\|AnimationExportFinishedEvent\|QueuedTask.Run" "cs_module/ExportVideoButton.cs"
```
Expected: class declared; `AnimationExportParameters` constructed; `BeginExport` called; `AnimationExportFinishedEvent.Subscribe`/`Unsubscribe` both present (one-shot subscription pattern); keyframe-building wrapped in `QueuedTask.Run`, but `BeginExport` itself is NOT inside a `QueuedTask.Run` call (confirm by reading the surrounding code — `BeginExport` must run on the UI thread per the SDK's documented `InvalidOperationException` requirement).

```bash
grep -n "await QueuedTask.Run" "cs_module/ExportVideoButton.cs"
```
Expected: exactly 1 occurrence (only around the keyframe-building block) — `BeginExport` must appear textually outside any `QueuedTask.Run(...)` call.

- [ ] **Step 3: Commit**

```bash
git add cs_module/ExportVideoButton.cs
git commit -m "$(cat <<'EOF'
Add ExportVideoButton: builds animation keyframes and exports video

Guards on AnimationState.VideoLengthSeconds/ExportFps being set (Load
& Animate must have run first this session). Builds TimeTrack
keyframes spanning the view's time range over the configured video
length, aligns the time span to InterpolationIntervalSeconds to
prevent flashing at any export resolution/range, then calls
BeginExport on the UI thread (required by the SDK) with a one-shot
AnimationExportFinishedEvent subscription driving the progress dialog
close and final success/failure message.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Final review pass — confirm the whole ribbon and file set are wired together correctly

**Files:** none modified — review only.

- [ ] **Step 1: Confirm no dangling references to deleted classes**

```bash
grep -rn "DrawBboxButton\|TrafficLoaderButton\|SceneButton\b" cs_module/ --include="*.cs"
```
Expected: no matches (empty output) — confirms every `.cs` file reference to the three deleted classes is gone. (Note: `SceneTrafficLayer`/`AnimationState.SceneTrafficLayer` legitimately contains the substring pattern differently and is a property name, not the class — a plain-text `SceneButton\b` word-boundary grep should not false-positive on it, but double check the actual grep output rather than assuming.)

- [ ] **Step 2: Confirm `Config.daml` and the C# classes agree**

```bash
grep -n "className=" cs_module/Config.daml
```
Expected: `className="LoadAndAnimateButton"` and `className="ExportVideoButton"` — both classes must exist as files by this point in the plan (Task 5 and Task 10 respectively).

```bash
ls cs_module/LoadAndAnimateButton.cs cs_module/ExportVideoButton.cs cs_module/ExportVideoDialog.xaml cs_module/ExportVideoViewModel.cs cs_module/ExportProgressDialog.xaml
```
Expected: all 5 files exist.

- [ ] **Step 3: Confirm the FPS/VideoLength/ExportFps chain is fully consistent**

```bash
grep -rn "\.Fps\b" cs_module/ --include="*.cs" --include="*.xaml"
```
Expected: no matches — the old `TrafficConfigViewModel.Fps` property (and all its usages) is fully replaced by `VideoLengthSeconds`/`ExportFps`/`ComputedInterpolationFps` everywhere.

```bash
grep -rn "ComputedInterpolationFps\|VideoLengthSeconds\|ExportFps\b\|InterpolationIntervalSeconds" cs_module/ --include="*.cs" | wc -l
```
Expected: a healthy number of occurrences (at minimum: 2 in `TrafficConfigViewModel.cs` defining them, 1+ in `LoadAndAnimateButton.cs` reading `ComputedInterpolationFps` and writing to `AnimationState`, 3 in `AnimationState.cs` declaring the fields, 2+ in `ExportVideoViewModel.cs`/`ExportVideoButton.cs` reading them back).

- [ ] **Step 4: Confirm the Python pipeline side needs no changes**

```bash
grep -n "interpolation_fps\|--fps" python_module/pipeline/main_pipeline.py cs_module/scripts/traffic_loader_wrapper.py
```
Expected: unchanged from before this plan — still accepts a float in the 0.1-60 range via `--fps`, confirming `LoadAndAnimateButton`'s `BuildExtraArgs(config, computedFps)` (Task 4) passes a compatible value with no pipeline-side changes needed.

- [ ] **Step 5: Write a manual test checklist for the user (this add-in cannot be built/run on this machine)**

Since the C# project targets `net8.0-windows` and requires ArcGIS Pro DLLs only present on a Windows machine with ArcGIS Pro installed, report to the user that the following must be manually verified on that machine before considering this work fully done:

1. Build `UTPS_Addin.csproj` in Visual Studio / `dotnet build` on Windows — confirm no compile errors. This plan's most novel API surface (`ArcGIS.Desktop.Mapping.Animations.Animation`, `TimeTrack`, `AnimationExportParameters`, `BeginExport`, `AnimationExportFinishedEvent`) was researched against Esri's SDK documentation but never compiled — this is the highest-risk area to verify first.
2. Confirm the ribbon shows exactly 2 buttons: "1. Load & Animate" and "2. Export Video".
3. Zoom to a study area, click "Load & Animate" — confirm the dialog shows Video Length + Export FPS fields (not the old single FPS field) with a live-updating computed-interval readout, and that entering values that would push the computed fps outside 0.1-60 shows a clamping note.
4. Complete the dialog with no stylx file — confirm the pipeline runs, 2D layer is added and colored (default red-green ramp), Time Slider works, and the 3D scene is created automatically with the dark basemap and semi-transparent 3D buildings (may require ArcGIS Online sign-in — confirm graceful behavior if not signed in, i.e. no crash, just missing basemap/buildings).
5. Re-run with a `.stylx` file that's missing some of the "1"-"15" named symbols (or an invalid path) — confirm the final dialog now explicitly states why the stylx renderer wasn't applied, instead of silently showing plain "Success."
6. Click "Export Video" before ever running Load & Animate (e.g. fresh Pro session with an existing layer) — confirm the "Please run 'Load & Animate' first" warning appears and no export dialog opens.
7. After a successful Load & Animate, click "Export Video" — confirm the dialog's End Second defaults to and is capped by the video length just configured, resolution dropdown works, and clicking Export shows the indeterminate progress dialog, followed by a success message with the output file path once done. Play the resulting MP4 and confirm no flashing.
8. Try exporting a short clip (e.g. seconds 5-10 of a 60s video) and confirm only that sub-range is in the output file.

This step produces no code — it's a report to hand to the user, not a task to check off silently.

- [ ] **Step 6: No commit for this task** (review-only, nothing to commit).

---

## Summary of Spec Coverage

| Spec Section | Task(s) |
|---|---|
| 1. Fused "Load & Animate" button | Tasks 4, 5, 6, 7 |
| 2. Video length + export FPS → interpolation interval | Task 2 (dialog), Task 4 (pipeline call site), Task 5 (Time Slider span) |
| 3. Stylx renderer failure visibility | Task 3 (RendererHelper), Task 5 (surfaced in result dialog) |
| 4. Human Geography Dark basemap + Esri 3D Buildings | Task 5 (`SetUpSceneAsync`) |
| 5. Export Video button | Tasks 1 (AnimationState fields), 8 (progress dialog), 9 (config dialog), 10 (button + export logic) |
