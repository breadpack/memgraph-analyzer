using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Tools {
    public class ZoneInfo {
        public string ZoneName;
        public long OverallSize;
        public int AllocationCount;
    }

    public static class ZoneParser {
        // "Zone DefaultMallocZone_0x....: Overall size: 123456; ..."
        private static readonly Regex ZoneHeaderRegex = new(
            @"^Zone\s+(\S+?)(?:_0x[0-9a-fA-F]+)?:\s+Overall size:\s+(\d+)");

        // "    Count: 123   Size: 456   <ClassName>"
        private static readonly Regex ClassInZoneRegex = new(
            @"^\s*(\d+)\s+(\d+)\s+");

        public static List<ZoneInfo> ParseZones(string output) {
            var zones = new List<ZoneInfo>();
            if (string.IsNullOrEmpty(output)) return zones;

            ZoneInfo current = null;
            foreach (var rawLine in output.Split('\n')) {
                var line = rawLine.TrimEnd('\r');

                var zoneMatch = ZoneHeaderRegex.Match(line);
                if (zoneMatch.Success) {
                    current = new ZoneInfo {
                        ZoneName = zoneMatch.Groups[1].Value,
                    };
                    if (long.TryParse(zoneMatch.Groups[2].Value, out long size))
                        current.OverallSize = size;
                    zones.Add(current);
                    continue;
                }

                if (current != null) {
                    var classMatch = ClassInZoneRegex.Match(line);
                    if (classMatch.Success) {
                        if (int.TryParse(classMatch.Groups[1].Value, out int count))
                            current.AllocationCount += count;
                    }
                }
            }

            return zones;
        }

        public static ZoneInfo ParseZoneForClass(string output, string className) {
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(className)) return null;

            var upperClassName = className.ToUpperInvariant();
            ZoneInfo currentZone = null;
            bool foundInCurrentZone = false;

            foreach (var rawLine in output.Split('\n')) {
                var line = rawLine.TrimEnd('\r');

                var zoneMatch = ZoneHeaderRegex.Match(line);
                if (zoneMatch.Success) {
                    if (foundInCurrentZone && currentZone != null)
                        return currentZone;

                    currentZone = new ZoneInfo {
                        ZoneName = zoneMatch.Groups[1].Value,
                    };
                    if (long.TryParse(zoneMatch.Groups[2].Value, out long size))
                        currentZone.OverallSize = size;
                    foundInCurrentZone = false;
                    continue;
                }

                if (currentZone != null && line.ToUpperInvariant().Contains(upperClassName)) {
                    foundInCurrentZone = true;
                }
            }

            return foundInCurrentZone ? currentZone : null;
        }

        public static string BuildHeapZonesCommand(string className, string memGraphPath) {
            string escaped = AddressTraceParser.EscapeForShell(className);
            string path = AddressTraceParser.EscapeForShell(memGraphPath);
            return $"-c \"heap --zones {escaped} {path} 2>/dev/null | head -100\"";
        }
    }
}
