using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Tools {
    public static class MemGraphCommandRunner {
        private const int DefaultTimeoutMs = 300000;

        private static Process _activeProcess;
        private static StringBuilder _outputBuilder;
        private static StringBuilder _errorBuilder;
        private static Action<CommandResult> _onComplete;
        private static double _startTime;
        private static int _timeoutMs;
        private static bool _isRunning;

        public static bool IsSupported {
            get {
#if UNITY_EDITOR_OSX
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsRunning => _isRunning;

        public static CommandResult Run(string command, string args, int timeoutMs = DefaultTimeoutMs) {
            var result = new CommandResult();
            try {
                var process = new Process();
                process.StartInfo.FileName = command;
                process.StartInfo.Arguments = args;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(timeoutMs);

                if (!process.HasExited) {
                    process.Kill();
                    result.Success = false;
                    result.Error = $"Process timed out after {timeoutMs}ms";
                    result.ExitCode = -1;
                    return result;
                }

                result.Output = output;
                result.Error = error;
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
            }
            catch (Exception ex) {
                result.Success = false;
                result.Error = ex.Message;
                result.ExitCode = -1;
            }
            return result;
        }

        public static void RunAsync(string command, string args, Action<CommandResult> onComplete,
            int timeoutMs = DefaultTimeoutMs) {
            if (_isRunning) {
                onComplete?.Invoke(new CommandResult {
                    Success = false,
                    Error = "Another command is already running",
                    ExitCode = -1,
                });
                return;
            }

            _outputBuilder = new StringBuilder();
            _errorBuilder = new StringBuilder();
            _onComplete = onComplete;
            _timeoutMs = timeoutMs;
            _isRunning = true;
            _startTime = EditorApplication.timeSinceStartup;

            try {
                _activeProcess = new Process();
                _activeProcess.StartInfo.FileName = command;
                _activeProcess.StartInfo.Arguments = args;
                _activeProcess.StartInfo.UseShellExecute = false;
                _activeProcess.StartInfo.RedirectStandardOutput = true;
                _activeProcess.StartInfo.RedirectStandardError = true;
                _activeProcess.StartInfo.CreateNoWindow = true;

                _activeProcess.OutputDataReceived += (_, e) => {
                    if (e.Data != null) _outputBuilder.AppendLine(e.Data);
                };
                _activeProcess.ErrorDataReceived += (_, e) => {
                    if (e.Data != null) _errorBuilder.AppendLine(e.Data);
                };

                _activeProcess.Start();
                _activeProcess.BeginOutputReadLine();
                _activeProcess.BeginErrorReadLine();

                EditorApplication.update += PollProcess;
            }
            catch (Exception ex) {
                _isRunning = false;
                onComplete?.Invoke(new CommandResult {
                    Success = false,
                    Error = ex.Message,
                    ExitCode = -1,
                });
            }
        }

        public static void Cancel() {
            if (!_isRunning || _activeProcess == null) return;
            try {
                if (!_activeProcess.HasExited) _activeProcess.Kill();
            }
            catch (Exception ex) {
                Debug.LogWarning($"[MemGraphAnalyzer] Failed to kill process: {ex.Message}");
            }
            Cleanup();
            _onComplete?.Invoke(new CommandResult {
                Success = false,
                Error = "Cancelled by user",
                ExitCode = -1,
            });
        }

        private static void PollProcess() {
            if (_activeProcess == null) {
                Cleanup();
                return;
            }

            double elapsed = (EditorApplication.timeSinceStartup - _startTime) * 1000;
            if (elapsed > _timeoutMs && !_activeProcess.HasExited) {
                try { _activeProcess.Kill(); }
                catch { /* ignored */ }
                Cleanup();
                _onComplete?.Invoke(new CommandResult {
                    Success = false,
                    Error = $"Process timed out after {_timeoutMs}ms",
                    ExitCode = -1,
                });
                return;
            }

            if (!_activeProcess.HasExited) return;

            var result = new CommandResult {
                Output = _outputBuilder.ToString(),
                Error = _errorBuilder.ToString(),
                ExitCode = _activeProcess.ExitCode,
                Success = _activeProcess.ExitCode == 0,
            };

            var callback = _onComplete;
            Cleanup();
            callback?.Invoke(result);
        }

        private static void Cleanup() {
            EditorApplication.update -= PollProcess;
            _isRunning = false;
            if (_activeProcess != null) {
                try { _activeProcess.Dispose(); }
                catch { /* ignored */ }
                _activeProcess = null;
            }
            _onComplete = null;
        }
    }
}
