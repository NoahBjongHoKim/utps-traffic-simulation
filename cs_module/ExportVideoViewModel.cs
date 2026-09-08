using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace UTPS_Addin
{
    /// <summary>
    /// Resolution presets for video export.
    /// </summary>
    public enum VideoResolution
    {
        Res720p,
        Res1080p,
        Res4K
    }

    /// <summary>
    /// ViewModel for ExportVideoDialog. Handles data binding, validation, and the
    /// output file picker command. Start/End second bounds are validated against
    /// AnimationState.VideoLengthSeconds (set by the most recent Load & Animate run).
    /// </summary>
    public class ExportVideoViewModel : INotifyPropertyChanged
    {
        private readonly Window _parentWindow;

        private string _outputFilePath;
        private VideoResolution _resolution = VideoResolution.Res1080p;
        private double _startSecond = 0.0;
        private double _endSecond;
        private string _validationMessage;
        private bool _hasValidationErrors;

        public ICommand BrowseOutputCommand { get; }
        public ICommand OkCommand { get; }

        public ExportVideoViewModel(Window parentWindow)
        {
            _parentWindow = parentWindow;

            BrowseOutputCommand = new RelayCommand(BrowseOutputFile);
            OkCommand = new RelayCommand(OnOk);

            _endSecond = AnimationState.VideoLengthSeconds;

            _outputFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UTPS",
                "output",
                "traffic_video.mp4"
            );
        }

        #region Properties

        public string OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                if (_outputFilePath != value)
                {
                    _outputFilePath = value;
                    OnPropertyChanged(nameof(OutputFilePath));
                    ClearValidation();
                }
            }
        }

        public VideoResolution Resolution
        {
            get => _resolution;
            set
            {
                if (_resolution != value)
                {
                    _resolution = value;
                    OnPropertyChanged(nameof(Resolution));
                    ClearValidation();
                }
            }
        }

        public double StartSecond
        {
            get => _startSecond;
            set
            {
                if (_startSecond != value)
                {
                    _startSecond = value;
                    OnPropertyChanged(nameof(StartSecond));
                    ClearValidation();
                }
            }
        }

        public double EndSecond
        {
            get => _endSecond;
            set
            {
                if (_endSecond != value)
                {
                    _endSecond = value;
                    OnPropertyChanged(nameof(EndSecond));
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

        #region File Browser

        private void BrowseOutputFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select Video Output Location",
                Filter = "MP4 Video (*.mp4)|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                FileName = Path.GetFileName(OutputFilePath) ?? "traffic_video.mp4"
            };

            string directory = Path.GetDirectoryName(OutputFilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFilePath = dialog.FileName;
            }
        }

        #endregion

        #region Validation

        private bool ValidateInputs()
        {
            var errors = new System.Text.StringBuilder();

            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                errors.AppendLine("• Output file path is required");
            }
            else
            {
                try
                {
                    string directory = Path.GetDirectoryName(OutputFilePath);
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

            if (StartSecond < 0)
            {
                errors.AppendLine("• Start second must be 0 or greater");
            }

            if (EndSecond <= StartSecond)
            {
                errors.AppendLine("• End second must be greater than start second");
            }

            if (EndSecond > AnimationState.VideoLengthSeconds)
            {
                errors.AppendLine($"• End second cannot exceed the video length from Load & Animate ({AnimationState.VideoLengthSeconds:F0}s)");
            }

            if (errors.Length > 0)
            {
                ValidationMessage = errors.ToString().TrimEnd();
                HasValidationErrors = true;
                return false;
            }

            return true;
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
                _parentWindow.DialogResult = true;
                _parentWindow.Close();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        /// <summary>
        /// Convert the selected resolution preset to pixel dimensions.
        /// </summary>
        public (int Width, int Height) GetResolutionPixels()
        {
            switch (Resolution)
            {
                case VideoResolution.Res720p:
                    return (1280, 720);
                case VideoResolution.Res4K:
                    return (3840, 2160);
                case VideoResolution.Res1080p:
                default:
                    return (1920, 1080);
            }
        }
    }
}
