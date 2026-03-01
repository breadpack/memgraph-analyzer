using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tools {
    /// <summary>
    /// Builds the Guide document (00_Guide.md) — a pure index/inventory.
    /// Contains no analysis content; only key metrics, issue→document mapping,
    /// document inventory, and recommended reading order.
    /// </summary>
    internal static class AIExportGuideBuilder {
        public static string Build(MemGraphReport report, AIDocumentInfo[] documents,
            MemGraphDiffResult diffResult) {
            var sb = new StringBuilder();

            // System prompt
            sb.AppendLine("You are reviewing a pre-analyzed Unity iOS memory snapshot.");
            sb.AppendLine("Each numbered document contains focused analysis findings.");
            sb.AppendLine("Read this guide first, then request specific documents as needed.");
            sb.AppendLine();

            // Title + meta
            sb.AppendLine("# MemGraph Analysis Guide");
            sb.AppendLine($"- File: {Path.GetFileName(report.FilePath)}");
            sb.AppendLine($"- Health: {report.Summary.OverallHealth}");
            sb.AppendLine($"- Analyzed: {report.AnalysisTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            // Key Metrics table
            BuildKeyMetrics(sb, report);

            // Issues → Document Map
            BuildIssueDocumentMap(sb, report);

            // Top 3 Optimizations (titles only + savings + document ref)
            BuildTopOptimizations(sb, report);

            // Document Inventory
            BuildDocumentInventory(sb, documents);

            // Recommended Reading Order
            BuildReadingOrder(sb, report, documents);

            return sb.ToString();
        }

        private static void BuildKeyMetrics(StringBuilder sb, MemGraphReport report) {
            sb.AppendLine("## Key Metrics");
            sb.AppendLine("| Metric | Value | Assessment |");
            sb.AppendLine("|---|---|---|");

            var fp = report.Footprint;
            long heapTotal = report.Heap.TotalBytes;
            long tracked = report.Summary.TrackedByUnity;
            long untracked = report.Summary.UntrackedByUnity;

            // Physical Footprint
            if (fp.PhysFootprint > 0) {
                string fpAssessment = report.Summary.OverallHealth == MemoryHealthStatus.Critical
                    ? "CRITICAL" : report.Summary.OverallHealth == MemoryHealthStatus.Warning
                    ? "WARNING" : "OK";
                sb.AppendLine($"| Physical Footprint | {Fmt(fp.PhysFootprint)} | {fpAssessment} |");
            }

            // Heap Total
            sb.AppendLine($"| Heap Total | {Fmt(heapTotal)} | - |");

            // Tracked / Untracked
            if (heapTotal > 0) {
                float untrackedPct = (float)untracked / heapTotal * 100f;
                string trackAssessment = untrackedPct > 50 ? "WARNING — High untracked"
                    : untrackedPct > 30 ? "MODERATE" : "OK";
                sb.AppendLine($"| Unity Tracked / Untracked | {Pct(tracked, heapTotal)} / {Pct(untracked, heapTotal)} | {trackAssessment} |");
            }

            // Leaks
            int leakCount = report.Leaks.TotalLeakCount;
            long leakBytes = report.Leaks.TotalLeakBytes;
            if (leakCount > 0) {
                string leakAssessment = leakBytes > 10L * 1024 * 1024 ? "WARNING"
                    : leakCount > 50 ? "WARNING" : "INFO";
                sb.AppendLine($"| Leaks | {leakCount} ({Fmt(leakBytes)}) | {leakAssessment} |");
            } else {
                sb.AppendLine("| Leaks | None | OK |");
            }

            // Resident
            sb.AppendLine($"| Resident (vmmap) | {Fmt(report.Summary.TotalResident)} | - |");
            sb.AppendLine();
        }

        private static void BuildIssueDocumentMap(StringBuilder sb, MemGraphReport report) {
            if (report.Insights == null || report.Insights.Count == 0) return;

            sb.AppendLine("## Issues → Document Map");
            sb.AppendLine("| # | Severity | Issue | See Document |");
            sb.AppendLine("|---|---|---|---|");
            int num = 0;
            foreach (var ins in report.Insights) {
                num++;
                string docRef = AIExportMarkdownBuilder.GetRelatedDocumentName(ins.Category);
                sb.AppendLine($"| {num} | {ins.Severity.ToString().ToUpper()} | {ins.Title} | {docRef} |");
            }
            sb.AppendLine();
        }

        private static void BuildTopOptimizations(StringBuilder sb, MemGraphReport report) {
            var opts = report.Optimizations;
            if (opts == null || opts.Recommendations.Count == 0) return;

            int count = Math.Min(opts.Recommendations.Count, 3);
            sb.AppendLine("## Top Optimizations (quick view)");
            for (int i = 0; i < count; i++) {
                var rec = opts.Recommendations[i];
                sb.AppendLine($"{i + 1}. **{rec.Title}** — ~{Fmt(rec.EstimatedSavings)} savings [{rec.Difficulty}] → see 01_Optimizations");
            }
            sb.AppendLine($"*Total estimated savings: ~{Fmt(opts.TotalEstimatedSavings)} across {opts.Recommendations.Count} recommendations*");
            sb.AppendLine();
        }

        private static void BuildDocumentInventory(StringBuilder sb, AIDocumentInfo[] documents) {
            sb.AppendLine("## Document Inventory");
            sb.AppendLine("| File | Est. Tokens | What It Contains |");
            sb.AppendLine("|---|---|---|");
            foreach (var doc in documents) {
                if (doc.Type == AIDocumentType.Guide) continue; // skip self
                if (!doc.IsAvailable) continue; // hide N/A documents to reduce noise
                sb.AppendLine($"| {doc.FileName} | ~{doc.EstimatedTokens:N0} | {doc.Highlight} |");
            }
            sb.AppendLine();
        }

        private static void BuildReadingOrder(StringBuilder sb, MemGraphReport report,
            AIDocumentInfo[] documents) {
            // Build priority list based on insight severity and data availability
            var priorities = new List<(AIDocumentType type, string reason, int score)>();

            // Score from insights
            if (report.Insights != null) {
                foreach (var ins in report.Insights) {
                    var docType = MapCategoryToDocument(ins.Category);
                    int score = ins.Severity == InsightSeverity.Critical ? 3
                        : ins.Severity == InsightSeverity.Warning ? 2 : 1;
                    priorities.Add((docType, ins.Title, score));
                }
            }

            // Always recommend optimizations if available
            var optsDoc = Array.Find(documents, d => d.Type == AIDocumentType.Optimizations);
            if (optsDoc != null && optsDoc.IsAvailable)
                priorities.Add((AIDocumentType.Optimizations, "validated improvement steps", 1));

            // Deduplicate by type, sum scores
            var grouped = priorities
                .GroupBy(p => p.type)
                .Select(g => new {
                    Type = g.Key,
                    Reason = g.OrderByDescending(p => p.score).First().reason,
                    Score = g.Sum(p => p.score),
                })
                .OrderByDescending(g => g.Score)
                .Take(5)
                .ToList();

            if (grouped.Count == 0) return;

            sb.AppendLine("## Recommended Reading Order");
            for (int i = 0; i < grouped.Count; i++) {
                var g = grouped[i];
                var doc = Array.Find(documents, d => d.Type == g.Type);
                if (doc == null || !doc.IsAvailable) continue;
                sb.AppendLine($"{i + 1}. **{doc.FileName}** — {g.Reason}");
            }
            sb.AppendLine();
        }

        private static AIDocumentType MapCategoryToDocument(InsightCategory category) {
            return category switch {
                InsightCategory.MemoryPressure => AIDocumentType.Heap,
                InsightCategory.Leaks         => AIDocumentType.Leaks,
                InsightCategory.NativePlugin  => AIDocumentType.Unity,
                InsightCategory.UnsafeUtility => AIDocumentType.Unity,
                InsightCategory.Fragmentation => AIDocumentType.Heap,
                InsightCategory.Untracked     => AIDocumentType.Unity,
                InsightCategory.ThreadStack   => AIDocumentType.Vmmap,
                InsightCategory.Graphics      => AIDocumentType.Vmmap,
                _ => AIDocumentType.Heap,
            };
        }

        private static string Fmt(long bytes) => AIExportMarkdownBuilder.Fmt(bytes);
        private static string Pct(long part, long total) => AIExportMarkdownBuilder.Pct(part, total);
    }
}
