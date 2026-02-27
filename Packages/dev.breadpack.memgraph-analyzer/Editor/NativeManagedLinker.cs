using System.Collections.Generic;
using UnityEngine;

namespace Tools {
    public static class NativeManagedLinker {
        /// <summary>
        /// Decodes Connection indices and builds native↔managed object links.
        /// Connection index encoding:
        ///   0..NativeObjectCount-1                           = native object index
        ///   NativeObjectCount..NativeObjectCount+GcHandleCount-1 = GC handle index
        /// </summary>
        public static NativeManagedLinkResult Link(SnapshotReport report) {
            var result = new NativeManagedLinkResult();
            int nativeCount = report.NativeObjectCount;
            int gcHandleCount = report.GcHandleCount;

            // Step 1: Build GcHandleIndex → ManagedObjectIndex map from crawler result
            var gcHandleToManaged = new Dictionary<int, int>();
            if (report.CrawlerResult != null) {
                foreach (var obj in report.CrawlerResult.Objects) {
                    if (obj.GcHandleIndex >= 0) {
                        gcHandleToManaged[obj.GcHandleIndex] = obj.ObjectIndex;
                    }
                }
            }

            // Step 2: Decode connections — find native↔gcHandle links
            var nativeToGcHandle = new Dictionary<int, int>();
            foreach (var conn in report.Connections) {
                int from = conn.From;
                int to = conn.To;

                // native → gcHandle
                if (from >= 0 && from < nativeCount &&
                    to >= nativeCount && to < nativeCount + gcHandleCount) {
                    int gcIdx = to - nativeCount;
                    nativeToGcHandle.TryAdd(from, gcIdx);
                }

                // gcHandle → native
                if (from >= nativeCount && from < nativeCount + gcHandleCount &&
                    to >= 0 && to < nativeCount) {
                    int gcIdx = from - nativeCount;
                    nativeToGcHandle.TryAdd(to, gcIdx);
                }
            }

            // Step 3: Build links from connection-decoded gcHandle mappings
            var linked = new HashSet<int>();
            foreach (var kv in nativeToGcHandle) {
                int nativeIdx = kv.Key;
                int gcIdx = kv.Value;

                if (gcHandleToManaged.TryGetValue(gcIdx, out int managedIdx)) {
                    AddLink(result, nativeIdx, managedIdx, gcIdx);
                    linked.Add(nativeIdx);
                }
            }

            // Step 4: Fallback — use NativeObjectInfo.GcHandleIndex for objects not yet linked
            for (int i = 0; i < report.NativeObjects.Count; i++) {
                var nativeObj = report.NativeObjects[i];
                if (linked.Contains(i)) continue;
                if (nativeObj.GcHandleIndex < 0) continue;

                if (gcHandleToManaged.TryGetValue(nativeObj.GcHandleIndex, out int managedIdx)) {
                    AddLink(result, i, managedIdx, nativeObj.GcHandleIndex);
                    linked.Add(i);
                }
            }

            // Step 5: Fill ManagedObjectInstance.NativeObjectIndex
            if (report.CrawlerResult != null) {
                foreach (var link in result.Links) {
                    if (link.ManagedObjectIndex >= 0 && link.ManagedObjectIndex < report.CrawlerResult.Objects.Count) {
                        report.CrawlerResult.Objects[link.ManagedObjectIndex].NativeObjectIndex = link.NativeObjectIndex;
                    }
                }
            }

            // Step 6: Calculate native retained size per managed object
            foreach (var kv in result.ManagedToNatives) {
                long totalNativeSize = 0;
                foreach (int nativeIdx in kv.Value) {
                    if (nativeIdx >= 0 && nativeIdx < report.NativeObjects.Count) {
                        totalNativeSize += report.NativeObjects[nativeIdx].Size;
                    }
                }
                result.NativeRetainedByManaged[kv.Key] = totalNativeSize;
            }

            result.LinkedCount = linked.Count;
            result.UnlinkedNativeCount = report.NativeObjects.Count - linked.Count;

            return result;
        }

        /// <summary>
        /// Quick mode: links only via GcHandleIndex when no CrawlerResult is available.
        /// Creates a reduced link result without managed object details.
        /// </summary>
        public static NativeManagedLinkResult LinkQuick(SnapshotReport report) {
            var result = new NativeManagedLinkResult();
            int linkedCount = 0;

            for (int i = 0; i < report.NativeObjects.Count; i++) {
                var obj = report.NativeObjects[i];
                if (obj.GcHandleIndex >= 0) {
                    // Without crawler, we can't resolve to managed object index
                    // but we know a link exists
                    result.Links.Add(new NativeManagedLink {
                        NativeObjectIndex = i,
                        ManagedObjectIndex = -1,
                        GcHandleIndex = obj.GcHandleIndex,
                    });
                    linkedCount++;
                }
            }

            result.LinkedCount = linkedCount;
            result.UnlinkedNativeCount = report.NativeObjects.Count - linkedCount;

            return result;
        }

        private static void AddLink(NativeManagedLinkResult result, int nativeIdx, int managedIdx, int gcHandleIdx) {
            result.Links.Add(new NativeManagedLink {
                NativeObjectIndex = nativeIdx,
                ManagedObjectIndex = managedIdx,
                GcHandleIndex = gcHandleIdx,
            });

            result.NativeToManaged[nativeIdx] = managedIdx;

            if (!result.ManagedToNatives.TryGetValue(managedIdx, out var list)) {
                list = new List<int>();
                result.ManagedToNatives[managedIdx] = list;
            }
            list.Add(nativeIdx);
        }
    }
}
