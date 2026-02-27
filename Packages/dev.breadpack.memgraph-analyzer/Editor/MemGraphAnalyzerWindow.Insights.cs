using System.Collections.Generic;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private void BuildInsights() {
            var insights = _report.Insights;
            insights.Clear();
            var s = _report.Summary;
            long footprint = _report.Footprint.PhysFootprint > 0 ? _report.Footprint.PhysFootprint : s.TotalResident;
            string fmtFp = VmmapParser.FormatSize(footprint);

            // 1. Memory Pressure (use phys_footprint — the actual Jetsam metric)
            if (footprint > 1536L * 1024 * 1024)
                AddInsight(insights, InsightSeverity.Critical, 1, InsightCategory.MemoryPressure,
                    "High Physical Footprint",
                    $"Physical footprint is {fmtFp}. Will crash on low-end iOS devices (iPhone SE/8/XR).",
                    "Reduce texture sizes, unload unused assets, check native plugin memory.");
            else if (footprint > 1228L * 1024 * 1024)
                AddInsight(insights, InsightSeverity.Warning, 2, InsightCategory.MemoryPressure,
                    "Elevated Physical Footprint",
                    $"Physical footprint is {fmtFp}. Approaching Jetsam limits for low-end devices.",
                    "Monitor memory growth over time. Profile with Unity Profiler.");

            // 2. Untracked Ratio
            if (_report.Heap.TotalBytes > 0 && s.UntrackedByUnity > _report.Heap.TotalBytes / 2)
                AddInsight(insights, InsightSeverity.Warning, 3, InsightCategory.Untracked,
                    "High Untracked Memory",
                    $"Untracked ({VmmapParser.FormatSize(s.UntrackedByUnity)}) exceeds 50% of heap.",
                    "Investigate native plugins and UnsafeUtility allocations.");

            // 3. Large Plugins
            foreach (var kv in s.PluginBreakdowns) {
                if (kv.Value > 100L * 1024 * 1024)
                    AddInsight(insights, InsightSeverity.Critical, 2, InsightCategory.NativePlugin,
                        $"Large Plugin: {kv.Key}", $"{kv.Key} is using {VmmapParser.FormatSize(kv.Value)}.",
                        $"Review {kv.Key} configuration. Reduce audio banks / disable unused features.");
                else if (kv.Value > 50L * 1024 * 1024)
                    AddInsight(insights, InsightSeverity.Warning, 4, InsightCategory.NativePlugin,
                        $"Plugin Memory: {kv.Key}", $"{kv.Key} is using {VmmapParser.FormatSize(kv.Value)}.",
                        $"Check {kv.Key} for memory optimization options.");
            }

            // 4. UnsafeUtility
            s.OwnerBreakdowns.TryGetValue(MemoryOwner.UnsafeUtility, out long unsafeBytes);
            if (unsafeBytes > 50L * 1024 * 1024)
                AddInsight(insights, InsightSeverity.Critical, 2, InsightCategory.UnsafeUtility,
                    "High UnsafeUtility Allocations", $"UnsafeUtility memory is {VmmapParser.FormatSize(unsafeBytes)}.",
                    "Check for missing UnsafeUtility.Free() calls. Use NativeArray where possible.");
            else if (unsafeBytes > 20L * 1024 * 1024)
                AddInsight(insights, InsightSeverity.Warning, 4, InsightCategory.UnsafeUtility,
                    "Elevated UnsafeUtility Usage", $"UnsafeUtility memory is {VmmapParser.FormatSize(unsafeBytes)}.",
                    "Verify all UnsafeUtility allocations are properly freed.");

            // 5. Leak Count
            int lc = _report.Leaks.TotalLeakCount;
            string leakSz = VmmapParser.FormatSize(_report.Leaks.TotalLeakBytes);
            if (lc > 50)
                AddInsight(insights, InsightSeverity.Critical, 1, InsightCategory.Leaks,
                    "Many Memory Leaks", $"{lc} leaks totaling {leakSz}.",
                    "Focus on largest leak groups first. Check the Leak Detection tab.");
            else if (lc > 10)
                AddInsight(insights, InsightSeverity.Warning, 3, InsightCategory.Leaks,
                    "Memory Leaks Detected", $"{lc} leaks totaling {leakSz}.",
                    "Review leak entries in the Leak Detection tab.");

            // 6. Dirty Ratio
            long dirty = s.TotalDirty;
            if (footprint > 0 && dirty > footprint * 80 / 100)
                AddInsight(insights, InsightSeverity.Warning, 3, InsightCategory.MemoryPressure,
                    "High Dirty Memory Ratio",
                    $"Dirty ({VmmapParser.FormatSize(dirty)}) is >80% of footprint ({fmtFp}).",
                    "High dirty ratio means most memory cannot be reclaimed. Reduce active allocations.");

            insights.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            bool hasCritical = false, hasWarning = false;
            foreach (var insight in insights) {
                if (insight.Severity == InsightSeverity.Critical) hasCritical = true;
                if (insight.Severity == InsightSeverity.Warning) hasWarning = true;
            }
            s.OverallHealth = hasCritical ? MemoryHealthStatus.Critical
                : hasWarning ? MemoryHealthStatus.Warning : MemoryHealthStatus.Good;
        }

        private static void AddInsight(List<MemoryInsight> list,
            InsightSeverity severity, int priority, InsightCategory category,
            string title, string description, string recommendation) {
            list.Add(new MemoryInsight {
                Severity = severity, Priority = priority, Category = category,
                Title = title, Description = description, Recommendation = recommendation,
            });
        }
    }
}
