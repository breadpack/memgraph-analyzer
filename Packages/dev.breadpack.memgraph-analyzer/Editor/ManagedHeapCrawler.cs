using System;
using System.Collections.Generic;
using UnityEditor;

namespace Tools {
    public static class ManagedHeapCrawler {
        public static CrawlerResult Crawl(ManagedHeapReader reader, TypeInfo[] types,
            SnapshotFieldDescription[] fields, ulong[] gcHandleTargets,
            int pointerSize, int objectHeaderSize) {
            var result = new CrawlerResult();

            if (types == null || types.Length == 0 || reader == null)
                return result;

            // Build typeInfoAddress -> typeIndex lookup
            var typeAddressToIndex = BuildTypeAddressLookup(types);

            // Queue for BFS crawling
            var queue = new Queue<ulong>();
            var visited = new HashSet<ulong>();
            var addressToObjectIndex = new Dictionary<ulong, int>();

            // Seed from GC Handles
            if (gcHandleTargets != null) {
                for (int i = 0; i < gcHandleTargets.Length; i++) {
                    ulong target = gcHandleTargets[i];
                    if (target == 0 || !reader.IsValidAddress(target))
                        continue;

                    if (visited.Add(target)) {
                        queue.Enqueue(target);

                        var obj = new ManagedObjectInstance {
                            Address = target,
                            IsGcRoot = true,
                            GcHandleIndex = i,
                        };
                        obj.ObjectIndex = result.Objects.Count;
                        addressToObjectIndex[target] = obj.ObjectIndex;
                        result.Objects.Add(obj);
                    }
                }
            }

            // Seed from static fields
            SeedFromStaticFields(reader, types, fields, pointerSize, objectHeaderSize,
                queue, visited, addressToObjectIndex, result);

            // BFS Crawl
            int processed = 0;
            int totalSeeded = queue.Count;

            while (queue.Count > 0) {
                ulong address = queue.Dequeue();
                processed++;

                if (processed % 5000 == 0) {
                    float progress = totalSeeded > 0
                        ? (float)processed / Math.Max(totalSeeded, processed + queue.Count) : 0;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Crawling Managed Heap",
                            $"Processed {processed:N0} objects ({queue.Count:N0} remaining)...",
                            progress)) {
                        EditorUtility.ClearProgressBar();
                        result.TotalCrawled = processed;
                        result.TotalSkipped = queue.Count;
                        FinalizeResult(result, types);
                        return result;
                    }
                }

                if (!addressToObjectIndex.TryGetValue(address, out int objectIndex))
                    continue;

                var managedObj = result.Objects[objectIndex];

                // Read type pointer from object header
                if (!reader.TryReadObjectTypePointer(address, out ulong typePtr))
                    continue;

                // Resolve type
                if (!typeAddressToIndex.TryGetValue(typePtr, out int typeIndex))
                    continue;

                if (typeIndex < 0 || typeIndex >= types.Length)
                    continue;

                var type = types[typeIndex];
                managedObj.TypeIndex = typeIndex;

                // Calculate size
                managedObj.Size = CalculateObjectSize(reader, address, type, types,
                    pointerSize, objectHeaderSize);

                // Track instance on type
                type.InstanceCount++;
                type.TotalInstanceSize += managedObj.Size;

                // Crawl fields to find references
                CrawlObjectFields(reader, address, type, types, fields,
                    pointerSize, objectHeaderSize,
                    queue, visited, addressToObjectIndex, result, objectIndex);
            }

            EditorUtility.ClearProgressBar();

            result.TotalCrawled = processed;
            FinalizeResult(result, types);

            return result;
        }

        private static void SeedFromStaticFields(ManagedHeapReader reader, TypeInfo[] types,
            SnapshotFieldDescription[] fields, int pointerSize, int objectHeaderSize,
            Queue<ulong> queue, HashSet<ulong> visited,
            Dictionary<ulong, int> addressToObjectIndex, CrawlerResult result) {
            for (int typeIdx = 0; typeIdx < types.Length; typeIdx++) {
                var type = types[typeIdx];
                if (type.StaticFieldBytes == null || type.StaticFieldBytes.Length == 0)
                    continue;
                if (type.FieldIndices == null)
                    continue;

                foreach (int fieldIdx in type.FieldIndices) {
                    if (fieldIdx < 0 || fieldIdx >= fields.Length) continue;
                    var field = fields[fieldIdx];
                    if (!field.IsStatic) continue;
                    if (field.TypeIndex < 0 || field.TypeIndex >= types.Length) continue;

                    var fieldType = types[field.TypeIndex];
                    if (fieldType.IsValueType) continue;

                    // Read pointer from static field bytes
                    int offset = field.Offset;
                    if (offset + pointerSize > type.StaticFieldBytes.Length) continue;

                    ulong target;
                    if (pointerSize == 8)
                        target = BitConverter.ToUInt64(type.StaticFieldBytes, offset);
                    else
                        target = BitConverter.ToUInt32(type.StaticFieldBytes, offset);

                    if (target == 0 || !reader.IsValidAddress(target)) continue;

                    if (visited.Add(target)) {
                        queue.Enqueue(target);

                        var obj = new ManagedObjectInstance {
                            Address = target,
                            IsGcRoot = true,
                        };
                        obj.ObjectIndex = result.Objects.Count;
                        addressToObjectIndex[target] = obj.ObjectIndex;
                        result.Objects.Add(obj);
                    }
                }
            }
        }

        private static void CrawlObjectFields(ManagedHeapReader reader, ulong objectAddress,
            TypeInfo type, TypeInfo[] types, SnapshotFieldDescription[] fields,
            int pointerSize, int objectHeaderSize,
            Queue<ulong> queue, HashSet<ulong> visited,
            Dictionary<ulong, int> addressToObjectIndex, CrawlerResult result, int parentIndex) {
            if (type.IsArray) {
                CrawlArrayElements(reader, objectAddress, type, types,
                    pointerSize, objectHeaderSize,
                    queue, visited, addressToObjectIndex, result, parentIndex);
                return;
            }

            if (type.FieldIndices == null) return;

            foreach (int fieldIdx in type.FieldIndices) {
                if (fieldIdx < 0 || fieldIdx >= fields.Length) continue;
                var field = fields[fieldIdx];
                if (field.IsStatic) continue;
                if (field.TypeIndex < 0 || field.TypeIndex >= types.Length) continue;

                var fieldType = types[field.TypeIndex];
                if (fieldType.IsValueType) continue;

                // Reference type field: read pointer
                ulong fieldAddress = objectAddress + (ulong)objectHeaderSize + (ulong)field.Offset;
                if (!reader.TryReadPointer(fieldAddress, out ulong target))
                    continue;

                if (target == 0 || !reader.IsValidAddress(target))
                    continue;

                int targetIndex;
                if (visited.Add(target)) {
                    queue.Enqueue(target);

                    var obj = new ManagedObjectInstance {
                        Address = target,
                    };
                    obj.ObjectIndex = result.Objects.Count;
                    targetIndex = obj.ObjectIndex;
                    addressToObjectIndex[target] = obj.ObjectIndex;
                    result.Objects.Add(obj);
                } else {
                    addressToObjectIndex.TryGetValue(target, out targetIndex);
                }

                result.References.Add(new ReferenceEdge {
                    FromObjectIndex = parentIndex,
                    ToObjectIndex = targetIndex,
                    FieldName = field.Name,
                });
            }
        }

        private static void CrawlArrayElements(ManagedHeapReader reader, ulong objectAddress,
            TypeInfo arrayType, TypeInfo[] types, int pointerSize, int objectHeaderSize,
            Queue<ulong> queue, HashSet<ulong> visited,
            Dictionary<ulong, int> addressToObjectIndex, CrawlerResult result, int parentIndex) {
            // Array layout: [header (objectHeaderSize)] [length (4 bytes)] [elements...]
            ulong lengthAddress = objectAddress + (ulong)objectHeaderSize;
            if (!reader.TryReadInt32(lengthAddress, out int length))
                return;

            if (length <= 0 || length > 1000000) // Safety limit
                return;

            // Determine element type - for arrays, the element type is typically the first field type
            // If it's a value type array, we don't need to crawl elements for references
            // For simplicity, we check if it could contain references by looking at pointer-sized elements
            ulong dataStart = lengthAddress + 4;
            // Align to pointer size
            ulong alignedDataStart = (dataStart + (ulong)pointerSize - 1) & ~((ulong)pointerSize - 1);

            // Only crawl reference type arrays (element size == pointer size)
            for (int i = 0; i < length && i < 10000; i++) { // Limit crawl for very large arrays
                ulong elementAddress = alignedDataStart + (ulong)(i * pointerSize);
                if (!reader.TryReadPointer(elementAddress, out ulong target))
                    break;

                if (target == 0 || !reader.IsValidAddress(target))
                    continue;

                int targetIndex;
                if (visited.Add(target)) {
                    queue.Enqueue(target);

                    var obj = new ManagedObjectInstance {
                        Address = target,
                    };
                    obj.ObjectIndex = result.Objects.Count;
                    targetIndex = obj.ObjectIndex;
                    addressToObjectIndex[target] = obj.ObjectIndex;
                    result.Objects.Add(obj);
                } else {
                    addressToObjectIndex.TryGetValue(target, out targetIndex);
                }

                result.References.Add(new ReferenceEdge {
                    FromObjectIndex = parentIndex,
                    ToObjectIndex = targetIndex,
                    FieldName = $"[{i}]",
                });
            }
        }

        private static long CalculateObjectSize(ManagedHeapReader reader, ulong address,
            TypeInfo type, TypeInfo[] types, int pointerSize, int objectHeaderSize) {
            if (type.IsArray) {
                // Array: header + length(4) + padding + elements
                ulong lengthAddress = address + (ulong)objectHeaderSize;
                if (!reader.TryReadInt32(lengthAddress, out int length))
                    return type.BaseSize;

                if (length < 0) length = 0;

                // Element size: use base size of element type, or pointer size for reference arrays
                int elementSize = pointerSize; // Default for reference arrays
                if (type.BaseSize > 0)
                    elementSize = type.BaseSize;

                long arraySize = objectHeaderSize + 4 + (long)length * elementSize;
                // Align to pointer size
                arraySize = (arraySize + pointerSize - 1) & ~(pointerSize - 1);
                return Math.Max(arraySize, objectHeaderSize);
            }

            // String: header + length(4) + chars(length * 2) + null terminator(2)
            if (type.Name == "String" && (type.Assembly == "mscorlib" ||
                                           type.Assembly == "System.Private.CoreLib")) {
                ulong lengthAddress = address + (ulong)objectHeaderSize;
                if (reader.TryReadInt32(lengthAddress, out int charCount) && charCount >= 0) {
                    long stringSize = objectHeaderSize + 4 + (long)charCount * 2 + 2;
                    stringSize = (stringSize + pointerSize - 1) & ~(pointerSize - 1);
                    return stringSize;
                }
            }

            // Regular object: use base size
            return type.BaseSize > 0 ? type.BaseSize : objectHeaderSize;
        }

        private static Dictionary<ulong, int> BuildTypeAddressLookup(TypeInfo[] types) {
            var lookup = new Dictionary<ulong, int>();
            for (int i = 0; i < types.Length; i++) {
                ulong addr = types[i].TypeInfoAddress;
                if (addr != 0) {
                    lookup[addr] = i;
                }
            }
            return lookup;
        }

        private static void FinalizeResult(CrawlerResult result, TypeInfo[] types) {
            // Build objectsByTypeIndex
            result.ObjectsByTypeIndex.Clear();
            foreach (var obj in result.Objects) {
                if (obj.TypeIndex < 0 || obj.TypeIndex >= types.Length) continue;

                if (!result.ObjectsByTypeIndex.TryGetValue(obj.TypeIndex, out var list)) {
                    list = new List<int>();
                    result.ObjectsByTypeIndex[obj.TypeIndex] = list;
                }
                list.Add(obj.ObjectIndex);
            }
        }
    }
}
