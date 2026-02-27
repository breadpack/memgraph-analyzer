using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools {
    public static class AssemblyAnalyzer {
        public static List<AssemblyInfo> Analyze(TypeInfo[] types, List<NativeObjectInfo> nativeObjects,
            CrawlerResult crawlerResult) {
            if (types == null || types.Length == 0)
                return new List<AssemblyInfo>();

            var assemblyGroups = GroupByAssembly(types);
            var assemblies = new List<AssemblyInfo>();

            foreach (var kv in assemblyGroups) {
                string assemblyName = kv.Key;
                var assemblyTypes = kv.Value;

                var namespaces = GroupByNamespace(assemblyTypes);
                var classification = SnapshotLoader.ClassifyAssembly(assemblyName);

                long totalSize = 0;
                int totalInstanceCount = 0;
                var namespaceInfos = new List<NamespaceInfo>();

                foreach (var nsKv in namespaces) {
                    var nsInfo = new NamespaceInfo {
                        Name = string.IsNullOrEmpty(nsKv.Key) ? "(global)" : nsKv.Key,
                        TypeCount = nsKv.Value.Count,
                    };

                    long nsSize = 0;
                    int nsInstances = 0;
                    foreach (var t in nsKv.Value) {
                        nsSize += t.TotalInstanceSize > 0 ? t.TotalInstanceSize : t.BaseSize;
                        nsInstances += t.InstanceCount;
                        nsInfo.Types.Add(t);
                    }
                    nsInfo.TotalSize = nsSize;
                    nsInfo.InstanceCount = nsInstances;

                    totalSize += nsSize;
                    totalInstanceCount += nsInstances;
                    namespaceInfos.Add(nsInfo);
                }

                // Sort namespaces by size descending
                namespaceInfos.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));

                var asmInfo = new AssemblyInfo {
                    Name = assemblyName,
                    Classification = classification,
                    TotalSize = totalSize,
                    TypeCount = assemblyTypes.Count,
                    InstanceCount = totalInstanceCount,
                };
                asmInfo.Namespaces.AddRange(namespaceInfos);
                assemblies.Add(asmInfo);
            }

            // Sort assemblies by size descending
            assemblies.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));

            return assemblies;
        }

        public static void MergeInstanceData(List<AssemblyInfo> assemblies, TypeInfo[] types) {
            if (assemblies == null || types == null) return;

            foreach (var asm in assemblies) {
                long totalSize = 0;
                int totalInstances = 0;

                foreach (var ns in asm.Namespaces) {
                    long nsSize = 0;
                    int nsInstances = 0;

                    foreach (var t in ns.Types) {
                        long typeSize = t.TotalInstanceSize > 0 ? t.TotalInstanceSize : t.BaseSize;
                        nsSize += typeSize;
                        nsInstances += t.InstanceCount;
                    }

                    // Re-sort types within namespace by size
                    ns.Types.Sort((a, b) => {
                        long sa = a.TotalInstanceSize > 0 ? a.TotalInstanceSize : a.BaseSize;
                        long sb = b.TotalInstanceSize > 0 ? b.TotalInstanceSize : b.BaseSize;
                        return sb.CompareTo(sa);
                    });

                    ns.TotalSize = nsSize;
                    ns.InstanceCount = nsInstances;
                    totalSize += nsSize;
                    totalInstances += nsInstances;
                }

                // Re-sort namespaces within assembly by size
                asm.Namespaces.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));

                asm.TotalSize = totalSize;
                asm.InstanceCount = totalInstances;
            }

            // Re-sort assemblies
            assemblies.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));
        }

        public static void BuildNativeTypeSummaries(SnapshotReport report) {
            if (report.NativeObjects == null || report.NativeObjects.Count == 0) return;

            var grouped = new Dictionary<string, NativeTypeSummary>();

            foreach (var obj in report.NativeObjects) {
                string key = obj.NativeTypeName ?? "(unknown)";
                if (!grouped.TryGetValue(key, out var summary)) {
                    summary = new NativeTypeSummary {
                        TypeName = key,
                        Category = obj.Category,
                    };
                    grouped[key] = summary;
                }
                summary.ObjectCount++;
                summary.TotalSize += obj.Size;
            }

            report.NativeTypeSummaries.Clear();
            report.NativeTypeSummaries.AddRange(grouped.Values.OrderByDescending(s => s.TotalSize));
        }

        private static Dictionary<string, List<TypeInfo>> GroupByAssembly(TypeInfo[] types) {
            var groups = new Dictionary<string, List<TypeInfo>>();

            foreach (var t in types) {
                string assembly = string.IsNullOrEmpty(t.Assembly) ? "(unknown)" : t.Assembly;
                if (!groups.TryGetValue(assembly, out var list)) {
                    list = new List<TypeInfo>();
                    groups[assembly] = list;
                }
                list.Add(t);
            }

            return groups;
        }

        private static Dictionary<string, List<TypeInfo>> GroupByNamespace(List<TypeInfo> types) {
            var groups = new Dictionary<string, List<TypeInfo>>();

            foreach (var t in types) {
                string ns = t.Namespace ?? "";
                if (!groups.TryGetValue(ns, out var list)) {
                    list = new List<TypeInfo>();
                    groups[ns] = list;
                }
                list.Add(t);
            }

            return groups;
        }
    }
}
