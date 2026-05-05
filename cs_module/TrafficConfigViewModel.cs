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
        private string _validationMessage;
        private bool _hasValidationErrors;

        // Commands
        public ICommand BrowseXmlCommand { get; }
        public ICommand BrowseGpkgCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand OkCommand { get; }

        public TrafficConfigViewModel(Window parentWindow)
        {
            _parentWindow = parentWindow;

            // Initialize commands
            BrowseXmlCommand = new RelayCommand(BrowseXmlFile);
            BrowseGpkgCommand = new RelayCommand(BrowseGpkgFile);
            BrowseOutputCommand = new RelayCommand(BrowseOutputFile);
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