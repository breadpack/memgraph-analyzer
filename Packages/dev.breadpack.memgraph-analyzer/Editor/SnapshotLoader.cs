#pragma warning disable CS0618 // PackedMemorySnapshot is obsolete but only available API without external packages

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Profiling.Memory.Experimental;

namespace Tools {
    public static partial class SnapshotLoader {
        public static SnapshotReport Load(string path) {
            var report = new SnapshotReport {
                FilePath = path,
                AnalysisTime = DateTime.Now,
                Phase = SnapshotAnalysisPhase.Loading,
            };

            PackedMemorySnapshot snapshot = null;
            try {
                snapshot = PackedMemorySnapshot.Load(path);
                if (snapshot == null) {
                    report.Phase = SnapshotAnalysisPhase.Error;
                    report.ErrorMessage = "PackedMemorySnapshot.Load() returned null.";
                    return report;
                }

                ExtractVmInfo(snapshot, report);
                ExtractTypes(snapshot, report);
                ExtractFields(snapshot, report);
                ExtractNativeObjects(snapshot, report);
                ExtractGcHandles(snapshot, report);
                ExtractConnections(snapshot, report);
                ExtractManagedHeapSections(snapshot, report);

                // Extract additional native allocation data (graceful skip on failure)
                try { ExtractNativeAllocations(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeAllocations skipped: {ex.Message}"); }

                try { ExtractNativeMemoryRegions(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeMemoryRegions skipped: {ex.Message}"); }

                try { ExtractNativeMemoryLabels(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeMemoryLabels skipped: {ex.Message}"); }

                try { ExtractNativeRootReferences(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeRootReferences skipped: {ex.Message}"); }

                try { ExtractNativeAllocationSites(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeAllocationSites skipped: {ex.Message}"); }

                try { ExtractNativeCallstackSymbols(snapshot, report); }
                catch (Exception ex) { Debug.LogWarning($"[SnapshotLoader] ExtractNativeCallstackSymbols skipped: {ex.Message}"); }

                report.Phase = SnapshotAnalysisPhase.ExtractingTypes;
            }
            catch (Exception ex) {
                report.Phase = SnapshotAnalysisPhase.Error;
                report.ErrorMessage = $"Failed to load snapshot: {ex.Message}";
                Debug.LogException(ex);
            }
            finally {
                snapshot?.Dispose();
            }

            return report;
        }

        private static void ExtractVmInfo(PackedMemorySnapshot snapshot, SnapshotReport report) {
            var vm = snapshot.virtualMachineInformation;
            report.PointerSize = vm.pointerSize;
            report.ObjectHeaderSize = vm.objectHeaderSize;
        }

        private static void ExtractTypes(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.typeDescriptions.GetNumEntries();
            report.TypeCount = count;

            var names = new string[count];
            var assemblies = new string[count];
            var flags = new TypeFlags[count];
            var sizes = new int[count];
            var typeInfoAddresses = new ulong[count];
            var fieldIndicesArrays = new int[count][];
            var staticFieldBytesArrays = new byte[count][];

            snapshot.typeDescriptions.typeDescriptionName.GetEntries(0, (uint)count, ref names);
            snapshot.typeDescriptions.assembly.GetEntries(0, (uint)count, ref assemblies);
            snapshot.typeDescriptions.flags.GetEntries(0, (uint)count, ref flags);
            snapshot.typeDescriptions.size.GetEntries(0, (uint)count, ref sizes);
            snapshot.typeDescriptions.typeInfoAddress.GetEntries(0, (uint)count, ref typeInfoAddresses);
            snapshot.typeDescriptions.fieldIndices.GetEntries(0, (uint)count, ref fieldIndicesArrays);
            snapshot.typeDescriptions.staticFieldBytes.GetEntries(0, (uint)count, ref staticFieldBytesArrays);

            var types = new TypeInfo[count];
            for (int i = 0; i < count; i++) {
                string fullName = names[i] ?? "";
                string ns = ExtractNamespace(fullName);
                string typeName = ExtractTypeName(fullName);

                types[i] = new TypeInfo {
                    TypeIndex = i,
                    Name = typeName,
                    Assembly = assemblies[i] ?? "",
                    Namespace = ns,
                    BaseSize = sizes[i],
                    TypeInfoAddress = typeInfoAddresses[i],
                    FieldIndices = fieldIndicesArrays[i] ?? Array.Empty<int>(),
                    StaticFieldBytes = staticFieldBytesArrays[i],
                    IsValueType = (flags[i] & TypeFlags.kValueType) != 0,
                    IsArray = (flags[i] & TypeFlags.kArray) != 0,
                };
            }

            report.Types = types;
        }

        private static void ExtractFields(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.fieldDescriptions.GetNumEntries();
            report.FieldCount = count;

            var names = new string[count];
            var offsets = new int[count];
            var typeIndices = new int[count];
            var isStatics = new bool[count];

            snapshot.fieldDescriptions.fieldDescriptionName.GetEntries(0, (uint)count, ref names);
            snapshot.fieldDescriptions.offset.GetEntries(0, (uint)count, ref offsets);
            snapshot.fieldDescriptions.typeIndex.GetEntries(0, (uint)count, ref typeIndices);
            snapshot.fieldDescriptions.isStatic.GetEntries(0, (uint)count, ref isStatics);

            var fields = new SnapshotFieldDescription[count];
            for (int i = 0; i < count; i++) {
                fields[i] = new SnapshotFieldDescription {
                    FieldIndex = i,
                    Name = names[i] ?? "",
                    Offset = offsets[i],
                    TypeIndex = typeIndices[i],
                    IsStatic = isStatics[i],
                };
            }

            report.Fields = fields;
        }

        private static void ExtractNativeObjects(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.nativeObjects.GetNumEntries();
            report.NativeObjectCount = count;

            var objectNames = new string[count];
            var instanceIds = new int[count];
            var sizes = new ulong[count];
            var nativeTypeIndices = new int[count];
            var gcHandleIndices = new int[count];
            var nativeObjectAddresses = new ulong[count];
            var flags = new ObjectFlags[count];
            var rootReferenceIds = new long[count];
            var hideFlags = new HideFlags[count];

            snapshot.nativeObjects.objectName.GetEntries(0, (uint)count, ref objectNames);
            snapshot.nativeObjects.instanceId.GetEntries(0, (uint)count, ref instanceIds);
            snapshot.nativeObjects.size.GetEntries(0, (uint)count, ref sizes);
            snapshot.nativeObjects.nativeTypeArrayIndex.GetEntries(0, (uint)count, ref nativeTypeIndices);
            snapshot.nativeObjects.gcHandleIndex.GetEntries(0, (uint)count, ref gcHandleIndices);
            snapshot.nativeObjects.nativeObjectAddress.GetEntries(0, (uint)count, ref nativeObjectAddresses);
            snapshot.nativeObjects.flags.GetEntries(0, (uint)count, ref flags);
            snapshot.nativeObjects.rootReferenceId.GetEntries(0, (uint)count, ref rootReferenceIds);
            snapshot.nativeObjects.hideFlags.GetEntries(0, (uint)count, ref hideFlags);

            // Extract native type names
            int nativeTypeCount = (int)snapshot.nativeTypes.GetNumEntries();
            var nativeTypeNames = new string[nativeTypeCount];
            snapshot.nativeTypes.typeName.GetEntries(0, (uint)nativeTypeCount, ref nativeTypeNames);

            var objects = new List<NativeObjectInfo>(count);
            long totalNativeSize = 0;

            for (int i = 0; i < count; i++) {
                string nativeTypeName = "";
                int nativeTypeIdx = nativeTypeIndices[i];
                if (nativeTypeIdx >= 0 && nativeTypeIdx < nativeTypeNames.Length)
                    nativeTypeName = nativeTypeNames[nativeTypeIdx] ?? "";

                var obj = new NativeObjectInfo {
                    Name = objectNames[i] ?? "",
                    InstanceId = instanceIds[i],
                    Size = (long)sizes[i],
                    NativeTypeName = nativeTypeName,
                    Category = CategorizeNativeType(nativeTypeName),
                    GcHandleIndex = gcHandleIndices[i],
                    NativeTypeArrayIndex = nativeTypeIdx,
                    NativeObjectListIndex = i,
                    NativeObjectAddress = nativeObjectAddresses[i],
                    Flags = (int)flags[i],
                    RootReferenceId = (int)rootReferenceIds[i],
                    HideFlags = (int)hideFlags[i],
                };
                objects.Add(obj);
                totalNativeSize += (long)sizes[i];
            }

            report.NativeObjects.AddRange(objects);
            report.Summary.TotalNativeSize = totalNativeSize;
            report.Summary.TotalNativeObjectCount = count;
        }

        private static void ExtractGcHandles(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.gcHandles.GetNumEntries();
            report.GcHandleCount = count;

            var targets = new ulong[count];
            snapshot.gcHandles.target.GetEntries(0, (uint)count, ref targets);
            report.GcHandleTargets = targets;
        }

        private static void ExtractConnections(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.connections.GetNumEntries();
            report.ConnectionCount = count;

            var froms = new int[count];
            var tos = new int[count];
            snapshot.connections.from.GetEntries(0, (uint)count, ref froms);
            snapshot.connections.to.GetEntries(0, (uint)count, ref tos);

            var connections = new SnapshotConnection[count];
            for (int i = 0; i < count; i++) {
                connections[i] = new SnapshotConnection { From = froms[i], To = tos[i] };
            }
            report.Connections = connections;
        }

        private static void ExtractManagedHeapSections(PackedMemorySnapshot snapshot, SnapshotReport report) {
            int count = (int)snapshot.managedHeapSections.GetNumEntries();
            report.ManagedHeapSectionCount = count;

            var startAddresses = new ulong[count];
            var bytesArrays = new byte[count][];
            snapshot.managedHeapSections.startAddress.GetEntries(0, (uint)count, ref startAddresses);
            snapshot.managedHeapSections.bytes.GetEntries(0, (uint)count, ref bytesArrays);

            var sections = new SnapshotHeapSection[count];
            long totalHeapSize = 0;

            for (int i = 0; i < count; i++) {
                var bytes = bytesArrays[i] ?? Array.Empty<byte>();
                sections[i] = new SnapshotHeapSection {
                    StartAddress = startAddresses[i],
                    Bytes = bytes,
                };
                totalHeapSize += bytes.Length;
            }

            report.ManagedHeapSections = sections;
            report.Summary.TotalManagedHeapSize = totalHeapSize;
        }

        #region Classification Utilities

        public static AssemblyClassification ClassifyAssembly(string assemblyName) {
            if (string.IsNullOrEmpty(assemblyName))
                return AssemblyClassification.ThirdParty;

            // User code assemblies
            if (assemblyName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
                return AssemblyClassification.UserCode;

            // Unity Editor assemblies
            if (assemblyName.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase))
                return AssemblyClassification.UnityEditor;

            // Unity Runtime assemblies
            if (assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Equals("UnityEngine", StringComparison.OrdinalIgnoreCase))
                return AssemblyClassification.UnityRuntime;

            // .NET / Mono assemblies
            if (assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                return AssemblyClassification.DotNet;

            return AssemblyClassification.ThirdParty;
        }

        public static NativeTypeCategory CategorizeNativeType(string typeName) {
            if (string.IsNullOrEmpty(typeName))
                return NativeTypeCategory.Other;

            if (typeName.Contains("Texture"))
                return NativeTypeCategory.Texture;
            if (typeName == "Mesh" || typeName.Contains("MeshFilter") || typeName.Contains("SkinnedMeshRenderer"))
                return NativeTypeCategory.Mesh;
            if (typeName.Contains("Material"))
                return NativeTypeCategory.Material;
            if (typeName.Contains("Shader"))
                return NativeTypeCategory.Shader;
            if (typeName.Contains("AnimationClip") || typeName.Contains("Animator"))
                return NativeTypeCategory.AnimationClip;
            if (typeName.Contains("AudioClip") || typeName.Contains("AudioSource"))
                return NativeTypeCategory.AudioClip;
            if (typeName.Contains("Font"))
                return NativeTypeCategory.Font;
            if (typeName == "ScriptableObject" || typeName.Contains("ScriptableObject"))
                return NativeTypeCategory.ScriptableObject;
            if (typeName == "GameObject")
                return NativeTypeCategory.GameObject;
            if (typeName.Contains("Component") || typeName == "MonoBehaviour" ||
                typeName == "Transform" || typeName == "RectTransform")
                return NativeTypeCategory.Component;

            return NativeTypeCategory.Other;
        }

        public static string GetClassificationDisplayName(AssemblyClassification classification) {
            return classification switch {
                AssemblyClassification.UserCode => "User Code",
                AssemblyClassification.UnityRuntime => "Unity Runtime",
                AssemblyClassification.UnityEditor => "Unity Editor",
                AssemblyClassification.DotNet => ".NET / Mono",
                AssemblyClassification.ThirdParty => "Third Party",
                _ => "Unknown",
            };
        }

        public static string GetNativeCategoryDisplayName(NativeTypeCategory category) {
            return category switch {
                NativeTypeCategory.Texture => "Textures",
                NativeTypeCategory.Mesh => "Meshes",
                NativeTypeCategory.Material => "Materials",
                NativeTypeCategory.Shader => "Shaders",
                NativeTypeCategory.AnimationClip => "Animations",
                NativeTypeCategory.AudioClip => "Audio",
                NativeTypeCategory.Font => "Fonts",
                NativeTypeCategory.ScriptableObject => "ScriptableObjects",
                NativeTypeCategory.GameObject => "GameObjects",
                NativeTypeCategory.Component => "Components",
                NativeTypeCategory.Other => "Other",
                _ => "Unknown",
            };
        }

        #endregion

        #region String Utilities

        private static string ExtractNamespace(string fullName) {
            if (string.IsNullOrEmpty(fullName)) return "";

            // Handle generic types: remove everything inside < >
            int genericIdx = fullName.IndexOf('<');
            string cleanName = genericIdx >= 0 ? fullName.Substring(0, genericIdx) : fullName;

            int lastDot = cleanName.LastIndexOf('.');
            return lastDot >= 0 ? cleanName.Substring(0, lastDot) : "";
        }

        private static string ExtractTypeName(string fullName) {
            if (string.IsNullOrEmpty(fullName)) return "";

            int lastDot = fullName.LastIndexOf('.');
            // For generic types, keep the full generic part after the last namespace dot
            if (lastDot >= 0) {
                int genericIdx = fullName.IndexOf('<');
                if (genericIdx >= 0 && genericIdx < lastDot) {
                    // The dot is inside generic args, use original
                    return fullName;
                }
                return fullName.Substring(lastDot + 1);
            }
            return fullName;
        }

        #endregion
    }
}

#pragma warning restore CS0618
