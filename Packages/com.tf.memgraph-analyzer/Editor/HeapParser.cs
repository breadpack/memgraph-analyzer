using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tools {
    public static class HeapParser {
        // Regex to strip trailing TYPE and BINARY columns from CLASS_NAME
        // TYPE is one of: C++, ObjC, C, Swift, Rust
        private static readonly Regex TypeBinaryRegex =
            new(@"\s{2,}(?:C\+\+|ObjC|C|Swift|Rust)\s+\S+\s*$");

        public static HeapResult Parse(string output) {
            var result = new HeapResult { RawOutput = output };
            if (string.IsNullOrEmpty(output)) return result;

            var rawEntries = ParseRawEntries(output, out int parseErrorCount);
            result.ParseErrorCount = parseErrorCount;

            // Group by ClassName to aggregate --showSizes rows
            var grouped = new Dictionary<string, List<HeapAllocation>>();
            foreach (var entry in rawEntries) {
                if (!grouped.TryGetValue(entry.ClassName, out var list)) {
                    list = new List<HeapAllocation>();
                    grouped[entry.ClassName] = list;
                }
                list.Add(entry);
            }

            foreach (var kv in grouped) {
                var entries = kv.Value;
                int totalCount = 0;
                long totalBytes = 0;
                foreach (var e in entries) {
                    totalCount += e.Count;
                    totalBytes += e.TotalBytes;
                }

                var sizeDist = new List<HeapSizeBucket>(entries.Count);
                foreach (var e in entries) {
                    sizeDist.Add(new HeapSizeBucket {
                        Size = e.AverageSize,
                        Count = e.Count,
                        TotalBytes = e.TotalBytes,
                    });
                }
                sizeDist.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));

                var first = entries[0];
                var allocation = new HeapAllocation {
                    ClassName = kv.Key,
                    Binary = first.Binary,
                    Owner = first.Owner,
                    Count = totalCount,
                    TotalBytes = totalBytes,
                    AverageSize = totalCount > 0 ? totalBytes / totalCount : 0,
                    SizeDistribution = sizeDist,
                };

                result.Allocations.Add(allocation);
                result.TotalBytes += totalBytes;
                result.TotalCount += totalCount;
            }

            return result;
        }

        private static List<HeapAllocation> ParseRawEntries(string output, out int parseErrorCount) {
            parseErrorCount = 0;
            var entries = new List<HeapAllocation>();
            var lines = output.Split('\n');
            var allocationRegex = new Regex(@"^\s*(\d+)\s+(\d+)\s+(\d+\.?\d*)\s+(.+)$");
            var zoneRegex = new Regex(@"^Zone\s+(.+?):\s+Overall size:\s+(\d+)");

            foreach (var rawLine in lines) {
                var line = rawLine.TrimEnd('\r');

                var zoneMatch = zoneRegex.Match(line);
                if (zoneMatch.Success) continue;

                var match = allocationRegex.Match(line);
                if (!match.Success) continue;

                try {
                    int count = int.Parse(match.Groups[1].Value);
                    long totalBytes = long.Parse(match.Groups[2].Value);
                    long avgSize = (long)double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    string rawClassName = match.Groups[4].Value.Trim();

                    if (rawClassName.StartsWith("===") || rawClassName.StartsWith("---")) continue;
                    if (rawClassName.IndexOf("CLASS_NAME", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string binary = ExtractBinary(rawClassName);
                    string className = TypeBinaryRegex.Replace(rawClassName, "").Trim();

                    entries.Add(new HeapAllocation {
                        Count = count,
                        TotalBytes = totalBytes,
                        AverageSize = avgSize,
                        ClassName = className,
                        Binary = binary,
                        Owner = ClassifyOwner(className, binary),
                    });
                }
                catch {
                    parseErrorCount++;
                }
            }

            return entries;
        }

        private static string ExtractBinary(string rawClassName) {
            var m = TypeBinaryRegex.Match(rawClassName);
            if (!m.Success) return null;
            // The last non-space token is the binary name
            var tail = m.Value.TrimEnd();
            int lastSpace = tail.LastIndexOf(' ');
            return lastSpace >= 0 ? tail.Substring(lastSpace + 1).Trim() : null;
        }

        public static MemoryOwner ClassifyOwner(string className, string binary = null) {
            if (string.IsNullOrEmpty(className) && string.IsNullOrEmpty(binary))
                return MemoryOwner.Unknown;

            var upper = (className ?? "").ToUpperInvariant();
            var binaryUpper = (binary ?? "").ToUpperInvariant();

            // Binary-based classification (most reliable)
            if (binaryUpper.Contains("UNITYFRAMEWORK") || binaryUpper.Contains("UNITY"))
                return ClassifyUnitySubtype(upper);
            if (binaryUpper.Contains("AGXMETAL") || binaryUpper.Contains("METAL") ||
                binaryUpper.Contains("IOACCELERATOR"))
                return MemoryOwner.GraphicsDriver;
            if (binaryUpper.Contains("FMOD") || binaryUpper.Contains("FIREBASE") ||
                binaryUpper.Contains("SPINE") || binaryUpper.Contains("CRASHLYTICS") ||
                binaryUpper.Contains("WWISE"))
                return MemoryOwner.NativePlugin;

            // ClassName-based classification (fallback)
            if (upper.Contains("UNSAFEUTILITY")) return MemoryOwner.UnsafeUtility;

            if (upper.Contains("MEMORYMANAGER") || upper.Contains("IL2CPP") ||
                upper.Contains("MONO") || upper.Contains("IPHONELABEL") ||
                upper.Contains("IPHONENEWALLOCATOR") || upper.Contains("IPHONELABELALLOCATOR") ||
                upper.Contains("IPHONENEWALLOCATOR") || upper.Contains("IPHONENEWALLOCATOR"))
                return ClassifyUnitySubtype(upper);

            if (upper.Contains("IOKIT") || upper.Contains("IOSURFACE") ||
                upper.Contains("METAL") || upper.Contains("MTL") ||
                upper.Contains("GPU") || upper.StartsWith("CG") || upper.Contains("AGX"))
                return MemoryOwner.GraphicsDriver;

            if (upper.Contains("FMOD") || upper.Contains("FIREBASE") ||
                upper.StartsWith("FIR") || upper.Contains("SPINE") ||
                upper.Contains("CRASHLYTICS") || upper.Contains("WWISE"))
                return MemoryOwner.NativePlugin;

            if (upper.Contains("STACK") || upper.Contains("PTHREAD"))
                return MemoryOwner.ThreadStack;

            if (upper.StartsWith("NS") || upper.StartsWith("CF") || upper.StartsWith("SK") ||
                upper.Contains("VM_ALLOCATE") || upper.Contains("DISPATCH") ||
                upper.StartsWith("OS_") || upper.Contains("LIBSYSTEM") ||
                upper.Contains("LIBDISPATCH"))
                return MemoryOwner.System;

            // System library binary detection
            if (binaryUpper.Contains("LIBSYSTEM") || binaryUpper.Contains("LIBDISPATCH") ||
                binaryUpper.Contains("LIBOBJC") || binaryUpper.Contains("COREFOUNDATION") ||
                binaryUpper.Contains("LIBSQLITE"))
                return MemoryOwner.System;

            return MemoryOwner.Unknown;
        }

        private static MemoryOwner ClassifyUnitySubtype(string upper) {
            if (upper.Contains("UNSAFEUTILITY")) return MemoryOwner.UnsafeUtility;
            return MemoryOwner.Unity;
        }

        public static string GetOwnerDisplayName(MemoryOwner owner) {
            return owner switch {
                MemoryOwner.Unknown => "Unknown",
                MemoryOwner.Unity => "Unity Engine",
                MemoryOwner.NativePlugin => "Native Plugin",
                MemoryOwner.System => "System",
                MemoryOwner.ThreadStack => "Thread Stack",
                MemoryOwner.GraphicsDriver => "Graphics/GPU",
                MemoryOwner.UnsafeUtility => "UnsafeUtility",
                _ => owner.ToString(),
            };
        }

        public static string DetectPluginName(string className, string binary = null) {
            if (!string.IsNullOrEmpty(binary)) {
                var bu = binary.ToUpperInvariant();
                if (bu.Contains("FMOD")) return "FMOD";
                if (bu.Contains("FIREBASE")) return "Firebase";
                if (bu.Contains("SPINE")) return "Spine";
                if (bu.Contains("CRASHLYTICS")) return "Crashlytics";
                if (bu.Contains("WWISE")) return "Wwise";
            }
            if (string.IsNullOrEmpty(className)) return null;
            var upper = className.ToUpperInvariant();
            if (upper.Contains("FMOD")) return "FMOD";
            if (upper.Contains("FIREBASE") || upper.StartsWith("FIR")) return "Firebase";
            if (upper.Contains("SPINE")) return "Spine";
            if (upper.Contains("CRASHLYTICS")) return "Crashlytics";
            if (upper.Contains("WWISE")) return "Wwise";
            return null;
        }

        public static Actionability GetActionability(HeapAllocation alloc) {
            return alloc.Owner switch {
                MemoryOwner.Unity => Actionability.Fixable,
                MemoryOwner.NativePlugin => Actionability.Fixable,
                MemoryOwner.UnsafeUtility => Actionability.Fixable,
                MemoryOwner.System => Actionability.SystemOwned,
                MemoryOwner.GraphicsDriver => Actionability.Monitor,
                MemoryOwner.ThreadStack => Actionability.Monitor,
                _ => Actionability.Monitor,
            };
        }

        public static string GetActionabilityLabel(Actionability actionability) {
            return actionability switch {
                Actionability.Fixable => "Fixable",
                Actionability.Monitor => "Monitor",
                Actionability.SystemOwned => "System",
                _ => "Unknown",
            };
        }
    }
}
