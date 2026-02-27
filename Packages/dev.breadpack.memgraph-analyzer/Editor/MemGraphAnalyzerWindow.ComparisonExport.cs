using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private void ExportDiffReport() {
            if (_diffResult == null) return;

            var defaultName = $"memgraph_diff_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var path = EditorUtility.SaveFilePanel("Export Diff Report", "", defaultName, "txt");
            if (string.IsNullOrEmpty(path)) return;

            try {
                var sb = new StringBuilder();
                sb.AppendLine("=== MemGraph Diff Report ===");
                sb.AppendLine($"Baseline: {_diffResult.Baseline?.FilePath}");
                sb.AppendLine($"Current:  {_diffResult.Current?.FilePath}");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                // Overall diff
                var d = _diffResult.Overall;
                sb.AppendLine("--- Overall Comparison ---");
                sb.AppendLine($"{"Metric",-20} {"Baseline",12} {"Current",12} {"Delta",12} {"Pct",8}");
                AppendDiffRow(sb, "Phys Footprint", d.BaselineFootprint, d.CurrentFootprint, d.FootprintDelta, d.FootprintDeltaPercent);
                AppendDiffRow(sb, "Heap Total", d.BaselineHeapTotal, d.CurrentHeapTotal, d.HeapDelta, d.HeapDeltaPercent);
                AppendDiffRow(sb, "Resident", d.BaselineResident, d.CurrentResident, d.ResidentDelta, d.ResidentDeltaPercent);
                AppendDiffRow(sb, "Dirty", d.BaselineDirty, d.CurrentDirty, d.DirtyDelta, d.DirtyDeltaPercent);
                AppendDiffRow(sb, "Virtual", d.BaselineVirtual, d.CurrentVirtual, d.VirtualDelta, d.VirtualDeltaPercent);
                sb.AppendLine();

                // Leak diff
                var ld = _diffResult.Leaks;
                sb.AppendLine($"Leaks: {ld.BaselineCount} -> {ld.CurrentCount} (delta: {ld.CountDelta:+#;-#;0})");
                sb.AppendLine($"Leak Bytes: {VmmapParser.FormatSize(ld.BaselineBytes)} -> {VmmapParser.FormatSize(ld.CurrentBytes)}");
                sb.AppendLine();

                // Heap diff
                var heap = _diffResult.Heap;
                sb.AppendLine("--- Heap Allocation Diff ---");
                sb.AppendLine($"New: {heap.NewClassCount} | Removed: {heap.RemovedClassCount} | " +
                              $"Increased: {heap.IncreasedClassCount} | Decreased: {heap.DecreasedClassCount}");
                sb.AppendLine($"Total Growth: +{VmmapParser.FormatSize(heap.TotalGrowth)} | " +
                              $"Total Shrink: -{VmmapParser.FormatSize(heap.TotalShrink)}");
                sb.AppendLine();

                sb.AppendLine($"{"ClassName",-40} {"Baseline",12} {"Current",12} {"Delta",12} {"Pct",8} {"Owner",-16}");
                var sorted = heap.Allocations
                    .OrderByDescending(a => Math.Abs(a.BytesDelta))
                    .Take(100);
                foreach (var alloc in sorted) {
                    string prefix = alloc.BytesDelta > 0 ? "+" : "";
                    string pctPre = alloc.BytesDeltaPercent > 0 ? "+" : "";
                    sb.AppendLine(
                        $"  {(alloc.ClassName ?? ""),-38} " +
                        $"{VmmapParser.FormatSize(alloc.BaselineBytes),12} " +
                        $"{VmmapParser.FormatSize(alloc.CurrentBytes),12} " +
                        $"{prefix + VmmapParser.FormatSize(alloc.BytesDelta),12} " +
                        $"{pctPre}{alloc.BytesDeltaPercent:F1}%,8 " +
                        $"{HeapParser.GetOwnerDisplayName(alloc.Owner),-16}");
                }
                sb.AppendLine();

                // Owner diff
                if (_diffResult.OwnerDiffs.Count > 0) {
                    sb.AppendLine("--- Owner Breakdown Diff ---");
                    foreach (var od in _diffResult.OwnerDiffs.OrderByDescending(x => Math.Abs(x.BytesDelta))) {
                        string prefix = od.BytesDelta > 0 ? "+" : "";
                        sb.AppendLine(
                            $"  {HeapParser.GetOwnerDisplayName(od.Key),-20} " +
                            $"{VmmapParser.FormatSize(od.BaselineBytes),12} -> " +
                            $"{VmmapParser.FormatSize(od.CurrentBytes),12} " +
                            $"({prefix}{VmmapParser.FormatSize(od.BytesDelta)})");
                    }
                    sb.AppendLine();
                }

                // Plugin diff
                if (_diffResult.PluginDiffs.Count > 0) {
                    sb.AppendLine("--- Plugin Breakdown Diff ---");
                    foreach (var pd in _diffResult.PluginDiffs.OrderByDescending(x => Math.Abs(x.BytesDelta))) {
                        string prefix = pd.BytesDelta > 0 ? "+" : "";
                        sb.AppendLine(
                            $"  {pd.Key,-20} " +
                            $"{VmmapParser.FormatSize(pd.BaselineBytes),12} -> " +
                            $"{VmmapParser.FormatSize(pd.CurrentBytes),12} " +
                            $"({prefix}{VmmapParser.FormatSize(pd.BytesDelta)})");
                    }
                    sb.AppendLine();
                }

                // Vmmap diff
                sb.AppendLine("--- Virtual Memory Region Diff ---");
                foreach (var region in _diffResult.Vmmap.Regions.OrderByDescending(r => Math.Abs(r.ResidentDelta))) {
                    if (region.ResidentDelta == 0 && region.BaselineResident == 0) continue;
                    string prefix = region.ResidentDelta > 0 ? "+" : "";
                    sb.AppendLine(
                        $"  {(region.RegionType ?? ""),-28} " +
                        $"Res: {VmmapParser.FormatSize(region.BaselineResident),10} -> " +
                        $"{VmmapParser.FormatSize(region.CurrentResident),10} " +
                        $"({prefix}{VmmapParser.FormatSize(region.ResidentDelta)})");
                }

                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[MemGraphAnalyzer] Diff report exported to: {path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex) {
                EditorUtility.DisplayDialog("Export Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        private static void AppendDiffRow(StringBuilder sb, string label,
            long baseline, long current, long delta, float pct) {
            string prefix = delta > 0 ? "+" : "";
            string pctPre = pct > 0 ? "+" : "";
            sb.AppendLine(
                $"  {label,-18} {VmmapParser.FormatSize(baseline),12} " +
                $"{VmmapParser.FormatSize(current),12} " +
                $"{prefix + VmmapParser.FormatSize(delta),12} " +
                $"{pctPre}{pct:F1}%");
        }
    }
}
