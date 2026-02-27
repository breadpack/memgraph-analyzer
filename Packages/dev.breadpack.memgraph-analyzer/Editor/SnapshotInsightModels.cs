using System.Collections.Generic;

namespace Tools {
    public enum SnapshotInsightSeverity { Info, Warning, Critical }

    public enum SnapshotInsightCategory {
        DuplicateAsset,
        UnreferencedNative,
        LargeStaticRetention,
        EmptyGameObjects,
        TypeExplosion,
        LargeNativeOwnership,
        HighAllocationOverhead,
        FragmentedRegion,
        UnmappedLargeAllocations,
        SubsystemMemoryHotspot,
    }

    public class SnapshotInsight {
        public SnapshotInsightSeverity Severity;
        public SnapshotInsightCategory Category;
        public string Title;
        public string Description;
        public string Recommendation;
        public long EstimatedSavings;
        public int Priority;
        public List<int> RelatedObjectIndices = new();
    }

    public class DuplicateAssetGroup {
        public string Name;
        public string NativeTypeName;
        public int Count;
        public long IndividualSize;
        public long TotalWastedSize;
    }

    public class UnreferencedAsset {
        public int NativeObjectIndex;
        public string Name;
        public long Size;
        public string Reason;
    }

    public class NativeManagedLink {
        public int NativeObjectIndex;
        public int ManagedObjectIndex;
        public int GcHandleIndex;
    }

    public class NativeManagedLinkResult {
        public readonly List<NativeManagedLink> Links = new();
        public readonly Dictionary<int, int> NativeToManaged = new();
        public readonly Dictionary<int, List<int>> ManagedToNatives = new();
        public readonly Dictionary<int, long> NativeRetainedByManaged = new();
        public int LinkedCount;
        public int UnlinkedNativeCount;
    }

    public class SnapshotInsightResult {
        public readonly List<SnapshotInsight> Insights = new();
        public readonly List<DuplicateAssetGroup> DuplicateAssets = new();
        public readonly List<UnreferencedAsset> UnreferencedAssets = new();
        public long TotalEstimatedSavings;
    }
}
