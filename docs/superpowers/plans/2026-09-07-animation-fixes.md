# Animation Workflow Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the ArcGIS Pro add-in's animation workflow: use the correct time field (fixing the flashing symptom as a side effect), support fractional FPS down to 0.1, auto-color points at import (with optional `.stylx`-based styling) instead of via a manual toggle button, and keep point symbology intact when switching to the 3D scene.

**Architecture:** Two codebases in one repo — a C# ArcGIS Pro add-in (`cs_module/`, .NET 8, WPF, ArcGIS Pro SDK) and a Python ETL pipeline (`python_module/`) invoked as a subprocess. Changes touch both sides: Python gains a new derived field (`style_id`) and a wider FPS range; C# gains a new optional file input, a shared renderer helper, and drops a now-redundant ribbon button.

**Tech Stack:** C# / .NET 8 / WPF / ArcGIS Pro SDK (`ArcGIS.Desktop.Mapping`, `ArcGIS.Core.CIM`), Python 3.12 / Pydantic / PyArrow / GeoPandas.

**Important constraint:** The C# project (`UTPS_Addin.csproj`) targets `net8.0-windows` and references ArcGIS Pro DLLs from `C:\Program Files\ArcGIS\Pro\bin\...` via absolute Windows paths. **It cannot be compiled or run on this machine (macOS).** All C# tasks below are verified by careful manual code review (no syntax errors, correct API usage per the design spec's researched signatures) rather than a build step. Flag this to the user clearly when a C# task is "complete" — it still needs a real build + manual test in ArcGIS Pro on Windows before being trusted. The Python side has no test framework installed (no pytest) — pure-logic changes are verified with small standalone `python3 -c` scripts; anything touching GeoPandas/PyArrow/ArcGIS-shaped data is verified by manual/code review since no sample data pipeline run is available in this environment either.

---

## Task 1: Time field fix — use `timestamp_dt` instead of `timestamp`

**Files:**
- Modify: `cs_module/TrafficLoaderButton.cs:365-369`
- Modify: `cs_module/SceneButton.cs:158-163`

- [ ] **Step 1: Fix the time field in `TrafficLoaderButton.cs`**

Current code (lines 365-369):
```csharp
                                    cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                    {
                                        StartTimeField = "timestamp",
                                        EndTimeField   = "timestamp",  // instant — same field for start and end
                                    };
```

Change to:
```csharp
                                    cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                    {
                                        StartTimeField = "timestamp_dt",
                                        EndTimeField   = "timestamp_dt",  // instant — same field for start and end
                                    };
```

- [ ] **Step 2: Fix the time field in `SceneButton.cs`**

Current code (lines 158-163):
```csharp
                                cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                {
                                    StartTimeField = "timestamp",
                                    EndTimeField   = "timestamp",
                                };
```

Change to:
```csharp
                                cimLayer.FeatureTable.TimeFields = new CIMTimeTableDefinition
                                {
                                    StartTimeField = "timestamp_dt",
                                    EndTimeField   = "timestamp_dt",
                                };
```

- [ ] **Step 3: Verify by code review**

Confirm both files now reference `timestamp_dt` and that this matches the column name produced in `python_module/pipeline/parquet_to_animation.py`'s `_write_chunk` parquet branch (search for `timestamp_dt` — it's the `datetime64[ms, UTC]` column, confirmed at the time this plan was written).

```bash
grep -n "timestamp_dt" "cs_module/TrafficLoaderButton.cs" "cs_module/SceneButton.cs" "python_module/pipeline/parquet_to_animation.py"
```
Expected: matches in all three files, with the C# files now saying `StartTimeField = "timestamp_dt"` / `EndTimeField = "timestamp_dt"`.

- [ ] **Step 4: Commit**

```bash
git add cs_module/TrafficLoaderButton.cs cs_module/SceneButton.cs
git commit -m "$(cat <<'EOF'
Use timestamp_dt as the time field for ArcGIS layers

The timestamp column is a plain string; timestamp_dt is a proper
datetime64[ms, UTC] column. ArcGIS's Time Slider needs a real
time-typed field to step through frames without skipping — this is
also the suspected root cause of the flashing-every-10s symptom
during playback.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Support fractional FPS (0.1–60) — Python pipeline config

**Files:**
- Modify: `python_module/pipeline/main_pipeline.py:177`

- [ ] **Step 1: Change `interpolation_fps` from int to float**

Current code (`ProcessingConfig`, line 177):
```python
    interpolation_fps: int = Field(1, ge=1, le=60, description="Frames per second for sub-second interpolation (e.g. 10 = 0.1s steps). Higher values produce smoother ArcGIS Time Slider animation.")
```

Change to:
```python
    interpolation_fps: float = Field(1.0, ge=0.1, le=60, description="Frames per second for sub-second interpolation (e.g. 10 = 0.1s steps, 0.2 = 5s steps). Higher values produce smoother ArcGIS Time Slider animation; lower values are useful for condensing long simulations into short videos.")
```

- [ ] **Step 2: Verify with a standalone Pydantic check**

```bash
python3 -c "
import sys
sys.path.insert(0, '.')
from python_module.pipeline.main_pipeline import ProcessingConfig

# Should succeed: fractional fps within range
p = ProcessingConfig(interpolation_fps=0.2)
assert p.interpolation_fps == 0.2, p.interpolation_fps

# Should succeed: old integer-style usage still works
p2 = ProcessingConfig(interpolation_fps=5)
assert p2.interpolation_fps == 5.0, p2.interpolation_fps

# Should fail: below floor
try:
    ProcessingConfig(interpolation_fps=0.05)
    raise AssertionError('expected ValidationError for fps below 0.1')
except Exception as e:
    assert 'ValidationError' in type(e).__name__, type(e)

print('OK: interpolation_fps accepts fractional values 0.1-60')
"
```
Expected output: `OK: interpolation_fps accepts fractional values 0.1-60` (requires `pydantic` and `pyyaml` etc. to be importable in this environment's Python — if imports fail due to missing packages like `geopandas`/`lxml` at module load time, this step should still work since `main_pipeline.py`'s imports are all present in `requirements.txt`; if the environment genuinely lacks these packages, note that in the task result and fall back to a manual code review of the `Field(...)` line instead).

- [ ] **Step 3: Commit**

```bash
git add python_module/pipeline/main_pipeline.py
git commit -m "$(cat <<'EOF'
Allow fractional interpolation FPS down to 0.1

Supports condensing long simulations (e.g. a full hour into a 30s
video) at low frame rates like 0.2 fps (5-second steps), previously
blocked by the integer-only >= 1 constraint.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Support fractional FPS — CLI wrapper

**Files:**
- Modify: `cs_module/scripts/traffic_loader_wrapper.py:274-275`

- [ ] **Step 1: Change `--fps` argparse type from int to float**

Current code (lines 274-275):
```python
    parser.add_argument('--fps', type=int, default=2,
                        help='Frames per second for sub-second interpolation (default: 1)')
```

Change to:
```python
    parser.add_argument('--fps', type=float, default=2.0,
                        help='Frames per second for sub-second interpolation, supports fractional values e.g. 0.2 (default: 2.0)')
```

- [ ] **Step 2: Verify with a standalone argparse check**

```bash
python3 -c "
import argparse
parser = argparse.ArgumentParser()
parser.add_argument('--fps', type=float, default=2.0)
args = parser.parse_args(['--fps', '0.2'])
assert args.fps == 0.2, args.fps
args2 = parser.parse_args([])
assert args2.fps == 2.0, args2.fps
print('OK: --fps accepts fractional values')
"
```
Expected output: `OK: --fps accepts fractional values`

- [ ] **Step 3: Commit**

```bash
git add cs_module/scripts/traffic_loader_wrapper.py
git commit -m "$(cat <<'EOF'
Accept fractional --fps values in traffic_loader_wrapper.py

Matches the widened FPS range (0.1-60) now supported by the pipeline
config, so the add-in can pass fractional FPS through the CLI.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Support fractional FPS — C# dialog and view model

**Files:**
- Modify: `cs_module/TrafficConfigViewModel.cs:25, 126-138, 283-286`
- Modify: `cs_module/TrafficConfigDialog.xaml:176-182`

- [ ] **Step 1: Change the `Fps` backing field and property to `double`**

Current code (`TrafficConfigViewModel.cs` line 25):
```csharp
        private int _fps = 5;
```

Change to:
```csharp
        private double _fps = 5.0;
```

Current code (lines 126-138):
```csharp
        public int Fps
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

- [ ] **Step 2: Update the FPS validation range**

Current code (lines 283-286):
```csharp
            // Validate FPS
            if (Fps < 1 || Fps > 30)
            {
                errors.AppendLine("• Interpolation FPS must be between 1 and 30");
            }
```

Change to:
```csharp
            // Validate FPS
            if (Fps < 0.1 || Fps > 60)
            {
                errors.AppendLine("• Interpolation FPS must be between 0.1 and 60");
            }
```

- [ ] **Step 3: Update the dialog's helper label text**

In `cs_module/TrafficConfigDialog.xaml`, current code (lines 176-182):
```xml
                    <TextBox Grid.Column="0"
                             Text="{Binding Fps, UpdateSourceTrigger=PropertyChanged}"
                             Style="{StaticResource FieldTextBox}"
                             ToolTip="Frames per second for trajectory interpolation (e.g. 5 = one point every 0.2 s)"/>
                    <TextBlock Grid.Column="1"
                               Text="frames per second (1–30)"
                               VerticalAlignment="Center"
                               Margin="8,0,0,0"
                               FontSize="10"
                               Foreground="#888"/>
```

Change to:
```xml
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
```

The `TextBox` binds `Fps` as plain text with `UpdateSourceTrigger=PropertyChanged`; WPF's default double converter accepts decimal input (e.g. "0.2") without any further XAML changes, since the property type change to `double` is what the binding converts against.

- [ ] **Step 4: Verify by code review**

```bash
grep -n "_fps\|public.*Fps\|Fps < \|Fps > " "cs_module/TrafficConfigViewModel.cs"
```
Expected: `_fps` declared as `double`, `Fps` property typed `double`, validation checks `Fps < 0.1 || Fps > 60`.

Confirm no other file references `config.Fps` in a way that assumes `int` (e.g. implicit narrowing casts). Check callers:
```bash
grep -rn "config.Fps\|\.Fps\b" "cs_module/TrafficLoaderButton.cs"
```
Expected: `TrafficLoaderButton.cs` uses `config.Fps` in `BuildExtraArgs` (`args.Append($"--fps {config.Fps}");` — string interpolation of a `double` works fine and produces e.g. `"0.2"`) and in `TimeToSeconds`/`spanSeconds = 1.0 / config.Fps` (already float-safe division). No cast issues.

- [ ] **Step 5: Commit**

```bash
git add cs_module/TrafficConfigViewModel.cs cs_module/TrafficConfigDialog.xaml
git commit -m "$(cat <<'EOF'
Allow fractional FPS entry in the Load Traffic Data dialog

Fps changes from int to double (range 0.1-60) so users can specify
frame rates like 0.2 for condensing long simulations into short
animations, matching the pipeline's widened FPS range.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Add `style_id` field to the pipeline output (linear speed bins 1–15)

**Files:**
- Modify: `python_module/pipeline/parquet_to_animation.py:301-426` (`interpolate_trajectory`)
- Modify: `python_module/pipeline/parquet_to_animation.py:516-687` (`parquet_to_export` / `_write_chunk`)

- [ ] **Step 1: Write a standalone verification script for the binning formula BEFORE editing (documents expected behavior)**

Save this as a scratch check (do not commit this file — it's just to confirm the formula before wiring it into the pipeline):

```bash
python3 -c "
import math

def style_id_for(s):
    if s is None or s <= 0:
        return 1
    bin_width = 1.0 / 15
    return min(15, max(1, math.ceil(s / bin_width)))

cases = [
    (0.0, 1),
    (0.01, 1),
    (1/15, 1),        # exactly on the first boundary -> ceil(1.0) = 1
    (1/15 + 1e-9, 2), # just past the first boundary -> bin 2
    (0.5, 8),         # ceil(0.5/(1/15)) = ceil(7.5) = 8
    (1.0, 15),
    (1.3, 15),        # clamped
    (None, 1),
]
for s, expected in cases:
    got = style_id_for(s)
    assert got == expected, f's={s}: expected {expected}, got {got}'
print('OK: style_id binning formula verified for all cases')
"
```
Expected output: `OK: style_id binning formula verified for all cases`

This confirms the exact formula (including the `s is None or s <= 0` guard and boundary rounding with `math.ceil`) before it's embedded in `interpolate_trajectory`.

- [ ] **Step 2: Add `style_id` computation to `interpolate_trajectory`**

In `python_module/pipeline/parquet_to_animation.py`, the function `interpolate_trajectory` (starting line 301) currently computes `s_rounded` and `speed_lvl` once near the top (lines 348-350):
```python
    # Pre-compute fields shared across all code paths
    s_rounded = round(relative_velocity, 3) if relative_velocity is not None else None
    speed_lvl = compute_speed_level(relative_velocity)
```

Add a `style_id` computation immediately after:
```python
    # Pre-compute fields shared across all code paths
    s_rounded = round(relative_velocity, 3) if relative_velocity is not None else None
    speed_lvl = compute_speed_level(relative_velocity)
    style_id = compute_style_id(relative_velocity)
```

Add the new `compute_style_id` function just above `compute_speed_level` (before line 111), so it lives alongside the other speed-classification helper:
```python
def compute_style_id(s):
    """Map relative speed (s = travelling_speed / freespeed) to a linear 1-15 bin.

    Unlike compute_speed_level (which uses non-linear thresholds tuned for the
    built-in red-green color ramp), this divides [0, 1.0] into 15 equal-width
    bins for matching against externally-authored .stylx point symbols named
    "1" through "15". Values above 1.0 (faster than freespeed) clamp to bin 15.

    Args:
        s: Relative speed ratio (travelling_speed / freespeed). None or <= 0 -> bin 1.

    Returns:
        Integer style bin 1-15.

    Example:
        >>> compute_style_id(0.0)
        1
        >>> compute_style_id(0.5)
        8
        >>> compute_style_id(1.3)
        15
    """
    if s is None or s <= 0.0:
        return 1
    bin_width = 1.0 / 15
    return min(15, max(1, math.ceil(s / bin_width)))


```

This requires `math` to already be imported in this file — confirm with:
```bash
grep -n "^import math" "python_module/pipeline/parquet_to_animation.py"
```
Expected: line 35 already has `import math` (from the existing `calculate_bearing` function's usage) — no new import needed.

Now add `style_id` to each of the three `properties` dicts inside `interpolate_trajectory` (snapshot-mode branch, time_delta==0 branch, and the main interpolation loop). Each currently ends with:
```python
                "s": s_rounded,
                "speed_level": speed_lvl,
```
Change each of the three occurrences to:
```python
                "s": s_rounded,
                "speed_level": speed_lvl,
                "style_id": style_id,
```

- [ ] **Step 3: Thread `style_id` through `parquet_to_export`'s row collection**

In `parquet_to_export` (line ~584-600), the `all_rows.append({...})` block currently is:
```python
                all_rows.append({
                    'x': coords[0],
                    'y': coords[1],
                    'timestamp': props['timestamp'],
                    'timestamp_dt': props['timestamp_dt'],
                    'time_s': props['time_s'],
                    'angle': props['angle'],
                    'person_id': props['person_id'],
                    'interval_id': props['interval_id'],
                    'travelling_speed': props['travelling_speed'],
                    'freespeed': props['freespeed'],
                    's': props['s'],
                    'speed_level': props['speed_level'],
                    '_feature': feature,  # keep original for GeoJSON writes
                })
```

Change to:
```python
                all_rows.append({
                    'x': coords[0],
                    'y': coords[1],
                    'timestamp': props['timestamp'],
                    'timestamp_dt': props['timestamp_dt'],
                    'time_s': props['time_s'],
                    'angle': props['angle'],
                    'person_id': props['person_id'],
                    'interval_id': props['interval_id'],
                    'travelling_speed': props['travelling_speed'],
                    'freespeed': props['freespeed'],
                    's': props['s'],
                    'speed_level': props['speed_level'],
                    'style_id': props['style_id'],
                    '_feature': feature,  # keep original for GeoJSON writes
                })
```

- [ ] **Step 4: Add `style_id` to the CSV writer in `_write_chunk`**

Current code (lines ~639-648):
```python
        if 'csv' in output_formats:
            path = f"{base}.csv"
            with open(path, 'w', newline='') as f:
                writer = csv_module.writer(f)
                writer.writerow(['x', 'y', 'timestamp', 'timestamp_dt', 'angle', 'person_id',
                                  'interval_id', 'travelling_speed', 'freespeed', 's', 'speed_level'])
                for r in rows:
                    writer.writerow([r['x'], r['y'], r['timestamp'], r['timestamp_dt'], r['angle'],
                                     r['person_id'], r['interval_id'],
                                     r['travelling_speed'], r['freespeed'], r['s'], r['speed_level']])
            logger.success(f"CSV created: {path}")
```

Change to:
```python
        if 'csv' in output_formats:
            path = f"{base}.csv"
            with open(path, 'w', newline='') as f:
                writer = csv_module.writer(f)
                writer.writerow(['x', 'y', 'timestamp', 'timestamp_dt', 'angle', 'person_id',
                                  'interval_id', 'travelling_speed', 'freespeed', 's', 'speed_level',
                                  'style_id'])
                for r in rows:
                    writer.writerow([r['x'], r['y'], r['timestamp'], r['timestamp_dt'], r['angle'],
                                     r['person_id'], r['interval_id'],
                                     r['travelling_speed'], r['freespeed'], r['s'], r['speed_level'],
                                     r['style_id']])
            logger.success(f"CSV created: {path}")
```

Note: the `parquet` and `geoparquet` branches build `pd.DataFrame([{k: v for k, v in r.items() if k != '_feature'} for r in rows])` — since `style_id` is now a key in every row dict (added in Step 3), it's automatically included in those outputs with no further code change needed.

- [ ] **Step 5: Verify with a standalone interpolation check**

```bash
python3 -c "
import sys
sys.path.insert(0, '.')
from python_module.pipeline.parquet_to_animation import interpolate_trajectory, compute_style_id

# Direct function test
assert compute_style_id(0.0) == 1
assert compute_style_id(0.5) == 8
assert compute_style_id(1.3) == 15
assert compute_style_id(None) == 1

# Integration test: interpolate_trajectory includes style_id in every feature
features = interpolate_trajectory(
    link_id='L1', time_enter=0, time_leave=10,
    start_coords=(0.0, 0.0), end_coords=(1.0, 1.0),
    person_id='p1', freespeed=10.0, link_length=100.0,
    bearing=45, interval_id=0, travelling_speed=10.0,
    snapshot_mode=False, fps=1
)
assert len(features) > 0
for feat in features:
    assert 'style_id' in feat['properties'], feat['properties']
    assert 1 <= feat['properties']['style_id'] <= 15

# Snapshot mode also includes style_id
snap_features = interpolate_trajectory(
    link_id='L1', time_enter=0, time_leave=10,
    start_coords=(0.0, 0.0), end_coords=(1.0, 1.0),
    person_id='p1', freespeed=10.0, link_length=100.0,
    bearing=45, interval_id=0, travelling_speed=10.0,
    snapshot_mode=True, fps=1
)
assert len(snap_features) == 1
assert 'style_id' in snap_features[0]['properties']

print('OK: style_id present in all interpolate_trajectory outputs')
"
```
Expected output: `OK: style_id present in all interpolate_trajectory outputs`

(If imports fail due to missing packages such as `geopandas`/`pyarrow`/`shapely` in this environment, note that in the task result and fall back to careful manual review of the diff instead — the logic itself was already validated standalone in Step 1.)

- [ ] **Step 6: Commit**

```bash
git add python_module/pipeline/parquet_to_animation.py
git commit -m "$(cat <<'EOF'
Add style_id field: linear 1-15 speed bin for stylx-based symbology

Distinct from the existing non-linear speed_level (tuned for the
built-in red-green color ramp), style_id divides relative speed
(s = travelling_speed / freespeed) into 15 equal linear bins over
[0, 1.0], for matching against externally-authored .stylx point
symbols named "1" through "15". Flows through to every output
format (parquet, geoparquet, csv, geojson).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Extract renderer logic into a shared `RendererHelper` class

**Files:**
- Create: `cs_module/RendererHelper.cs`
- Modify: `cs_module/UTPS_Addin.csproj` (verify new file is picked up — .NET SDK-style projects auto-include `.cs` files by default, so likely no change needed)

- [ ] **Step 1: Confirm the csproj uses SDK-style implicit compilation (no explicit `<Compile Include>` list)**

```bash
grep -n "Compile Include" "cs_module/UTPS_Addin.csproj"
```
Expected: no output (empty) — confirming the project uses the default SDK behavior where all `.cs` files in the project directory are compiled automatically, so a new `RendererHelper.cs` file needs no `.csproj` edit.

- [ ] **Step 2: Create `RendererHelper.cs` with the graduated color renderer (moved from `SymbolizeButton.cs`)**

```csharp
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
```

- [ ] **Step 3: Verify by code review**

Read the new file back and confirm:
- Namespace matches the project (`UTPS_Addin`).
- No leftover reference to `SymbolizeButton`-specific state (`_isColored`) — this is a stateless static helper.
- `ApplyStylxRendererAsync` is `async` and returns `Task`, matching how it will be awaited from `TrafficLoaderButton.AddLayersToMap` (Task 7).

- [ ] **Step 4: Commit**

```bash
git add cs_module/RendererHelper.cs
git commit -m "$(cat <<'EOF'
Add RendererHelper with speed-color and stylx-based renderers

Extracts the graduated speed-color renderer logic out of
SymbolizeButton (being removed) into a shared static helper, and adds
a new stylx-based Unique Values renderer keyed on style_id that maps
symbols named "1"-"15" from a user-provided .stylx file.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Wire up optional `.stylx` picker in the config dialog

**Files:**
- Modify: `cs_module/TrafficConfigViewModel.cs`
- Modify: `cs_module/TrafficConfigDialog.xaml`

- [ ] **Step 1: Add `StylxFilePath` property and browse command to `TrafficConfigViewModel.cs`**

Add a new private field alongside the existing ones (near line 20-26):
```csharp
        private string _xmlFilePath;
        private string _gpkgFilePath;
        private string _startTime = "08:00";
        private string _endTime = "09:00";
        private string _outputPath;
        private double _fps = 5.0;
        private string _stylxFilePath;
        private string _validationMessage;
        private bool _hasValidationErrors;
```

Add a new command declaration alongside the existing ones (near line 30-33):
```csharp
        public ICommand BrowseXmlCommand { get; }
        public ICommand BrowseGpkgCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand BrowseStylxCommand { get; }
        public ICommand OkCommand { get; }
```

Wire it up in the constructor alongside the other command initializations (near line 40-43):
```csharp
            BrowseXmlCommand = new RelayCommand(BrowseXmlFile);
            BrowseGpkgCommand = new RelayCommand(BrowseGpkgFile);
            BrowseOutputCommand = new RelayCommand(BrowseOutputFile);
            BrowseStylxCommand = new RelayCommand(BrowseStylxFile);
            OkCommand = new RelayCommand(OnOk);
```

Add the property (near the other properties, e.g. after `Fps`, before `ValidationMessage`):
```csharp
        public string StylxFilePath
        {
            get => _stylxFilePath;
            set
            {
                if (_stylxFilePath != value)
                {
                    _stylxFilePath = value;
                    OnPropertyChanged(nameof(StylxFilePath));
                    ClearValidation();
                }
            }
        }
```

Add the browse method alongside `BrowseGpkgFile`/`BrowseOutputFile` (in the `#region File Browser Methods` block):
```csharp
        private void BrowseStylxFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Style File (optional)",
                Filter = "Style Files (*.stylx)|*.stylx|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                StylxFilePath = dialog.FileName;
            }
        }
```

- [ ] **Step 2: No validation required for `StylxFilePath`**

Confirm `ValidateInputs()` is NOT modified to require this field — an empty `StylxFilePath` is valid (falls back to the speed-color renderer). No code change needed here; this step is a checkpoint, not an edit.

- [ ] **Step 3: Add the optional Style File row to `TrafficConfigDialog.xaml`**

In `cs_module/TrafficConfigDialog.xaml`, insert a new row after the "Interpolation FPS" block (after line 183, before the "Validation Messages" `<Border>` at line 186):

```xml
                <!-- Style File (optional) -->
                <TextBlock Text="Style File (optional)" Style="{StaticResource FieldLabel}" Margin="0,12,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0"
                             Text="{Binding StylxFilePath, UpdateSourceTrigger=PropertyChanged}"
                             Style="{StaticResource FieldTextBox}"
                             ToolTip="Optional .stylx file with point symbols named 1-15, used to color points by relative speed. Leave blank to use the default red-to-green color ramp."/>
                    <Button Grid.Column="1"
                            Content="Browse..."
                            Style="{StaticResource BrowseButton}"
                            Command="{Binding BrowseStylxCommand}"/>
                </Grid>
                <TextBlock FontSize="10" Foreground="#888" Margin="0,4,0,0">
                    Optional. If provided, points are colored using symbols "1"-"15" from this style
                    file based on relative speed. If left blank, the default red-to-green color
                    ramp is used.
                </TextBlock>
```

- [ ] **Step 4: Verify by code review**

```bash
grep -n "StylxFilePath\|BrowseStylxCommand\|BrowseStylxFile" "cs_module/TrafficConfigViewModel.cs" "cs_module/TrafficConfigDialog.xaml"
```
Expected: `StylxFilePath` property + backing field in the ViewModel, `BrowseStylxCommand` declared/wired/bound in both files, `BrowseStylxFile` method present, XAML `TextBox`/`Button` bound to the new command.

- [ ] **Step 5: Commit**

```bash
git add cs_module/TrafficConfigViewModel.cs cs_module/TrafficConfigDialog.xaml
git commit -m "$(cat <<'EOF'
Add optional .stylx style file picker to the config dialog

Users can now optionally select a .stylx file containing point
symbols named 1-15; if provided, imported points will be colored
using those symbols based on relative speed instead of the default
red-to-green ramp.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Apply symbology at import time in `TrafficLoaderButton`

**Files:**
- Modify: `cs_module/TrafficLoaderButton.cs`

- [ ] **Step 1: Apply the appropriate renderer after the layer is added, before the success message**

In `AddLayersToMap`, inside the `QueuedTask.Run(async () => { ... })` block, after Step F (setting `MapView.Time`) and the `AnimationState` assignments (around line 424-427, just after `AnimationState.TrafficLayer = layer;` and before `System.Diagnostics.Debug.WriteLine($"Feature Class layer added: {eventsAdded}");`), add:

Current code (lines 424-429):
```csharp
                        // Store in AnimationState for downstream buttons
                        AnimationState.OutputGdbPath = gdbPath;
                        AnimationState.TrafficFeatureClassName = fcName;
                        AnimationState.TrafficLayer = layer;

                        System.Diagnostics.Debug.WriteLine($"Feature Class layer added: {eventsAdded}");
```

Change to:
```csharp
                        // Store in AnimationState for downstream buttons
                        AnimationState.OutputGdbPath = gdbPath;
                        AnimationState.TrafficFeatureClassName = fcName;
                        AnimationState.TrafficLayer = layer;

                        // Apply symbology: stylx-based if a style file was provided, otherwise
                        // the default red-to-green speed color ramp. Previously this required a
                        // separate manual "Toggle Speed Color" step — now it's automatic on import.
                        if (!string.IsNullOrWhiteSpace(config.StylxFilePath) && File.Exists(config.StylxFilePath))
                        {
                            await RendererHelper.ApplyStylxRendererAsync(layer, config.StylxFilePath);
                        }
                        else
                        {
                            RendererHelper.ApplySpeedColorRenderer(layer);
                        }

                        System.Diagnostics.Debug.WriteLine($"Feature Class layer added: {eventsAdded}");
```

Note: `RendererHelper.ApplyStylxRendererAsync` itself wraps its body in `QueuedTask.Run(...)`, and this call site is already inside an outer `QueuedTask.Run(async () => { ... })`. Nested `QueuedTask.Run` calls are safe in the ArcGIS Pro SDK (each call schedules/executes on the CIM worker thread), so no restructuring is needed — this mirrors the existing pattern already used elsewhere in this method (e.g. `Geoprocessing.ExecuteToolAsync` calls awaited inside the same outer `QueuedTask.Run`).

- [ ] **Step 2: Verify by code review**

```bash
grep -n "RendererHelper\." "cs_module/TrafficLoaderButton.cs"
```
Expected: two call sites — `RendererHelper.ApplyStylxRendererAsync(layer, config.StylxFilePath)` and `RendererHelper.ApplySpeedColorRenderer(layer)`.

Confirm `using` statements at the top of `TrafficLoaderButton.cs` don't need additions — `RendererHelper` lives in the same `UTPS_Addin` namespace, so no new `using` is required.

- [ ] **Step 3: Commit**

```bash
git add cs_module/TrafficLoaderButton.cs
git commit -m "$(cat <<'EOF'
Auto-apply symbology when traffic data is loaded

Points are now colored immediately after import — using the
stylx-based renderer if a style file was provided in the config
dialog, otherwise the default red-to-green speed color ramp. This
replaces the separate manual "Toggle Speed Color" step.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Remove the "Toggle Speed Color" button

**Files:**
- Delete: `cs_module/SymbolizeButton.cs`
- Modify: `cs_module/Config.daml`

- [ ] **Step 1: Delete `SymbolizeButton.cs`**

```bash
git rm "cs_module/SymbolizeButton.cs"
```

- [ ] **Step 2: Remove the button and tooltip block from `Config.daml`**

Current code (lines 29-94, showing the relevant button block to remove, lines 68-79):
```xml
        <!-- Toggle Speed Color button -->
        <button id="UTPS_Anim_Symbolize_Button" caption="3. Toggle Speed Color"
                className="SymbolizeButton" loadOnClick="true"
                smallImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonOrange16.png"
                largeImage="pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/GenericButtonOrange32.png">
          <tooltip heading="Toggle Speed-Based Coloring">
            Toggle between speed-based color coding and plain white symbols.
            When active: red (slow) → green (fast) graduated color by speed_level field.
            Click again to revert to simple white points.
            <disabledText />
          </tooltip>
        </button>

```

Remove this entire `<button>...</button>` block.

Also update the `<group>` element (line 31-36), which currently lists all four buttons:
```xml
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_SetStudyArea_Button" size="large"/>
          <button refID="UTPS_Addin_TrafficLoader_Button" size="large"/>
          <button refID="UTPS_Anim_Symbolize_Button" size="large"/>
          <button refID="UTPS_Anim_Scene_Button" size="large"/>
        </group>
```

Change to:
```xml
        <group id="UTPS_Anim_Group" caption="Animation Workflow" appearsOnAddInTab="true">
          <button refID="UTPS_Anim_SetStudyArea_Button" size="large"/>
          <button refID="UTPS_Addin_TrafficLoader_Button" size="large"/>
          <button refID="UTPS_Anim_Scene_Button" size="large"/>
        </group>
```

- [ ] **Step 3: Renumber the "Switch to 3D" button caption**

Current code (line 82):
```xml
        <button id="UTPS_Anim_Scene_Button" caption="4. Switch to 3D"
```

Change to:
```xml
        <button id="UTPS_Anim_Scene_Button" caption="3. Switch to 3D"
```

- [ ] **Step 4: Verify by code review**

```bash
grep -n "Symbolize\|caption=" "cs_module/Config.daml"
```
Expected: no matches for "Symbolize" anywhere in the file; captions read "1. Set Study Area", "2. Load Traffic Data", "3. Switch to 3D".

```bash
test -f "cs_module/SymbolizeButton.cs" && echo "STILL EXISTS - FAIL" || echo "OK: deleted"
```
Expected: `OK: deleted`

- [ ] **Step 5: Commit**

```bash
git add cs_module/Config.daml
git commit -m "$(cat <<'EOF'
Remove Toggle Speed Color button

Coloring now happens automatically at import time (see previous
commit), making this manual toggle step redundant. Renumbers the
Switch to 3D button from step 4 to step 3.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Keep point symbology (not cubes) in the 3D scene

**Files:**
- Modify: `cs_module/SceneButton.cs`

- [ ] **Step 1: Remove the `Apply3DCubeRenderer` method and its call site**

Current code (Steps 5 & 6 block, lines 139-149):
```csharp
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
```

Change to:
```csharp
                // ── 5 & 6. Symbolize (clone 2D renderer) + enable time ────────────────
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
```

Now remove the `Apply3DCubeRenderer` method entirely. Current code (lines 206-226):
```csharp
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
```

Delete the entire `Apply3DCubeRenderer` method (keep the closing `}` for the class and namespace):
```csharp
    }
}
```

- [ ] **Step 2: Update the result message text to no longer mention cubes**

Current code (lines 181-193):
```csharp
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
```

Change to:
```csharp
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                    $"3D Local Scene '{SceneName}' is ready.\n\n" +
                    "• World Topographic Map added as basemap\n" +
                    (sceneLayer != null
                        ? $"• Traffic layer '{fcName}' added, matching the 2D map's colors/symbols\n"
                        : $"• Could not add traffic layer — check path: {fcFullPath}\n") +
                    "• Terrain surface activates automatically (requires ArcGIS Online sign-in)\n\n" +
                    "To add buildings:\n" +
                    "  • Add your own building footprints via the Catalog pane\n" +
                    "  • Or use Insert → Add Elevation Source / Building Layer",
                    "3D Scene Ready",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
```

- [ ] **Step 3: Update the class doc comment header (no longer mentions white cubes)**

Current code (lines 13-27):
```csharp
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
```

Change to:
```csharp
    /// <summary>
    /// Open (or reuse) a Local Scene in the project and add the traffic data as points.
    ///
    /// What this button does:
    ///   1. Looks for an existing Local Scene named "Traffic 3D Scene" in the project.
    ///      If none exists, creates a new one.
    ///   2. Opens/activates that scene's map view.
    ///   3. Adds Esri's World Topographic Map as a basemap for visual context.
    ///   4. Adds the traffic GDB Feature Class into the scene map as a new layer.
    ///   5. Clones the renderer from the 2D traffic layer, so points keep the same
    ///      colors/symbols (speed color ramp or stylx-based) as in the 2D map.
    ///   6. Enables time on the scene layer (timestamp_dt field).
    ///
    /// Note: ArcGIS Pro does not support converting a 2D map view to 3D in-place via the SDK.
    /// This button creates a dedicated Local Scene instead.
    /// </summary>
```

- [ ] **Step 4: Verify by code review**

```bash
grep -n "Apply3DCubeRenderer\|Simple3DMarkerStyle\|cube" "cs_module/SceneButton.cs"
```
Expected: no matches (case-insensitive check recommended: `grep -in "cube" cs_module/SceneButton.cs` should also return nothing).

```bash
grep -n "GetRenderer\|SetRenderer" "cs_module/SceneButton.cs"
```
Expected: one `GetRenderer()` call on `sourceLayer` (i.e. `AnimationState.TrafficLayer`), one `SetRenderer(rendererDef)` call on `sceneLayer`.

- [ ] **Step 5: Commit**

```bash
git add cs_module/SceneButton.cs
git commit -m "$(cat <<'EOF'
Clone 2D renderer onto 3D scene layer instead of forcing white cubes

Points now keep whatever symbology was applied in the 2D map (speed
color ramp or stylx-based styling) when viewed in the 3D scene,
rather than being replaced with plain white cubes.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Final review pass — confirm all pieces are wired together correctly

**Files:** none modified — review only.

- [ ] **Step 1: Confirm the full symbology chain end-to-end by reading all four touched C# files together**

Read `cs_module/TrafficConfigViewModel.cs`, `cs_module/TrafficConfigDialog.xaml`, `cs_module/TrafficLoaderButton.cs`, `cs_module/RendererHelper.cs`, and `cs_module/SceneButton.cs` in full. Confirm:
- `StylxFilePath` flows from the dialog binding → `TrafficConfigViewModel` → is read in `TrafficLoaderButton.AddLayersToMap` via `config.StylxFilePath` (the `config` parameter there is the `TrafficConfigViewModel` instance, confirmed by its existing usage of `config.XmlFilePath`, `config.GpkgFilePath`, etc. throughout that file).
- `RendererHelper.ApplyStylxRendererAsync` / `ApplySpeedColorRenderer` are called exactly once each, from `TrafficLoaderButton.AddLayersToMap`, and never from `SceneButton` (which only clones, never re-derives).
- `SceneButton` no longer references `Apply3DCubeRenderer` anywhere (already checked in Task 10 Step 4, re-confirm here as part of the holistic pass).
- `Config.daml` has exactly 3 buttons in the `UTPS_Anim_Group`, no dangling `refID` to a deleted button.

```bash
grep -rn "UTPS_Anim_Symbolize_Button\|SymbolizeButton" "cs_module/" --include="*.cs" --include="*.daml"
```
Expected: no output (empty) — confirms no dangling references to the removed button anywhere in the C# project.

- [ ] **Step 2: Confirm the Python side end-to-end**

```bash
grep -n "style_id" "python_module/pipeline/parquet_to_animation.py"
```
Expected: matches in `compute_style_id` definition, its three call-sites inside `interpolate_trajectory`'s three `properties` dicts, the `all_rows.append` dict, and the CSV writer header/row — at least 7 occurrences total.

```bash
grep -n "interpolation_fps\|--fps" "python_module/pipeline/main_pipeline.py" "cs_module/scripts/traffic_loader_wrapper.py"
```
Expected: `main_pipeline.py` shows `interpolation_fps: float = Field(1.0, ge=0.1, le=60, ...)`; `traffic_loader_wrapper.py` shows `type=float` for `--fps`.

- [ ] **Step 3: Write a manual test checklist for the user (this add-in cannot be built/run on this machine)**

Since the C# project targets `net8.0-windows` and requires ArcGIS Pro DLLs only present on a Windows machine with ArcGIS Pro installed, report to the user that the following must be manually verified on that machine before considering this work fully done:

1. Build `UTPS_Addin.csproj` in Visual Studio / `dotnet build` on Windows — confirm no compile errors (this plan's C# changes were verified only by careful manual code review, not an actual compiler).
2. Run a full "Load Traffic Data" cycle with no `.stylx` file — confirm points appear colored red→green by speed immediately after import (no separate toggle click needed), and confirm there is no "Toggle Speed Color" button in the ribbon anymore.
3. Run the same with a real `.stylx` file containing point symbols named "1"-"15" — confirm points are colored/shaped per those symbols instead.
4. Enable the Time Slider and confirm smooth playback (checking specifically whether the ~10s flashing is gone).
5. Set FPS to a fractional value like `0.2` in the dialog — confirm no validation error, and confirm the resulting animation has visibly coarser (5-second) steps.
6. Click "Switch to 3D" — confirm the scene shows the same colors/symbols as the 2D map, not white cubes.
7. In the layer's Properties → Time tab (both 2D and 3D scene layers), confirm the Start/End Time field shows `timestamp_dt`.

This step produces no code — it's a report to hand to the user, not a task to check off silently.

- [ ] **Step 4: No commit for this task** (review-only, nothing to commit).

---

## Summary of Spec Coverage

| Spec Issue | Task(s) |
|---|---|
| 1. Time field should be `timestamp_dt` | Task 1 |
| 2. Flashing every 10s | Task 1 (fix), Task 11 Step 3 item 4 (manual verification) |
| 3. FPS below 1, down to 0.2 | Tasks 2, 3, 4 |
| 4. Auto-color at import + optional `.stylx`, remove toggle button | Tasks 5, 6, 7, 8, 9 |
| 5. Keep points (not cubes) in 3D | Task 10 |
