using System;
using System.ComponentModel;
using System.Text;
using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Progress dialog for displaying Python pipeline execution status.
    /// </summary>
    public partial class ProgressDialog : Window, INotifyPropertyChanged
    {
        private PythonRunner _runner;
        private string _statusMessage;
        private int _progressPercent;
        private StringBuilder _logBuilder;
        private string _detailedLog;
        private bool _canCancel;
        private bool _cancelled;

        public event PropertyChangedEventHandler PropertyChanged;

        public ProgressDialog()
        {
            InitializeComponent();
            DataContext = this;

            _logBuilder = new StringBuilder();
            _statusMessage = "Initializing...";
            _progressPercent = 0;
            _canCancel = true;
            _cancelled = false;

            UpdateLog("Starting traffic data processing...");
        }

        #region Properties

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        public int ProgressPercent
        {
            get => _progressPercent;
            set
            {
                if (_progressPercent != value)
                {
                    _progressPercent = Math.Max(0, Math.Min(100, value));
                    OnPropertyChanged(nameof(ProgressPercent));
                }
            }
        }

        public string DetailedLog
        {
            get => _detailedLog;
            set
            {
                if (_detailedLog != value)
                {
                    _detailedLog = value;
                    OnPropertyChanged(nameof(DetailedLog));
                }
            }
        }

        public bool CanCancel
        {
            get => _canCancel;
            set
            {
                if (_canCancel != value)
                {
                    _canCancel = value;
                    OnPropertyChanged(nameof(CanCancel));
                }
            }
        }

        #endregion

        /// <summary>
        /// Start the pipeline with the given PythonRunner.
        /// </summary>
        public void StartProcessing(PythonRunner runner)
        {
            _runner = runner;

            // Wire up events
            _runner.ProgressChanged += OnProgressChanged;
            _runner.ErrorOccurred += OnErrorOccurred;
            _runner.ProcessingComplete += OnProcessingComplete;

            // Update UI
            StatusMessage = "Starting pipeline...";
            UpdateLog("Python process started");
        }

        /// <summary>
        /// Handle progress updates from Python.
        /// </summary>
        private void OnProgressChanged(string stage, int percent, string message)
        {
            // Must update UI on UI thread
            Dispatcher.Invoke(() =>
            {
                ProgressPercent = percent;
                StatusMessage = message;
                UpdateLog($"[{stage}] {message}");
            });
        }

        /// <summary>
        /// Handle errors from Python.
        /// </summary>
        private void OnErrorOccurred(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateLog($"ERROR: {errorMessage}");

                // If it's a fatal error (not just a warning), show message and close
                if (errorMessage.Contains("failed") || errorMessage.Contains("not found"))
                {
                    CanCancel = false;
                    StatusMessage = "Processing failed - see log for details";

                    MessageBox.Show(
                        $"An error occurred during processing:\n\n{errorMessage}\n\nSee the log for more details.",
                        "Processing Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );

                    DialogResult = false;
                    Close();
                }
            });
        }

        /// <summary>
        /// Handle successful completion.
        /// </summary>
        private void OnProcessingComplete(string outputPath)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressPercent = 100;
                StatusMessage = "Processing complete!";
                UpdateLog($"SUCCESS: Output saved to {outputPath}");
                CanCancel = false;

                MessageBox.Show(
                    $"Traffic data processing complete!\n\nOutput saved to:\n{outputPath}\n\n" +
                    "The data is now ready to be loaded into ArcGIS Pro.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DialogResult = true;
                Close();
            });
        }

        /// <summary>
        /// Add a message to the detailed log.
        /// </summary>
        private void UpdateLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuilder.AppendLine($"[{timestamp}] {message}");

            // Keep last 500 lines to prevent memory issues
            var lines = _logBuilder.ToString().Split('\n');
            if (lines.Length > 500)
            {
                _logBuilder.Clear();
                for (int i = lines.Length - 500; i < lines.Length; i++)
                {
                    _logBuilder.AppendLine(lines[i]);
                }
            }

            DetailedLog = _logBuilder.ToString();
        }

        /// <summary>
        /// Handle cancel button click.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cancelled)
                return;

            var result = MessageBox.Show(
                "Are you sure you want to cancel processing?\n\nThis cannot be undone.",
                "Cancel Processing",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                _cancelled = true;
                CanCancel = false;
                StatusMessage = "Cancelling...";
                UpdateLog("User requested cancellation");

                _runner?.Cancel();

                DialogResult = false;
                Close();
            }
        }

        /// <summary>
        /// Handle window closing - ensure process is killed.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_canCancel && !_cancelled)
            {
                e.Cancel = true;
                CancelButton_Click(this, null);
            }
            else
            {
                _runner?.Cancel();
            }

            base.OnClosing(e);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}