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

        // Matches a single frame: "0xADDR (binary.name) function_name_with_spaces_and_parens"
        // Group 1 = address, Group 2 = binary, Group 3 = function name (everything after binary)
        private static readonly Regex FrameRegex = new(
            @"(0x[0-9a-fA-F]+)\s+\(([^)]+)\)\s+(.+)");

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

                var alloc = new TracedAllocation {
                    CallCount = callCount,
                    TotalBytes = totalBytes,
                };
                foreach (var f in frames) {
                    alloc.Frames.Add(f);
                }
                results.Add(alloc);
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

                // Extract just the function name (before C++ parameter list)
                var fullFunc = match.Groups[3].Value.Trim();
                var funcName = StripCppParams(fullFunc);

                frames.Add(new StackFrame {
                    Address = match.Groups[1].Value,
                    Binary = match.Groups[2].Value.Trim(),
                    FunctionName = funcName,
                });
            }

            return frames;
        }

        /// <summary>
        /// Strips C++ parameter list from function name.
        /// "Shader::AwakeFromLoad(AwakeFromLoadMode)" → "Shader::AwakeFromLoad"
        /// Keeps Objective-C selectors: "-[UnityFramework runUI...]" untouched.
        /// </summary>
        private static string StripCppParams(string funcName) {
            if (string.IsNullOrEmpty(funcName)) return funcName;

            // Objective-C methods start with - or +, keep them as-is
            if (funcName[0] == '-' || funcName[0] == '+')
                return funcName;

            // Find the first '(' that starts a C++ parameter list
            int parenDepth = 0;
            for (int i = 0; i < funcName.Length; i++) {
                if (funcName[i] == '<') parenDepth++; // skip template args
                else if (funcName[i] == '>') parenDepth--;
                else if (funcName[i] == '(' && parenDepth == 0) {
                    var before = funcName.Substring(0, i).TrimEnd();
                    return before.Length > 0 ? before : funcName;
                }
            }

            return funcName;
        }

        /// <summary>
        /// Builds a shell command for malloc_history -allBySize.
        /// Note: -allBySize does NOT support size filters like [100k+] (callTree only).
        /// Output is already sorted by size descending, so head limit captures largest allocations.
        /// Uses -q to suppress the header/footer for cleaner parsing.
        /// </summary>
        public static string BuildCommand(string memGraphPath, int lineLimit = 5000) {
            var path = AddressTraceParser.EscapeForShell(memGraphPath);
            return $"-c \"malloc_history {path} -allBySize -q 2>&1 | head -{lineLimit}\"";
        }
    }
}
