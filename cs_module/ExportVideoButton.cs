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
