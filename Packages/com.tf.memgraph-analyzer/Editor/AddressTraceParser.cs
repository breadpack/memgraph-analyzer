using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tools {
    public static class AddressTraceParser {
        // heap -addresses output: "    0x283200000 (65536 bytes) non-object"
        private static readonly Regex AddressLineRegex = new(
            @"(0x[0-9a-fA-F]+)\s+\((\d+)\s+bytes?\)");

        // heap -addresses output: bare address "    0x283200000"
        private static readonly Regex BareAddressRegex = new(
            @"^\s*(0x[0-9a-fA-F]+)\s*$");

        // malloc_history block header: "ALLOC 0xADDR-0xADDR [size=N]: thread ..."
        // or: "0xADDR (N bytes) ALLOC ..."
        private static readonly Regex AllocHeaderRegex = new(
            @"(?:ALLOC\s+)?(0x[0-9a-fA-F]+)(?:-0x[0-9a-fA-F]+)?\s+(?:\[size=(\d+)\]|\((\d+)\s+bytes?\))");

        // Stack frame: "  N  binary  0xADDR function + offset"
        private static readonly Regex FrameRegex = new(
            @"^\s*\d+\s+(\S+)\s+(0x[0-9a-fA-F]+)\s+(.+?)(?:\s*\+\s*\d+)?\s*$");

        public static List<(string address, long size)> ParseHeapAddresses(string output) {
            var results = new List<(string, long)>();
            if (string.IsNullOrEmpty(output)) return results;

            foreach (var rawLine in output.Split('\n')) {
                var line = rawLine.TrimEnd('\r');

                // Try address with size
                var match = AddressLineRegex.Match(line);
                if (match.Success) {
                    string addr = match.Groups[1].Value;
                    long size = long.TryParse(match.Groups[2].Value, out long s) ? s : 0;
                    results.Add((addr, size));
                    continue;
                }

                // Try bare address
                var bareMatch = BareAddressRegex.Match(line);
                if (bareMatch.Success) {
                    results.Add((bareMatch.Groups[1].Value, 0));
                }
            }

            return results;
        }

        public static List<AddressTrace> ParseMallocHistory(string output) {
            var traces = new List<AddressTrace>();
            if (string.IsNullOrEmpty(output)) return traces;

            AddressTrace current = null;
            foreach (var rawLine in output.Split('\n')) {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Check for allocation header
                var headerMatch = AllocHeaderRegex.Match(line);
                if (headerMatch.Success) {
                    current = new AddressTrace {
                        Address = headerMatch.Groups[1].Value,
                    };
                    // size from [size=N] or (N bytes)
                    string sizeStr = headerMatch.Groups[2].Success
                        ? headerMatch.Groups[2].Value
                        : headerMatch.Groups[3].Value;
                    current.Size = long.TryParse(sizeStr, out long sz) ? sz : 0;
                    traces.Add(current);
                    continue;
                }

                // Check for stack frame
                if (current == null) continue;
                var frameMatch = FrameRegex.Match(line);
                if (frameMatch.Success) {
                    current.Frames.Add(new StackFrame {
                        Binary = frameMatch.Groups[1].Value.Trim(),
                        Address = frameMatch.Groups[2].Value,
                        FunctionName = frameMatch.Groups[3].Value.Trim(),
                    });
                }
            }

            return traces;
        }

        public static string EscapeForShell(string input) {
            if (string.IsNullOrEmpty(input)) return "''";
            // Wrap in single quotes, escape any internal single quotes
            return "'" + input.Replace("'", "'\\''") + "'";
        }

        public static string BuildHeapAddressesCommand(string className, string memGraphPath, int limit = 20) {
            string escaped = EscapeForShell(className);
            string path = EscapeForShell(memGraphPath);
            return $"-c \"heap -addresses {escaped} -sortBySize {path} 2>/dev/null | head -{limit}\"";
        }

        public static string BuildMallocHistoryCommand(
            string memGraphPath, List<string> addresses, int lineLimit = 500) {
            string path = EscapeForShell(memGraphPath);
            string addrs = string.Join(" ", addresses);
            return $"-c \"malloc_history {path} {addrs} 2>&1 | head -{lineLimit}\"";
        }

        public static List<StackFrame> GetSignificantFrames(AddressTrace trace, int maxFrames = 12) {
            if (trace?.Frames == null) return new List<StackFrame>();
            var result = new List<StackFrame>();
            foreach (var frame in trace.Frames) {
                if (result.Count >= maxFrames) break;
                // Skip low-level malloc internals
                if (frame.FunctionName != null && IsLowLevelAllocator(frame.FunctionName))
                    continue;
                result.Add(frame);
            }
            return result;
        }

        private static bool IsLowLevelAllocator(string funcName) {
            var upper = funcName.ToUpperInvariant();
            return upper.StartsWith("MALLOC_ZONE") ||
                   upper.StartsWith("SZONE_") ||
                   upper.StartsWith("TINY_") ||
                   upper.StartsWith("SMALL_") ||
                   upper.StartsWith("LARGE_") ||
                   upper == "MALLOC" ||
                   upper == "CALLOC" ||
                   upper == "REALLOC" ||
                   upper.StartsWith("_MALLOC_ZONE") ||
                   upper.StartsWith("DEFAULT_ZONE");
        }
    }
}
