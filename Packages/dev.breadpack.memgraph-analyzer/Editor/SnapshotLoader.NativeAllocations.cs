#pragma warning disable CS0618 // PackedMemorySnapshot is obsolete but only available API without external packages

using System;
using System.Collections.Generic;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine;

namespace Tools {
    public static partial class SnapshotLoader {
        private static void ExtractNativeAllocations(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeAllocations.GetNumEntries();
            if (count <= 0) return;

            var addresses = new ulong[count];
            var sizes = new ulong[count];
            var overheadSizes = new int[count];
            var paddingSizes = new int[count];
            var regionIndices = new int[count];
            var rootReferenceIds = new long[count];
            var allocationSiteIds = new long[count];

            snapshot.nativeAllocations.address.GetEntries(0, (uint)count, ref addresses);
            snapshot.nativeAllocations.size.GetEntries(0, (uint)count, ref sizes);
            snapshot.nativeAllocations.overheadSize.GetEntries(0, (uint)count, ref overheadSizes);
            snapshot.nativeAllocations.paddingSize.GetEntries(0, (uint)count, ref paddingSizes);
            snapshot.nativeAllocations.memoryRegionIndex.GetEntries(0, (uint)count, ref regionIndices);
            snapshot.nativeAllocations.rootReferenceId.GetEntries(0, (uint)count, ref rootReferenceIds);
            snapshot.nativeAllocations.allocationSiteId.GetEntries(0, (uint)count, ref allocationSiteIds);

            long totalSize = 0;
            long totalOverhead = 0;
            long totalPadding = 0;

            for (int i = 0; i < count; i++) {
                report.NativeAllocations.Add(new NativeAllocationInfo {
                    Address = addresses[i],
                    Size = sizes[i],
                    OverheadSize = (ulong)overheadSizes[i],
                    PaddingSize = (ulong)paddingSizes[i],
                    MemoryRegionIndex = regionIndices[i],
                    RootReferenceId = (int)rootReferenceIds[i],
                    AllocationSiteId = (int)allocationSiteIds[i],
                });
                totalSize += (long)sizes[i];
                totalOverhead += overheadSizes[i];
                totalPadding += paddingSizes[i];
            }

            report.Summary.TotalNativeAllocationSize = totalSize;
            report.Summary.TotalNativeOverheadSize = totalOverhead;
            report.Summary.TotalNativePaddingSize = totalPadding;

            Debug.Log($"[SnapshotLoader] Extracted {count} native allocations " +
                      $"(total: {VmmapParser.FormatSize(totalSize)}, overhead: {VmmapParser.FormatSize(totalOverhead)}, padding: {VmmapParser.FormatSize(totalPadding)})");
        }

        private static void ExtractNativeMemoryRegions(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeMemoryRegions.GetNumEntries();
            if (count <= 0) return;

            var names = new string[count];
            var parentIndices = new int[count];
            var addressBases = new ulong[count];
            var addressSizes = new ulong[count];
            var firstAllocationIndices = new int[count];
            var numAllocations = new int[count];

            snapshot.nativeMemoryRegions.memoryRegionName.GetEntries(0, (uint)count, ref names);
            snapshot.nativeMemoryRegions.parentIndex.GetEntries(0, (uint)count, ref parentIndices);
            snapshot.nativeMemoryRegions.addressBase.GetEntries(0, (uint)count, ref addressBases);
            snapshot.nativeMemoryRegions.addressSize.GetEntries(0, (uint)count, ref addressSizes);
            snapshot.nativeMemoryRegions.firstAllocationIndex.GetEntries(0, (uint)count, ref firstAllocationIndices);
            snapshot.nativeMemoryRegions.numAllocations.GetEntries(0, (uint)count, ref numAllocations);

            for (int i = 0; i < count; i++) {
                report.NativeMemoryRegions.Add(new NativeMemoryRegionInfo {
                    Name = names[i] ?? "",
                    ParentIndex = parentIndices[i],
                    AddressBase = addressBases[i],
                    AddressSize = addressSizes[i],
                    FirstAllocationIndex = firstAllocationIndices[i],
                    NumAllocations = numAllocations[i],
                });
            }

            Debug.Log($"[SnapshotLoader] Extracted {count} native memory regions");
        }

        private static void ExtractNativeMemoryLabels(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeMemoryLabels.GetNumEntries();
            if (count <= 0) return;

            var names = new string[count];
            snapshot.nativeMemoryLabels.memoryLabelName.GetEntries(0, (uint)count, ref names);

            for (int i = 0; i < count; i++) {
                report.NativeMemoryLabels.Add(new NativeMemoryLabelInfo {
                    Name = names[i] ?? "",
                });
            }

            Debug.Log($"[SnapshotLoader] Extracted {count} native memory labels");
        }

        private static void ExtractNativeRootReferences(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeRootReferences.GetNumEntries();
            if (count <= 0) return;

            var ids = new long[count];
            var areaNames = new string[count];
            var objectNames = new string[count];
            var accumulatedSizes = new ulong[count];

            snapshot.nativeRootReferences.id.GetEntries(0, (uint)count, ref ids);
            snapshot.nativeRootReferences.areaName.GetEntries(0, (uint)count, ref areaNames);
            snapshot.nativeRootReferences.objectName.GetEntries(0, (uint)count, ref objectNames);
            snapshot.nativeRootReferences.accumulatedSize.GetEntries(0, (uint)count, ref accumulatedSizes);

            for (int i = 0; i < count; i++) {
                report.NativeRootReferences.Add(new NativeRootReferenceInfo {
                    Id = ids[i],
                    AreaName = areaNames[i] ?? "",
                    ObjectName = objectNames[i] ?? "",
                    AccumulatedSize = (long)accumulatedSizes[i],
                });
            }

            Debug.Log($"[SnapshotLoader] Extracted {count} native root references");
        }

        private static void ExtractNativeAllocationSites(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeAllocationSites.GetNumEntries();
            if (count <= 0) return;

            var ids = new long[count];
            var memoryLabelIndices = new int[count];
            var callstackSymbolArrays = new ulong[count][];

            snapshot.nativeAllocationSites.id.GetEntries(0, (uint)count, ref ids);
            snapshot.nativeAllocationSites.memoryLabelIndex.GetEntries(0, (uint)count, ref memoryLabelIndices);
            snapshot.nativeAllocationSites.callstackSymbols.GetEntries(0, (uint)count, ref callstackSymbolArrays);

            for (int i = 0; i < count; i++) {
                var rawSymbols = callstackSymbolArrays[i];
                int[] symbolIndices;
                if (rawSymbols != null && rawSymbols.Length > 0) {
                    symbolIndices = new int[rawSymbols.Length];
                    for (int j = 0; j < rawSymbols.Length; j++)
                        symbolIndices[j] = (int)rawSymbols[j];
                } else {
                    symbolIndices = Array.Empty<int>();
                }

                report.NativeAllocationSites.Add(new NativeAllocationSiteInfo {
                    Id = (int)ids[i],
                    MemoryLabelIndex = memoryLabelIndices[i],
                    CallstackSymbolIndices = symbolIndices,
                });
            }

            Debug.Log($"[SnapshotLoader] Extracted {count} native allocation sites");
        }

        private static void ExtractNativeCallstackSymbols(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeCallstackSymbols.GetNumEntries();
            if (count <= 0) return;

            var symbols = new ulong[count];
            var readableStackTraces = new string[count];

            snapshot.nativeCallstackSymbols.symbol.GetEntries(0, (uint)count, ref symbols);
            snapshot.nativeCallstackSymbols.readableStackTrace.GetEntries(0, (uint)count, ref readableStackTraces);

            for (int i = 0; i < count; i++) {
                report.NativeCallstackSymbols.Add(new NativeCallstackSymbolInfo {
                    Symbol = symbols[i],
                    ReadableStackTrace = readableStackTraces[i] ?? "",
                });
            }

            Debug.Log($"[SnapshotLoader] Extracted {count} native callstack symbols");
        }
    }
}

#pragma warning restore CS0618
