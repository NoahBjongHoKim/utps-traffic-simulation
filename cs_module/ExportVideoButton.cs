using ArcGIS.Desktop.Framework.Contracts;
using System;

namespace UTPS_Addin
{
    /// <summary>
    /// Export the current view's Time Slider animation to an MP4 video.
    ///
    /// TEMPORARILY DISABLED: the real implementation below (wrapped in
    /// #if EXPORT_VIDEO_ENABLED / #endif) uses ArcGIS Pro SDK Animation/TimeTrack/
    /// BeginExport APIs that are still being debugged against a real ArcGIS Pro
    /// build. Define EXPORT_VIDEO_ENABLED (or just delete this stub OnClick and the
    /// #if/#endif wrapper) once that's sorted out to restore the real behavior.
    /// This stub keeps the ribbon button present and functional (so the rest of the
    /// project builds and the Load &amp; Animate button can be tested) without
    /// touching any of the not-yet-verified Animation SDK types.
    /// </summary>
    internal class ExportVideoButton : Button
    {
        protected override void OnClick()
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                "Video export is temporarily disabled while the ArcGIS Animation SDK " +
                "integration is being debugged. Use 'Load & Animate' in the meantime.",
                "Not Yet Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }
}

#if EXPORT_VIDEO_ENABLED
// ─────────────────────────────────────────────────────────────────────────────
// Real implementation, disabled until the Animation SDK build issues are
// resolved. To restore: delete the stub class above, remove this #if block's
// guard (or define EXPORT_VIDEO_ENABLED in the .csproj), and rebuild.
// ─────────────────────────────────────────────────────────────────────────────
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using System.Linq;
using System.Threading.Tasks;

namespace UTPS_Addin
{
    internal class ExportVideoButtonReal : Button
    {
        private const string AnimationName = "TrafficAnimation";

        // Guards against overlapping exports triggered via this button (e.g. a rapid
        // double-click before the first export's dialog has appeared). Does not and
        // cannot guard against a concurrent export started through ArcGIS Pro's own
        // built-in Export Movie UI — that is out of scope for this add-in's code.
        private static bool _exportInProgress = false;

        protected override async void OnClick()
        {
            try
            {
                if (_exportInProgress)
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(
                        "A video export is already in progress. Please wait for it to finish.",
                        "Export In Progress",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

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

                _exportInProgress = true;
                try
                {
                    await RunExportAsync(mapView, viewModel);
                }
                finally
                {
                    _exportInProgress = false;
                }
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

                // Capture the ORIGINAL full data time range before anything below
                // mutates mapView.Time. Without this, a second export in the same
                // session would read the collapsed span from the previous export's
                // mutation instead of the real data range, silently producing a
                // video frozen near the first frame.
                DateTime originalStart = mapView.Time.Start;
                DateTime originalEnd = mapView.Time.End;

                var startExtent = new TimeExtent(originalStart);
                var endExtent = new TimeExtent(originalEnd);

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
                    mapView.Time = new TimeRange(originalStart, originalStart + TimeSpan.FromSeconds(spanSeconds));
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Animation keyframes built: {originalStart:HH:mm:ss} -> " +
                    $"{originalEnd:HH:mm:ss} over {AnimationState.VideoLengthSeconds}s video, " +
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
#endif
