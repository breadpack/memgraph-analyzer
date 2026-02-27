using System;
using System.Collections.Generic;

namespace Tools {
    public class SnapshotReport {
        public string FilePath;
        public DateTime AnalysisTime;
        public SnapshotAnalysisPhase Phase = SnapshotAnalysisPhase.Idle;
        public string ErrorMessage;

        public SnapshotMemorySummary Summary = new();
        public readonly List<AssemblyInfo> Assemblies = new();
        public readonly List<NativeObjectInfo> NativeObjects = new();
        public readonly List<NativeTypeSummary> NativeTypeSummaries = new();
        public TypeInfo[] Types = Array.Empty<TypeInfo>();
        public SnapshotFieldDescription[] Fields = Array.Empty<SnapshotFieldDescription>();

        // Raw extracted data for crawler
        public int PointerSize;
        public int ObjectHeaderSize;
        public ulong[] GcHandleTargets = Array.Empty<ulong>();
        public SnapshotConnection[] Connections = Array.Empty<SnapshotConnection>();
        public SnapshotHeapSection[] ManagedHeapSections = Array.Empty<SnapshotHeapSection>();

        // Counts for summary display
        public int TypeCount;
        public int NativeObjectCount;
        public int GcHandleCount;
        public int ManagedHeapSectionCount;
        public int FieldCount;
        public int ConnectionCount;

        // Crawler result (populated in Sprint 3)
        public CrawlerResult CrawlerResult;

        // Retained sizes (populated in Sprint 4)
        public Dictionary<int, long> RetainedSizes;

        // Native↔Managed link and insight results
        public NativeManagedLinkResult LinkResult;
        public SnapshotInsightResult InsightResult;

        public bool SkipCrawl;
    }

    public class SnapshotMemorySummary {
        public long TotalNativeSize;
        public long TotalManagedHeapSize;
        public int TotalNativeObjectCount;
        public int TotalManagedObjectCount;

        public readonly Dictionary<AssemblyClassification, long> SizeByClassification = new();
        public readonly Dictionary<AssemblyClassification, int> TypeCountByClassification = new();
    }

    public class AssemblyInfo {
        public string Name;
        public AssemblyClassification Classification;
        public long TotalSize;
        public int TypeCount;
        public int InstanceCount;
        public readonly List<NamespaceInfo> Namespaces = new();
    }

    public class NamespaceInfo {
        public string Name;
        public long TotalSize;
        public int TypeCount;
        public int InstanceCount;
        public readonly List<TypeInfo> Types = new();
    }

    public class TypeInfo {
        public int TypeIndex;
        public string Name;
        public string Assembly;
        public string Namespace;
        public int BaseSize;
        public ulong TypeInfoAddress;
        public int[] FieldIndices;
        public byte[] StaticFieldBytes;
        public bool IsValueType;
        public bool IsArray;

        // Populated by crawler
        public int InstanceCount;
        public long TotalInstanceSize;
    }

    public class NativeObjectInfo {
        public string Name;
        public int InstanceId;
        public long Size;
        public string NativeTypeName;
        public NativeTypeCategory Category;
        public int GcHandleIndex;
        public int NativeTypeArrayIndex;
        public int NativeObjectListIndex;
    }

    public class NativeTypeSummary {
        public string TypeName;
        public NativeTypeCategory Category;
        public int ObjectCount;
        public long TotalSize;
    }

    public class SnapshotFieldDescription {
        public int FieldIndex;
        public string Name;
        public int Offset;
        public int TypeIndex;
        public bool IsStatic;
    }

    public class SnapshotConnection {
        public int From;
        public int To;
    }

    public class SnapshotHeapSection {
        public ulong StartAddress;
        public byte[] Bytes;
    }
}
