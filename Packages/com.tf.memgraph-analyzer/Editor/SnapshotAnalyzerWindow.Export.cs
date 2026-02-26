using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private void DrawSnapshotExportButtons() {
            if (GUILayout.Button(AnalyzerGuidance.ExportText, EditorStyles.toolbarButton, GUILayout.Width(75))) {
                ExportSnapshotTextReport();
            }
            if (GUILayout.Button(AnalyzerGuidance.ExportCsv, EditorStyles.toolbarButton, GUILayout.Width(70))) {
                ExportSnapshotCsv();
            }
            if (GUILayout.Button(AnalyzerGuidance.CopySummary, EditorStyles.toolbarButton, GUILayout.Width(90))) {
                CopySnapshotSummaryToClipboard();
            }
        }

        private void ExportSnapshotTextReport() {
            var defaultName = $"snapshot_report_{_report.AnalysisTime:yyyyMMdd_HHmmss}.txt";
            var path = EditorUtility.SaveFilePanel("Export Snapshot Report", "", defaultName, "txt");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new StringBuilder();
                sb.AppendLine("=== Unity Memory Snapshot Analysis Report ===");
                sb.AppendLine($"File: {_report.FilePath}");
                sb.AppendLine($"Analysis Time: {_report.AnalysisTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Mode: {(_report.SkipCrawl ? "Quick" : "Full")}");
                sb.AppendLine();

                // VM Info
                sb.AppendLine("--- Virtual Machine ---");
                sb.AppendLine($"Pointer Size: {_report.PointerSize} bytes");
                sb.AppendLine($"Object Header: {_report.ObjectHeaderSize} bytes");
                sb.AppendLine();

                // Counts
                sb.AppendLine("--- Counts ---");
                sb.AppendLine($"Types:              {_report.TypeCount:N0}");
                sb.AppendLine($"Fields:             {_report.FieldCount:N0}");
                sb.AppendLine($"Native Objects:     {_report.NativeObjectCount:N0}");
                sb.AppendLine($"GC Handles:         {_report.GcHandleCount:N0}");
                sb.AppendLine($"Managed Heap Sections: {_report.ManagedHeapSectionCount:N0}");
                sb.AppendLine($"Connections:        {_report.ConnectionCount:N0}");
                if (_report.CrawlerResult != null)
                    sb.AppendLine($"Crawled Objects:    {_report.CrawlerResult.TotalCrawled:N0}");
                sb.AppendLine();

                // Memory
                sb.AppendLine("--- Memory Summary ---");
                sb.AppendLine($"Native Total:       {VmmapParser.FormatSize(_report.Summary.TotalNativeSize)}");
                sb.AppendLine($"Managed Heap Total: {VmmapParser.FormatSize(_report.Summary.TotalManagedHeapSize)}");
                sb.AppendLine();

                // Classification
                sb.AppendLine("--- Assembly Classification ---");
                foreach (var kv in _report.Summary.SizeByClassification.OrderByDescending(kv => kv.Value)) {
                    int typeCount = 0;
                    _report.Summary.TypeCountByClassification.TryGetValue(kv.Key, out typeCount);
                    sb.AppendLine($"  {SnapshotLoader.GetClassificationDisplayName(kv.Key),-20} " +
                                 $"{VmmapParser.FormatSize(kv.Value),12} ({typeCount} types)");
                }
                sb.AppendLine();

                // Assemblies
                sb.AppendLine("--- Assemblies (by size) ---");
                sb.AppendLine($"{"Assembly",-40} {"Size",12} {"Types",8} {"Instances",10} {"Classification",-15}");
                foreach (var asm in _report.Assemblies) {
                    sb.AppendLine($"  {asm.Name,-38} {VmmapParser.FormatSize(asm.TotalSize),12} " +
                                 $"{asm.TypeCount,8} {asm.InstanceCount,10} " +
                                 $"{SnapshotLoader.GetClassificationDisplayName(asm.Classification),-15}");
                }
                sb.AppendLine();

                // Native type summaries
                sb.AppendLine("--- Native Types (by size) ---");
                sb.AppendLine($"{"Type",-30} {"Count",8} {"Total Size",12}");
                foreach (var summary in _report.NativeTypeSummaries.Take(30)) {
                    sb.AppendLine($"  {summary.TypeName,-28} {summary.ObjectCount,8} " +
                                 $"{VmmapParser.FormatSize(summary.TotalSize),12}");
                }
                sb.AppendLine();

                // Top managed types
                if (_report.CrawlerResult != null) {
                    sb.AppendLine("--- Top 30 Managed Types (by instance size) ---");
                    sb.AppendLine($"{"Type",-40} {"Instances",10} {"Total Size",12} {"Assembly",-20}");
                    var topTypes = _report.Types
                        .Where(t => t.InstanceCount > 0)
                        .OrderByDescending(t => t.TotalInstanceSize)
                        .Take(30);
                    foreach (var t in topTypes) {
                        sb.AppendLine($"  {t.Name,-38} {t.InstanceCount,10} " +
                                     $"{VmmapParser.FormatSize(t.TotalInstanceSize),12} {t.Assembly,-20}");
                    }
                }

                // Native-Managed Link Summary
                if (_report.LinkResult != null) {
                    sb.AppendLine();
                    sb.AppendLine("--- Native-Managed Link Summary ---");
                    sb.AppendLine($"Linked:     {_report.LinkResult.LinkedCount:N0}");
                    sb.AppendLine($"Unlinked:   {_report.LinkResult.UnlinkedNativeCount:N0}");
                    sb.AppendLine($"Total Links: {_report.LinkResult.Links.Count:N0}");
                }

                // Insights
                if (_report.InsightResult != null && _report.InsightResult.Insights.Count > 0) {
                    sb.AppendLine();
                    sb.AppendLine("--- Insights ---");
                    sb.AppendLine($"Total Estimated Savings: {VmmapParser.FormatSize(_report.InsightResult.TotalEstimatedSavings)}");
                    sb.AppendLine();
                    foreach (var insight in _report.InsightResult.Insights) {
                        string sev = insight.Severity.ToString().ToUpper();
                        sb.AppendLine($"  [{sev}] {insight.Title}");
                        sb.AppendLine($"    {insight.Description}");
                        sb.AppendLine($"    >> {insight.Recommendation}");
                        if (insight.EstimatedSavings > 0)
                            sb.AppendLine($"    Estimated savings: {VmmapParser.FormatSize(insight.EstimatedSavings)}");
                        sb.AppendLine();
                    }

                    // Duplicate Assets
                    if (_report.InsightResult.DuplicateAssets.Count > 0) {
                        sb.AppendLine("--- Duplicate Assets ---");
                        sb.AppendLine($"{"Name",-30} {"Type",-20} {"Count",6} {"Each",12} {"Wasted",12}");
                        foreach (var dup in _report.InsightResult.DuplicateAssets.Take(20)) {
                            sb.AppendLine($"  {dup.Name,-28} {dup.NativeTypeName,-20} {dup.Count,6} " +
                                         $"{VmmapParser.FormatSize(dup.IndividualSize),12} " +
                                         $"{VmmapParser.FormatSize(dup.TotalWastedSize),12}");
                        }
                        sb.AppendLine();
                    }
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[SnapshotAnalyzer] Report exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private void ExportSnapshotCsv() {
            var defaultName = $"snapshot_assemblies_{_report.AnalysisTime:yyyyMMdd_HHmmss}.csv";
            var path = EditorUtility.SaveFilePanel("Export CSV", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new StringBuilder();
                sb.AppendLine("Assembly,Classification,Size,TypeCount,InstanceCount,Namespace,TypeName,TypeSize,TypeInstances");

                foreach (var asm in _report.Assemblies) {
                    foreach (var ns in asm.Namespaces) {
                        foreach (var t in ns.Types) {
                            long size = t.TotalInstanceSize > 0 ? t.TotalInstanceSize : t.BaseSize;
                            sb.AppendLine(
                                $"\"{asm.Name}\"," +
                                $"{SnapshotLoader.GetClassificationDisplayName(asm.Classification)}," +
                                $"{asm.TotalSize}," +
                                $"{asm.TypeCount}," +
                                $"{asm.InstanceCount}," +
                                $"\"{ns.Name}\"," +
                                $"\"{t.Name}\"," +
                                $"{size}," +
                                $"{t.InstanceCount}");
                        }
                    }
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[SnapshotAnalyzer] CSV exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private void CopySnapshotSummaryToClipboard() {
            var sb = new StringBuilder();
            sb.AppendLine($"Snapshot: {Path.GetFileName(_report.FilePath)}");
            sb.AppendLine($"Native: {VmmapParser.FormatSize(_report.Summary.TotalNativeSize)} | " +
                          $"Managed Heap: {VmmapParser.FormatSize(_report.Summary.TotalManagedHeapSize)}");
            sb.AppendLine($"Types: {_report.TypeCount:N0} | " +
                          $"Native Objects: {_report.NativeObjectCount:N0} | " +
                          $"GC Handles: {_report.GcHandleCount:N0}");

            if (_report.CrawlerResult != null) {
                sb.AppendLine($"Crawled Objects: {_report.CrawlerResult.TotalCrawled:N0}");
            }

            // Classification breakdown
            foreach (var kv in _report.Summary.SizeByClassification.OrderByDescending(kv => kv.Value)) {
                sb.AppendLine($"  {SnapshotLoader.GetClassificationDisplayName(kv.Key)}: {VmmapParser.FormatSize(kv.Value)}");
            }

            // Top native types
            sb.AppendLine("Top Native Types:");
            foreach (var summary in _report.NativeTypeSummaries.Take(5)) {
                sb.AppendLine($"  {summary.TypeName}: {VmmapParser.FormatSize(summary.TotalSize)} ({summary.ObjectCount})");
            }

            // Top insights
            if (_report.InsightResult != null && _report.InsightResult.Insights.Count > 0) {
                sb.AppendLine("Top Issues:");
                foreach (var insight in _report.InsightResult.Insights.Take(3)) {
                    string sev = insight.Severity.ToString().ToUpper();
                    sb.AppendLine($"  [{sev}] {insight.Title} (~{VmmapParser.FormatSize(insight.EstimatedSavings)})");
                }
            }

            // Link summary
            if (_report.LinkResult != null) {
                sb.AppendLine($"Native-Managed Links: {_report.LinkResult.LinkedCount:N0} linked, " +
                             $"{_report.LinkResult.UnlinkedNativeCount:N0} unlinked");
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[SnapshotAnalyzer] Summary copied to clipboard.");
        }
    }
}
