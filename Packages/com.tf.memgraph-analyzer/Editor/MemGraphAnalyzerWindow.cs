using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow : EditorWindow {
        [MenuItem("Tools/MemGraph Analyzer")]
        public static void ShowWindow() {
            GetWindow<MemGraphAnalyzerWindow>("MemGraph Analyzer");
        }

        private string _memGraphPath = "";
        private MemGraphReport _report;
        private int _selectedTab;
        private bool _stylesInitialized;

        private GUIStyle _headerStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _successStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _healthBadgeStyle;

        private static readonly string[] TabLabels = {
            "Summary", "Virtual Memory", "Heap Analysis", "Leak Detection", "Unity-Specific", "Comparison"
        };

        // Comparison state
        private string _baselinePath = "";
        private string _currentPath = "";
        private MemGraphReport _baselineReport;
        private MemGraphReport _currentReport;
        private MemGraphDiffResult _diffResult;
        private ComparisonPhase _comparisonPhase = ComparisonPhase.Idle;

        private enum ComparisonPhase { Idle, AnalyzingBaseline, AnalyzingCurrent, Calculating, Complete, Error }
        private string _comparisonError;

        private static readonly Color HealthGoodColor = new(0.3f, 0.85f, 0.3f, 1f);
        private static readonly Color HealthWarningColor = new(1f, 0.8f, 0.2f, 1f);
        private static readonly Color HealthCriticalColor = new(1f, 0.3f, 0.3f, 1f);

        private static readonly Color BarBgColor = new(0.2f, 0.2f, 0.2f, 0.5f);
        private static readonly Color BarUnityColor = new(0.3f, 0.6f, 1f, 0.8f);
        private static readonly Color BarPluginColor = new(1f, 0.6f, 0.2f, 0.8f);
        private static readonly Color BarSystemColor = new(0.5f, 0.5f, 0.5f, 0.8f);
        private static readonly Color BarGpuColor = new(0.4f, 0.8f, 0.4f, 0.8f);
        private static readonly Color BarUnsafeColor = new(1f, 0.3f, 0.3f, 0.8f);
        private static readonly Color BarUnknownColor = new(0.6f, 0.6f, 0.6f, 0.5f);
        private static readonly Color SelectionColor = new(0.24f, 0.48f, 0.9f, 0.3f);

        private void InitStyles() {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 13
            };
            _errorStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) }
            };
            _successStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(0.4f, 0.8f, 0.4f) }
            };
            _warningStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(1f, 0.8f, 0.2f) }
            };
            _mutedStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = Color.gray }
            };
            _healthBadgeStyle = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void OnGUI() {
            InitStyles();

            if (!MemGraphCommandRunner.IsSupported) {
                EditorGUILayout.HelpBox(
                    "MemGraph Analyzer requires macOS.\n" +
                    "vmmap, heap, leaks commands are only available on macOS.",
                    MessageType.Warning);
                return;
            }

            DrawToolbar();
            DrawProgressBar();

            if (_report != null && _report.Phase == AnalysisPhase.Complete) {
                _selectedTab = GUILayout.Toolbar(_selectedTab, TabLabels);
                GUILayout.Space(4);

                switch (_selectedTab) {
                    case 0: DrawSummaryTab(); break;
                    case 1: DrawVmmapTab(); break;
                    case 2: DrawHeapTab(); break;
                    case 3: DrawLeaksTab(); break;
                    case 4: DrawUnityTab(); break;
                    case 5: DrawComparisonTab(); break;
                }
            } else {
                if (_report != null && _report.Phase == AnalysisPhase.Error) {
                    EditorGUILayout.HelpBox(
                        $"Analysis failed: {_report.ErrorMessage}",
                        MessageType.Error);
                }

                // Allow Comparison tab even without a completed analysis
                if (_report == null || _report.Phase != AnalysisPhase.Complete) {
                    GUILayout.Space(8);
                    if (GUILayout.Button("Open Comparison Tool", GUILayout.Height(28))) {
                        _selectedTab = 5;
                    }
                    if (_selectedTab == 5) {
                        GUILayout.Space(4);
                        DrawComparisonTab();
                    }
                }
            }
        }

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("File:", GUILayout.Width(30));
            var displayPath = string.IsNullOrEmpty(_memGraphPath) ? "(none)" : Path.GetFileName(_memGraphPath);
            GUILayout.Label(displayPath, EditorStyles.toolbarButton, GUILayout.MinWidth(100));

            if (GUILayout.Button("Browse...", EditorStyles.toolbarButton, GUILayout.Width(70))) {
                var path = EditorUtility.OpenFilePanel("Select .memgraph file", "", "memgraph");
                if (!string.IsNullOrEmpty(path)) {
                    _memGraphPath = path;
                }
            }

            var isRunning = _report != null && _report.Phase != AnalysisPhase.Idle &&
                            _report.Phase != AnalysisPhase.Complete && _report.Phase != AnalysisPhase.Error;

            GUI.enabled = !string.IsNullOrEmpty(_memGraphPath) && !isRunning;
            if (GUILayout.Button("Analyze", EditorStyles.toolbarButton, GUILayout.Width(60))) {
                StartAnalysis();
            }
            GUI.enabled = true;

            if (isRunning) {
                if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(55))) {
                    MemGraphCommandRunner.Cancel();
                    if (_report != null) {
                        _report.Phase = AnalysisPhase.Error;
                        _report.ErrorMessage = "Cancelled by user";
                    }
                }
            }

            GUILayout.FlexibleSpace();

            if (_report != null && _report.Phase == AnalysisPhase.Complete) {
                DrawExportButtons();
            }

            if (GUILayout.Button("Snapshot", EditorStyles.toolbarButton, GUILayout.Width(65))) {
                SnapshotAnalyzerWindow.ShowWindow();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawProgressBar() {
            if (_report == null) return;
            var phase = _report.Phase;
            if (phase == AnalysisPhase.Idle || phase == AnalysisPhase.Complete || phase == AnalysisPhase.Error)
                return;

            float progress = phase switch {
                AnalysisPhase.RunningFootprint => 0.05f,
                AnalysisPhase.RunningVmmapSummary => 0.15f,
                AnalysisPhase.RunningVmmap => 0.35f,
                AnalysisPhase.RunningHeap => 0.50f,
                AnalysisPhase.RunningLeaks => 0.65f,
                AnalysisPhase.RunningCallTree => 0.82f,
                AnalysisPhase.Categorizing => 0.93f,
                _ => 0f,
            };
            string label = phase switch {
                AnalysisPhase.RunningFootprint => "Running footprint...",
                AnalysisPhase.RunningVmmapSummary => "Running vmmap --summary...",
                AnalysisPhase.RunningVmmap => "Running vmmap (detailed)...",
                AnalysisPhase.RunningHeap => "Running heap --showSizes --sortBySize...",
                AnalysisPhase.RunningLeaks => "Running leaks...",
                AnalysisPhase.RunningCallTree => "Running malloc_history (call tree)...",
                AnalysisPhase.Categorizing => "Categorizing results...",
                _ => "Processing...",
            };
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress, label);
        }

        private void StartAnalysis() {
            if (!ValidateFile()) return;
            _cachedLeakGroups = null;
            _selectedHeapRow = -1;
            _addressTraceCache.Clear();
            _report = new MemGraphReport {
                FilePath = _memGraphPath,
                AnalysisTime = DateTime.Now,
                Phase = AnalysisPhase.RunningFootprint,
            };
            RunFootprint();
        }

        private bool ValidateFile() {
            if (string.IsNullOrEmpty(_memGraphPath)) {
                EditorUtility.DisplayDialog("Error", "Please select a .memgraph file.", "OK");
                return false;
            }
            if (!File.Exists(_memGraphPath)) {
                EditorUtility.DisplayDialog("Error", $"File not found:\n{_memGraphPath}", "OK");
                return false;
            }
            if (!_memGraphPath.EndsWith(".memgraph", StringComparison.OrdinalIgnoreCase)) {
                EditorUtility.DisplayDialog("Error", "File must have .memgraph extension.", "OK");
                return false;
            }
            return true;
        }

        #region Analysis Pipeline

        private void RunFootprint() {
            _report.Phase = AnalysisPhase.RunningFootprint;
            Repaint();
            MemGraphCommandRunner.RunAsync("footprint", $"\"{_memGraphPath}\"", result => {
                // footprint is non-critical; continue even on failure
                if (result.Success || !string.IsNullOrEmpty(result.Output))
                    _report.Footprint = VmmapParser.ParseFootprint(result.Output ?? result.Error);
                RunVmmapSummary();
            });
        }

        private void RunVmmapSummary() {
            _report.Phase = AnalysisPhase.RunningVmmapSummary;
            Repaint();

            MemGraphCommandRunner.RunAsync("vmmap", $"--summary \"{_memGraphPath}\"", result => {
                if (!result.Success) {
                    OnPipelineError($"vmmap --summary failed: {result.Error}");
                    return;
                }
                _report.Vmmap = VmmapParser.ParseSummary(result.Output);
                RunVmmapDetailed();
            });
        }

        private void RunVmmapDetailed() {
            _report.Phase = AnalysisPhase.RunningVmmap;
            Repaint();

            MemGraphCommandRunner.RunAsync("vmmap", $"\"{_memGraphPath}\"", result => {
                // vmmap detailed is non-critical; continue even on failure
                if (result.Success) {
                    _report.Vmmap.DetailedRawOutput = result.Output;
                    _report.Vmmap.Regions.AddRange(VmmapParser.ParseDetailed(result.Output));
                }
                RunHeap();
            });
        }

        private void RunHeap() {
            _report.Phase = AnalysisPhase.RunningHeap;
            Repaint();

            MemGraphCommandRunner.RunAsync("heap", $"--showSizes --sortBySize \"{_memGraphPath}\"", result => {
                if (result.Success) {
                    _report.Heap = HeapParser.Parse(result.Output);
                } else {
                    // heap is non-critical for vmmap results
                    _report.Heap.RawOutput = result.Error;
                }
                RunLeaks();
            });
        }

        private void RunLeaks() {
            _report.Phase = AnalysisPhase.RunningLeaks;
            Repaint();

            MemGraphCommandRunner.RunAsync("leaks", $"\"{_memGraphPath}\"", result => {
                // leaks returns non-zero exit code when leaks are found, so check output
                if (!string.IsNullOrEmpty(result.Output)) {
                    _report.Leaks = LeaksParser.Parse(result.Output);
                } else if (!string.IsNullOrEmpty(result.Error)) {
                    _report.Leaks = LeaksParser.Parse(result.Error);
                }
                RunCallTree();
            });
        }

        private void RunCallTree() {
            _report.Phase = AnalysisPhase.RunningCallTree;
            Repaint();

            // Targeted query: only allocations >= 100KB for better signal-to-noise
            // -ignoreThreads merges all threads into one tree
            var path = AddressTraceParser.EscapeForShell(_memGraphPath);
            var shellArgs = $"-c \"malloc_history {path} -callTree -invert -ignoreThreads -chargeSystemLibraries '[100k+]' 2>&1 | head -5000\"";
            MemGraphCommandRunner.RunAsync("/bin/sh", shellArgs, result => {
                if (!string.IsNullOrEmpty(result.Output))
                    _report.CallTree = CallTreeParser.ParseInvertedCallTree(result.Output);
                BuildSummary();
            });
        }

        private void BuildSummary() {
            _report.Phase = AnalysisPhase.Categorizing;
            Repaint();

            var summary = _report.Summary;

            // Total from vmmap
            if (_report.Vmmap.Total != null) {
                summary.TotalVirtual = _report.Vmmap.Total.VirtualSize;
                summary.TotalResident = _report.Vmmap.Total.ResidentSize;
                summary.TotalDirty = _report.Vmmap.Total.DirtySize;
                summary.TotalSwapped = _report.Vmmap.Total.SwappedSize;
            }

            // Category breakdowns from vmmap summary
            long totalResident = summary.TotalResident > 0 ? summary.TotalResident : 1;
            foreach (var row in _report.Vmmap.Summary) {
                summary.CategoryBreakdowns.Add(new MemoryCategoryBreakdown {
                    Category = row.RegionType,
                    Size = row.ResidentSize,
                    Percentage = (float)row.ResidentSize / totalResident * 100f,
                });
            }

            // Override with footprint's phys_footprint (actual Jetsam metric)
            if (_report.Footprint.PhysFootprint > 0) {
                summary.TotalResident = _report.Footprint.PhysFootprint;
                summary.TotalDirty = _report.Footprint.TotalDirty;
            }

            // Owner breakdowns from heap
            foreach (var alloc in _report.Heap.Allocations) {
                if (!summary.OwnerBreakdowns.ContainsKey(alloc.Owner))
                    summary.OwnerBreakdowns[alloc.Owner] = 0;
                summary.OwnerBreakdowns[alloc.Owner] += alloc.TotalBytes;

                var plugin = HeapParser.DetectPluginName(alloc.ClassName, alloc.Binary);
                if (plugin != null) {
                    if (!summary.PluginBreakdowns.ContainsKey(plugin))
                        summary.PluginBreakdowns[plugin] = 0;
                    summary.PluginBreakdowns[plugin] += alloc.TotalBytes;
                }
            }

            // Tracked vs Untracked estimate
            summary.OwnerBreakdowns.TryGetValue(MemoryOwner.Unity, out long unityBytes);
            summary.TrackedByUnity = unityBytes;
            summary.UntrackedByUnity = _report.Heap.TotalBytes - unityBytes;

            BuildInsights();
            _report.Optimizations = OptimizationGuide.Analyze(_report);

            _report.Phase = AnalysisPhase.Complete;
            Repaint();
        }

        private void OnPipelineError(string message) {
            _report.Phase = AnalysisPhase.Error;
            _report.ErrorMessage = message;
            Repaint();
        }

        #endregion

        #region Utility

        private static Color GetOwnerColor(MemoryOwner owner) {
            return owner switch {
                MemoryOwner.Unity => BarUnityColor,
                MemoryOwner.NativePlugin => BarPluginColor,
                MemoryOwner.System => BarSystemColor,
                MemoryOwner.GraphicsDriver => BarGpuColor,
                MemoryOwner.UnsafeUtility => BarUnsafeColor,
                MemoryOwner.ThreadStack => new Color(0.7f, 0.5f, 0.9f, 0.8f),
                _ => BarUnknownColor,
            };
        }

        private static Color GetHealthColor(MemoryHealthStatus status) {
            return status switch {
                MemoryHealthStatus.Good => HealthGoodColor,
                MemoryHealthStatus.Warning => HealthWarningColor,
                MemoryHealthStatus.Critical => HealthCriticalColor,
                _ => Color.white,
            };
        }

        private static Color GetSeverityColor(InsightSeverity severity) {
            return severity switch {
                InsightSeverity.Critical => HealthCriticalColor,
                InsightSeverity.Warning => HealthWarningColor,
                InsightSeverity.Info => new Color(0.6f, 0.8f, 1f, 1f),
                _ => Color.white,
            };
        }

        private static string GetSeverityIcon(InsightSeverity severity) {
            return severity switch {
                InsightSeverity.Critical => "[!!]",
                InsightSeverity.Warning => "[!]",
                InsightSeverity.Info => "[i]",
                _ => "",
            };
        }

        private static Color GetActionabilityColor(Actionability actionability) {
            return actionability switch {
                Actionability.Fixable => HealthGoodColor,
                Actionability.Monitor => HealthWarningColor,
                Actionability.SystemOwned => new Color(0.6f, 0.6f, 0.6f, 1f),
                _ => Color.white,
            };
        }

        #endregion
    }
}
