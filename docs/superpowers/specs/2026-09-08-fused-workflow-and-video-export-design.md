# Fused Load & Animate Workflow + Video Export — Design Spec

Date: 2026-09-08

## Background

Following the animation workflow fixes shipped in the previous iteration (timestamp_dt field, fractional FPS, auto-coloring, stylx support, 3D scene renderer cloning), this spec covers a larger restructuring: collapsing the 3-button sequential workflow into a single "Load & Animate" action, replacing the manual FPS input with a derived video-length/export-fps calculation, adding a video export button, fixing a symbology bug discovered in manual testing, and enriching the 3D scene with a dark basemap and 3D buildings.

## Current State (before this spec)

Ribbon has 4 buttons: Set Study Area, Load Traffic Data, Toggle Speed Color (already removed in the prior iteration, leaving 3: Set Study Area, Load Traffic Data, Switch to 3D). The user manually clicks through Set Study Area → Load Traffic Data → Switch to 3D in sequence. The Load Traffic Data dialog has an `interpolation_fps` field (0.1–60, direct input). Symbology at import auto-applies either a stylx-based Unique Values renderer or falls back silently to a graduated color ramp if the stylx path fails for any reason.

## Changes

### 1. Fused "Load & Animate" button

Replaces `DrawBboxButton`, `TrafficLoaderButton`, and `SceneButton` with a single new `LoadAndAnimateButton`. On click, in order:

1. **Capture bbox** — read `MapView.Active.Extent` immediately (same logic as today's `DrawBboxButton`: reproject to WGS84, store in `AnimationState.BboxFilter`). Mandatory — no active map view is treated as an error the same way it is today (existing "No Active Map" warning).
2. **Open config dialog** — same dialog as today (`TrafficConfigDialog`/`TrafficConfigViewModel`), with the FPS field replaced per section 2 below, plus the existing optional stylx picker.
3. **Run pipeline** — same subprocess/GDB/Feature-Class creation logic as today's `TrafficLoaderButton.AddLayersToMap`, using the derived interpolation fps from section 2.
4. **Apply symbology** — same auto-apply logic as today (stylx-based or graduated fallback), with the visibility fix from section 3.
5. **Enable time** — same as today, on `timestamp_dt`, with `MapView.Time` span set per section 2's alignment fix.
6. **Create/open 3D scene** — same as today's `SceneButton` logic (create/reuse "Traffic 3D Scene" Local Scene, activate its pane), but with the basemap/buildings changes from section 4, cloning the 2D renderer onto the scene layer (unchanged from the prior iteration), and enabling time on the scene layer.

`DrawBboxButton.cs`, `TrafficLoaderButton.cs`, and `SceneButton.cs` are deleted. Their logic is merged into one new file, `LoadAndAnimateButton.cs`. This will be a large file (comparable to today's `TrafficLoaderButton.cs`, which is itself already the largest button file) — acceptable since it represents one cohesive, sequential operation; no further internal splitting is required by this spec beyond what already exists (e.g. `RendererHelper` stays a separate shared helper, not pulled into this file).

`AnimationState.cs` gains three new fields, set during this flow, read later by Export Video:
- `public static double VideoLengthSeconds { get; set; }`
- `public static double ExportFps { get; set; }`
- `public static double InterpolationIntervalSeconds { get; set; }` (= `1.0 / computed_fps`, the value used for the Time Slider span and later for the export animation's step alignment)

`Config.daml` ends up with exactly 2 buttons in the `UTPS_Anim_Group`: "1. Load & Animate" and "2. Export Video" (section 5).

### 2. Video length + export FPS replace the direct FPS input

**Problem:** the current dialog asks for `interpolation_fps` directly. The user wants to instead specify "how long should the final video be" and "what FPS should the exported video have," with the pipeline's actual point-interpolation density derived from those two values plus the simulated time range.

**Dialog changes (`TrafficConfigViewModel`/`TrafficConfigDialog.xaml`):**
- Remove the `Fps` property/field/validation/XAML row.
- Add `VideoLengthSeconds` (double, e.g. default 60) and `ExportFps` (double, e.g. default 24) properties, each with a corresponding XAML input row.
- Add a read-only computed-value display, live-updated (via `OnPropertyChanged` triggering recomputation whenever `StartTime`, `EndTime`, `VideoLengthSeconds`, or `ExportFps` changes):
  ```
  total_sim_seconds = end_time_seconds - start_time_seconds
  total_frames = VideoLengthSeconds * ExportFps
  raw_interval_seconds = total_sim_seconds / total_frames
  raw_fps = 1.0 / raw_interval_seconds
  computed_fps = clamp(raw_fps, 0.1, 60)
  computed_interval_seconds = 1.0 / computed_fps
  ```
  Display text: `"Computed: 1 point every {computed_interval_seconds:F2}s"`. If `computed_fps != raw_fps` (clamping occurred), append: `"(requested 1 point every {raw_interval_seconds:F2}s, clamped to {computed_interval_seconds:F2}s — {reason})"` where `reason` is `"60 fps interpolation limit"` or `"0.1 fps interpolation limit"` depending on which bound was hit.
- Validation: `VideoLengthSeconds > 0`, `ExportFps > 0` (basic positivity checks, matching the existing validation style — no upper bound enforced beyond what the computed-fps clamp already handles).

**Pipeline call site:** `LoadAndAnimateButton` passes `computed_fps` to the Python pipeline exactly where `config.Fps` was passed before (CLI `--fps` argument) — no changes needed to `main_pipeline.py`, `traffic_loader_wrapper.py`, or `parquet_to_animation.py`, since they already accept a float fps in the 0.1–60 range from the prior iteration.

**Time Slider span (unchanged formula, new source value):** `spanSeconds = 1.0 / computed_fps`, applied to `MapView.Time` exactly as today — this guarantees the initial Time Slider window always covers one full interpolation step, regardless of what video-length/fps combination produced `computed_fps`.

**Storing for later use:** `LoadAndAnimateButton` sets `AnimationState.VideoLengthSeconds = config.VideoLengthSeconds`, `AnimationState.ExportFps = config.ExportFps`, `AnimationState.InterpolationIntervalSeconds = computed_interval_seconds` after a successful run.

**Why no restrictions on allowed length/fps combinations:** flashing during playback/export is fundamentally a function of whether the displayed time window (span) at each rendered instant contains a data point — not of whether the chosen numbers "divide evenly." As long as the span used at both preview time (section above) and export time (section 5) is always set to `computed_interval_seconds` (or wider), any combination of video length and export fps is safe. Restricting user inputs would be solving the wrong layer of the problem and wouldn't even fully guarantee safety on its own.

### 3. Stylx renderer failure visibility fix

**Problem (root cause of a bug found in manual testing):** `RendererHelper.ApplyStylxRenderer` silently falls back to `ApplySpeedColorRenderer` (graduated colors) in three cases — style item not found after registration, zero of the 15 named symbols matched, or any exception during lookup/construction. The user observed graduated colors being applied when unique-value stylx symbols were expected, with no indication of why.

**Fix:** change `ApplyStylxRenderer`'s signature from `void` to return a nullable failure reason:
```csharp
public static string ApplyStylxRenderer(FeatureLayer layer, string stylxPath)
```
- Returns `null` on success (unique values renderer applied).
- Returns `"Style file not found in project after registration"` if `styleItem == null`.
- Returns `"No symbols named 1-15 found in style file"` if `classes.Count == 0`.
- Returns `$"Error loading style file: {ex.Message}"` if an exception is caught.
- In every failure case, `ApplySpeedColorRenderer(layer)` is still called as the fallback (behavior unchanged) — only the return value changes, to surface *why* the fallback happened.

**Caller change (`LoadAndAnimateButton`):** capture the return value when calling `ApplyStylxRenderer` (in the branch where a stylx path was provided). If non-null, include it in the final result dialog, e.g.:
```
⚠ Style file could not be applied: {reason}
   Using default color ramp instead.
```
instead of (or alongside) the existing plain success message. If no stylx path was provided at all, no such note appears (this only fires when the user provided a path and it failed).

This spec does not change the symbol lookup/matching logic itself (`LookupItem`/`SearchSymbols` by name "1"–"15") — that's deferred until the user has tested "Match Layer Symbology to a Style" manually in ArcGIS Pro and confirmed whether the root cause is a naming mismatch, a `StyleItemType` issue, or something else.

### 4. Human Geography Dark basemap + Esri 3D Buildings

In `LoadAndAnimateButton`'s 3D-scene-setup step (replacing today's `SceneButton` basemap logic):

**Basemap** — replace the World Topographic Map add with the Human Geography Dark basemap's **base layer only** (not the separate detail/label vector tile layers):
```csharp
var basemapUri = new Uri("https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_v2/VectorTileServer");
LayerFactory.Instance.CreateLayer(basemapUri, sceneMap, layerName: "Human Geography Dark Base");
```

**3D Buildings** — add Esri's global 3D Buildings scene layer (chosen over the older, sunsetted OpenStreetMap 3D Buildings layer) at 50% transparency:
```csharp
var buildingsUri = new Uri("https://basemaps3d.arcgis.com/arcgis/rest/services/Esri3D_Buildings_v1/SceneServer");
var buildingsLayer = LayerFactory.Instance.CreateLayer(buildingsUri, sceneMap, layerName: "Esri 3D Buildings") as Layer;
if (buildingsLayer != null)
{
    buildingsLayer.SetTransparency(50); // 0 = opaque, 100 = fully transparent
}
```

Both additions are wrapped in their own try/catch, matching the existing pattern for World Topographic Map and terrain (which already carries a code comment noting it requires ArcGIS Online sign-in) — if either add fails (e.g. not signed in), log via `Debug.WriteLine` and continue; never block the rest of the Load & Animate workflow. This is the same graceful degradation already used elsewhere in this method.

### 5. New "Export Video" button

New files: `ExportVideoButton.cs`, `ExportVideoDialog.xaml` + `ExportVideoDialog.xaml.cs`, `ExportVideoViewModel.cs`, `ExportProgressDialog.xaml` + `.xaml.cs` (a minimal indeterminate-progress dialog, distinct from the existing pipeline `ProgressDialog` which shows percent-based pipeline progress).

**Guard:** on click, if `AnimationState.VideoLengthSeconds <= 0` (unset — Load & Animate hasn't run this session), show a warning dialog: *"Please run 'Load & Animate' first."* and return without opening the export dialog.

**`ExportVideoViewModel` fields:**
- `OutputFilePath` (string) — via `SaveFileDialog`, filter `MP4 Video (*.mp4)|*.mp4`, `DefaultExt = ".mp4"`, `AddExtension = true`.
- `Resolution` (enum/selection: `720p` = 1280×720, `1080p` = 1920×1080, `4K` = 3840×2160) — dropdown, default `1080p`.
- `StartSecond` (double, default 0) and `EndSecond` (double, default `AnimationState.VideoLengthSeconds`) — validated `0 ≤ StartSecond < EndSecond ≤ AnimationState.VideoLengthSeconds`.
- No FPS field — reused directly from `AnimationState.ExportFps`.
- No resolution-based time estimate shown (explicitly excluded per user decision — resolution affects export time unpredictably enough that an estimate isn't worth showing).

**Export logic (`ExportVideoButton`, on dialog OK):**

1. Inside `QueuedTask.Run`, build animation keyframes on the active `MapView`'s `Map.Animation`:
   ```csharp
   var animation = mapView.Map.Animation;
   var timeTrack = animation.Tracks.OfType<TimeTrack>().First();
   var startExtent = new TimeExtent(dataStartDt);   // from AnimationState / MapView.Time
   var endExtent   = new TimeExtent(dataEndDt);
   timeTrack.CreateKeyframe(startExtent, TimeSpan.Zero, AnimationTransition.Linear);
   timeTrack.CreateKeyframe(endExtent, TimeSpan.FromSeconds(AnimationState.VideoLengthSeconds), AnimationTransition.Linear);
   ```
2. **Flashing-prevention span alignment:** the layer's time span (used when the animation engine samples the map at each output frame) must match `AnimationState.InterpolationIntervalSeconds` from section 2 — set via the same `CIMTimeTableDefinition`/`MapView.Time` span mechanism already used at import time, applied here again before export so the exported video's per-frame sampling window always contains a data point, regardless of the start/end second range or resolution chosen for this specific export. (Exact API call to set span at export time — as opposed to interactively via `MapView.Time` — needs confirming during implementation; if `MapView.Time`'s span is sufficient and animation export respects it, no new API is needed beyond what's already used; otherwise this needs a follow-up API check during implementation, flagged as an open item below.)
3. Back on the **UI thread** (not inside `QueuedTask.Run` — `BeginExport` requires this per the ArcGIS Pro SDK, throwing `InvalidOperationException` otherwise):
   ```csharp
   var (width, height) = ResolutionToPixels(viewModel.Resolution); // e.g. 1080p -> (1920, 1080)
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
   bool started = mapView.Animation.BeginExport("TrafficAnimation", exportParams);
   ```
4. If `started`, show `ExportProgressDialog` — an indeterminate/marquee WPF `ProgressBar` (`IsIndeterminate="True"`), no percentage text, just a spinner and a "Exporting video..." label. **No real percent-complete is available from the ArcGIS Pro SDK's `BeginExport` API** (confirmed: only a start signal and a completion event exist) — a determinate bar would require bypassing the built-in exporter entirely (manual frame-by-frame capture + custom video encoding), which is out of scope for this spec.
5. Subscribe to `ArcGIS.Desktop.Mapping.Events.AnimationExportFinishedEvent` before calling `BeginExport`; in the handler, close `ExportProgressDialog` and show a result dialog: success with the output path if `args.ErrorMessage` is null/empty, or the error message if not. Unsubscribe from the event after handling it (one-shot).

## Files Touched

| File | Change |
|---|---|
| `cs_module/LoadAndAnimateButton.cs` (new) | Fused bbox capture + config dialog + pipeline run + symbology + time + 3D scene setup |
| `cs_module/DrawBboxButton.cs` | Deleted (logic merged into LoadAndAnimateButton) |
| `cs_module/TrafficLoaderButton.cs` | Deleted (logic merged into LoadAndAnimateButton) |
| `cs_module/SceneButton.cs` | Deleted (logic merged into LoadAndAnimateButton) |
| `cs_module/TrafficConfigViewModel.cs` | Remove `Fps`; add `VideoLengthSeconds`, `ExportFps`, computed-interval display logic |
| `cs_module/TrafficConfigDialog.xaml` | Remove FPS row; add Video Length + Export FPS rows + computed-value readout |
| `cs_module/RendererHelper.cs` | `ApplyStylxRenderer` returns failure reason string instead of `void` |
| `cs_module/AnimationState.cs` | Add `VideoLengthSeconds`, `ExportFps`, `InterpolationIntervalSeconds` |
| `cs_module/ExportVideoButton.cs` (new) | Export Video button click handler, animation/keyframe/export logic |
| `cs_module/ExportVideoDialog.xaml` + `.xaml.cs` (new) | Export configuration dialog (path, resolution, start/end second) |
| `cs_module/ExportVideoViewModel.cs` (new) | MVVM view model for the export dialog |
| `cs_module/ExportProgressDialog.xaml` + `.xaml.cs` (new) | Indeterminate progress dialog shown during export |
| `cs_module/Config.daml` | 2 buttons: "1. Load & Animate", "2. Export Video" |

## Out of Scope

- No change to the Python pipeline itself (`main_pipeline.py`, `xml_to_parquet.py`, `parquet_to_animation.py`, `parquet_to_heatmap.py`) — the fps range and computation already support everything this spec needs.
- No fix to the underlying stylx symbol-matching logic (`LookupItem`/`SearchSymbols`) — only failure visibility is added; the actual root cause is deferred pending the user's manual ArcGIS Pro testing.
- No determinate (percent-complete) export progress bar — confirmed unsupported by the ArcGIS Pro SDK's `BeginExport` API without a full custom frame-capture-and-encode replacement, which is out of scope.
- No video formats besides MP4.
- No 2D-map equivalent of the dark basemap/3D buildings changes — 3D scene only.
- No artificial restrictions on video length/export-fps combinations — flashing is prevented via span-matching (section 2), not input validation.

## Open Implementation Question (flagged for the implementation plan)

Section 5, step 2's exact mechanism for setting the export-time sampling span (as opposed to the interactive `MapView.Time` span already used at import time) needs to be confirmed against the ArcGIS Pro SDK during implementation — specifically whether `MapView.Time`'s span, once set, is respected by `Animation`/`BeginExport`'s frame sampling, or whether a separate CIM-level or `TimeTrack`-level span setting is needed. This should be resolved as part of writing the implementation plan (a design decision within Task scope, not a blocker to approving this spec) — if `MapView.Time`'s existing span isn't sufficient, the closest available lever should be used and clearly documented in the plan.
