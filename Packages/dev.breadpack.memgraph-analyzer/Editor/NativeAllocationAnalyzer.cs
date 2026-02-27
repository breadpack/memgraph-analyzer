using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools {
    public static class NativeAllocationAnalyzer {
        public static NativeAllocationAnalysis Analyze(SnapshotReport report) {
            var result = new NativeAllocationAnalysis();

            if (report.NativeAllocations.Count == 0) return result;

            BuildRegionTree(report, result);
            GroupByRootReferences(report, result);
            GroupByLabels(report, result);
            MatchAllocationsToObjects(report, result);
            ResolveCallstacks(report, result);
            CalculateOverheadTotals(report, result);

            return result;
        }

        #region Region Hierarchy Tree

        private static void BuildRegionTree(SnapshotReport report, NativeAllocationAnalysis result) {
            if (report.NativeMemoryRegions.Count == 0) return;

            // Build region nodes
            var nodes = new Dictionary<int, RegionTreeNode>();
            for (int i = 0; i < report.NativeMemoryRegions.Count; i++) {
                var region = report.NativeMemoryRegions[i];
                nodes[i] = new RegionTreeNode {
                    RegionIndex = i,
                    Name = region.Name,
                };
            }

            // Calculate per-region allocation sizes from allocations
            foreach (var alloc in report.NativeAllocations) {
                int regionIdx = alloc.MemoryRegionIndex;
                if (regionIdx >= 0 && nodes.TryGetValue(regionIdx, out var node)) {
                    node.TotalSize += (long)alloc.Size;
                    node.AllocationCount++;
                    node.OverheadSize += (long)alloc.OverheadSize;
                }
            }

            // Build tree structure
            var rootNodes = new List<RegionTreeNode>();
            for (int i = 0; i < report.NativeMemoryRegions.Count; i++) {
                var region = report.NativeMemoryRegions[i];
                int parentIdx = region.ParentIndex;

                if (parentIdx < 0 || parentIdx == i || !nodes.ContainsKey(parentIdx)) {
                    rootNodes.Add(nodes[i]);
                } else {
                    nodes[parentIdx].Children.Add(nodes[i]);
                }
            }

            // Propagate sizes from children to parents (bottom-up)
            foreach (var root in rootNodes) {
                PropagateRegionSizes(root);
            }

            // Sort by size descending
            rootNodes.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));

            result.RegionTree.AddRange(rootNodes);
        }

        private static void PropagateRegionSizes(RegionTreeNode node) {
            foreach (var child in node.Children) {
                PropagateRegionSizes(child);
                node.TotalSize += child.TotalSize;
                node.AllocationCount += child.AllocationCount;
                node.OverheadSize += child.OverheadSize;
            }
            node.Children.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));
        }

        #endregion

        #region Root Reference Grouping

        private static void GroupByRootReferences(SnapshotReport report, NativeAllocationAnalysis result) {
            if (report.NativeRootReferences.Count == 0) return;

            // Build root reference lookup
            var rootRefById = new Dictionary<long, NativeRootReferenceInfo>();
            foreach (var rootRef in report.NativeRootReferences) {
                rootRefById[rootRef.Id] = rootRef;
            }

            // Group allocations by rootReferenceId
            var groups = new Dictionary<int, RootRefGroup>();
            for (int i = 0; i < report.NativeAllocations.Count; i++) {
                var alloc = report.NativeAllocations[i];
                int refId = alloc.RootReferenceId;

                if (!groups.TryGetValue(refId, out var group)) {
                    string areaName = "Unknown";
                    string objectName = "Unknown";
                    if (rootRefById.TryGetValue(refId, out var rootRef)) {
                        areaName = rootRef.AreaName;
                        objectName = rootRef.ObjectName;
                    }

                    group = new RootRefGroup {
                        RootReferenceId = refId,
                        AreaName = areaName,
                        ObjectName = objectName,
                    };
                    groups[refId] = group;
                }

                group.TotalSize += (long)alloc.Size;
                group.AllocationCount++;
                group.Allocations.Add(i);
            }

            // Link native objects to root reference groups
            foreach (var obj in report.NativeObjects) {
                int refId = obj.RootReferenceId;
                if (groups.TryGetValue(refId, out var group)) {
                    group.LinkedNativeObjects.Add(obj.NativeObjectListIndex);
                }
            }

            // Sort by total size descending
            var sorted = groups.Values.ToList();
            sorted.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));

            result.RootReferenceBreakdown.AddRange(sorted);
        }

        #endregion

        #region Label Grouping

        private static void GroupByLabels(SnapshotReport report, NativeAllocationAnalysis result) {
            if (report.NativeMemoryLabels.Count == 0 || report.NativeAllocationSites.Count == 0) return;

            // Build site → label index map
            var siteLabelMap = new Dictionary<int, int>();
            foreach (var site in report.NativeAllocationSites) {
                siteLabelMap[site.Id] = site.MemoryLabelIndex;
            }

            // Group allocations by label
            var labelSizes = new Dictionary<int, (long totalSize, int count)>();
            foreach (var alloc in report.NativeAllocations) {
                int siteId = alloc.AllocationSiteId;
                if (!siteLabelMap.TryGetValue(siteId, out int labelIdx)) continue;

                if (!labelSizes.TryGetValue(labelIdx, out var entry))
                    entry = (0, 0);

                labelSizes[labelIdx] = (entry.totalSize + (long)alloc.Size, entry.count + 1);
            }

            foreach (var kv in labelSizes) {
                string labelName = kv.Key >= 0 && kv.Key < report.NativeMemoryLabels.Count
                    ? report.NativeMemoryLabels[kv.Key].Name
                    : $"Label_{kv.Key}";

                result.LabelBreakdown.Add(new LabelGroup {
                    Name = labelName,
                    TotalSize = kv.Value.totalSize,
                    AllocationCount = kv.Value.count,
                });
            }

            result.LabelBreakdown.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));
        }

        #endregion

        #region Address-Based Object Matching

        private static void MatchAllocationsToObjects(SnapshotReport report, NativeAllocationAnalysis result) {
            if (report.NativeObjects.Count == 0) return;

            // Build address → native object index map
            var addressToObject = new Dictionary<ulong, int>();
            for (int i = 0; i < report.NativeObjects.Count; i++) {
                ulong addr = report.NativeObjects[i].NativeObjectAddress;
                if (addr != 0)
                    addressToObject[addr] = i;
            }

            for (int i = 0; i < report.NativeAllocations.Count; i++) {
                var alloc = report.NativeAllocations[i];
                if (addressToObject.TryGetValue(alloc.Address, out int objIdx)) {
                    result.AllocationToObjectMap[i] = objIdx;
                } else {
                    result.UnmappedAllocations.Add(i);
                }
            }
        }

        #endregion

        #region Callstack Resolution

        private static void ResolveCallstacks(SnapshotReport report, NativeAllocationAnalysis result) {
            if (report.NativeAllocationSites.Count == 0 || report.NativeCallstackSymbols.Count == 0) return;

            // Build site ID → site object map
            var siteById = new Dictionary<int, NativeAllocationSiteInfo>();
            foreach (var site in report.NativeAllocationSites) {
                siteById[site.Id] = site;
            }

            // Resolve each allocation site's callstack symbols
            foreach (var kv in siteById) {
                var site = kv.Value;
                if (site.CallstackSymbolIndices == null || site.CallstackSymbolIndices.Length == 0) continue;

                var frames = new List<string>();
                foreach (int symbolIdx in site.CallstackSymbolIndices) {
                    if (symbolIdx >= 0 && symbolIdx < report.NativeCallstackSymbols.Count) {
                        var sym = report.NativeCallstackSymbols[symbolIdx];
                        string frame = !string.IsNullOrEmpty(sym.ReadableStackTrace)
                            ? sym.ReadableStackTrace
                            : $"0x{sym.Symbol:X}";
                        frames.Add(frame);
                    }
                }

                if (frames.Count > 0) {
                    result.ResolvedCallstacks[kv.Key] = frames.ToArray();
                }
            }
        }

        #endregion

        #region Overhead Totals

        private static void CalculateOverheadTotals(SnapshotReport report, NativeAllocationAnalysis result) {
            long totalOverhead = 0;
            long totalPadding = 0;
            long totalUseful = 0;

            foreach (var alloc in report.NativeAllocations) {
                totalOverhead += (long)alloc.OverheadSize;
                totalPadding += (long)alloc.PaddingSize;
                totalUseful += (long)alloc.Size;
            }

            result.TotalOverhead = totalOverhead;
            result.TotalPadding = totalPadding;
            result.TotalUseful = totalUseful;
        }

        #endregion
    }
}
