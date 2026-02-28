using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tools {
    /// <summary>
    /// Analysis-first Markdown builder for AI agent export.
    /// Each method produces a self-contained Detail document (~1.5-2K tokens).
    /// No GUI dependencies — all methods are static and return Markdown strings.
    /// Split into partial: this file has core sections + helpers.
    /// </summary>
    internal static partial class AIExportMarkdownBuilder {

        internal static string BuildDetailHeader(string title, MemGraphReport report) {
            var sb = new StringBuilder();
            sb.AppendLine($"# MemGraph Detail: {title}");
            sb.AppendLine($"*Source: {Path.GetFileName(report.FilePath)} | {report.AnalysisTime:yyyy-MM-dd HH:mm}*");
            sb.AppendLine();
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 01_Optimizations — kept mostly as-is (already analysis-first)
        // ------------------------------------------------------------------

        public static string BuildOptimizations(MemGraphReport report) {
            var opts = report.Optimizations;
            if (opts == null || opts.Recommendations.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Optimization Recommendations", report));
            sb.AppendLine($"Total estimated savings: ~{Fmt(opts.TotalEstimatedSavings)}");
            sb.AppendLine($"Recommendations: {opts.Recommendations.Count}");
            sb.AppendLine();

            for (int i = 0; i < opts.Recommendations.Count; i++) {
                var rec = opts.Recommendations[i];
                sb.AppendLine($"### {i + 1}. [{rec.Difficulty}] {rec.Title} (~{Fmt(rec.EstimatedSavings)})");
                sb.AppendLine(rec.Description);
                if (rec.ActionSteps.Count > 0) {
                    foreach (var step in rec.ActionSteps) {
                        sb.AppendLine($"- {step}");
                    }
                }
                if (rec.RelatedAllocations.Count > 0) {
                    sb.AppendLine($"- Related: {string.Join(", ", rec.RelatedAllocations)}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 02_Heap — pattern analysis + compressed evidence table
        // ------------------------------------------------------------------

        public static string BuildHeapAnalysis(MemGraphReport report, List<HeapAllocation> filteredRows,
            string heapFilter, int heapOwnerFilter) {
            var allocs = filteredRows ?? report.Heap.Allocations;
            if (allocs == null || allocs.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Heap Analysis", report));

            long heapTotal = report.Heap.TotalBytes;

            // Filters note
            var filters = new List<string>();
            if (!string.IsNullOrEmpty(heapFilter)) filters.Add($"search=\"{heapFilter}\"");
            if (heapOwnerFilter > 0) filters.Add($"owner={GetOwnerFilterName(heapOwnerFilter)}");
            if (filters.Count > 0)
                sb.AppendLine($"*Filters applied: {string.Join(", ", filters)}*\n");

            sb.AppendLine("## Detected Patterns");
            sb.AppendLine();

            int patternNum = 0;

            // Pattern 1: Large Allocations (avg > 10 MB)
            var largeAvg = allocs
                .Where(a => a.AverageSize > 10L * 1024 * 1024)
                .OrderByDescending(a => a.TotalBytes)
                .Take(5)
                .ToList();
            if (largeAvg.Count > 0) {
                long totalImpact = largeAvg.Sum(a => a.TotalBytes);
                patternNum++;
                sb.AppendLine($"### {patternNum}. Large Allocations (avg > 10 MB)");
                sb.AppendLine($"{largeAvg.Count} types with abnormally large average allocation size:");
                sb.AppendLine("| Class | Avg Size | Total | Count | Owner |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var a in largeAvg) {
                    sb.AppendLine($"| {a.ClassName} | {Fmt(a.AverageSize)} | {Fmt(a.TotalBytes)} | {a.Count:N0} | {HeapParser.GetOwnerDisplayName(a.Owner)} |");
                }
                sb.AppendLine($"**Impact**: ~{Fmt(totalImpact)} in oversized allocations");
                sb.AppendLine("**Suggestion**: Review import settings, use streaming for large assets");
                sb.AppendLine();
            }

            // Pattern 2: Pooling Candidates (>1000 allocs, <1KB avg, >1MB total)
            var poolCandidates = allocs
                .Where(a => a.Count > 1000 && a.AverageSize < 1024 && a.TotalBytes > 1024 * 1024)
                .OrderByDescending(a => a.TotalBytes)
                .Take(5)
                .ToList();
            if (poolCandidates.Count > 0) {
                long totalImpact = poolCandidates.Sum(a => a.TotalBytes);
                patternNum++;
                sb.AppendLine($"### {patternNum}. Pooling Candidates (>1000 allocs, <1KB avg, >1MB total)");
                sb.AppendLine($"{poolCandidates.Count} types suitable for object pooling:");
                sb.AppendLine("| Class | Count | Avg | Total | Owner |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var a in poolCandidates) {
                    sb.AppendLine($"| {a.ClassName} | {a.Count:N0} | {Fmt(a.AverageSize)} | {Fmt(a.TotalBytes)} | {HeapParser.GetOwnerDisplayName(a.Owner)} |");
                }
                sb.AppendLine($"**Impact**: ~{Fmt(totalImpact)} in high-churn small allocations");
                sb.AppendLine("**Suggestion**: Implement object pooling for these types");
                sb.AppendLine();
            }

            // Pattern 3: Type Fragmentation
            int singleCount = allocs.Count(a => a.Count == 1);
            float fragRatio = allocs.Count > 0 ? (float)singleCount / allocs.Count * 100f : 0;
            if (fragRatio > 50f) {
                patternNum++;
                sb.AppendLine($"### {patternNum}. Type Fragmentation");
                sb.AppendLine($"{fragRatio:F0}% of heap types ({singleCount}/{allocs.Count}) have exactly 1 allocation (high type diversity).");
                sb.AppendLine("**Assessment**: May indicate over-allocation or excessive specialization.");
                sb.AppendLine();
            }

            if (patternNum == 0) {
                sb.AppendLine("No suspicious patterns detected in the current allocation data.");
                sb.AppendLine();
            }

            // Owner Breakdown
            var ownerGroups = allocs
                .GroupBy(a => a.Owner)
                .Select(g => new { Owner = g.Key, Size = g.Sum(a => a.TotalBytes) })
                .OrderByDescending(g => g.Size)
                .ToList();
            if (ownerGroups.Count > 0) {
                sb.AppendLine("## Owner Breakdown");
                sb.AppendLine("| Owner | Size | % | Actionability |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var g in ownerGroups) {
                    string actionability = GetOwnerActionability(g.Owner);
                    sb.AppendLine($"| {HeapParser.GetOwnerDisplayName(g.Owner)} | {Fmt(g.Size)} | {Pct(g.Size, heapTotal)} | {actionability} |");
                }
                sb.AppendLine();
            }

            // Related Issues
            AppendRelatedIssues(sb, report, InsightCategory.MemoryPressure, InsightCategory.Untracked, InsightCategory.Fragmentation);

            // Top Allocations (evidence, top 15)
            int evidenceCount = Math.Min(allocs.Count, 15);
            sb.AppendLine($"## Top Allocations (evidence, top {evidenceCount})");
            sb.AppendLine("| # | Count | Bytes | Avg | Class | Owner |");
            sb.AppendLine("|---|---|---|---|---|---|");
            for (int i = 0; i < evidenceCount; i++) {
                var a = allocs[i];
                sb.AppendLine($"| {i + 1} | {a.Count:N0} | {Fmt(a.TotalBytes)} | {Fmt(a.AverageSize)} | {a.ClassName} | {HeapParser.GetOwnerDisplayName(a.Owner)} |");
            }
            sb.AppendLine();

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 03_Leaks — group analysis + remediation guide
        // ------------------------------------------------------------------

        public static string BuildLeakAnalysis(MemGraphReport report) {
            if (report.Leaks == null || report.Leaks.TotalLeakCount == 0) {
                return "# MemGraph Detail: Leak Analysis\nNo leaks detected.\n\n";
            }

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Leak Analysis", report));

            var leaks = report.Leaks;
            var groups = LeaksParser.GroupLeaks(leaks);

            sb.AppendLine("## Summary");
            sb.AppendLine($"Total: {leaks.TotalLeakCount} leaks, {Fmt(leaks.TotalLeakBytes)} | {groups.Count} groups by type");
            sb.AppendLine();

            // Leak Groups by severity (max 20)
            int groupCount = Math.Min(groups.Count, 20);
            sb.AppendLine("## Leak Groups (by severity)");
            sb.AppendLine("| Severity | Type/Zone | Count | Total | Owner |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < groupCount; i++) {
                var g = groups[i];
                sb.AppendLine($"| {g.Severity.ToString().ToUpper()} | {g.TypeOrZone} | {g.Entries.Count} | {Fmt(g.TotalBytes)} | {HeapParser.GetOwnerDisplayName(g.Owner)} |");
            }
            if (groups.Count > groupCount)
                sb.AppendLine($"*...and {groups.Count - groupCount} more groups*");
            sb.AppendLine();

            // Remediation by Owner
            var ownerGroups = groups
                .GroupBy(g => g.Owner)
                .Select(og => new {
                    Owner = og.Key,
                    LeakCount = og.Sum(g => g.Entries.Count),
                    TotalBytes = og.Sum(g => g.TotalBytes),
                    Groups = og.ToList(),
                })
                .OrderByDescending(o => o.TotalBytes)
                .ToList();

            sb.AppendLine("## Remediation by Owner");
            sb.AppendLine();
            foreach (var owner in ownerGroups) {
                sb.AppendLine($"### {HeapParser.GetOwnerDisplayName(owner.Owner)} ({owner.LeakCount} leaks, {Fmt(owner.TotalBytes)})");
                switch (owner.Owner) {
                    case MemoryOwner.Unity:
                        sb.AppendLine("- Check unreleased textures/meshes");
                        sb.AppendLine("- Verify Destroy() <-> Instantiate() pairing");
                        sb.AppendLine("- Review AssetBundle unloading");
                        break;
                    case MemoryOwner.NativePlugin:
                        sb.AppendLine("- Report to plugin vendor with leak details");
                        sb.AppendLine("- Check plugin version updates");
                        sb.AppendLine("- Verify plugin cleanup on scene transitions");
                        break;
                    case MemoryOwner.UnsafeUtility:
                        sb.AppendLine("- Verify UnsafeUtility.Malloc/Free pairs");
                        sb.AppendLine("- Check NativeArray/NativeList disposal");
                        sb.AppendLine("- Use safety checks in development builds");
                        break;
                    case MemoryOwner.GraphicsDriver:
                        sb.AppendLine("- Review render target lifecycle");
                        sb.AppendLine("- Check GPU resource disposal on scene changes");
                        break;
                    default:
                        sb.AppendLine("- Investigate allocation origin via malloc_history");
                        sb.AppendLine("- Check for missing Dispose() calls");
                        break;
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 06_Vmmap — region analysis + anomaly detection
        // ------------------------------------------------------------------

        public static string BuildVmmapAnalysis(MemGraphReport report) {
            var vmmap = report.Vmmap;
            if (vmmap == null || vmmap.Summary.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append(BuildDetailHeader("Virtual Memory Analysis", report));

            // Anomaly Detection
            sb.AppendLine("## Anomaly Detection");
            sb.AppendLine();
            int anomalyCount = 0;

            // GPU excessive
            long gpuResident = 0;
            foreach (var row in vmmap.Summary) {
                var upper = (row.RegionType ?? "").ToUpperInvariant();
                if (upper.Contains("IOKIT") || upper.Contains("IOSURFACE") || upper.Contains("GPU") || upper.Contains("IOACCELERATOR"))
                    gpuResident += row.ResidentSize;
            }
            if (gpuResident > 200L * 1024 * 1024) {
                anomalyCount++;
                sb.AppendLine($"- **GPU Memory Excessive**: {Fmt(gpuResident)} in GPU-related regions (threshold: 200 MB)");
            }

            // Stack excessive
            var stackRegion = vmmap.Summary.FirstOrDefault(r =>
                r.RegionType != null && r.RegionType.Contains("Stack", StringComparison.OrdinalIgnoreCase));
            if (stackRegion != null && stackRegion.RegionCount > 50) {
                anomalyCount++;
                sb.AppendLine($"- **Excessive Thread Stacks**: {stackRegion.RegionCount} stack regions (threshold: 50)");
            }

            // Large virtual vs resident gap
            if (vmmap.Total != null && vmmap.Total.VirtualSize > 0) {
                float residentRatio = (float)vmmap.Total.ResidentSize / vmmap.Total.VirtualSize;
                if (residentRatio < 0.3f) {
                    anomalyCount++;
                    sb.AppendLine($"- **Low Resident Ratio**: {residentRatio * 100:F0}% of virtual is resident (possible fragmentation)");
                }
            }

            if (anomalyCount == 0)
                sb.AppendLine("No anomalies detected.");
            sb.AppendLine();

            // Region Breakdown (top 15 by resident)
            var sorted = vmmap.Summary.OrderByDescending(r => r.ResidentSize).Take(15).ToList();
            sb.AppendLine($"## Region Breakdown (top {sorted.Count} by resident)");
            sb.AppendLine("| Region Type | Virtual | Resident | Dirty | Swapped | Count |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var row in sorted) {
                sb.AppendLine(
                    $"| {row.RegionType} | {Fmt(row.VirtualSize)} | {Fmt(row.ResidentSize)} " +
                    $"| {Fmt(row.DirtySize)} | {Fmt(row.SwappedSize)} | {row.RegionCount} |");
            }
            if (vmmap.Total != null) {
                var t = vmmap.Total;
                sb.AppendLine(
                    $"| **TOTAL** | **{Fmt(t.VirtualSize)}** | **{Fmt(t.ResidentSize)}** " +
                    $"| **{Fmt(t.DirtySize)}** | **{Fmt(t.SwappedSize)}** | **{t.RegionCount}** |");
            }
            sb.AppendLine();

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Kept: BuildHealthInsights (reused by Guide)
        // ------------------------------------------------------------------

        public static string BuildHealthInsights(List<MemoryInsight> insights, int maxItems) {
            if (insights == null || insights.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("## Issues & Insights");
            sb.AppendLine("| Severity | Issue | Recommendation |");
            sb.AppendLine("|---|---|---|");
            int count = Math.Min(insights.Count, maxItems > 0 ? maxItems : insights.Count);
            for (int i = 0; i < count; i++) {
                var ins = insights[i];
                sb.AppendLine($"| {ins.Severity.ToString().ToUpper()} | {ins.Title} | {ins.Recommendation} |");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        #region Helpers

        internal static string Fmt(long bytes) => VmmapParser.FormatSize(bytes);

        internal static string Pct(long part, long total) {
            if (total <= 0) return "0%";
            return $"{(float)part / total * 100f:F1}%";
        }

        internal static string SignedNum(int value) {
            return value > 0 ? $"+{value}" : value.ToString();
        }

        internal static void AppendDiffRow(StringBuilder sb, string label,
            long baseline, long current, long delta, float pct) {
            string prefix = delta > 0 ? "+" : "";
            string pctPre = pct > 0 ? "+" : "";
            sb.AppendLine($"| {label} | {Fmt(baseline)} | {Fmt(current)} | {prefix}{Fmt(delta)} | {pctPre}{pct:F1}% |");
        }

        internal static string GetOwnerFilterName(int ownerFilter) {
            return ownerFilter switch {
                0 => "All",
                1 => "Unknown",
                2 => "Unity",
                3 => "Native Plugin",
                4 => "System",
                5 => "Thread Stack",
                6 => "Graphics/GPU",
                7 => "UnsafeUtility",
                _ => "All",
            };
        }

        internal static string GetOwnerActionability(MemoryOwner owner) {
            return owner switch {
                MemoryOwner.Unity => "Fixable",
                MemoryOwner.NativePlugin => "Fixable (report to vendor)",
                MemoryOwner.UnsafeUtility => "Fixable",
                MemoryOwner.System => "Not actionable",
                MemoryOwner.GraphicsDriver => "Monitor",
                MemoryOwner.ThreadStack => "Monitor",
                _ => "Investigate",
            };
        }

        private static InsightCategory MapDocumentToCategory(AIDocumentType type) {
            return type switch {
                AIDocumentType.Heap => InsightCategory.MemoryPressure,
                AIDocumentType.Leaks => InsightCategory.Leaks,
                AIDocumentType.Unity => InsightCategory.Untracked,
                AIDocumentType.Vmmap => InsightCategory.Graphics,
                _ => InsightCategory.MemoryPressure,
            };
        }

        internal static string GetRelatedDocumentName(InsightCategory category) {
            return category switch {
                InsightCategory.MemoryPressure => "02_Heap, 01_Optimizations",
                InsightCategory.Leaks          => "03_Leaks",
                InsightCategory.NativePlugin   => "05_Unity, 02_Heap",
                InsightCategory.UnsafeUtility  => "05_Unity",
                InsightCategory.Fragmentation  => "02_Heap",
                InsightCategory.Untracked      => "05_Unity, 02_Heap",
                InsightCategory.ThreadStack    => "06_Vmmap",
                InsightCategory.Graphics       => "06_Vmmap, 05_Unity",
                _ => "02_Heap",
            };
        }

        private static void AppendRelatedIssues(StringBuilder sb, MemGraphReport report,
            params InsightCategory[] categories) {
            if (report.Insights == null || report.Insights.Count == 0) return;

            var relevant = report.Insights
                .Where(i => categories.Contains(i.Category))
                .ToList();
            if (relevant.Count == 0) return;

            sb.AppendLine("## Related Issues");
            foreach (var ins in relevant) {
                sb.AppendLine($"- [{ins.Severity.ToString().ToUpper()}] {ins.Title}");
            }
            sb.AppendLine();
        }

        #endregion
    }
}
