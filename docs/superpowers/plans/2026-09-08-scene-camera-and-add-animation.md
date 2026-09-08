# Scene Camera, Add Animation Swap, Speed Multiplier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-zoom the 3D scene to a 45° oblique view over the study area, replace the (currently disabled) Export Video button with ArcGIS Pro's built-in "Add Animation" command, and rework the config dialog's video-length input into a speed-multiplier input with a live-computed video length readout.

**Architecture:** All changes are in the existing `cs_module/` ArcGIS Pro add-in. The camera auto-zoom is a new final step appended to `LoadAndAnimateButton.SetUpSceneAsync`. The Export Video feature (6 files, currently stubbed/disabled from a prior session) is deleted outright and replaced with a direct `refID` reference to Esri's built-in animation command in `Config.daml` — no new C# needed for that swap. The dialog rework replaces one property (`VideoLengthSeconds`) with another (`SpeedMultiplier`) and updates two computed properties' formulas in `TrafficConfigViewModel.cs`.

**Tech Stack:** C# / .NET 8 / WPF / ArcGIS Pro SDK (`ArcGIS.Desktop.Mapping`, `ArcGIS.Core.Geometry`). No compiler available in this environment (macOS) — every task is verified by careful manual code review against exact API signatures researched for this plan, not an actual build. This must be built and manually tested in ArcGIS Pro on Windows before being trusted.

---

## Task 1: Delete the Export Video feature (6 files)

**Files:**
- Delete: `cs_module/ExportVideoButton.cs`
- Delete: `cs_module/ExportVideoDialog.xaml`
- Delete: `cs_module/ExportVideoDialog.xaml.cs`
- Delete: `cs_module/ExportVideoViewModel.cs`
- Delete: `cs_module/ExportProgressDialog.xaml`
- Delete: `cs_module/ExportProgressDialog.xaml.cs`

- [ ] **Step 1: Delete all 6 files**

```bash
git rm cs_module/ExportVideoButton.cs cs_module/ExportVideoDialog.xaml cs_module/ExportVideoDialog.xaml.cs cs_module/ExportVideoViewModel.cs cs_module/ExportProgressDialog.xaml cs_module/ExportProgressDialog.xaml.cs
```

- [ ] **Step 2: Verify no other `.cs`/`.xaml` file references the deleted classes**

```bash
grep -rln "ExportVideoButton\|ExportVideoDialog\|ExportVideoViewModel\|ExportProgressDialog\|VideoResolution\b" cs_module/ --include="*.cs" --include="*.xaml"
```
Expected: no output (empty) — `Config.daml` is fixed separately in Task 3, and this grep only covers `.cs`/`.xaml` files.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Delete the Export Video feature

Superseded by referencing ArcGIS Pro's own built-in "Add Animation"
command directly in Config.daml (next commit) instead of a custom
export button. Removes ExportVideoButton, ExportVideoDialog,
ExportVideoViewModel, and ExportProgressDialog entirely.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Remove Export-related fields from `AnimationState`

**Files:**
- Modify: `cs_module/AnimationState.cs`

- [ ] **Step 1: Remove `VideoLengthSeconds`, `ExportFps`, `InterpolationIntervalSeconds`**

Current code (lines 57-79):
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

Change to:
```csharp
        // ── Animation settings ──────────────────────────────────────────────────
        /// <summary>Target duration of the exported animation in seconds. Default: 60.</summary>
        public static double AnimationDurationSeconds { get; set; } = 60.0;
```

- [ ] **Step 2: Remove their `Reset()` entries**

Current code (lines 93-109):
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
        }
```

- [ ] **Step 3: Verify by code review**

```bash
grep -n "VideoLengthSeconds\|ExportFps\|InterpolationIntervalSeconds" cs_module/AnimationState.cs
```
Expected: no output (empty).

- [ ] **Step 4: Commit**

```bash
git add cs_module/AnimationState.cs
git commit -m "$(cat <<'EOF'
Remove VideoLengthSeconds, ExportFps, InterpolationIntervalSeconds

Dead state now that the Export Video feature is deleted — nothing
reads these fields anymore.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Update `Config.daml` — remove Export Video, add Esri's built-in "Add Animation"

**Files:**
- Modify: `cs_module/Config.daml`

- [ ] **Step 1: Replace the Export Video button entry with a direct reference to Esri's built-in command**

Current code (lines 29-64):
```xml
      <groups>
        <!-- Animation Workflow group — load &amp; animate traffic data, then export video -->
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_LoadAndAnimate_Button" size="large"/>
          <button refID="UTPS_Anim_ExportVideo_Button" size="large"/>
        </group>
      </groups>

      <controls>
        <!-- Load &amp; Animate button (fused: study area + load data + 3D scene) -->
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

Change to:
```xml
      <groups>
        <!-- Animation Workflow group — load &amp; animate traffic data, then build an animation -->
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_LoadAndAnimate_Button" size="large"/>
          <button refID="esri_mapping_enableAnimationButton" size="large"/>
        </group>
      </groups>

      <controls>
        <!-- Load &amp; Animate button (fused: study area + load data + 3D scene) -->
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
      </controls> 
```

Note: `esri_mapping_enableAnimationButton` is Esri's own built-in "Add" animation command (confirmed against Esri's official ArcGIS Pro DAML ID Reference wiki — it is the same command shown in ArcGIS Pro's own View → Animation ribbon group). It carries its own caption ("Add"), icon, and enable/disable behavior — no `<button id="...">` control definition is needed for it in this file, since it's not defined by this add-in.

- [ ] **Step 2: Verify by code review**

```bash
grep -n "className=\|refID=\|<button " cs_module/Config.daml
```
Expected: `UTPS_Anim_LoadAndAnimate_Button` (className `LoadAndAnimateButton`) is the only `<button id="...">` control defined; the group references exactly 2 `refID`s: `UTPS_Anim_LoadAndAnimate_Button` and `esri_mapping_enableAnimationButton`.

```bash
python3 -c "
import xml.etree.ElementTree as ET
ET.parse('cs_module/Config.daml')
print('OK: valid XML')
"
```
Expected: `OK: valid XML`.

- [ ] **Step 3: Commit**

```bash
git add cs_module/Config.daml
git commit -m "$(cat <<'EOF'
Replace Export Video button with Esri's built-in Add Animation command

References esri_mapping_enableAnimationButton directly via refID
(Esri's real View > Animation > Add command, confirmed against the
official ArcGIS Pro DAML ID Reference) instead of a custom export
button. Shown with its own native caption and icon.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Speed Multiplier replaces Video Length in the config dialog

**Files:**
- Modify: `cs_module/TrafficConfigViewModel.cs`
- Modify: `cs_module/TrafficConfigDialog.xaml`

- [ ] **Step 1: Replace the `_videoLengthSeconds` field with `_speedMultiplier`**

Current code (line 25):
```csharp
        private double _videoLengthSeconds = 60.0;
```

Change to:
```csharp
        private int _speedMultiplier = 4;
```

- [ ] **Step 2: Replace the `VideoLengthSeconds` property with `SpeedMultiplier`, and add `ComputedVideoLengthSeconds`**

Current code (lines 132-145):
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
```

Change to:
```csharp
        public int SpeedMultiplier
        {
            get => _speedMultiplier;
            set
            {
                if (_speedMultiplier != value)
                {
                    _speedMultiplier = value;
                    OnPropertyChanged(nameof(SpeedMultiplier));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    ClearValidation();
                }
            }
        }

        /// <summary>
        /// Live-computed video length in seconds: the simulated time range
        /// (EndTime - StartTime) divided by SpeedMultiplier. Read-only, derived —
        /// not persisted, not settable directly.
        /// </summary>
        public double ComputedVideoLengthSeconds
        {
            get
            {
                if (!IsValidTimeFormat(StartTime) || !IsValidTimeFormat(EndTime) || SpeedMultiplier <= 0)
                {
                    return 0;
                }

                double totalSimSeconds = TimeToSeconds(EndTime) - TimeToSeconds(StartTime);
                if (totalSimSeconds <= 0)
                {
                    return 0;
                }

                return totalSimSeconds / SpeedMultiplier;
            }
        }
```

- [ ] **Step 3: Update `ComputedInterpolationFps` and `ComputedIntervalText` to use the new formula**

Current code (lines 162-223):
```csharp
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

Change to:
```csharp
        /// <summary>
        /// Computed interpolation fps = ExportFps / SpeedMultiplier, clamped to the
        /// pipeline's supported 0.1-60 fps range. Read by the caller after a successful
        /// dialog close.
        /// </summary>
        public double ComputedInterpolationFps
        {
            get
            {
                if (SpeedMultiplier <= 0 || ExportFps <= 0)
                {
                    return 1.0; // sane default while inputs are incomplete/invalid
                }

                double rawFps = ExportFps / SpeedMultiplier;
                return Math.Min(60.0, Math.Max(0.1, rawFps));
            }
        }

        /// <summary>
        /// Live-updating text shown in the dialog describing the computed video length
        /// and interpolation interval, including a note if the raw fps was clamped.
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

                if (totalSimSeconds <= 0 || SpeedMultiplier <= 0 || ExportFps <= 0)
                {
                    return "Computed: enter a valid time range, speed multiplier, and export fps above.";
                }

                double videoLengthSeconds = totalSimSeconds / SpeedMultiplier;
                double rawFps = ExportFps / SpeedMultiplier;
                double clampedFps = Math.Min(60.0, Math.Max(0.1, rawFps));
                double clampedIntervalSeconds = 1.0 / clampedFps;

                string videoLengthText = $"Computed video length: {videoLengthSeconds:F1}s";

                if (Math.Abs(clampedFps - rawFps) < 0.0001)
                {
                    return $"{videoLengthText}\n1 point every {clampedIntervalSeconds:F2}s";
                }

                string reason = rawFps > 60.0 ? "60 fps interpolation limit" : "0.1 fps interpolation limit";
                return $"{videoLengthText}\nRequested 1 point every {(1.0 / rawFps):F2}s, " +
                       $"clamped to {clampedIntervalSeconds:F2}s ({reason})";
            }
        }
```

- [ ] **Step 4: Add `SpeedMultiplier`/`ComputedVideoLengthSeconds` notification to `StartTime`/`EndTime` setters**

Current code (lines 88-116, both `StartTime` and `EndTime` properties already notify `ComputedIntervalText`):
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

Change both to also notify `ComputedVideoLengthSeconds`:
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
                    OnPropertyChanged(nameof(ComputedVideoLengthSeconds));
                    ClearValidation();
                }
            }
        }
```
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
                    OnPropertyChanged(nameof(ComputedVideoLengthSeconds));
                    ClearValidation();
                }
            }
        }
```

Also update the new `SpeedMultiplier` property (from Step 2) to notify `ComputedVideoLengthSeconds`:
```csharp
        public int SpeedMultiplier
        {
            get => _speedMultiplier;
            set
            {
                if (_speedMultiplier != value)
                {
                    _speedMultiplier = value;
                    OnPropertyChanged(nameof(SpeedMultiplier));
                    OnPropertyChanged(nameof(ComputedIntervalText));
                    OnPropertyChanged(nameof(ComputedVideoLengthSeconds));
                    ClearValidation();
                }
            }
        }
```

- [ ] **Step 5: Update validation — Speed Multiplier must be a positive integer**

Current code (lines 396-406):
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

Change to:
```csharp
            // Validate speed multiplier and export fps (the computed interpolation fps is
            // always clamped to a valid range internally, so no need to validate that)
            if (SpeedMultiplier < 1)
            {
                errors.AppendLine("• Speed Multiplier must be a whole number of 1 or greater");
            }

            if (ExportFps <= 0)
            {
                errors.AppendLine("• Export FPS must be greater than 0");
            }
```

- [ ] **Step 6: Update `TrafficConfigDialog.xaml` — replace the Video Length input with Speed Multiplier**

Current code (`cs_module/TrafficConfigDialog.xaml` lines 166-200):
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

Change to:
```xml
                <!-- Speed Multiplier + Export FPS -->
                <TextBlock Text="Animation Speed" Style="{StaticResource FieldLabel}" Margin="0,12,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="20"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0">
                        <TextBlock Text="Speed Multiplier (×N faster than real-time)" FontSize="11" Margin="0,0,0,4"/>
                        <TextBox Text="{Binding SpeedMultiplier, UpdateSourceTrigger=PropertyChanged}"
                                 Style="{StaticResource FieldTextBox}"
                                 ToolTip="How many times faster than real-time the animation should play (whole number, e.g. 4)"/>
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

- [ ] **Step 7: Verify by code review**

```bash
grep -n "VideoLengthSeconds\|SpeedMultiplier\|ComputedVideoLengthSeconds\|ComputedInterpolationFps\|ComputedIntervalText" cs_module/TrafficConfigViewModel.cs cs_module/TrafficConfigDialog.xaml
```
Expected: no `VideoLengthSeconds` matches anywhere; `SpeedMultiplier` present in both files (property + XAML binding); `ComputedVideoLengthSeconds` present in the ViewModel; `ComputedInterpolationFps`/`ComputedIntervalText` present with updated bodies.

```bash
python3 -c "
content = open('cs_module/TrafficConfigViewModel.cs').read()
o, c = content.count('{'), content.count('}')
print(f'braces: {o} open, {c} close, balanced={o==c}')
"
```
Expected: `balanced=True`.

```bash
python3 -c "
import xml.etree.ElementTree as ET
ET.parse('cs_module/TrafficConfigDialog.xaml')
print('OK: valid XML')
"
```
Expected: `OK: valid XML`.

- [ ] **Step 8: Commit**

```bash
git add cs_module/TrafficConfigViewModel.cs cs_module/TrafficConfigDialog.xaml
git commit -m "$(cat <<'EOF'
Replace Video Length input with Speed Multiplier

Users now enter how many times faster than real-time the animation
should play (an integer, e.g. 4x) instead of a target video length
directly. Video length is now a live-computed, read-only value
(simulated time range / multiplier), shown alongside the existing
computed interpolation interval. The interpolation fps formula
simplifies to ExportFps / SpeedMultiplier (mathematically equivalent
to the prior video-length-based formula).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Update `LoadAndAnimateButton` to use `SpeedMultiplier` instead of `VideoLengthSeconds`

**Files:**
- Modify: `cs_module/LoadAndAnimateButton.cs`

- [ ] **Step 1: Update the debug log line referencing `viewModel.VideoLengthSeconds`**

Current code (line 111):
```csharp
                        System.Diagnostics.Debug.WriteLine($"Video Length: {viewModel.VideoLengthSeconds}s, Export FPS: {viewModel.ExportFps}");
```

Change to:
```csharp
                        System.Diagnostics.Debug.WriteLine($"Speed Multiplier: {viewModel.SpeedMultiplier}x, Export FPS: {viewModel.ExportFps}");
```

- [ ] **Step 2: Remove the now-deleted `AnimationState` field assignments**

Current code (lines 144-147):
```csharp
                double computedFps = config.ComputedInterpolationFps;
                AnimationState.VideoLengthSeconds = config.VideoLengthSeconds;
                AnimationState.ExportFps = config.ExportFps;
                AnimationState.InterpolationIntervalSeconds = 1.0 / computedFps;
```

Change to:
```csharp
                double computedFps = config.ComputedInterpolationFps;
```

(`AnimationState.VideoLengthSeconds`, `.ExportFps`, `.InterpolationIntervalSeconds` were removed from `AnimationState.cs` in Task 2 — this call site must stop referencing them or the project won't compile. `computedFps` itself is still used locally throughout this method — e.g. `BuildExtraArgs(config, computedFps)` on the next line, and the Time Slider span calculation later in `AddLayersToMap` — so it must remain.)

- [ ] **Step 3: Verify by code review**

```bash
grep -n "AnimationState.VideoLengthSeconds\|AnimationState.ExportFps\|AnimationState.InterpolationIntervalSeconds\|viewModel.VideoLengthSeconds\|config.VideoLengthSeconds" cs_module/LoadAndAnimateButton.cs
```
Expected: no output (empty).

```bash
grep -n "computedFps" cs_module/LoadAndAnimateButton.cs
```
Expected: still multiple occurrences (declaration + `BuildExtraArgs` call + `AddLayersToMap` call + the Time Slider span calculation inside `AddLayersToMap`) — confirming `computedFps` itself was correctly preserved, only the `AnimationState.*` assignment lines were removed.

- [ ] **Step 4: Commit**

```bash
git add cs_module/LoadAndAnimateButton.cs
git commit -m "$(cat <<'EOF'
Stop writing to removed AnimationState video/fps fields

VideoLengthSeconds, ExportFps, and InterpolationIntervalSeconds were
removed from AnimationState (Task 2) since only the deleted Export
Video feature read them. computedFps itself is unchanged and still
used locally for the pipeline --fps argument and the Time Slider
span calculation.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Auto-zoom the 3D scene to a 45° oblique view over the study area

**Files:**
- Modify: `cs_module/LoadAndAnimateButton.cs`

- [ ] **Step 1: Capture the scene's `MapView` from the pane, and add the camera auto-zoom as the final step of `SetUpSceneAsync`**

Current code (the `CreateMapPaneAsync` call, around line 568):
```csharp
                IMapPane pane = await ProApp.Panes.CreateMapPaneAsync(sceneMap);
                System.Diagnostics.Debug.WriteLine($"Scene pane opened: {pane != null}");

                await Task.Delay(500);
```

Change to:
```csharp
                IMapPane pane = await ProApp.Panes.CreateMapPaneAsync(sceneMap);
                System.Diagnostics.Debug.WriteLine($"Scene pane opened: {pane != null}");

                MapView scenePane = pane?.MapView;

                await Task.Delay(500);
```

Current code (the end of the method, around lines 688-698):
```csharp
                    AnimationState.SceneTrafficLayer = sceneLayer;
                }

                return (sceneLayer != null, sceneLayer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetUpSceneAsync error: {ex}");
                return (false, null);
            }
        }
```

Change to:
```csharp
                    AnimationState.SceneTrafficLayer = sceneLayer;
                }

                // ── Auto-zoom to the study area at a 45° oblique angle ────────────────
                if (scenePane != null && AnimationState.BboxFilter != null)
                {
                    try
                    {
                        var wgs84 = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(4326);
                        var bb = AnimationState.BboxFilter;
                        var envelope = ArcGIS.Core.Geometry.EnvelopeBuilderEx.CreateEnvelope(
                            bb.XMin, bb.YMin, bb.XMax, bb.YMax, wgs84);

                        await scenePane.ZoomToAsync(envelope, TimeSpan.FromSeconds(1));

                        var camera = scenePane.Camera;
                        camera.Pitch = -45;
                        camera.Heading = 0;
                        await scenePane.ZoomToAsync(camera, TimeSpan.Zero);

                        System.Diagnostics.Debug.WriteLine("Scene camera auto-zoomed to study area at 45° pitch");
                    }
                    catch (Exception ex)
                    {
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
```

Note: `ZoomToAsync` is documented by Esri as safe to call directly (not nested inside `QueuedTask.Run`) — it's designed for UI-thread invocation, unlike the CIM-mutation calls elsewhere in this method (`SetRenderer`, `SetDefinition`, `LayerFactory.CreateLayer`, etc.) which correctly stay wrapped in `QueuedTask.Run`. Do NOT wrap this new block in `QueuedTask.Run` — that would deviate from the documented pattern (mirrors the same nested-QueuedTask.Run mistake found and fixed earlier in this codebase's history for the video export feature).

- [ ] **Step 2: Verify by code review**

```bash
grep -n "MapView scenePane\|pane?.MapView\|ZoomToAsync\|camera.Pitch\|camera.Heading\|EnvelopeBuilderEx" cs_module/LoadAndAnimateButton.cs
```
Expected: `MapView scenePane = pane?.MapView;` present; two `ZoomToAsync` calls (envelope, then camera); `camera.Pitch = -45;` and `camera.Heading = 0;` present; `EnvelopeBuilderEx.CreateEnvelope` present.

```bash
grep -n "await QueuedTask.Run" cs_module/LoadAndAnimateButton.cs
```
Expected: the count should be unchanged from before this task's edit (confirm by comparing against `git show HEAD~1:cs_module/LoadAndAnimateButton.cs | grep -c "await QueuedTask.Run"` if needed) — the new camera-zoom block must NOT add a new `QueuedTask.Run` call.

Confirm the file is still brace-balanced:
```bash
python3 -c "
content = open('cs_module/LoadAndAnimateButton.cs').read()
o, c = content.count('{'), content.count('}')
print(f'braces: {o} open, {c} close, balanced={o==c}')
"
```
Expected: `balanced=True`.

- [ ] **Step 3: Commit**

```bash
git add cs_module/LoadAndAnimateButton.cs
git commit -m "$(cat <<'EOF'
Auto-zoom the 3D scene to the study area at a 45° oblique angle

After all scene layers are added, zooms to the study area bbox
(letting ArcGIS compute an appropriate camera distance), then tilts
the resulting camera to -45° pitch / 0° heading and reapplies it —
mirroring Esri's own documented zoom-then-adjust-camera pattern.
Skipped silently if no study area was set or the zoom fails, matching
the existing graceful-degradation pattern used for the basemap and
3D buildings layers.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Final review pass

**Files:** none modified — review only.

- [ ] **Step 1: Confirm no dangling references to the deleted Export Video feature**

```bash
grep -rln "ExportVideoButton\|ExportVideoDialog\|ExportVideoViewModel\|ExportProgressDialog\|VideoResolution\b\|UTPS_Anim_ExportVideo_Button" cs_module/
```
Expected: no output (empty) — confirms both the `.cs`/`.xaml` files and `Config.daml` are fully clean of the deleted feature.

- [ ] **Step 2: Confirm `SpeedMultiplier` is used consistently across the config dialog and its consumer**

```bash
grep -rn "SpeedMultiplier\|VideoLengthSeconds" cs_module/ --include="*.cs" --include="*.xaml"
```
Expected: `SpeedMultiplier` appears in `TrafficConfigViewModel.cs` (field, property, validation, formulas) and `TrafficConfigDialog.xaml` (binding); `VideoLengthSeconds` appears nowhere.

- [ ] **Step 3: Confirm all touched files are brace-balanced / valid XML**

```bash
for f in cs_module/LoadAndAnimateButton.cs cs_module/TrafficConfigViewModel.cs cs_module/AnimationState.cs; do
  python3 -c "
content = open('$f').read()
o, c = content.count('{'), content.count('}')
status = 'OK' if o == c else 'MISMATCH'
print(f'$f: {o} open / {c} close -> {status}')
"
done
python3 -c "
import xml.etree.ElementTree as ET
for f in ['cs_module/Config.daml', 'cs_module/TrafficConfigDialog.xaml']:
    ET.parse(f)
    print(f'{f}: OK valid XML')
"
```
Expected: all `OK`.

- [ ] **Step 4: Write a manual test checklist for the user (this add-in cannot be built/run on this machine)**

Report to the user that the following must be manually verified on Windows before considering this work fully done:

1. Build `UTPS_Addin.csproj` — confirm no compile errors. `pane.MapView` (`IMapPane.MapView`) and `EnvelopeBuilderEx.CreateEnvelope` are the two newest, least-tested API calls in this plan — the most likely spots for a build surprise if this plan's research was wrong.
2. Confirm the ribbon shows exactly 2 buttons: "1. Load & Animate" and Esri's native "Add" (animation) button — hovering the second should show it behaves identically to View → Animation → Add in stock ArcGIS Pro.
3. Run "Load & Animate" — confirm the dialog shows "Speed Multiplier" (not "Video Length") with a live "Computed video length: Xs" readout that updates as you change the multiplier, export FPS, or time range.
4. After the pipeline completes and the 3D scene is created, confirm the scene automatically zooms to the study area and tilts to roughly a 45° angle, without needing manual navigation.
5. Re-test the `.stylx` style file fix from the prior session (if not already confirmed) — the symbol lookup fix (matching by `Name` instead of `LookupItem`'s `Key`) should now find symbols named "1"-"15" successfully.

This step produces no code — it's a report to hand to the user, not a task to check off silently.

- [ ] **Step 5: No commit for this task** (review-only, nothing to commit).

---

## Summary of Spec Coverage

| Spec Section | Task(s) |
|---|---|
| 1. 3D scene auto-zoom to 45° oblique view | Task 6 |
| 2. Remove Export Video, add Esri's built-in "Add Animation" | Tasks 1, 3 |
| 3. Dialog: Speed Multiplier replaces Video Length | Task 4 |
| 4. AnimationState cleanup | Tasks 2, 5 |
