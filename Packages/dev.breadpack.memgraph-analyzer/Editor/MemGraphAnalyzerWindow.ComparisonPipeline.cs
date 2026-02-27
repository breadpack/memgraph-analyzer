using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private void StartComparison() {
            if (string.IsNullOrEmpty(_baselinePath) || string.IsNullOrEmpty(_currentPath)) {
                EditorUtility.DisplayDialog("Error", "Please select both baseline and current .memgraph files.", "OK");
                return;
            }
            if (!File.Exists(_baselinePath) || !File.Exists(_currentPath)) {
                EditorUtility.DisplayDialog("Error", "One or both files do not exist.", "OK");
                return;
            }

            _comparisonPhase = ComparisonPhase.AnalyzingBaseline;
            _comparisonError = null;
            _diffResult = null;
            _baselineReport = new MemGraphReport {
                FilePath = _baselinePath,
                AnalysisTime = DateTime.Now,
                Phase = AnalysisPhase.RunningFootprint,
            };
            Repaint();

            RunComparisonFootprint(_baselineReport, _baselinePath, () => {
                // Baseline done, start current
                _comparisonPhase = ComparisonPhase.AnalyzingCurrent;
                _currentReport = new MemGraphReport {
                    FilePath = _currentPath,
                    AnalysisTime = DateTime.Now,
                    Phase = AnalysisPhase.RunningFootprint,
                };
                Repaint();

                RunComparisonFootprint(_currentReport, _currentPath, () => {
                    // Both done, calculate diff
                    _comparisonPhase = ComparisonPhase.Calculating;
                    Repaint();

                    try {
                        _diffResult = MemGraphDiffCalculator.Calculate(_baselineReport, _currentReport);
                        _comparisonPhase = ComparisonPhase.Complete;
                    }
                    catch (Exception ex) {
                        _comparisonPhase = ComparisonPhase.Error;
                        _comparisonError = $"Diff calculation failed: {ex.Message}";
                    }
                    Repaint();
                });
            });
        }

        private void RunComparisonFootprint(MemGraphReport report, string path, Action onComplete) {
            report.Phase = AnalysisPhase.RunningFootprint;
            Repaint();
            MemGraphCommandRunner.RunAsync("footprint", $"\"{path}\"", result => {
                if (result.Success || !string.IsNullOrEmpty(result.Output))
                    report.Footprint = VmmapParser.ParseFootprint(result.Output ?? result.Error);
                RunComparisonVmmapSummary(report, path, onComplete);
            });
        }

        private void RunComparisonVmmapSummary(MemGraphReport report, string path, Action onComplete) {
            report.Phase = AnalysisPhase.RunningVmmapSummary;
            Repaint();
            MemGraphCommandRunner.RunAsync("vmmap", $"--summary \"{path}\"", result => {
                if (result.Success)
                    report.Vmmap = VmmapParser.ParseSummary(result.Output);
                RunComparisonHeap(report, path, onComplete);
            });
        }

        private void RunComparisonHeap(MemGraphReport report, string path, Action onComplete) {
            report.Phase = AnalysisPhase.RunningHeap;
            Repaint();
            MemGraphCommandRunner.RunAsync("heap", $"--showSizes --sortBySize \"{path}\"", result => {
                if (result.Success)
                    report.Heap = HeapParser.Parse(result.Output);
                RunComparisonLeaks(report, path, onComplete);
            });
        }

        private void RunComparisonLeaks(MemGraphReport report, string path, Action onComplete) {
            report.Phase = AnalysisPhase.RunningLeaks;
            Repaint();
            MemGraphCommandRunner.RunAsync("leaks", $"\"{path}\"", result => {
                if (!string.IsNullOrEmpty(result.Output))
                    report.Leaks = LeaksParser.Parse(result.Output);
                else if (!string.IsNullOrEmpty(result.Error))
                    report.Leaks = LeaksParser.Parse(result.Error);
                FinalizeComparisonReport(report, onComplete);
            });
        }

        private void FinalizeComparisonReport(MemGraphReport report, Action onComplete) {
            report.Phase = AnalysisPhase.Categorizing;
            Repaint();

            var summary = report.Summary;
            if (report.Vmmap.Total != null) {
                summary.TotalVirtual = report.Vmmap.Total.VirtualSize;
                summary.TotalResident = report.Vmmap.Total.ResidentSize;
                summary.TotalDirty = report.Vmmap.Total.DirtySize;
                summary.TotalSwapped = report.Vmmap.Total.SwappedSize;
            }

            if (report.Footprint.PhysFootprint > 0) {
                summary.TotalResident = report.Footprint.PhysFootprint;
                summary.TotalDirty = report.Footprint.TotalDirty;
            }

            foreach (var alloc in report.Heap.Allocations) {
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

            summary.OwnerBreakdowns.TryGetValue(MemoryOwner.Unity, out long unityBytes);
            summary.TrackedByUnity = unityBytes;
            summary.UntrackedByUnity = report.Heap.TotalBytes - unityBytes;

            report.Phase = AnalysisPhase.Complete;
            Repaint();
            onComplete?.Invoke();
        }
    }
}
