using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools {
    public static class SnapshotInsightGenerator {
        private const long MB = 1024L * 1024;

        public static SnapshotInsightResult Analyze(SnapshotReport report, NativeManagedLinkResult linkResult) {
            var result = new SnapshotInsightResult();

            DetectDuplicateAssets(report, result);
            DetectUnreferencedNatives(report, linkResult, result);
            DetectLargeStaticRetention(report, linkResult, result);
            DetectEmptyGameObjects(report, result);
            DetectTypeExplosion(report, result);
            DetectLargeNativeOwnership(report, linkResult, result);

            // Native allocation insights
            DetectHighOverhead(report, result);
            DetectUnmappedAllocations(report, result);
            DetectSubsystemHotspots(report, result);

            RankInsights(result);

            result.TotalEstimatedSavings = 0;
            foreach (var insight in result.Insights) {
                result.TotalEstimatedSavings += insight.EstimatedSavings;
            }

            return result;
        }

        #region Duplicate Assets

        private static void DetectDuplicateAssets(SnapshotReport report, SnapshotInsightResult result) {
            // Group by (Name + NativeTypeName + Size) to find duplicates
            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < report.NativeObjects.Count; i++) {
                var obj = report.NativeObjects[i];
                if (string.IsNullOrEmpty(obj.Name) || obj.Size <= 0) continue;
                // Only check asset-like categories
                if (obj.Category == NativeTypeCategory.Component ||
                    obj.Category == NativeTypeCategory.Other) continue;

                string key = $"{obj.Name}|{obj.NativeTypeName}|{obj.Size}";
                if (!groups.TryGetValue(key, out var list)) {
                    list = new List<int>();
                    groups[key] = list;
                }
                list.Add(i);
            }

            long totalWasted = 0;
            foreach (var kv in groups) {
                if (kv.Value.Count <= 1) continue;

                var first = report.NativeObjects[kv.Value[0]];
                long wastedSize = (long)(kv.Value.Count - 1) * first.Size;

                result.DuplicateAssets.Add(new DuplicateAssetGroup {
                    Name = first.Name,
                    NativeTypeName = first.NativeTypeName,
                    Count = kv.Value.Count,
                    IndividualSize = first.Size,
                    TotalWastedSize = wastedSize,
                });
                totalWasted += wastedSize;
            }

            // Sort by wasted size descending
            result.DuplicateAssets.Sort((a, b) => b.TotalWastedSize.CompareTo(a.TotalWastedSize));

            if (totalWasted <= 0) return;

            var severity = totalWasted > 10 * MB
                ? SnapshotInsightSeverity.Critical
                : totalWasted > MB ? SnapshotInsightSeverity.Warning : SnapshotInsightSeverity.Info;

            int dupCount = result.DuplicateAssets.Count;
            string topNames = string.Join(", ", result.DuplicateAssets.Take(3).Select(d => d.Name));

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.DuplicateAsset,
                Title = $"Duplicate Assets Detected ({dupCount} groups)",
                Description = $"{dupCount} asset groups have duplicates, wasting {VmmapParser.FormatSize(totalWasted)}. " +
                              $"Top: {topNames}",
                Recommendation = "Use asset deduplication. Share materials/textures via references instead of copies. " +
                                 "Check AssetBundle build settings for duplicate inclusion.",
                EstimatedSavings = totalWasted,
            });
        }

        #endregion

        #region Unreferenced Natives

        private static void DetectUnreferencedNatives(SnapshotReport report,
            NativeManagedLinkResult linkResult, SnapshotInsightResult result) {
            // Only flag key asset categories
            var assetCategories = new HashSet<NativeTypeCategory> {
                NativeTypeCategory.Texture, NativeTypeCategory.Mesh,
                NativeTypeCategory.AudioClip, NativeTypeCategory.Material,
            };

            long totalUnreferenced = 0;
            for (int i = 0; i < report.NativeObjects.Count; i++) {
                var obj = report.NativeObjects[i];
                if (!assetCategories.Contains(obj.Category)) continue;
                if (obj.Size <= 0) continue;

                bool hasGcHandle = obj.GcHandleIndex >= 0;
                bool hasLink = linkResult != null && linkResult.NativeToManaged.ContainsKey(i);

                if (hasGcHandle || hasLink) continue;

                string reason = "No GC handle and no managed reference found";
                result.UnreferencedAssets.Add(new UnreferencedAsset {
                    NativeObjectIndex = i,
                    Name = obj.Name,
                    Size = obj.Size,
                    Reason = reason,
                });
                totalUnreferenced += obj.Size;
            }

            // Sort by size descending
            result.UnreferencedAssets.Sort((a, b) => b.Size.CompareTo(a.Size));

            if (totalUnreferenced <= 0) return;

            var severity = totalUnreferenced > 50 * MB
                ? SnapshotInsightSeverity.Critical
                : totalUnreferenced > 5 * MB ? SnapshotInsightSeverity.Warning : SnapshotInsightSeverity.Info;

            int count = result.UnreferencedAssets.Count;
            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.UnreferencedNative,
                Title = $"Unreferenced Native Assets ({count})",
                Description = $"{count} native assets ({VmmapParser.FormatSize(totalUnreferenced)}) have no managed references. " +
                              $"These may be leaked or loaded unnecessarily.",
                Recommendation = "Call Resources.UnloadUnusedAssets() or Addressables.Release(). " +
                                 "Verify asset lifecycle management in your loading code.",
                EstimatedSavings = totalUnreferenced,
            });
        }

        #endregion

        #region Large Static Retention

        private static void DetectLargeStaticRetention(SnapshotReport report,
            NativeManagedLinkResult linkResult, SnapshotInsightResult result) {
            if (report.CrawlerResult == null) return;

            long totalStaticRetained = 0;
            var largeStaticRoots = new List<int>();

            foreach (var obj in report.CrawlerResult.Objects) {
                if (!obj.IsGcRoot || obj.GcHandleIndex >= 0) continue; // static field root (not gc handle root)

                long retained = 0;
                if (report.RetainedSizes != null && report.RetainedSizes.TryGetValue(obj.ObjectIndex, out long retSize))
                    retained = retSize;

                long nativeRetained = 0;
                if (linkResult != null && linkResult.NativeRetainedByManaged.TryGetValue(obj.ObjectIndex, out long nrSize))
                    nativeRetained = nrSize;

                long total = retained + nativeRetained;
                if (total > 5 * MB) {
                    totalStaticRetained += total;
                    largeStaticRoots.Add(obj.ObjectIndex);
                }
            }

            if (totalStaticRetained <= 0) return;

            var severity = totalStaticRetained > 50 * MB
                ? SnapshotInsightSeverity.Critical
                : SnapshotInsightSeverity.Warning;

            // Build description with top root type names
            var topTypes = new List<string>();
            foreach (int idx in largeStaticRoots.Take(3)) {
                var obj = report.CrawlerResult.Objects[idx];
                if (obj.TypeIndex >= 0 && obj.TypeIndex < report.Types.Length)
                    topTypes.Add(report.Types[obj.TypeIndex].Name);
            }
            string typeList = topTypes.Count > 0 ? string.Join(", ", topTypes) : "unknown types";

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.LargeStaticRetention,
                Title = $"Large Static Field Retention ({largeStaticRoots.Count} roots)",
                Description = $"Static fields retain {VmmapParser.FormatSize(totalStaticRetained)} including native objects. " +
                              $"Top types: {typeList}",
                Recommendation = "Move large data from static fields to instance fields with proper lifecycle. " +
                                 "Use weak references or lazy loading for caches.",
                EstimatedSavings = totalStaticRetained / 2, // conservative: 50% reducible
                RelatedObjectIndices = largeStaticRoots,
            });
        }

        #endregion

        #region Empty GameObjects

        private static void DetectEmptyGameObjects(SnapshotReport report, SnapshotInsightResult result) {
            int emptyCount = 0;
            long totalSize = 0;

            foreach (var obj in report.NativeObjects) {
                if (obj.NativeTypeName != "GameObject") continue;
                if (obj.Size < 500) {
                    emptyCount++;
                    totalSize += obj.Size;
                }
            }

            if (emptyCount <= 500) return;

            var severity = emptyCount > 2000
                ? SnapshotInsightSeverity.Warning
                : SnapshotInsightSeverity.Info;

            long avgSize = emptyCount > 0 ? totalSize / emptyCount : 0;
            long savings = (long)(emptyCount * avgSize * 0.3);

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.EmptyGameObjects,
                Title = $"Many Small GameObjects ({emptyCount:N0})",
                Description = $"{emptyCount:N0} GameObjects with <500 bytes each (total: {VmmapParser.FormatSize(totalSize)}). " +
                              $"These may be empty containers adding overhead.",
                Recommendation = "Flatten hierarchy where possible. Use ECS for large entity counts. " +
                                 "Remove unnecessary empty GameObjects.",
                EstimatedSavings = savings,
            });
        }

        #endregion

        #region Type Explosion

        private static void DetectTypeExplosion(SnapshotReport report, SnapshotInsightResult result) {
            if (report.Types == null || report.Types.Length == 0) return;

            // Check user code assemblies for type explosion
            var userAssemblies = new Dictionary<string, (int typeCount, long totalSize)>();
            foreach (var type in report.Types) {
                if (SnapshotLoader.ClassifyAssembly(type.Assembly) != AssemblyClassification.UserCode) continue;
                if (string.IsNullOrEmpty(type.Assembly)) continue;

                if (!userAssemblies.TryGetValue(type.Assembly, out var entry))
                    entry = (0, 0);

                long size = type.TotalInstanceSize > 0 ? type.TotalInstanceSize : type.BaseSize;
                userAssemblies[type.Assembly] = (entry.typeCount + 1, entry.totalSize + size);
            }

            foreach (var kv in userAssemblies) {
                if (kv.Value.typeCount <= 200 || kv.Value.totalSize >= MB) continue;

                long savings = (long)kv.Value.typeCount * 200;
                result.Insights.Add(new SnapshotInsight {
                    Severity = SnapshotInsightSeverity.Info,
                    Category = SnapshotInsightCategory.TypeExplosion,
                    Title = $"Type Explosion in {kv.Key}",
                    Description = $"{kv.Value.typeCount} types but only {VmmapParser.FormatSize(kv.Value.totalSize)} total size. " +
                                  $"Many small types add metadata overhead.",
                    Recommendation = "Consider consolidating small types. Review if generics or code generation " +
                                     "are creating excessive type definitions.",
                    EstimatedSavings = savings,
                });
            }
        }

        #endregion

        #region Large Native Ownership

        private static void DetectLargeNativeOwnership(SnapshotReport report,
            NativeManagedLinkResult linkResult, SnapshotInsightResult result) {
            if (linkResult == null || report.CrawlerResult == null) return;

            // Find managed objects owning large amounts of native memory
            var largeOwners = new List<(int managedIdx, long nativeSize, string typeName)>();

            foreach (var kv in linkResult.NativeRetainedByManaged) {
                if (kv.Value < 10 * MB) continue;

                string typeName = "Unknown";
                if (kv.Key >= 0 && kv.Key < report.CrawlerResult.Objects.Count) {
                    int typeIdx = report.CrawlerResult.Objects[kv.Key].TypeIndex;
                    if (typeIdx >= 0 && typeIdx < report.Types.Length)
                        typeName = report.Types[typeIdx].Name;
                }
                largeOwners.Add((kv.Key, kv.Value, typeName));
            }

            if (largeOwners.Count == 0) return;

            largeOwners.Sort((a, b) => b.nativeSize.CompareTo(a.nativeSize));
            long totalOwned = largeOwners.Sum(o => o.nativeSize);

            var severity = totalOwned > 100 * MB
                ? SnapshotInsightSeverity.Critical
                : totalOwned > 50 * MB ? SnapshotInsightSeverity.Warning : SnapshotInsightSeverity.Info;

            string topOwners = string.Join(", ",
                largeOwners.Take(3).Select(o => $"{o.typeName} ({VmmapParser.FormatSize(o.nativeSize)})"));

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.LargeNativeOwnership,
                Title = $"Large Native Memory Ownership ({largeOwners.Count} objects)",
                Description = $"{largeOwners.Count} managed objects own {VmmapParser.FormatSize(totalOwned)} of native memory. " +
                              $"Top: {topOwners}",
                Recommendation = "Review the lifecycle of these managed wrappers. Ensure native resources are " +
                                 "released when no longer needed (Dispose pattern, Destroy).",
                EstimatedSavings = totalOwned / 4, // conservative estimate
                RelatedObjectIndices = largeOwners.Select(o => o.managedIdx).ToList(),
            });
        }

        #endregion

        #region High Allocation Overhead

        private static void DetectHighOverhead(SnapshotReport report, SnapshotInsightResult result) {
            var analysis = report.NativeAllocationAnalysis;
            if (analysis == null || analysis.TotalUseful <= 0) return;

            long totalOverhead = analysis.TotalOverhead + analysis.TotalPadding;
            float overheadRatio = (float)totalOverhead / analysis.TotalUseful;

            if (overheadRatio <= 0.10f) return;

            var severity = overheadRatio > 0.25f
                ? SnapshotInsightSeverity.Critical
                : SnapshotInsightSeverity.Warning;

            // Find top overhead regions
            var highOverheadRegions = analysis.RegionTree
                .Where(r => r.OverheadSize > 0)
                .OrderByDescending(r => r.OverheadSize)
                .Take(3)
                .Select(r => $"{r.Name} ({VmmapParser.FormatSize(r.OverheadSize)})")
                .ToList();

            string regionList = highOverheadRegions.Count > 0
                ? string.Join(", ", highOverheadRegions)
                : "various regions";

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.HighAllocationOverhead,
                Title = $"High Allocation Overhead ({overheadRatio:P1})",
                Description = $"Total overhead/padding: {VmmapParser.FormatSize(totalOverhead)} " +
                              $"({overheadRatio:P1} of {VmmapParser.FormatSize(analysis.TotalUseful)} useful). " +
                              $"Top regions: {regionList}",
                Recommendation = "Consider using pool allocators for frequent small allocations. " +
                                 "Review allocator configuration for regions with high overhead.",
                EstimatedSavings = totalOverhead / 2,
            });
        }

        #endregion

        #region Unmapped Large Allocations

        private static void DetectUnmappedAllocations(SnapshotReport report, SnapshotInsightResult result) {
            var analysis = report.NativeAllocationAnalysis;
            if (analysis == null || analysis.UnmappedAllocations.Count == 0) return;

            long totalUnmapped = 0;
            int largeUnmappedCount = 0;

            foreach (int allocIdx in analysis.UnmappedAllocations) {
                if (allocIdx < 0 || allocIdx >= report.NativeAllocations.Count) continue;
                long size = (long)report.NativeAllocations[allocIdx].Size;
                totalUnmapped += size;
                if (size >= MB) largeUnmappedCount++;
            }

            if (largeUnmappedCount == 0) return;

            var severity = totalUnmapped > 50 * MB
                ? SnapshotInsightSeverity.Critical
                : totalUnmapped > 10 * MB ? SnapshotInsightSeverity.Warning : SnapshotInsightSeverity.Info;

            result.Insights.Add(new SnapshotInsight {
                Severity = severity,
                Category = SnapshotInsightCategory.UnmappedLargeAllocations,
                Title = $"Unmapped Large Allocations ({largeUnmappedCount})",
                Description = $"{largeUnmappedCount} allocations >=1MB (total: {VmmapParser.FormatSize(totalUnmapped)}) " +
                              $"are not linked to any native object. " +
                              $"These may be internal engine buffers or leaked allocations.",
                Recommendation = "Investigate unmapped allocations in the Allocations tab. " +
                                 "Check if custom native plugins are allocating without proper tracking.",
                EstimatedSavings = totalUnmapped / 4,
            });
        }

        #endregion

        #region Subsystem Memory Hotspots

        private static void DetectSubsystemHotspots(SnapshotReport report, SnapshotInsightResult result) {
            var analysis = report.NativeAllocationAnalysis;
            if (analysis == null || analysis.RootReferenceBreakdown.Count == 0) return;

            long totalAllocSize = analysis.TotalUseful;
            if (totalAllocSize <= 0) return;

            // Group by AreaName
            var areaSizes = new Dictionary<string, long>();
            foreach (var g in analysis.RootReferenceBreakdown) {
                if (!areaSizes.TryGetValue(g.AreaName, out long size))
                    size = 0;
                areaSizes[g.AreaName] = size + g.TotalSize;
            }

            // Find areas using >30% of total
            foreach (var kv in areaSizes) {
                float ratio = (float)kv.Value / totalAllocSize;
                if (ratio <= 0.30f) continue;

                result.Insights.Add(new SnapshotInsight {
                    Severity = SnapshotInsightSeverity.Warning,
                    Category = SnapshotInsightCategory.SubsystemMemoryHotspot,
                    Title = $"Subsystem Hotspot: {kv.Key} ({ratio:P0})",
                    Description = $"The \"{kv.Key}\" subsystem uses {VmmapParser.FormatSize(kv.Value)} " +
                                  $"({ratio:P1} of total native allocations). " +
                                  $"This is a dominant memory consumer.",
                    Recommendation = $"Review the \"{kv.Key}\" subsystem's memory usage in the Allocations tab. " +
                                     "Consider optimizing asset sizes or reducing object counts in this area.",
                    EstimatedSavings = (long)(kv.Value * 0.1f),
                });
            }
        }

        #endregion

        #region Ranking

        private static void RankInsights(SnapshotInsightResult result) {
            foreach (var insight in result.Insights) {
                int severityWeight = insight.Severity switch {
                    SnapshotInsightSeverity.Critical => 3,
                    SnapshotInsightSeverity.Warning => 2,
                    SnapshotInsightSeverity.Info => 1,
                    _ => 1,
                };
                float mbSavings = insight.EstimatedSavings / (float)MB;
                insight.Priority = (int)(severityWeight * Math.Max(mbSavings, 1f));
            }

            result.Insights.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        #endregion
    }
}
