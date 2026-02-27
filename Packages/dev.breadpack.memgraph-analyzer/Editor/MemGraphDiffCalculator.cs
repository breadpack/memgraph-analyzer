using System;
using System.Collections.Generic;

namespace Tools {
    public static class MemGraphDiffCalculator {
        public static MemGraphDiffResult Calculate(MemGraphReport baseline, MemGraphReport current) {
            var result = new MemGraphDiffResult {
                Baseline = baseline,
                Current = current,
            };

            CalculateOverall(baseline, current, result.Overall);
            CalculateHeapDiff(baseline, current, result.Heap);
            CalculateVmmapDiff(baseline, current, result.Vmmap);
            CalculateFootprintDiff(baseline, current, result.Footprint);
            CalculateLeakDiff(baseline, current, result.Leaks);
            CalculateOwnerDiffs(baseline, current, result.OwnerDiffs);
            CalculatePluginDiffs(baseline, current, result.PluginDiffs);

            return result;
        }

        private static void CalculateOverall(MemGraphReport baseline, MemGraphReport current, OverallDiff diff) {
            diff.BaselineFootprint = baseline.Footprint.PhysFootprint > 0
                ? baseline.Footprint.PhysFootprint : baseline.Summary.TotalResident;
            diff.CurrentFootprint = current.Footprint.PhysFootprint > 0
                ? current.Footprint.PhysFootprint : current.Summary.TotalResident;
            diff.FootprintDelta = diff.CurrentFootprint - diff.BaselineFootprint;
            diff.FootprintDeltaPercent = SafePercent(diff.FootprintDelta, diff.BaselineFootprint);

            diff.BaselineResident = baseline.Summary.TotalResident;
            diff.CurrentResident = current.Summary.TotalResident;
            diff.ResidentDelta = diff.CurrentResident - diff.BaselineResident;
            diff.ResidentDeltaPercent = SafePercent(diff.ResidentDelta, diff.BaselineResident);

            diff.BaselineHeapTotal = baseline.Heap.TotalBytes;
            diff.CurrentHeapTotal = current.Heap.TotalBytes;
            diff.HeapDelta = diff.CurrentHeapTotal - diff.BaselineHeapTotal;
            diff.HeapDeltaPercent = SafePercent(diff.HeapDelta, diff.BaselineHeapTotal);

            diff.BaselineDirty = baseline.Summary.TotalDirty;
            diff.CurrentDirty = current.Summary.TotalDirty;
            diff.DirtyDelta = diff.CurrentDirty - diff.BaselineDirty;
            diff.DirtyDeltaPercent = SafePercent(diff.DirtyDelta, diff.BaselineDirty);

            diff.BaselineVirtual = baseline.Summary.TotalVirtual;
            diff.CurrentVirtual = current.Summary.TotalVirtual;
            diff.VirtualDelta = diff.CurrentVirtual - diff.BaselineVirtual;
            diff.VirtualDeltaPercent = SafePercent(diff.VirtualDelta, diff.BaselineVirtual);
        }

        private static void CalculateHeapDiff(MemGraphReport baseline, MemGraphReport current, HeapDiff diff) {
            var baseMap = new Dictionary<string, HeapAllocation>(StringComparer.OrdinalIgnoreCase);
            foreach (var alloc in baseline.Heap.Allocations) {
                if (!string.IsNullOrEmpty(alloc.ClassName))
                    baseMap[alloc.ClassName] = alloc;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Process current allocations
            foreach (var alloc in current.Heap.Allocations) {
                if (string.IsNullOrEmpty(alloc.ClassName)) continue;
                visited.Add(alloc.ClassName);

                var entry = new HeapAllocationDiff {
                    ClassName = alloc.ClassName,
                    Owner = alloc.Owner,
                    CurrentCount = alloc.Count,
                    CurrentBytes = alloc.TotalBytes,
                };

                if (baseMap.TryGetValue(alloc.ClassName, out var baseAlloc)) {
                    entry.BaselineCount = baseAlloc.Count;
                    entry.BaselineBytes = baseAlloc.TotalBytes;
                    entry.BytesDelta = entry.CurrentBytes - entry.BaselineBytes;
                    entry.CountDelta = entry.CurrentCount - entry.BaselineCount;
                    entry.BytesDeltaPercent = SafePercent(entry.BytesDelta, entry.BaselineBytes);

                    if (entry.BytesDelta > 0) {
                        entry.Direction = DiffDirection.Increased;
                        diff.IncreasedClassCount++;
                        diff.TotalGrowth += entry.BytesDelta;
                    } else if (entry.BytesDelta < 0) {
                        entry.Direction = DiffDirection.Decreased;
                        diff.DecreasedClassCount++;
                        diff.TotalShrink += Math.Abs(entry.BytesDelta);
                    } else {
                        entry.Direction = DiffDirection.Unchanged;
                        diff.UnchangedClassCount++;
                    }
                } else {
                    entry.Direction = DiffDirection.New;
                    entry.BytesDelta = entry.CurrentBytes;
                    entry.CountDelta = entry.CurrentCount;
                    entry.BytesDeltaPercent = 100f;
                    diff.NewClassCount++;
                    diff.TotalGrowth += entry.CurrentBytes;
                }

                diff.Allocations.Add(entry);
            }

            // Process removed allocations (in baseline but not current)
            foreach (var kv in baseMap) {
                if (visited.Contains(kv.Key)) continue;
                var baseAlloc = kv.Value;
                diff.Allocations.Add(new HeapAllocationDiff {
                    ClassName = baseAlloc.ClassName,
                    Owner = baseAlloc.Owner,
                    Direction = DiffDirection.Removed,
                    BaselineCount = baseAlloc.Count,
                    BaselineBytes = baseAlloc.TotalBytes,
                    CurrentCount = 0,
                    CurrentBytes = 0,
                    BytesDelta = -baseAlloc.TotalBytes,
                    CountDelta = -baseAlloc.Count,
                    BytesDeltaPercent = -100f,
                });
                diff.RemovedClassCount++;
                diff.TotalShrink += baseAlloc.TotalBytes;
            }
        }

        private static void CalculateVmmapDiff(MemGraphReport baseline, MemGraphReport current, VmmapDiff diff) {
            var baseMap = new Dictionary<string, VmmapSummaryRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in baseline.Vmmap.Summary) {
                if (!string.IsNullOrEmpty(row.RegionType))
                    baseMap[row.RegionType] = row;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in current.Vmmap.Summary) {
                if (string.IsNullOrEmpty(row.RegionType)) continue;
                visited.Add(row.RegionType);

                var entry = new VmmapRegionDiff {
                    RegionType = row.RegionType,
                    CurrentResident = row.ResidentSize,
                    CurrentDirty = row.DirtySize,
                    CurrentVirtual = row.VirtualSize,
                };

                if (baseMap.TryGetValue(row.RegionType, out var baseRow)) {
                    entry.BaselineResident = baseRow.ResidentSize;
                    entry.BaselineDirty = baseRow.DirtySize;
                    entry.BaselineVirtual = baseRow.VirtualSize;
                    entry.ResidentDelta = entry.CurrentResident - entry.BaselineResident;
                    entry.DirtyDelta = entry.CurrentDirty - entry.BaselineDirty;
                    entry.VirtualDelta = entry.CurrentVirtual - entry.BaselineVirtual;
                    entry.ResidentDeltaPercent = SafePercent(entry.ResidentDelta, entry.BaselineResident);
                    entry.Direction = entry.ResidentDelta > 0 ? DiffDirection.Increased
                        : entry.ResidentDelta < 0 ? DiffDirection.Decreased
                        : DiffDirection.Unchanged;
                } else {
                    entry.Direction = DiffDirection.New;
                    entry.ResidentDelta = entry.CurrentResident;
                    entry.DirtyDelta = entry.CurrentDirty;
                    entry.VirtualDelta = entry.CurrentVirtual;
                    entry.ResidentDeltaPercent = 100f;
                }

                diff.Regions.Add(entry);
            }

            foreach (var kv in baseMap) {
                if (visited.Contains(kv.Key)) continue;
                diff.Regions.Add(new VmmapRegionDiff {
                    RegionType = kv.Key,
                    Direction = DiffDirection.Removed,
                    BaselineResident = kv.Value.ResidentSize,
                    BaselineDirty = kv.Value.DirtySize,
                    BaselineVirtual = kv.Value.VirtualSize,
                    ResidentDelta = -kv.Value.ResidentSize,
                    DirtyDelta = -kv.Value.DirtySize,
                    VirtualDelta = -kv.Value.VirtualSize,
                    ResidentDeltaPercent = -100f,
                });
            }
        }

        private static void CalculateFootprintDiff(MemGraphReport baseline, MemGraphReport current, FootprintDiff diff) {
            diff.BaselinePhysFootprint = baseline.Footprint.PhysFootprint;
            diff.CurrentPhysFootprint = current.Footprint.PhysFootprint;
            diff.PhysFootprintDelta = diff.CurrentPhysFootprint - diff.BaselinePhysFootprint;

            diff.BaselineDirty = baseline.Footprint.TotalDirty;
            diff.CurrentDirty = current.Footprint.TotalDirty;
            diff.DirtyDelta = diff.CurrentDirty - diff.BaselineDirty;

            diff.BaselineClean = baseline.Footprint.TotalClean;
            diff.CurrentClean = current.Footprint.TotalClean;
            diff.CleanDelta = diff.CurrentClean - diff.BaselineClean;

            diff.BaselineReclaimable = baseline.Footprint.TotalReclaimable;
            diff.CurrentReclaimable = current.Footprint.TotalReclaimable;
            diff.ReclaimableDelta = diff.CurrentReclaimable - diff.BaselineReclaimable;
        }

        private static void CalculateLeakDiff(MemGraphReport baseline, MemGraphReport current, LeakDiff diff) {
            diff.BaselineCount = baseline.Leaks.TotalLeakCount;
            diff.CurrentCount = current.Leaks.TotalLeakCount;
            diff.CountDelta = diff.CurrentCount - diff.BaselineCount;

            diff.BaselineBytes = baseline.Leaks.TotalLeakBytes;
            diff.CurrentBytes = current.Leaks.TotalLeakBytes;
            diff.BytesDelta = diff.CurrentBytes - diff.BaselineBytes;
        }

        private static void CalculateOwnerDiffs(MemGraphReport baseline, MemGraphReport current,
            List<BreakdownDiff<MemoryOwner>> diffs) {
            var allOwners = new HashSet<MemoryOwner>();
            foreach (var kv in baseline.Summary.OwnerBreakdowns) allOwners.Add(kv.Key);
            foreach (var kv in current.Summary.OwnerBreakdowns) allOwners.Add(kv.Key);

            foreach (var owner in allOwners) {
                baseline.Summary.OwnerBreakdowns.TryGetValue(owner, out long baseBytes);
                current.Summary.OwnerBreakdowns.TryGetValue(owner, out long curBytes);
                long delta = curBytes - baseBytes;
                diffs.Add(new BreakdownDiff<MemoryOwner> {
                    Key = owner,
                    BaselineBytes = baseBytes,
                    CurrentBytes = curBytes,
                    BytesDelta = delta,
                    BytesDeltaPercent = SafePercent(delta, baseBytes),
                    Direction = delta > 0 ? DiffDirection.Increased
                        : delta < 0 ? DiffDirection.Decreased
                        : DiffDirection.Unchanged,
                });
            }
        }

        private static void CalculatePluginDiffs(MemGraphReport baseline, MemGraphReport current,
            List<BreakdownDiff<string>> diffs) {
            var allPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in baseline.Summary.PluginBreakdowns) allPlugins.Add(kv.Key);
            foreach (var kv in current.Summary.PluginBreakdowns) allPlugins.Add(kv.Key);

            foreach (var plugin in allPlugins) {
                baseline.Summary.PluginBreakdowns.TryGetValue(plugin, out long baseBytes);
                current.Summary.PluginBreakdowns.TryGetValue(plugin, out long curBytes);
                long delta = curBytes - baseBytes;
                diffs.Add(new BreakdownDiff<string> {
                    Key = plugin,
                    BaselineBytes = baseBytes,
                    CurrentBytes = curBytes,
                    BytesDelta = delta,
                    BytesDeltaPercent = SafePercent(delta, baseBytes),
                    Direction = delta > 0 ? DiffDirection.Increased
                        : delta < 0 ? DiffDirection.Decreased
                        : DiffDirection.Unchanged,
                });
            }
        }

        private static float SafePercent(long delta, long baseline) {
            if (baseline == 0) return delta != 0 ? 100f : 0f;
            return (float)delta / baseline * 100f;
        }
    }
}
