using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Tools {
    public static class VmmapParser {
        public static VmmapResult ParseSummary(string output) {
            var result = new VmmapResult { RawOutput = output };
            if (string.IsNullOrEmpty(output)) return result;

            var lines = output.Split('\n');
            bool inTable = false;

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r');

                if (line.StartsWith("===") || line.StartsWith("---")) {
                    inTable = true;
                    continue;
                }

                if (!inTable) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try {
                    var row = ParseSummaryLine(line);
                    if (row == null) continue;

                    if (row.RegionType.Equals("TOTAL", StringComparison.OrdinalIgnoreCase)) {
                        result.Total = row;
                    } else if (!row.RegionType.StartsWith("TOTAL,", StringComparison.OrdinalIgnoreCase)) {
                        result.Summary.Add(row);
                    }
                }
                catch {
                    result.ParseErrorCount++;
                }
            }

            return result;
        }

        private static VmmapSummaryRow ParseSummaryLine(string line) {
            var parts = SplitSummaryColumns(line);
            if (parts == null || parts.Length < 3) return null;

            var row = new VmmapSummaryRow { RegionType = parts[0].Trim() };
            if (string.IsNullOrWhiteSpace(row.RegionType)) return null;

            if (parts.Length >= 2) row.VirtualSize = ParseSize(parts[1]);
            if (parts.Length >= 3) row.ResidentSize = ParseSize(parts[2]);
            if (parts.Length >= 4) row.DirtySize = ParseSize(parts[3]);
            if (parts.Length >= 5) row.SwappedSize = ParseSize(parts[4]);

            // RegionCount is the last purely numeric column (new format has more columns)
            for (int i = parts.Length - 1; i >= 5; i--) {
                var trimmed = parts[i].Trim();
                if (int.TryParse(trimmed, out int count)) {
                    row.RegionCount = count;
                    break;
                }
            }

            return row;
        }

        private static string[] SplitSummaryColumns(string line) {
            var matches = Regex.Split(line.Trim(), @"\s{2,}");
            if (matches.Length < 3) return null;
            return matches;
        }

        public static List<VmmapRegion> ParseDetailed(string output) {
            var regions = new List<VmmapRegion>();
            if (string.IsNullOrEmpty(output)) return regions;

            var lines = output.Split('\n');
            var regionRegex = new Regex(
                @"^(\S+)\s+([0-9a-fA-F]+)-([0-9a-fA-F]+)\s+\[\s*([^\]]+)\]\s+(\S+)(?:/\S+)?\s+SM=(\S+)\s*(.*)?$");

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r');
                var match = regionRegex.Match(line);
                if (!match.Success) continue;

                try {
                    regions.Add(new VmmapRegion {
                        RegionType = match.Groups[1].Value,
                        AddressStart = match.Groups[2].Value,
                        AddressEnd = match.Groups[3].Value,
                        Size = ParseSize(match.Groups[4].Value),
                        Protection = match.Groups[5].Value,
                        ShareMode = match.Groups[6].Value,
                        Detail = match.Groups[7].Value.Trim(),
                    });
                }
                catch { /* skip unparseable regions */ }
            }

            return regions;
        }

        public static FootprintResult ParseFootprint(string output) {
            var result = new FootprintResult { RawOutput = output };
            if (string.IsNullOrEmpty(output)) return result;

            var lines = output.Split('\n');
            bool inTable = false;

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Detect table start: header line with "Dirty" and "Category"
                if (line.Contains("Dirty") && line.Contains("Category")) {
                    inTable = true;
                    continue;
                }

                // Skip separator lines
                if (line.StartsWith("---") && line.Contains("---")) {
                    if (inTable) continue;
                    continue;
                }
                if (line.StartsWith("===")) continue;

                // Parse phys_footprint from auxiliary data
                if (line.StartsWith("phys_footprint:") && !line.Contains("peak")) {
                    var val = line.Substring("phys_footprint:".Length).Trim();
                    result.PhysFootprint = ParseSize(val);
                    continue;
                }
                if (line.StartsWith("phys_footprint_peak:")) {
                    var val = line.Substring("phys_footprint_peak:".Length).Trim();
                    result.PhysFootprintPeak = ParseSize(val);
                    continue;
                }

                if (!inTable) continue;

                // Parse category rows: "540 MB    0 B    0 B    345    untagged (VM_ALLOCATE)"
                // TOTAL row: "1617 MB   149 MB   14 MB   23905   TOTAL"
                var parts = SplitSummaryColumns(line);
                if (parts == null || parts.Length < 5) continue;

                try {
                    // Check if this is the TOTAL row
                    bool isTotal = false;
                    for (int i = 0; i < parts.Length; i++) {
                        if (parts[i].Trim().Equals("TOTAL", StringComparison.OrdinalIgnoreCase)) {
                            isTotal = true;
                            break;
                        }
                    }

                    long dirty = ParseSize(parts[0]);
                    long clean = ParseSize(parts[1]);
                    long reclaimable = ParseSize(parts[2]);
                    int.TryParse(parts[3].Trim(), out int regionCount);
                    string name = parts.Length >= 5 ? parts[4].Trim() : "";

                    // Override name detection for TOTAL
                    if (isTotal || name.Equals("TOTAL", StringComparison.OrdinalIgnoreCase)) {
                        result.TotalDirty = dirty;
                        result.TotalClean = clean;
                        result.TotalReclaimable = reclaimable;
                    } else if (!string.IsNullOrEmpty(name)) {
                        result.Categories.Add(new FootprintCategory {
                            Name = name,
                            DirtySize = dirty,
                            CleanSize = clean,
                            ReclaimableSize = reclaimable,
                            RegionCount = regionCount,
                        });
                    }
                }
                catch { /* skip unparseable lines */ }
            }

            return result;
        }

        public static long ParseSize(string sizeStr) {
            if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
            sizeStr = sizeStr.Trim();

            if (long.TryParse(sizeStr, out long directValue)) return directValue;

            var match = Regex.Match(sizeStr, @"^([0-9]*\.?[0-9]+)\s*([KMGT]?)B?$", RegexOptions.IgnoreCase);
            if (!match.Success) return 0;

            if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value)) return 0;
            var suffix = match.Groups[2].Value.ToUpperInvariant();

            return suffix switch {
                "K" => (long)(value * 1024),
                "M" => (long)(value * 1024 * 1024),
                "G" => (long)(value * 1024 * 1024 * 1024),
                "T" => (long)(value * 1024L * 1024 * 1024 * 1024),
                _ => (long)value,
            };
        }

        public static string FormatSize(long bytes) {
            if (bytes < 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
