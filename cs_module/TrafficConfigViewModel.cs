using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace UTPS_Addin
{
    /// <summary>
    /// ViewModel for TrafficConfigDialog.
    /// Handles data binding, validation, and file picker commands.
    /// </summary>
    public class TrafficConfigViewModel : INotifyPropertyChanged
    {
        private readonly Window _parentWindow;

        // Private fields for properties
        private string _xmlFilePath;
        private string _gpkgFilePath;
        private string _startTime = "08:00";
        private string _endTime = "09:00";
        private string _outputPath;
        private double _videoLengthSeconds = 60.0;
        private double _exportFps = 24.0;
        private string _stylxFilePath;
        private string _validationMessage;
        private bool _hasValidationErrors;

        // Commands
        public ICommand BrowseXmlCommand { get; }
        public ICommand BrowseGpkgCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand BrowseStylxCommand { get; }
        public ICommand OkCommand { get; }

        public TrafficConfigViewModel(Window parentWindow)
        {
            _parentWindow = parentWindow;

            // Initialize commands
            BrowseXmlCommand = new RelayCommand(BrowseXmlFile);
            BrowseGpkgCommand = new RelayCommand(BrowseGpkgFile);
            BrowseOutputCommand = new RelayCommand(BrowseOutputFile);
            BrowseStylxCommand = new RelayCommand(BrowseStylxFile);
            OkCommand = new RelayCommand(OnOk);

            // Set default output path
            _outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UTPS",
                "output",
                "export_1"
            );
        }

        #region Properties

        public string XmlFilePath
        {
            get => _xmlFilePath;
            set
            {
                if (_xmlFilePath != value)
                {
                    _xmlFilePath = value;
                    OnPropertyChanged(nameof(XmlFilePath));
                    ClearValidation();
                }
            }
        }

        public string GpkgFilePath
        {
            get => _gpkgFilePath;
            set
            {
                if (_gpkgFilePath != value)
                {
                    _gpkgFilePath = value;
                    OnPropertyChanged(nameof(GpkgFilePath));
                    ClearValidation();
                }
            }
        }

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

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (_outputPath != value)
                {
                    _outputPath = value;
                    OnPropertyChanged(nameof(OutputPath));
                    ClearValidation();
                }
            }
        }

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

        #region File Browser Methods

        private void BrowseXmlFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select XML Events File",
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                XmlFilePath = dialog.FileName;
            }
        }

        private void BrowseGpkgFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Road Network GeoPackage",
                Filter = "GeoPackage Files (*.gpkg)|*.gpkg|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                GpkgFilePath = dialog.FileName;
            }
        }

        private void BrowseOutputFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select Output Location",
                Filter = "GeoParquet Files (*.parquet)|*.parquet|All Files (*.*)|*.*",
                DefaultExt = ".parquet",
                AddExtension = true,
                FileName = Path.GetFileName(OutputPath) ?? "traffic_output"
            };

            // Set initial directory if path exists
            string directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog() == true)
            {
                // Remove extension - Python script will add it
                OutputPath = Path.ChangeExtension(dialog.FileName, null);
            }
        }

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

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            var errors = new System.Text.StringBuilder();

            // Validate XML file
            if (string.IsNullOrWhiteSpace(XmlFilePath))
            {
                errors.AppendLine("• XML Events File is required");
            }
            else if (!File.Exists(XmlFilePath))
            {
                errors.AppendLine($"• XML file does not exist: {XmlFilePath}");
            }

            // Validate GPKG file
            if (string.IsNullOrWhiteSpace(GpkgFilePath))
            {
                errors.AppendLine("• Road Network GeoPackage is required");
            }
            else if (!File.Exists(GpkgFilePath))
            {
                errors.AppendLine($"• GeoPackage file does not exist: {GpkgFilePath}");
            }

            // Validate start time format
            if (string.IsNullOrWhiteSpace(StartTime))
            {
                errors.AppendLine("• Start Time is required");
            }
            else if (!IsValidTimeFormat(StartTime))
            {
                errors.AppendLine("• Start Time must be in HH:MM or HH:MM:SS format (e.g., 08:00 or 08:00:00)");
            }

            // Validate end time format
            if (string.IsNullOrWhiteSpace(EndTime))
            {
                errors.AppendLine("• End Time is required");
            }
            else if (!IsValidTimeFormat(EndTime))
            {
                errors.AppendLine("• End Time must be in HH:MM or HH:MM:SS format (e.g., 09:00 or 08:01:30)");
            }

            // Validate time range
            if (IsValidTimeFormat(StartTime) && IsValidTimeFormat(EndTime))
            {
                if (!IsValidTimeRange(StartTime, EndTime))
                {
                    errors.AppendLine("• End Time must be after Start Time");
                }
            }

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

            // Validate output path
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                errors.AppendLine("• Output Path is required");
            }
            else
            {
                try
                {
                    string directory = Path.GetDirectoryName(OutputPath);
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

            // Set validation state
            if (errors.Length > 0)
            {
                ValidationMessage = errors.ToString().TrimEnd();
                HasValidationErrors = true;
                return false;
            }

            return true;
        }

        private bool IsValidTimeFormat(string time)
        {
            // Match HH:MM or HH:MM:SS format (24-hour)
            var regex = new Regex(@"^([0-1][0-9]|2[0-3]):([0-5][0-9])(?::([0-5][0-9]))?$");
            return regex.IsMatch(time);
        }

        private int TimeToSeconds(string time)
        {
            var parts = time.Split(':');
            int h = int.Parse(parts[0]);
            int m = int.Parse(parts[1]);
            int s = parts.Length == 3 ? int.Parse(parts[2]) : 0;
            return h * 3600 + m * 60 + s;
        }

        private bool IsValidTimeRange(string startTime, string endTime)
        {
            try
            {
                return TimeToSeconds(endTime) > TimeToSeconds(startTime);
            }
            catch
            {
                return false;
            }
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
                // Validation passed - close dialog with OK result
                _parentWindow.DialogResult = true;
                _parentWindow.Close();
            }
            // If validation fails, errors are shown in UI - dialog stays open
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Simple RelayCommand implementation for button commands.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}