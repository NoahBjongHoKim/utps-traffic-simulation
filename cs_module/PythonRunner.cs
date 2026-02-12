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
            // Get path to Python executable
            // Try system Python first, then fall back to embedded Python
            _pythonExePath = FindPythonExecutable();

            // Get script path
            string addInFolder = Path.GetDirectoryName(typeof(PythonRunner).Assembly.Location);
            _scriptPath = Path.Combine(addInFolder, "scripts", "traffic_loader_wrapper.py");

            // Validate paths
            if (!File.Exists(_pythonExePath))
            {
                throw new FileNotFoundException($"Python executable not found at: {_pythonExePath}");
            }

            if (!File.Exists(_scriptPath))
            {
                throw new FileNotFoundException($"Wrapper script not found at: {_scriptPath}");
            }
        }

        /// <summary>
        /// Find Python executable - tries system Python first, then embedded Python.
        /// </summary>
        private static string FindPythonExecutable()
        {
            string addInFolder = Path.GetDirectoryName(typeof(PythonRunner).Assembly.Location);

            // Option 1: Embedded Python (for deployment)
            string embeddedPython = Path.Combine(addInFolder, "python_embed", "python.exe");
            if (File.Exists(embeddedPython))
            {
                Debug.WriteLine($"Using embedded Python: {embeddedPython}");
                return embeddedPython;
            }

            // Option 2: Conda/Miniforge/Mamba installations (check first as they're common for data science)
            string[] condaPaths = new[]
            {
                @"C:\Users\" + Environment.UserName + @"\Miniforge3\python.exe",
                @"C:\Users\" + Environment.UserName + @"\miniforge3\python.exe",
                @"C:\Users\" + Environment.UserName + @"\Mambaforge\python.exe",
                @"C:\Users\" + Environment.UserName + @"\mambaforge\python.exe",
                @"C:\Users\" + Environment.UserName + @"\Miniconda3\python.exe",
                @"C:\Users\" + Environment.UserName + @"\miniconda3\python.exe",
                @"C:\Users\" + Environment.UserName + @"\Anaconda3\python.exe",
                @"C:\Users\" + Environment.UserName + @"\anaconda3\python.exe",
                @"C:\ProgramData\Miniforge3\python.exe",
                @"C:\ProgramData\Mambaforge\python.exe",
                @"C:\ProgramData\Miniconda3\python.exe",
                @"C:\ProgramData\Anaconda3\python.exe",
            };

            foreach (string path in condaPaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Using Conda/Miniforge Python: {path}");
                    return path;
                }
            }

            // Option 3: Standard Python installations
            string[] pythonPaths = new[]
            {
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python312\python.exe",
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python311\python.exe",
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python310\python.exe",
            };

            foreach (string path in pythonPaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Using system Python: {path}");
                    return path;
                }
            }

            // Option 4: Try to find Python on PATH (but exclude Windows Store stub)
            string pythonFromPath = FindPythonOnPath();
            if (!string.IsNullOrEmpty(pythonFromPath))
            {
                Debug.WriteLine($"Using Python from PATH: {pythonFromPath}");
                return pythonFromPath;
            }

            // If all else fails, return embedded path (will fail validation, but gives clear error message)
            return embeddedPython;
        }

        /// <summary>
        /// Try to find Python on the system PATH, excluding Windows Store stub.
        /// </summary>
        private static string FindPythonOnPath()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "python.exe",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    // Get all Python paths found
                    string[] paths = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string path in paths)
                    {
                        string trimmedPath = path.Trim();

                        // Skip Windows Store stub python.exe
                        if (trimmedPath.Contains("WindowsApps") || trimmedPath.Contains("Microsoft\\WindowsApps"))
                        {
                            Debug.WriteLine($"Skipping Windows Store stub: {trimmedPath}");
                            continue;
                        }

                        if (File.Exists(trimmedPath))
                        {
                            return trimmedPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding Python on PATH: {ex.Message}");
            }

            return null;
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

            // Clear ArcGIS Pro's Python environment variables to use system Python
            // This prevents ArcGIS from hijacking the Python environment
            _process.StartInfo.EnvironmentVariables.Remove("PYTHONPATH");
            _process.StartInfo.EnvironmentVariables.Remove("PYTHONHOME");
            _process.StartInfo.EnvironmentVariables.Remove("CONDA_DEFAULT_ENV");
            _process.StartInfo.EnvironmentVariables.Remove("CONDA_PREFIX");

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
                string pythonExePath = FindPythonExecutable();
                string scriptPath = Path.Combine(addInFolder, "scripts", "traffic_loader_wrapper.py");

                if (!File.Exists(pythonExePath))
                {
                    errorMessage = $"Python executable not found.\n\n" +
                                   $"Searched locations:\n" +
                                   $"• Embedded Python: {Path.Combine(addInFolder, "python_embed", "python.exe")}\n" +
                                   $"• System Python (C:\\Python3xx\\python.exe)\n" +
                                   $"• User Python (AppData\\Local\\Programs\\Python)\n" +
                                   $"• Python on PATH\n\n" +
                                   $"Please install Python 3.10+ or include python_embed folder with the add-in.";
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