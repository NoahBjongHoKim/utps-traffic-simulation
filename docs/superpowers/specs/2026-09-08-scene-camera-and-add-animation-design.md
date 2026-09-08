# 3D Auto-Zoom, Add Animation Swap, Speed Multiplier — Design Spec

Date: 2026-09-08

## 1. 3D scene auto-zoom to 45° oblique view

In `LoadAndAnimateButton.SetUpSceneAsync`, as the final step (after basemap, buildings, feature class, renderer clone, time-enable all succeed), if `AnimationState.BboxFilter` is non-null, capture the scene's `MapView` (from `ProApp.Panes.CreateMapPaneAsync`'s pane, or `MapView.Active` after the existing 500ms delay) and:

```csharp
var wgs84 = ArcGIS.Core.Geometry.SpatialReferenceBuilder.CreateSpatialReference(4326);
var envelope = ArcGIS.Core.Geometry.EnvelopeBuilderEx.CreateEnvelope(
    AnimationState.BboxFilter.XMin, AnimationState.BboxFilter.YMin,
    AnimationState.BboxFilter.XMax, AnimationState.BboxFilter.YMax, wgs84);

await scenePane.ZoomToAsync(envelope, TimeSpan.FromSeconds(1));

var camera = scenePane.Camera;
camera.Pitch = -45;
camera.Heading = 0;
await scenePane.ZoomToAsync(camera, TimeSpan.Zero);
```

Wrapped in its own try/catch matching the existing basemap/buildings graceful-degradation pattern — on failure or null `BboxFilter`, skip silently.

## 2. Remove Export Video, add Esri's built-in "Add Animation"

Delete: `ExportVideoButton.cs`, `ExportVideoDialog.xaml`/`.xaml.cs`, `ExportVideoViewModel.cs`, `ExportProgressDialog.xaml`/`.xaml.cs`.

`Config.daml`: remove the `UTPS_Anim_ExportVideo_Button` control; add `<button refID="esri_mapping_enableAnimationButton" size="large"/>` to `UTPS_Anim_Group`. This is Esri's real "Add" (animation) command (confirmed via Esri's official DAML ID reference wiki), shown with its own native caption/icon — not renamed. Ribbon ends up with 2 buttons: "1. Load & Animate" + Esri's native "Add".

## 3. Dialog: Speed Multiplier replaces Video Length

`TrafficConfigViewModel.cs`:
- Replace `VideoLengthSeconds` (double) with `SpeedMultiplier` (int, default 4). Validation: must be a positive integer ≥ 1, distinct error message.
- `ExportFps` unchanged.
- New/changed computed properties:
  - `ComputedVideoLengthSeconds` = `totalSimSeconds / SpeedMultiplier` (read-only, live)
  - `ComputedInterpolationFps` = `clamp(ExportFps / SpeedMultiplier, 0.1, 60)` — mathematically equivalent simplification of the prior video-length-based formula
  - `ComputedIntervalText` shows both computed video length and computed interval, same clamp-note behavior as before
- XAML: "Video Length (seconds)" input → "Speed Multiplier" (integer input, label "×N faster than real-time"); add a live read-only "Computed video length: {X}s" line next to the existing computed-interval line.

## 4. AnimationState cleanup

Remove `VideoLengthSeconds`, `ExportFps`, `InterpolationIntervalSeconds` properties and their `Reset()` entries from `AnimationState.cs` — nothing reads them once Export Video is gone. `LoadAndAnimateButton`'s Time Slider span calculation switches from `AnimationState.InterpolationIntervalSeconds` to computing `1.0 / computedFps` inline (using the existing local `computedFps` from `config.ComputedInterpolationFps`).

## Out of scope

No changes to the Python pipeline, `RendererHelper.cs`, or the stylx fix already shipped separately.
