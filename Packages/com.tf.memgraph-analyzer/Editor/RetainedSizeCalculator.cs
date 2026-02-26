using System.Collections.Generic;
using UnityEditor;

namespace Tools {
    public static class RetainedSizeCalculator {
        public static Dictionary<int, long> Calculate(CrawlerResult crawlerResult) {
            var retainedSizes = new Dictionary<int, long>();
            if (crawlerResult == null || crawlerResult.Objects.Count == 0)
                return retainedSizes;

            int objectCount = crawlerResult.Objects.Count;

            // Build reverse reference map: who references this object?
            var referencedBy = new Dictionary<int, List<int>>();
            foreach (var edge in crawlerResult.References) {
                if (!referencedBy.TryGetValue(edge.ToObjectIndex, out var list)) {
                    list = new List<int>();
                    referencedBy[edge.ToObjectIndex] = list;
                }
                list.Add(edge.FromObjectIndex);
            }

            // Build forward reference map: what does this object reference?
            var referencesTo = new Dictionary<int, List<int>>();
            foreach (var edge in crawlerResult.References) {
                if (!referencesTo.TryGetValue(edge.FromObjectIndex, out var list)) {
                    list = new List<int>();
                    referencesTo[edge.FromObjectIndex] = list;
                }
                list.Add(edge.ToObjectIndex);
            }

            // Simplified dominator calculation:
            // An object's retained size = its own size + sizes of objects only reachable through it
            // We use a simplified BFS approach: for each GC root, compute dominated subtree

            // First, find all GC roots
            var gcRoots = new List<int>();
            foreach (var obj in crawlerResult.Objects) {
                if (obj.IsGcRoot)
                    gcRoots.Add(obj.ObjectIndex);
            }

            // Compute immediate dominator for each object using simplified BFS
            var dominator = new int[objectCount];
            for (int i = 0; i < objectCount; i++)
                dominator[i] = -1;

            // Mark GC roots as self-dominating
            foreach (int root in gcRoots)
                dominator[root] = root;

            // BFS from GC roots
            var queue = new Queue<int>();
            var visited = new HashSet<int>();

            foreach (int root in gcRoots) {
                queue.Enqueue(root);
                visited.Add(root);
            }

            int processed = 0;
            while (queue.Count > 0) {
                int current = queue.Dequeue();
                processed++;

                if (processed % 10000 == 0) {
                    float progress = (float)processed / objectCount;
                    EditorUtility.DisplayProgressBar("Calculating Retained Sizes",
                        $"Processing {processed:N0} / {objectCount:N0}...", progress);
                }

                if (!referencesTo.TryGetValue(current, out var children))
                    continue;

                foreach (int child in children) {
                    if (child < 0 || child >= objectCount) continue;

                    if (visited.Add(child)) {
                        dominator[child] = current;
                        queue.Enqueue(child);
                    } else if (dominator[child] != -1 && dominator[child] != current) {
                        // Multiple parents -> dominated by common ancestor (simplified: mark as multi-parent)
                        dominator[child] = -2; // special: has multiple dominators
                    }
                }
            }

            EditorUtility.ClearProgressBar();

            // Calculate retained sizes bottom-up
            // Each object's retained = own size + sum of retained sizes of exclusively dominated children
            foreach (var obj in crawlerResult.Objects) {
                retainedSizes[obj.ObjectIndex] = obj.Size;
            }

            // Add exclusively dominated children's sizes to their dominators
            for (int i = 0; i < objectCount; i++) {
                int dom = dominator[i];
                if (dom >= 0 && dom != i && dom < objectCount) {
                    // This object is exclusively dominated by dom
                    if (retainedSizes.TryGetValue(i, out long childSize)) {
                        if (!retainedSizes.ContainsKey(dom))
                            retainedSizes[dom] = crawlerResult.Objects[dom].Size;
                        retainedSizes[dom] += childSize;
                    }
                }
            }

            return retainedSizes;
        }

        public static ReferenceChain FindReferenceChain(int targetIndex, CrawlerResult crawlerResult,
            TypeInfo[] types) {
            var chain = new ReferenceChain();
            if (crawlerResult == null || targetIndex < 0 || targetIndex >= crawlerResult.Objects.Count)
                return chain;

            // Build reverse reference map
            var referencedBy = new Dictionary<int, List<ReferenceEdge>>();
            foreach (var edge in crawlerResult.References) {
                if (!referencedBy.TryGetValue(edge.ToObjectIndex, out var list)) {
                    list = new List<ReferenceEdge>();
                    referencedBy[edge.ToObjectIndex] = list;
                }
                list.Add(edge);
            }

            // BFS from target back to a GC root
            var parentMap = new Dictionary<int, ReferenceEdge>();
            var visited = new HashSet<int> { targetIndex };
            var queue = new Queue<int>();
            queue.Enqueue(targetIndex);
            int gcRootIndex = -1;

            while (queue.Count > 0) {
                int current = queue.Dequeue();
                var obj = crawlerResult.Objects[current];

                if (obj.IsGcRoot) {
                    gcRootIndex = current;
                    break;
                }

                if (!referencedBy.TryGetValue(current, out var parents))
                    continue;

                foreach (var edge in parents) {
                    if (visited.Add(edge.FromObjectIndex)) {
                        parentMap[current] = edge;
                        queue.Enqueue(edge.FromObjectIndex);
                    }
                }
            }

            if (gcRootIndex < 0)
                return chain;

            // Reconstruct path from GC root to target
            var path = new List<int>();
            int walk = targetIndex;
            path.Add(walk);

            while (parentMap.TryGetValue(walk, out var edge)) {
                walk = edge.FromObjectIndex;
                path.Add(walk);
                if (walk == gcRootIndex) break;
            }

            path.Reverse();

            // Build chain nodes
            for (int i = 0; i < path.Count; i++) {
                int idx = path[i];
                var obj = crawlerResult.Objects[idx];
                string typeName = obj.TypeIndex >= 0 && obj.TypeIndex < types.Length
                    ? types[obj.TypeIndex].Name : "(unknown)";

                string fieldName = "";
                if (i < path.Count - 1) {
                    // Find the edge from path[i] to path[i+1]
                    foreach (var edge in crawlerResult.References) {
                        if (edge.FromObjectIndex == path[i] && edge.ToObjectIndex == path[i + 1]) {
                            fieldName = edge.FieldName;
                            break;
                        }
                    }
                }

                chain.Nodes.Add(new ReferenceChainNode {
                    ObjectIndex = idx,
                    TypeIndex = obj.TypeIndex,
                    TypeName = typeName,
                    FieldName = fieldName,
                    IsGcRoot = obj.IsGcRoot,
                });
            }

            return chain;
        }

        public static List<ReferenceEdge> GetIncomingReferences(int objectIndex,
            CrawlerResult crawlerResult) {
            var incoming = new List<ReferenceEdge>();
            if (crawlerResult == null) return incoming;

            foreach (var edge in crawlerResult.References) {
                if (edge.ToObjectIndex == objectIndex)
                    incoming.Add(edge);
            }
            return incoming;
        }

        public static List<ReferenceEdge> GetOutgoingReferences(int objectIndex,
            CrawlerResult crawlerResult) {
            var outgoing = new List<ReferenceEdge>();
            if (crawlerResult == null) return outgoing;

            foreach (var edge in crawlerResult.References) {
                if (edge.FromObjectIndex == objectIndex)
                    outgoing.Add(edge);
            }
            return outgoing;
        }
    }
}
