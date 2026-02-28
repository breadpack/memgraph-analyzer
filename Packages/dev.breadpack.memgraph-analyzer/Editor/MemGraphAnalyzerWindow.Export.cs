using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private void DrawExportButtons() {
            if (GUILayout.Button(AnalyzerGuidance.ExportText, EditorStyles.toolbarButton, GUILayout.Width(75))) {
                ExportTextReport();
            }
            if (GUILayout.Button(AnalyzerGuidance.ExportCsv, EditorStyles.toolbarButton, GUILayout.Width(70))) {
                ExportHeapCsv();
            }
            if (GUILayout.Button(AnalyzerGuidance.CopySummary, EditorStyles.toolbarButton, GUILayout.Width(90))) {
                CopySummaryToClipboard();
            }
        }

        private void ExportTextReport() {
            var defaultName = $"memgraph_report_{_report.AnalysisTime:yyyyMMdd_HHmmss}.txt";
            var path = EditorUtility.SaveFilePanel("Export Text Report", "", defaultName, "txt");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new StringBuilder();
                sb.AppendLine("=== MemGraph Analysis Report ===");
                sb.AppendLine($"File: {_report.FilePath}");
                sb.AppendLine($"Analysis Time: {_report.AnalysisTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                // Footprint (iOS Jetsam metric)
                var fp = _report.Footprint;
                if (fp.PhysFootprint > 0) {
                    sb.AppendLine("--- iOS Physical Footprint ---");
                    sb.AppendLine($"phys_footprint:      {VmmapParser.FormatSize(fp.PhysFootprint)}");
                    sb.AppendLine($"phys_footprint_peak: {VmmapParser.FormatSize(fp.PhysFootprintPeak)}");
                    sb.AppendLine($"Dirty:       {VmmapParser.FormatSize(fp.TotalDirty)}");
                    sb.AppendLine($"Clean:       {VmmapParser.FormatSize(fp.TotalClean)}");
                    sb.AppendLine($"Reclaimable: {VmmapParser.FormatSize(fp.TotalReclaimable)}");
                    sb.AppendLine();
                }

                // Summary
                sb.AppendLine("--- Memory Overview (vmmap) ---");
                sb.AppendLine($"Total Virtual:  {VmmapParser.FormatSize(_report.Summary.TotalVirtual)}");
                sb.AppendLine($"Total Resident: {VmmapParser.FormatSize(_report.Summary.TotalResident)}");
                sb.AppendLine($"Total Dirty:    {VmmapParser.FormatSize(_report.Summary.TotalDirty)}");
                sb.AppendLine($"Total Swapped:  {VmmapParser.FormatSize(_report.Summary.TotalSwapped)}");
                sb.AppendLine();

                // Heap summary
                sb.AppendLine("--- Heap Summary ---");
                sb.AppendLine($"Total Heap:       {VmmapParser.FormatSize(_report.Heap.TotalBytes)}");
                sb.AppendLine($"Total Allocations: {_report.Heap.TotalCount:N0}");
                sb.AppendLine($"Unity Tracked:    {VmmapParser.FormatSize(_report.Summary.TrackedByUnity)}");
                sb.AppendLine($"Untracked:        {VmmapParser.FormatSize(_report.Summary.UntrackedByUnity)}");
                sb.AppendLine();

                // Owner breakdown
                sb.AppendLine("--- Owner Breakdown ---");
                foreach (var kv in _report.Summary.OwnerBreakdowns.OrderByDescending(kv => kv.Value)) {
                    float pct = _report.Heap.TotalBytes > 0
                        ? (float)kv.Value / _report.Heap.TotalBytes * 100f : 0;
                    sb.AppendLine($"  {HeapParser.GetOwnerDisplayName(kv.Key),-20} {VmmapParser.FormatSize(kv.Value),12} ({pct:F1}%)");
                }
                sb.AppendLine();

                // Plugin breakdown
                if (_report.Summary.PluginBreakdowns.Count > 0) {
                    sb.AppendLine("--- Native Plugin Breakdown ---");
                    foreach (var kv in _report.Summary.PluginBreakdowns.OrderByDescending(kv => kv.Value)) {
                        sb.AppendLine($"  {kv.Key,-20} {VmmapParser.FormatSize(kv.Value),12}");
                    }
                    sb.AppendLine();
                }

                // Vmmap summary
                sb.AppendLine("--- Virtual Memory (vmmap) ---");
                sb.AppendLine($"{"Region Type",-30} {"Virtual",12} {"Resident",12} {"Dirty",12} {"Swapped",12} {"Count",6}");
                foreach (var row in _report.Vmmap.Summary.OrderByDescending(r => r.ResidentSize)) {
                    sb.AppendLine(
                        $"  {row.RegionType,-28} {VmmapParser.FormatSize(row.VirtualSize),12} " +
                        $"{VmmapParser.FormatSize(row.ResidentSize),12} {VmmapParser.FormatSize(row.DirtySize),12} " +
                        $"{VmmapParser.FormatSize(row.SwappedSize),12} {row.RegionCount,6}");
                }
                if (_report.Vmmap.Total != null) {
                    var t = _report.Vmmap.Total;
                    sb.AppendLine(
                        $"  {"TOTAL",-28} {VmmapParser.FormatSize(t.VirtualSize),12} " +
                        $"{VmmapParser.FormatSize(t.ResidentSize),12} {VmmapParser.FormatSize(t.DirtySize),12} " +
                        $"{VmmapParser.FormatSize(t.SwappedSize),12} {t.RegionCount,6}");
                }
                sb.AppendLine();

                // Leaks
                sb.AppendLine("--- Leak Detection ---");
                if (_report.Leaks.TotalLeakCount == 0) {
                    sb.AppendLine("No leaks detected.");
                } else {
                    sb.AppendLine($"{_report.Leaks.TotalLeakCount} leaks, " +
                                  $"{VmmapParser.FormatSize(_report.Leaks.TotalLeakBytes)} total");
                    foreach (var leak in _report.Leaks.Leaks) {
                        sb.AppendLine($"  {leak.Address} size={leak.Size} type={leak.TypeOrZone}");
                        if (!string.IsNullOrEmpty(leak.StackTrace)) {
                            sb.AppendLine($"    {leak.StackTrace.Replace("\n", "\n    ")}");
                        }
                    }
                }
                sb.AppendLine();

                // Insights
                sb.AppendLine("--- Issues & Recommendations ---");
                sb.AppendLine($"Overall Health: {_report.Summary.OverallHealth}");
                if (_report.Insights.Count == 0) {
                    sb.AppendLine("No issues detected.");
                } else {
                    foreach (var insight in _report.Insights) {
                        sb.AppendLine($"[{insight.Severity.ToString().ToUpper()}] {insight.Title}");
                        sb.AppendLine($"  {insight.Description}");
                        sb.AppendLine($"  Recommendation: {insight.Recommendation}");
                    }
                }
                sb.AppendLine();

                // Optimization Recommendations
                if (_report.Optimizations != null && _report.Optimizations.Recommendations.Count > 0) {
                    sb.AppendLine("--- Optimization Recommendations ---");
                    sb.AppendLine($"Total recommendations: {_report.Optimizations.Recommendations.Count}");
                    sb.AppendLine($"Estimated total savings: {VmmapParser.FormatSize(_report.Optimizations.TotalEstimatedSavings)}");
                    sb.AppendLine();
                    foreach (var rec in _report.Optimizations.Recommendations) {
                        sb.AppendLine($"[{rec.Difficulty}] [{rec.Category}] {rec.Title}");
                        sb.AppendLine($"  {rec.Description}");
                        sb.AppendLine($"  Estimated savings: ~{VmmapParser.FormatSize(rec.EstimatedSavings)}");
                        foreach (var step in rec.ActionSteps) {
                            sb.AppendLine($"    * {step}");
                        }
                        sb.AppendLine();
                    }
                }

                // Asset & Logic Allocation Trace
                if (_report.AllocationTrace != null && _report.AllocationTrace.Allocations.Count > 0) {
                    var at = _report.AllocationTrace;
                    sb.AppendLine("--- Asset & Logic Allocation Trace ---");
                    sb.AppendLine("Category Breakdown:");
                    foreach (var cat in at.CategoryBreakdown) {
                        sb.AppendLine($"  {cat.Category,-20} {VmmapParser.FormatSize(cat.TotalBytes),12} ({cat.Percentage:F1}%)");
                    }
                    sb.AppendLine();

                    sb.AppendLine("Top Allocations (by size):");
                    foreach (var alloc in at.Allocations.OrderByDescending(a => a.TotalBytes).Take(50)) {
                        string ctrlLabel = alloc.Controllability switch {
                            Controllability.UserControllable => "UserControllable",
                            Controllability.PartiallyControllable => "Partially",
                            Controllability.EngineOwned => "EngineOwned",
                            Controllability.SystemOwned => "SystemOwned",
                            _ => "Unknown",
                        };
                        string typeLabel = alloc.AssetType != AssetType.None
                            ? $"{alloc.Category}/{alloc.AssetType}"
                            : alloc.Category.ToString();
                        string funcLabel = alloc.TopUserFunction
                            ?? (alloc.TopEngineFunction != null
                                ? CallTreeParser.FormatFunctionName(alloc.TopEngineFunction)
                                : "(unknown)");
                        sb.AppendLine(
                            $"  [{ctrlLabel}] [{typeLabel}] {funcLabel}  " +
                            $"{VmmapParser.FormatSize(alloc.TotalBytes)} ({alloc.CallCount} call{(alloc.CallCount != 1 ? "s" : "")})");
                    }
                    sb.AppendLine();
                }

                // Top heap allocations
                sb.AppendLine("--- Top 50 Heap Allocations ---");
                sb.AppendLine($"{"Count",8} {"Bytes",12} {"Avg",10} {"Owner",-16} Class Name");
                foreach (var alloc in _report.Heap.Allocations.OrderByDescending(a => a.TotalBytes).Take(50)) {
                    sb.AppendLine(
                        $"  {alloc.Count,6} {VmmapParser.FormatSize(alloc.TotalBytes),12} " +
                        $"{VmmapParser.FormatSize(alloc.AverageSize),10} " +
                        $"{HeapParser.GetOwnerDisplayName(alloc.Owner),-16} {alloc.ClassName}");
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[MemGraphAnalyzer] Report exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private void ExportHeapCsv() {
            var defaultName = $"memgraph_heap_{_report.AnalysisTime:yyyyMMdd_HHmmss}.csv";
            var path = EditorUtility.SaveFilePanel("Export Heap CSV", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new StringBuilder();
                sb.AppendLine("Count,TotalBytes,AverageSize,ClassName,Owner");

                foreach (var alloc in _report.Heap.Allocations.OrderByDescending(a => a.TotalBytes)) {
                    var className = alloc.ClassName?.Replace("\"", "\"\"") ?? "";
                    sb.AppendLine(
                        $"{alloc.Count},{alloc.TotalBytes},{alloc.AverageSize}," +
                        $"\"{className}\",{HeapParser.GetOwnerDisplayName(alloc.Owner)}");
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[MemGraphAnalyzer] CSV exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private void ExportAllocationTraceCsv() {
            if (_report?.AllocationTrace == null || _report.AllocationTrace.Allocations.Count == 0) {
                EditorUtility.DisplayDialog("No Data", "No allocation trace data to export.", "OK");
                return;
            }

            var defaultName = $"memgraph_alloc_trace_{_report.AnalysisTime:yyyyMMdd_HHmmss}.csv";
            var path = EditorUtility.SaveFilePanel("Export Allocation Trace CSV", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("CallCount,TotalBytes,Category,AssetType,Controllability,TopUserFunction,TopEngineFunction,Summary");

                foreach (var alloc in _report.AllocationTrace.Allocations.OrderByDescending(a => a.TotalBytes)) {
                    var userFunc = (alloc.TopUserFunction ?? "").Replace("\"", "\"\"");
                    var engineFunc = (alloc.TopEngineFunction != null
                        ? CallTreeParser.FormatFunctionName(alloc.TopEngineFunction)
                        : "").Replace("\"", "\"\"");
                    var summary = (alloc.Summary ?? "").Replace("\"", "\"\"");
                    sb.AppendLine(
                        $"{alloc.CallCount},{alloc.TotalBytes},{alloc.Category},{alloc.AssetType}," +
                        $"{alloc.Controllability},\"{userFunc}\",\"{engineFunc}\",\"{summary}\"");
                }

                System.IO.File.WriteAllText(path, sb.ToString());
                Debug.Log($"[MemGraphAnalyzer] Allocation trace CSV exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (System.Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private void CopySummaryToClipboard() {
            var sb = new StringBuilder();
            sb.AppendLine($"MemGraph: {Path.GetFileName(_report.FilePath)}");
            if (_report.Footprint.PhysFootprint > 0) {
                sb.AppendLine($"Footprint: {VmmapParser.FormatSize(_report.Footprint.PhysFootprint)} | " +
                              $"Peak: {VmmapParser.FormatSize(_report.Footprint.PhysFootprintPeak)} | " +
                              $"Dirty: {VmmapParser.FormatSize(_report.Footprint.TotalDirty)}");
            }
            sb.AppendLine($"Virtual: {VmmapParser.FormatSize(_report.Summary.TotalVirtual)} | " +
                          $"Resident: {VmmapParser.FormatSize(_report.Summary.TotalResident)} | " +
                          $"Dirty: {VmmapParser.FormatSize(_report.Summary.TotalDirty)}");
            sb.AppendLine($"Heap Total: {VmmapParser.FormatSize(_report.Heap.TotalBytes)} | " +
                          $"Unity Tracked: {VmmapParser.FormatSize(_report.Summary.TrackedByUnity)} | " +
                          $"Untracked: {VmmapParser.FormatSize(_report.Summary.UntrackedByUnity)}");

            if (_report.Summary.PluginBreakdowns.Count > 0) {
                sb.Append("Plugins: ");
                sb.AppendLine(string.Join(", ",
                    _report.Summary.PluginBreakdowns
                        .OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{kv.Key}={VmmapParser.FormatSize(kv.Value)}")));
            }

            sb.AppendLine($"Leaks: {_report.Leaks.TotalLeakCount} ({VmmapParser.FormatSize(_report.Leaks.TotalLeakBytes)})");
            sb.AppendLine($"Health: {_report.Summary.OverallHealth}");
            if (_report.Insights.Count > 0) {
                var top = _report.Insights[0];
                sb.AppendLine($"Top Issue: [{top.Severity}] {top.Title} - {top.Recommendation}");
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[MemGraphAnalyzer] Summary copied to clipboard.");
        }
    }
}
