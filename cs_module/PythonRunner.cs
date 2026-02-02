using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UTPS_Addin
{
    /// <summary>
    /// Handles execution of Python scripts with embedded Python interpreter.
    /// Provides progress reporting and error handling.
    /// </summary>
    public class PythonRunner
    {
        private Process _process;
        private readonly string _pythonExePath;
        private readonly string _scriptPath;

        /// <summary>
        /// Event fired when progress is reported from Python script.
        /// Arguments: stage, percent, message
        /// </summary>
        public event Action<string, int, string> ProgressChanged;

        /// <summary>
        /// Event fired when an error occurs.
        /// </summary>
        public event Action<string> ErrorOccurred;

        /// <summary>
        /// Event fired when processing completes successfully.
        /// </summary>
        public event Action<string> ProcessingComplete;

        public PythonRunner()
        {
            // Get path to embedded Python
            string addInFolder = Path.GetDirectoryName(typeof(PythonRunner).Assembly.Location);
            _pythonExePath = Path.Combine(addInFolder, "python_embed", "python.exe");
            _scriptPath = Path.Combine(addInFolder, "scripts", "traffic_loader_wrapper.py");

            // Validate paths
            if (!File.Exists(_pythonExePath))
            {
                throw new FileNotFoundException($"Embedded Python not found at: {_pythonExePath}");
            }

            if (!File.Exists(_scriptPath))
            {
                throw new FileNotFoundException($"Wrapper script not found at: {_scriptPath}");
            }
        }

        /// <summary>
        /// Run the traffic loader pipeline asynchronously.
        /// </summary>
        public void RunPipeline(string xmlPath, string gpkgPath, string startTime, string endTime, string outputPath)
        {
            if (_process != null && !_process.HasExited)
            {
                throw new InvalidOperationException("Pipeline is already running");
            }

            // Build command-line arguments
            var arguments = new StringBuilder();
            arguments.Append($"\"{_scriptPath}\" ");
            arguments.Append($"--xml \"{xmlPath}\" ");
            arguments.Append($"--gpkg \"{gpkgPath}\" ");
            arguments.Append($"--start-time \"{startTime}\" ");
            arguments.Append($"--end-time \"{endTime}\" ");
            arguments.Append($"--output \"{outputPath}\"");

            // Setup process
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonExePath,
                    Arguments = arguments.ToString(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_scriptPath)
                }
            };

            // Wire up output handlers
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;

            // Log what we're running (for debugging)
            Debug.WriteLine($"Python: {_pythonExePath}");
            Debug.WriteLine($"Arguments: {arguments}");

            try
            {
                // Start the process
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to start Python process: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Cancel the running process.
        /// </summary>
        public void Cancel()
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                    ErrorOccurred?.Invoke("Processing cancelled by user");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing process: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Wait for the process to complete (blocking).
        /// </summary>
        public int WaitForExit()
        {
            if (_process == null)
            {
                throw new InvalidOperationException("Process has not been started");
            }

            _process.WaitForExit();
            return _process.ExitCode;
        }

        /// <summary>
        /// Parse and handle stdout from Python script.
        /// Expected format: "PROGRESS: stage | percent | message"
        ///                  "ERROR: error message"
        ///                  "SUCCESS: output_path"
        /// </summary>
        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            string line = e.Data.Trim();
            Debug.WriteLine($"Python stdout: {line}");

            try
            {
                // Parse PROGRESS messages
                if (line.StartsWith("PROGRESS:"))
                {
                    // Format: "PROGRESS: STAGE | 50 | Message text"
                    var match = Regex.Match(line, @"PROGRESS:\s*(\w+)\s*\|\s*(\d+)\s*\|\s*(.+)");
                    if (match.Success)
                    {
                        string stage = match.Groups[1].Value;
                        int percent = int.Parse(match.Groups[2].Value);
                        string message = match.Groups[3].Value;

                        ProgressChanged?.Invoke(stage, percent, message);
                    }
                }
                // Parse ERROR messages
                else if (line.StartsWith("ERROR:"))
                {
                    string errorMessage = line.Substring(6).Trim();
                    ErrorOccurred?.Invoke(errorMessage);
                }
                // Parse SUCCESS messages
                else if (line.StartsWith("SUCCESS:"))
                {
                    string outputPath = line.Substring(8).Trim();
                    ProcessingComplete?.Invoke(outputPath);
                }
                // Log other output for debugging
                else
                {
                    Debug.WriteLine($"Python output: {line}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing Python output: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle stderr from Python script.
        /// </summary>
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            string line = e.Data.Trim();
            Debug.WriteLine($"Python stderr: {line}");

            // Some libraries print warnings to stderr - don't treat as fatal
            // Only raise ErrorOccurred if it looks like a real error
            if (line.Contains("Error") || line.Contains("Exception") || line.Contains("Traceback"))
            {
                ErrorOccurred?.Invoke($"Python error: {line}");
            }
        }

        /// <summary>
        /// Check if Python environment is properly set up.
        /// </summary>
        public static bool ValidatePythonEnvironment(out string errorMessage)
        {
            errorMessage = null;

            try
            {
                string addInFolder = Path.GetDirectoryName(typeof(PythonRunner).Assembly.Location);
                string pythonExePath = Path.Combine(addInFolder, "python_embed", "python.exe");
                string scriptPath = Path.Combine(addInFolder, "scripts", "traffic_loader_wrapper.py");

                if (!File.Exists(pythonExePath))
                {
                    errorMessage = $"Embedded Python not found. Expected at:\n{pythonExePath}\n\n" +
                                   "Please ensure the python_embed folder is included with the add-in.";
                    return false;
                }

                if (!File.Exists(scriptPath))
                {
                    errorMessage = $"Wrapper script not found. Expected at:\n{scriptPath}\n\n" +
                                   "Please ensure the scripts folder is included with the add-in.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error validating Python environment: {ex.Message}";
                return false;
            }
        }
    }
}