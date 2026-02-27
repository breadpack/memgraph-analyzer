using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tools {
    public static class CallTreeParser {
        private const int MaxDepth = 8;

        // Matches: COUNT (SIZE) function_name  (in BINARY) + offset  [address]
        private static readonly Regex NodeRegex = new(
            @"(\d+)\s+\(([^)]+)\)\s+(.+?)\s{2,}\(in\s+([^)]+)\)");

        // Matches IL2CPP mangled names with optional namespace chain:
        // Namespace_SubNS_ClassName_MethodName_mHASH
        private static readonly Regex IL2CppRegex = new(
            @"^([A-Za-z][A-Za-z0-9_]*?)_([A-Za-z][A-Za-z0-9]*)_m[0-9A-Fa-f]{32}");

        // Matches IL2CPP async/generic: U3CMethodU3Ed__NN_MoveNext_mHASH
        private static readonly Regex IL2CppAsyncRegex = new(
            @"^U3C([A-Za-z0-9_]+)U3E(d__\d+)_([A-Za-z0-9_]+?)_m[0-9A-Fa-f]{32}");

        // Matches generic types: List_1_Add_mHASH, Dictionary_2_TryGetValue_mHASH
        private static readonly Regex IL2CppGenericRegex = new(
            @"^([A-Za-z][A-Za-z0-9]*)_(\d+)_([A-Za-z][A-Za-z0-9_]*?)_m[0-9A-Fa-f]{32}");

        // Matches hash suffix for IsUserCode check
        private static readonly Regex HashSuffixRegex = new(@"_m[0-9A-Fa-f]{32,}$");

        public static List<CallTreeEntry> ParseInvertedCallTree(string output) {
            var roots = new List<CallTreeEntry>();
            if (string.IsNullOrEmpty(output)) return roots;

            var lines = output.Split('\n');
            bool started = false;
            var depthStack = new List<CallTreeEntry>(); // stack indexed by depth

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r');

                if (!started) {
                    if (line.Contains("Call graph:") || line.Contains("<< TOTAL >>"))
                        started = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                int depth = GetDepth(line);
                if (depth > MaxDepth) continue;

                var match = NodeRegex.Match(line);
                if (!match.Success) continue;

                int count;
                if (!int.TryParse(match.Groups[1].Value, out count)) continue;

                long totalBytes = ParseSizeString(match.Groups[2].Value);
                string functionName = match.Groups[3].Value.Trim();
                string binary = match.Groups[4].Value.Trim();

                var entry = new CallTreeEntry {
                    FunctionName = functionName,
                    Binary = binary,
                    Count = count,
                    TotalBytes = totalBytes,
                    Depth = depth,
                };

                if (depth == 0) {
                    roots.Add(entry);
                    // Reset stack
                    depthStack.Clear();
                    depthStack.Add(entry);
                } else {
                    // Find parent at depth-1
                    while (depthStack.Count > depth)
                        depthStack.RemoveAt(depthStack.Count - 1);

                    if (depthStack.Count > 0) {
                        var parent = depthStack[depthStack.Count - 1];
                        parent.Children.Add(entry);
                    }

                    if (depthStack.Count <= depth)
                        depthStack.Add(entry);
                    else
                        depthStack[depth] = entry;
                }
            }

            return roots;
        }

        public static List<CallTreeEntry> ExtractCallers(
            List<CallTreeEntry> roots, string targetFunction, int maxDepth = 3) {
            var results = new List<CallTreeEntry>();
            if (roots == null || string.IsNullOrEmpty(targetFunction)) return results;

            var targetUpper = targetFunction.ToUpperInvariant();
            foreach (var root in roots) {
                if (root.FunctionName != null &&
                    root.FunctionName.ToUpperInvariant().Contains(targetUpper)) {
                    // Return children (callers in inverted tree)
                    CollectChildren(root, results, maxDepth, 0);
                    break;
                }
                // Also search one level deep
                SearchNode(root, targetUpper, results, maxDepth);
            }

            results.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
            return results;
        }

        private static void SearchNode(
            CallTreeEntry node, string targetUpper,
            List<CallTreeEntry> results, int maxDepth) {
            foreach (var child in node.Children) {
                if (child.FunctionName != null &&
                    child.FunctionName.ToUpperInvariant().Contains(targetUpper)) {
                    CollectChildren(child, results, maxDepth, 0);
                    return;
                }
                SearchNode(child, targetUpper, results, maxDepth);
            }
        }

        private static void CollectChildren(
            CallTreeEntry node, List<CallTreeEntry> results, int maxDepth, int currentDepth) {
            if (currentDepth >= maxDepth) return;
            foreach (var child in node.Children) {
                results.Add(child);
            }
        }

        public static string FormatFunctionName(string functionName) {
            if (string.IsNullOrEmpty(functionName)) return functionName;

            // Try async/generic pattern first: U3CMethodU3Ed__NN_MoveNext_mHASH
            var asyncMatch = IL2CppAsyncRegex.Match(functionName);
            if (asyncMatch.Success) {
                string method = asyncMatch.Groups[1].Value;
                string suffix = asyncMatch.Groups[3].Value;
                return $"<{method}>.{suffix}";
            }

            // Try generic type: List_1_Add_mHASH → List<>.Add
            var genericMatch = IL2CppGenericRegex.Match(functionName);
            if (genericMatch.Success) {
                string typeName = genericMatch.Groups[1].Value;
                string methodName = genericMatch.Groups[3].Value;
                return $"{typeName}<>.{methodName}";
            }

            // Try standard IL2CPP: [Namespace_...]ClassName_MethodName_mHASH
            var match = IL2CppRegex.Match(functionName);
            if (match.Success) {
                string prefix = match.Groups[1].Value;
                string methodName = match.Groups[2].Value;
                // Convert underscore-separated namespace chain to dots
                string dotted = prefix.Replace('_', '.');
                return $"{dotted}.{methodName}";
            }

            return functionName;
        }

        public static bool IsUserCode(string functionName, string binary = null) {
            if (string.IsNullOrEmpty(functionName)) return false;
            // IL2CPP functions have _mHEXHASH suffix
            return HashSuffixRegex.IsMatch(functionName);
        }

        private static int GetDepth(string line) {
            int depth = 0;
            foreach (char c in line) {
                if (c == ' ' || c == '\t') continue;
                if (c == '+' || c == '!' || c == ':' || c == '|') {
                    depth++;
                } else {
                    break;
                }
            }
            return depth;
        }

        private static long ParseSizeString(string sizeStr) {
            if (string.IsNullOrEmpty(sizeStr)) return 0;
            sizeStr = sizeStr.Trim();

            // Handle formats like "1024M", "506.2M", "13.6K", "4096" (plain bytes)
            long multiplier = 1;
            string numPart = sizeStr;

            if (sizeStr.EndsWith("G", StringComparison.OrdinalIgnoreCase)) {
                multiplier = 1024L * 1024 * 1024;
                numPart = sizeStr.Substring(0, sizeStr.Length - 1);
            } else if (sizeStr.EndsWith("M", StringComparison.OrdinalIgnoreCase)) {
                multiplier = 1024L * 1024;
                numPart = sizeStr.Substring(0, sizeStr.Length - 1);
            } else if (sizeStr.EndsWith("K", StringComparison.OrdinalIgnoreCase)) {
                multiplier = 1024L;
                numPart = sizeStr.Substring(0, sizeStr.Length - 1);
            }

            if (double.TryParse(numPart.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return (long)(val * multiplier);

            return 0;
        }
    }
}
