using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tools {
    /// <summary>
    /// Advanced analysis builders: AllocationTrace, Unity, Comparison.
    /// </summary>
    internal static partial class AIExportMarkdownBuilder {

        // ------------------------------------------------------------------
        // 04_AllocationTrace — controllability assessment + user-actionable focus
        // ------------------------------------------------------------------

        public static string BuildAllocationTraceAnalysis(MemGraphReport report,
            List<TracedAllocation> filteredRows,
            AllocationCategory catFilter, Controllability ctrlFilter, AssetType assetFilter) {
            var trace = report.AllocationTrace;
            if (trace == null || trace.Allocations.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Allocation Trace Analysis", report));

            sb.AppendLine($"Total analyzed: {Fmt(trace.TotalAnalyzedBytes)} ({trace.TotalAnalyzedCount:N0} allocations)");
            sb.AppendLine();

            // Filters note
            var filters = new List<string>();
            if ((int)catFilter >= 0) filters.Add($"category={catFilter}");
            if ((int)ctrlFilter >= 0) filters.Add($"controllability={ctrlFilter}");
            if ((int)assetFilter >= 0) filters.Add($"assetType={assetFilter}");
            if (filters.Count > 0)
                sb.AppendLine($"*Filters applied: {string.Join(", ", filters)}*\n");

            // Controllability Assessment
            var source = filteredRows ?? trace.Allocations;

            var ctrlGroups = source
                .GroupBy(a => a.Controllability)
                .Select(g => new {
                    Level = g.Key,
                    Count = g.Count(),
                    Bytes = g.Sum(a => a.TotalBytes),
                })
                .OrderBy(g => (int)g.Level)
                .ToList();

            long totalBytes = ctrlGroups.Sum(g => g.Bytes);

            sb.AppendLine("## Controllability Assessment");
            sb.AppendLine("| Level | Count | Bytes | % | Action |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var g in ctrlGroups) {
                string action = g.Level switch {
                    Controllability.UserControllable => "Direct optimization possible",
                    Controllability.PartiallyControllable => "Settings/config changes",
                    Controllability.EngineOwned => "Monitor only",
                    Controllability.SystemOwned => "Not actionable",
                    _ => "?",
                };
                sb.AppendLine($"| {g.Level} | {g.Count} | {Fmt(g.Bytes)} | {Pct(g.Bytes, totalBytes)} | {action} |");
            }
            sb.AppendLine();

            // Category Breakdown
            if (trace.CategoryBreakdown.Count > 0) {
                sb.AppendLine("## Category Breakdown");
                sb.AppendLine("| Category | Size | % |");
                sb.AppendLine("|---|---|---|");
                foreach (var cat in trace.CategoryBreakdown) {
                    sb.AppendLine($"| {cat.Category} | {Fmt(cat.TotalBytes)} | {cat.Percentage:F1}% |");
                }
                sb.AppendLine();
            }

            // Priority: User-Controllable Allocations (top 15)
            var userControllable = source
                .Where(a => a.Controllability == Controllability.UserControllable)
                .OrderByDescending(a => a.TotalBytes)
                .Take(15)
                .ToList();

            if (userControllable.Count > 0) {
                sb.AppendLine($"## Priority: User-Controllable Allocations (top {userControllable.Count})");
                sb.AppendLine("| # | Calls | Bytes | Category | Asset | Function |");
                sb.AppendLine("|---|---|---|---|---|---|");
                for (int i = 0; i < userControllable.Count; i++) {
                    var a = userControllable[i];
                    string typeLabel = a.AssetType != AssetType.None
                        ? $"{a.Category}/{a.AssetType}" : a.Category.ToString();
                    string func = a.TopUserFunction
                        ?? (a.TopEngineFunction != null
                            ? CallTreeParser.FormatFunctionName(a.TopEngineFunction)
                            : "(unknown)");
                    sb.AppendLine($"| {i + 1} | {a.CallCount} | {Fmt(a.TotalBytes)} | {typeLabel} | {a.AssetType} | {TruncateName(func, 60)} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 05_Unity — tracked/untracked + plugin health + GPU/stacks
        // ------------------------------------------------------------------

        public static string BuildUnityAnalysis(MemGraphReport report) {
            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Unity-Specific Analysis", report));

            long heapTotal = report.Heap.TotalBytes;
            long tracked = report.Summary.TrackedByUnity;
            long untracked = report.Summary.UntrackedByUnity;

            // Tracking Assessment
            sb.AppendLine("## Tracking Assessment");
            float untrackedPct = heapTotal > 0 ? (float)untracked / heapTotal * 100f : 0;
            string assessment = untrackedPct > 50 ? "WARNING — High untracked ratio"
                : untrackedPct > 30 ? "MODERATE — Review untracked sources"
                : "GOOD";
            sb.AppendLine($"- Unity Tracked: {Fmt(tracked)} ({Pct(tracked, heapTotal)})");
            sb.AppendLine($"- Untracked: {Fmt(untracked)} ({Pct(untracked, heapTotal)})");
            sb.AppendLine($"- Assessment: **{assessment}**");
            sb.AppendLine();

            // Top Unity Allocations (top 15)
            var unityAllocs = report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.Unity)
                .OrderByDescending(a => a.TotalBytes)
                .Take(15)
                .ToList();
            if (unityAllocs.Count > 0) {
                sb.AppendLine("## Top Unity Allocations");
                sb.AppendLine("| Count | Bytes | Avg | Class |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var a in unityAllocs) {
                    sb.AppendLine($"| {a.Count:N0} | {Fmt(a.TotalBytes)} | {Fmt(a.AverageSize)} | {TruncateName(a.ClassName)} |");
                }
                sb.AppendLine();
            }

            // Plugin Health Assessment
            if (report.Summary.PluginBreakdowns.Count > 0) {
                long totalPluginBytes = report.Summary.PluginBreakdowns.Values.Sum();
                sb.AppendLine("## Native Plugin Health");
                sb.AppendLine($"Total plugin memory: {Fmt(totalPluginBytes)}");
                sb.AppendLine();
                sb.AppendLine("| Plugin | Size | Assessment |");
                sb.AppendLine("|---|---|---|");
                foreach (var kv in report.Summary.PluginBreakdowns.OrderByDescending(kv => kv.Value)) {
                    string pluginAssessment = kv.Value > 100L * 1024 * 1024 ? "CRITICAL — Investigate"
                        : kv.Value > 50L * 1024 * 1024 ? "WARNING — Monitor"
                        : "OK";
                    sb.AppendLine($"| {kv.Key} | {Fmt(kv.Value)} | {pluginAssessment} |");
                }
                sb.AppendLine();
            }

            // GPU/Graphics Regions
            var gpuRegions = report.Vmmap.Summary
                .Where(r => r.RegionType != null && (
                    r.RegionType.Contains("IOKit", StringComparison.OrdinalIgnoreCase) ||
                    r.RegionType.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                    r.RegionType.Contains("IOAccelerator", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(r => r.ResidentSize)
                .ToList();
            if (gpuRegions.Count > 0) {
                sb.AppendLine("## GPU/Graphics Regions");
                sb.AppendLine("| Region | Resident | Virtual |");
                sb.AppendLine("|---|---|---|");
                foreach (var r in gpuRegions) {
                    sb.AppendLine($"| {r.RegionType} | {Fmt(r.ResidentSize)} | {Fmt(r.VirtualSize)} |");
                }
                sb.AppendLine();
            }

            // Thread Stacks
            var stackRegion = report.Vmmap.Summary
                .FirstOrDefault(r => r.RegionType != null &&
                    r.RegionType.Contains("Stack", StringComparison.OrdinalIgnoreCase));
            if (stackRegion != null) {
                sb.AppendLine("## Thread Stacks");
                sb.AppendLine($"- Resident: {Fmt(stackRegion.ResidentSize)}");
                sb.AppendLine($"- Virtual: {Fmt(stackRegion.VirtualSize)}");
                sb.AppendLine($"- Count: {stackRegion.RegionCount}");
                if (stackRegion.RegionCount > 50)
                    sb.AppendLine($"- **WARNING**: Excessive threads ({stackRegion.RegionCount} > 50)");
                sb.AppendLine();
            }

            // UnsafeUtility Allocations (top 10)
            var unsafeAllocs = report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.UnsafeUtility)
                .OrderByDescending(a => a.TotalBytes)
                .Take(10)
                .ToList();
            if (unsafeAllocs.Count > 0) {
                sb.AppendLine("## UnsafeUtility Allocations");
                sb.AppendLine("| Count | Bytes | Class |");
                sb.AppendLine("|---|---|---|");
                foreach (var a in unsafeAllocs) {
                    sb.AppendLine($"| {a.Count:N0} | {Fmt(a.TotalBytes)} | {TruncateName(a.ClassName)} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 07_Comparison — diff analysis + worsened/improved summary
        // ------------------------------------------------------------------

        public static string BuildComparisonAnalysis(MemGraphReport report, MemGraphDiffResult diff) {
            if (diff == null) return "";

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Comparison Diff Analysis", report));

            sb.AppendLine($"- Baseline: {Path.GetFileName(diff.Baseline?.FilePath ?? "?")}");
            sb.AppendLine($"- Current: {Path.GetFileName(diff.Current?.FilePath ?? "?")}");
            sb.AppendLine();

            // Worsened / Improved Summary
            var d = diff.Overall;
            sb.AppendLine("## Change Summary");

            var worsened = new List<string>();
            var improved = new List<string>();

            if (d.FootprintDelta > 0) worsened.Add($"Footprint +{Fmt(d.FootprintDelta)} ({d.FootprintDeltaPercent:+0.0;-0.0}%)");
            else if (d.FootprintDelta < 0) improved.Add($"Footprint {Fmt(d.FootprintDelta)} ({d.FootprintDeltaPercent:+0.0;-0.0}%)");

            if (d.HeapDelta > 0) worsened.Add($"Heap +{Fmt(d.HeapDelta)} ({d.HeapDeltaPercent:+0.0;-0.0}%)");
            else if (d.HeapDelta < 0) improved.Add($"Heap {Fmt(d.HeapDelta)} ({d.HeapDeltaPercent:+0.0;-0.0}%)");

            if (d.ResidentDelta > 0) worsened.Add($"Resident +{Fmt(d.ResidentDelta)} ({d.ResidentDeltaPercent:+0.0;-0.0}%)");
            else if (d.ResidentDelta < 0) improved.Add($"Resident {Fmt(d.ResidentDelta)} ({d.ResidentDeltaPercent:+0.0;-0.0}%)");

            var ld = diff.Leaks;
            if (ld.CountDelta > 0) worsened.Add($"Leaks +{ld.CountDelta} ({SignedNum(ld.CountDelta)})");
            else if (ld.CountDelta < 0) improved.Add($"Leaks {ld.CountDelta}");

            if (worsened.Count > 0) {
                sb.AppendLine("**Worsened:**");
                foreach (var w in worsened) sb.AppendLine($"- {w}");
            }
            if (improved.Count > 0) {
                sb.AppendLine("**Improved:**");
                foreach (var im in improved) sb.AppendLine($"- {im}");
            }
            if (worsened.Count == 0 && improved.Count == 0)
                sb.AppendLine("No significant changes detected.");
            sb.AppendLine();

            // Overall Metrics
            sb.AppendLine("## Overall Metrics");
            sb.AppendLine("| Metric | Baseline | Current | Delta | % |");
            sb.AppendLine("|---|---|---|---|---|");
            AppendDiffRow(sb, "Phys Footprint", d.BaselineFootprint, d.CurrentFootprint, d.FootprintDelta, d.FootprintDeltaPercent);
            AppendDiffRow(sb, "Heap Total", d.BaselineHeapTotal, d.CurrentHeapTotal, d.HeapDelta, d.HeapDeltaPercent);
            AppendDiffRow(sb, "Resident", d.BaselineResident, d.CurrentResident, d.ResidentDelta, d.ResidentDeltaPercent);
            AppendDiffRow(sb, "Dirty", d.BaselineDirty, d.CurrentDirty, d.DirtyDelta, d.DirtyDeltaPercent);
            AppendDiffRow(sb, "Virtual", d.BaselineVirtual, d.CurrentVirtual, d.VirtualDelta, d.VirtualDeltaPercent);
            sb.AppendLine();

            sb.AppendLine($"Leaks: {ld.BaselineCount} -> {ld.CurrentCount} (delta: {SignedNum(ld.CountDelta)})");
            sb.AppendLine();

            // Heap Allocation Changes
            var heap = diff.Heap;
            sb.AppendLine("## Heap Allocation Changes");
            sb.AppendLine($"- New classes: {heap.NewClassCount}");
            sb.AppendLine($"- Removed: {heap.RemovedClassCount}");
            sb.AppendLine($"- Increased: {heap.IncreasedClassCount}");
            sb.AppendLine($"- Decreased: {heap.DecreasedClassCount}");
            sb.AppendLine($"- Total growth: +{Fmt(heap.TotalGrowth)}");
            sb.AppendLine($"- Total shrink: -{Fmt(heap.TotalShrink)}");
            sb.AppendLine();

            // Top Changed Allocations (max 30)
            int count = Math.Min(heap.Allocations.Count, 30);
            var sorted = heap.Allocations
                .OrderByDescending(a => Math.Abs(a.BytesDelta))
                .Take(count)
                .ToList();

            if (sorted.Count > 0) {
                sb.AppendLine($"## Top {count} Changed Allocations (by |delta|)");
                sb.AppendLine("| Class | Baseline | Current | Delta | % | Owner |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var a in sorted) {
                    string prefix = a.BytesDelta > 0 ? "+" : "";
                    string pctPre = a.BytesDeltaPercent > 0 ? "+" : "";
                    sb.AppendLine(
                        $"| {TruncateName(a.ClassName)} | {Fmt(a.BaselineBytes)} | {Fmt(a.CurrentBytes)} " +
                        $"| {prefix}{Fmt(a.BytesDelta)} | {pctPre}{a.BytesDeltaPercent:F1}% " +
                        $"| {HeapParser.GetOwnerDisplayName(a.Owner)} |");
                }
                sb.AppendLine();
            }

            // Owner Breakdown Diff
            if (diff.OwnerDiffs.Count > 0) {
                sb.AppendLine("## Owner Breakdown Diff");
                sb.AppendLine("| Owner | Baseline | Current | Delta |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var od in diff.OwnerDiffs.OrderByDescending(x => Math.Abs(x.BytesDelta))) {
                    string prefix = od.BytesDelta > 0 ? "+" : "";
                    sb.AppendLine(
                        $"| {HeapParser.GetOwnerDisplayName(od.Key)} | {Fmt(od.BaselineBytes)} " +
                        $"| {Fmt(od.CurrentBytes)} | {prefix}{Fmt(od.BytesDelta)} |");
                }
                sb.AppendLine();
            }

            // vmmap Region Diff (top 15)
            var regionDiffs = diff.Vmmap.Regions
                .Where(r => r.ResidentDelta != 0 || r.BaselineResident != 0)
                .OrderByDescending(r => Math.Abs(r.ResidentDelta))
                .Take(15)
                .ToList();
            if (regionDiffs.Count > 0) {
                sb.AppendLine("## vmmap Region Diff (top 15)");
                sb.AppendLine("| Region | Baseline Res. | Current Res. | Delta |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var r in regionDiffs) {
                    string prefix = r.ResidentDelta > 0 ? "+" : "";
                    sb.AppendLine(
                        $"| {r.RegionType} | {Fmt(r.BaselineResident)} " +
                        $"| {Fmt(r.CurrentResident)} | {prefix}{Fmt(r.ResidentDelta)} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
