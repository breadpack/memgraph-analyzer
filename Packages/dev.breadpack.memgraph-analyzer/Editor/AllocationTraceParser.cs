using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Tools {
    /// <summary>
    /// Parses malloc_history -allBySize output into TracedAllocation entries.
    /// Each line format: "N call(s) for BYTES bytes: 0xADDR (binary) func | 0xADDR (binary) func | ..."
    /// </summary>
    public static class AllocationTraceBySizeParser {
        // Matches: "2 calls for 50429952 bytes:" or "1 call for 1024 bytes:"
        private static readonly Regex HeaderRegex = new(
            @"^(\d+)\s+calls?\s+for\s+(\d+)\s+bytes?:");

        // Matches a single frame: "0xADDR (binary) function_name"
        private static readonly Regex FrameRegex = new(
            @"0x[0-9a-fA-F]+\s+\(([^)]+)\)\s+(\S+)");

        public static List<TracedAllocation> Parse(string output) {
            var results = new List<TracedAllocation>();
            if (string.IsNullOrEmpty(output)) return results;

            foreach (var rawLine in output.Split('\n')) {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var headerMatch = HeaderRegex.Match(line);
                if (!headerMatch.Success) continue;

                if (!int.TryParse(headerMatch.Groups[1].Value, out int callCount)) continue;
                if (!long.TryParse(headerMatch.Groups[2].Value, out long totalBytes)) continue;

                // Parse frames from the rest of the line after the header
                var framesText = line.Substring(headerMatch.Length);
                var frames = ParseFrames(framesText);

                results.Add(new TracedAllocation {
                    CallCount = callCount,
                    TotalBytes = totalBytes,
                    Frames = { },
                });
                // Add frames to the readonly list
                foreach (var f in frames) {
                    results[results.Count - 1].Frames.Add(f);
                }
            }

            return results;
        }

        private static List<StackFrame> ParseFrames(string framesText) {
            var frames = new List<StackFrame>();
            if (string.IsNullOrEmpty(framesText)) return frames;

            // Split by | to get individual frames
            var segments = framesText.Split('|');
            foreach (var segment in segments) {
                var trimmed = segment.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var match = FrameRegex.Match(trimmed);
                if (!match.Success) continue;

                frames.Add(new StackFrame {
                    Binary = match.Groups[1].Value.Trim(),
                    FunctionName = match.Groups[2].Value.Trim(),
                    Address = ExtractAddress(trimmed),
                });
            }

            return frames;
        }

        private static string ExtractAddress(string segment) {
            int idx = segment.IndexOf("0x");
            if (idx < 0) return "";
            int end = idx + 2;
            while (end < segment.Length && IsHexChar(segment[end])) end++;
            return segment.Substring(idx, end - idx);
        }

        private static bool IsHexChar(char c) {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>
        /// Builds a shell command for malloc_history -allBySize with size filter and line limit.
        /// </summary>
        public static string BuildCommand(string memGraphPath, string sizeFilter = "[100k+]", int lineLimit = 10000) {
            var path = AddressTraceParser.EscapeForShell(memGraphPath);
            return $"-c \"malloc_history {path} -allBySize {sizeFilter} 2>&1 | head -{lineLimit}\"";
        }
    }
}
