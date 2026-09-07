# Animation Workflow Fixes — Design Spec

Date: 2026-09-07

## Background

The UTPS Traffic Simulation Loader add-in has a 4-step animation workflow (Set Study Area → Load Traffic Data → Toggle Speed Color → Switch to 3D). Five issues were identified in current behavior. This spec covers all five as one coordinated change, since several are causally related (the time-field fix is expected to resolve the flashing symptom) and several touch the same files.

## Issues and Fixes

### 1. Time field should be `timestamp_dt`, not `timestamp`

**Problem:** `TrafficLoaderButton.AddLayersToMap` and `SceneButton.OnClick` both enable time on the layer using `StartTimeField = "timestamp"` / `EndTimeField = "timestamp"`. The pipeline's output Parquet has two relevant columns: `timestamp` (an ISO-8601 **string**, e.g. `"2024-01-01T08:00:00.100"`) and `timestamp_dt` (a proper `datetime64[ms, UTC]` column). ArcGIS Pro's Time Slider works correctly only against a real time-typed field.

**Fix:** Change `StartTimeField`/`EndTimeField` to `"timestamp_dt"` in both locations:
- `TrafficLoaderButton.cs` (`CIMTimeTableDefinition` in `AddLayersToMap`)
- `SceneButton.cs` (`CIMTimeTableDefinition` after adding the scene layer)

No other logic changes.

### 2. Points flash every ~10 seconds during Time Slider playback

**Diagnosis:** User confirmed playback uses ArcGIS Pro's built-in Time Slider (not Animation/keyframes), and FPS is in the 1–5 range. The interpolation loop in `parquet_to_animation.interpolate_trajectory` is dense and gapless at any FPS (`while t <= time_delta + 1e-9: ... t += step`), so there is no evidence of a data-generation gap. The most likely cause is the Time Slider stepping against the string-typed `timestamp` field, which ArcGIS cannot index/bucket as reliably as a real datetime field, causing intermittent skipped or misaligned steps.

**Fix:** No separate code change — this is expected to be resolved by fix #1. If flashing persists after switching to `timestamp_dt`, that would indicate an actual data gap and warrants a follow-up investigation, but is out of scope for this spec since no such gap is evident in the current interpolation logic.

### 3. Support FPS below 1 (as low as 0.2)

**Problem:** FPS is currently constrained to integers ≥ 1 in both the C# dialog (spinner 1–30) and the Python config (`ge=1, le=60`). A user condensing a full hour of simulation into a 30s video needs ~0.2 fps (5-second steps), well below the current floor.

**Fix — change FPS to a float, range 0.1–60, throughout the chain:**
- `TrafficConfigViewModel.cs`: `Fps` property `int` → `double`, default remains `5.0`. Validation message and range check updated to `0.1`–`60`.
- `TrafficConfigDialog.xaml`: numeric input allows decimal entry (e.g. remove `IsInteger`-style restriction if present, or switch to a text box with decimal validation).
- `traffic_loader_wrapper.py`: `--fps` argparse `type=int` → `type=float`.
- `main_pipeline.py` `ProcessingConfig.interpolation_fps`: Pydantic field `int` → `float`, `Field(1.0, ge=0.1, le=60)`.
- `parquet_to_animation.py`: no change needed — `step = 1.0 / fps` and all downstream math are already float-safe.
- `TrafficLoaderButton.cs`: `spanSeconds = 1.0 / config.Fps` (Time Slider span) is already correct for fractional FPS — no change.

**Short-link behavior at low FPS (confirmed, no change needed):** `interpolate_trajectory`'s loop always executes at least once for any `time_delta >= 0` (starts at `t=0.0`), so a vehicle that fully traverses a link faster than one frame step still gets at least one point at its entry position. This existing behavior is preserved as FPS decreases.

### 4. Auto-color points at import; remove "Toggle Speed Color" button; optional `.stylx` support

**Problem:** Coloring is currently a manual, separate toggle step (`SymbolizeButton`) that must be clicked after loading data. The user wants:
- Points colored automatically when loaded (step 2), no separate toggle step.
- An **optional** `.stylx` style file input. If provided, points are drawn using named point symbols `"1"`–`"15"` from that file, chosen by a **new linear** speed metric (distinct from the existing non-linear `speed_level` field). If not provided, fall back to the existing red→green graduated color renderer (previously toggle-only, now default).

**New pipeline field — `style_id` (1–15, linear):**

Added in `parquet_to_animation.py`, `interpolate_trajectory`, alongside the existing `speed_level` computation. Based on `s` (relative speed = `travelling_speed / freespeed`), split into 15 equal linear bins over `[0, 1.0]`, clamped for `s > 1.0`:

```python
bin_width = 1.0 / 15
if s_rounded is None or s_rounded <= 0:
    style_id = 1
else:
    style_id = min(15, max(1, math.ceil(s_rounded / bin_width)))
```

- `style_id` is added to `feature['properties']` in `interpolate_trajectory`, so it flows through every output format the same way `speed_level` does.
- `process_parquet_chunk` requires no change (it doesn't enumerate individual property names).
- `parquet_to_export`'s `all_rows.append({...})` dict and `_write_chunk`'s CSV header/row must include `style_id`. Parquet/GeoParquet writes already serialize the full row dict, so they pick it up automatically once it's in `all_rows`.

**C# side — new optional `.stylx` input:**

- `TrafficConfigViewModel.cs`: new `StylxFilePath` string property (nullable/empty = not set) + `BrowseStylxCommand` (file filter `Style Files (*.stylx)|*.stylx|All Files (*.*)|*.*`). No validation required — empty is valid.
- `TrafficConfigDialog.xaml`: new optional row "Style File (optional):" with text box + Browse button, following the existing XML/GPKG row pattern.
- `TrafficLoaderButton.AddLayersToMap`: after the Feature Class layer is created and before the success dialog, apply symbology:
  - **If `config.StylxFilePath` is set:**
    1. `await QueuedTask.Run(() => StyleHelper.AddStyle(Project.Current, config.StylxFilePath));`
    2. Retrieve `StyleProjectItem` via `Project.Current.GetItems<StyleProjectItem>()` matching by file name.
    3. For each `N` in 1–15, look up `myStyle.LookupItem(StyleItemType.PointSymbol, N.ToString())` (or `SearchSymbols` if exact lookup is unreliable), cast `.Symbol` to `CIMPointSymbol`.
    4. Build a `CIMUniqueValueRenderer` manually (per Esri's documented pattern — `UniqueValueRendererDefinition` does not support per-value custom symbols):
       - `Fields = new[] { "style_id" }`
       - One `CIMUniqueValueClass` per symbol found, `Values = [{ FieldValues = ["N"] }]`, `Symbol = symbol.MakeSymbolReference()`
       - Grouped under a single `CIMUniqueValueGroup`
       - `DefaultSymbol` = a plain fallback (e.g. small white circle) for any value outside 1–15
    5. `layer.SetRenderer(renderer);`
  - **If not set:** apply the existing graduated red→green renderer (see below), keyed on `speed_level` as before.
- **Shared helper:** move the renderer-building logic currently in `SymbolizeButton.ApplySpeedColorRenderer` (and the plain-white fallback `ApplyWhiteRenderer`, if still needed anywhere) into a new static class, e.g. `RendererHelper.cs`, with methods:
  - `RendererHelper.ApplySpeedColorRenderer(FeatureLayer layer)` — existing red→green graduated logic, unchanged.
  - `RendererHelper.ApplyStylxRenderer(FeatureLayer layer, string stylxPath)` — new logic described above.
  Both `TrafficLoaderButton` and `SceneButton` call into this shared helper as needed.
- **Remove the toggle button entirely:**
  - Delete `SymbolizeButton.cs`.
  - Remove the `<button id="UTPS_Anim_Symbolize_Button">` block (with its tooltip) from `Config.daml`.
  - Renumber remaining button captions: "4. Switch to 3D" → "3. Switch to 3D" (Set Study Area and Load Traffic Data keep their numbers 1–2).

### 5. Keep points (not cubes) when switching to 3D scene

**Problem:** `SceneButton.Apply3DCubeRenderer` unconditionally overrides symbology with plain white 3D cubes, discarding whatever coloring/styling was applied in 2D.

**Fix:**
- Delete `Apply3DCubeRenderer` entirely from `SceneButton.cs`.
- After the scene's Feature Class layer (`sceneLayer`) is created, clone the renderer directly from the source 2D layer:
  ```csharp
  var rendererDef = AnimationState.TrafficLayer.GetRenderer();
  sceneLayer.SetRenderer(rendererDef);
  ```
- No `CanSetRenderer` guard and no fallback — apply directly per explicit user decision to accept this risk and validate through manual testing in ArcGIS Pro. (Research found no documented confirmation either way of 2D point-symbol renderer compatibility with 3D scene layers; `style_id`/`speed_level` fields exist on the same underlying GDB Feature Class in both cases, so field-name mismatch is not a concern.)
- Time-enabling logic in `SceneButton` already updated per fix #1 (`timestamp_dt`) — no further change there.

## Files Touched

| File | Change |
|---|---|
| `cs_module/TrafficLoaderButton.cs` | Time field fix (#1); apply stylx-or-graduated renderer after layer creation (#4) |
| `cs_module/SceneButton.cs` | Time field fix (#1); remove `Apply3DCubeRenderer`, clone 2D renderer instead (#5) |
| `cs_module/TrafficConfigViewModel.cs` | `Fps` int→double, range 0.1–60 (#3); new `StylxFilePath` property + browse command (#4) |
| `cs_module/TrafficConfigDialog.xaml` | FPS input allows decimals (#3); new optional Style File row (#4) |
| `cs_module/RendererHelper.cs` (new) | Shared `ApplySpeedColorRenderer` + new `ApplyStylxRenderer` (#4) |
| `cs_module/SymbolizeButton.cs` | Deleted (#4) |
| `cs_module/Config.daml` | Remove Toggle Speed Color button/tooltip; renumber captions (#4) |
| `cs_module/scripts/traffic_loader_wrapper.py` | `--fps` argparse type int→float (#3) |
| `python_module/pipeline/main_pipeline.py` | `interpolation_fps` Pydantic field int→float, ge=0.1 (#3) |
| `python_module/pipeline/parquet_to_animation.py` | New `style_id` field computation in `interpolate_trajectory`; carry through `parquet_to_export`/`_write_chunk` CSV output (#4) |

## Out of Scope

- No change to `parquet_to_heatmap.py` (heatmap workflow is separate from the point-animation workflow these fixes target).
- No change to the XML→Parquet stage (`xml_to_parquet.py`).
- No new fallback/guard for renderer cross-compatibility in the 3D scene (#5) — explicitly deferred to manual testing per user decision.
- No investigation into a possible residual data gap for issue #2 beyond the time-field fix, since no gap is evident in current interpolation logic.
