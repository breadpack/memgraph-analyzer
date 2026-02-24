using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Tools {
    public static class LeaksParser {
        public static LeaksResult Parse(string output) {
            var result = new LeaksResult { RawOutput = output };
            if (string.IsNullOrEmpty(output)) return result;

            var lines = output.Split('\n');

            // Summary line: "Process NNNNN: N leaks for N total leaked bytes."
            var summaryRegex = new Regex(@"Process\s+\d+:\s+(\d+)\s+leaks?\s+for\s+(\d+)\s+total");
            // Leak entry header: "Leak: 0x12345678  size=NNN  zone: DefaultMallocZone_0x..."
            var leakHeaderRegex = new Regex(@"Leak:\s+(0x[0-9a-fA-F]+)\s+size=(\d+)\s+zone:\s*(.*)");
            // Alternative format: "ROOT LEAK: <addr>  size=N  type=XXX"
            var rootLeakRegex = new Regex(@"(?:ROOT\s+)?LEAK:\s+(0x[0-9a-fA-F]+)\s+size=(\d+)(?:\s+zone:\s*(.*?))?(?:\s+type=(.*))?");

            LeakEntry currentLeak = null;
            var stackBuilder = new StringBuilder();

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r');

                // Check summary
                var summaryMatch = summaryRegex.Match(line);
                if (summaryMatch.Success) {
                    if (int.TryParse(summaryMatch.Groups[1].Value, out int leakCount))
                        result.TotalLeakCount = leakCount;
                    if (long.TryParse(summaryMatch.Groups[2].Value, out long leakBytes))
                        result.TotalLeakBytes = leakBytes;
                    continue;
                }

                // Check leak header
                var leakMatch = leakHeaderRegex.Match(line);
                if (!leakMatch.Success) leakMatch = rootLeakRegex.Match(line);

                if (leakMatch.Success) {
                    FinalizeLeak(currentLeak, stackBuilder, result);

                    try {
                        currentLeak = new LeakEntry {
                            Address = leakMatch.Groups[1].Value,
                            Size = long.TryParse(leakMatch.Groups[2].Value, out long s) ? s : 0,
                            TypeOrZone = leakMatch.Groups.Count > 3 ? leakMatch.Groups[3].Value.Trim() : "",
                        };
                        if (leakMatch.Groups.Count > 4 && !string.IsNullOrEmpty(leakMatch.Groups[4].Value)) {
                            currentLeak.TypeOrZone = leakMatch.Groups[4].Value.Trim();
                        }
                        currentLeak.Owner = HeapParser.ClassifyOwner(currentLeak.TypeOrZone);
                        stackBuilder.Clear();
                    }
                    catch {
                        result.ParseErrorCount++;
                        currentLeak = null;
                    }
                    continue;
                }

                // Stack trace lines (indented or starting with frame number)
                if (currentLeak != null && !string.IsNullOrWhiteSpace(line)) {
                    stackBuilder.AppendLine(line);
                }
            }

            FinalizeLeak(currentLeak, stackBuilder, result);

            // If we found leaks but no summary line, count from entries
            if (result.TotalLeakCount == 0 && result.Leaks.Count > 0) {
                result.TotalLeakCount = result.Leaks.Count;
                result.TotalLeakBytes = 0;
                foreach (var leak in result.Leaks) {
                    result.TotalLeakBytes += leak.Size;
                }
            }

            return result;
        }

        private static void FinalizeLeak(LeakEntry leak, StringBuilder stackBuilder, LeaksResult result) {
            if (leak == null) return;
            leak.StackTrace = stackBuilder.ToString().TrimEnd();
            result.Leaks.Add(leak);
            stackBuilder.Clear();
        }

        public static List<LeakGroup> GroupLeaks(LeaksResult leaksResult) {
            var groups = new Dictionary<string, LeakGroup>();

            foreach (var leak in leaksResult.Leaks) {
                string key = string.IsNullOrEmpty(leak.TypeOrZone) ? "(unknown)" : leak.TypeOrZone;

                if (!groups.TryGetValue(key, out var group)) {
                    group = new LeakGroup {
                        TypeOrZone = key,
                        Owner = leak.Owner,
                    };
                    groups[key] = group;
                }

                group.Entries.Add(leak);
                group.TotalBytes += leak.Size;
            }

            foreach (var group in groups.Values) {
                group.Severity = group.TotalBytes > 1024 * 1024 ? InsightSeverity.Critical
                    : group.Entries.Count > 10 ? InsightSeverity.Warning
                    : InsightSeverity.Info;
            }

            var result = new List<LeakGroup>(groups.Values);
            result.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
            return result;
        }
    }
}
