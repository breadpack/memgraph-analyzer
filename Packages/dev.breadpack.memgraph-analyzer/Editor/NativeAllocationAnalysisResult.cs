using System.Collections.Generic;

namespace Tools {
    public class NativeAllocationAnalysis {
        public readonly List<RegionTreeNode> RegionTree = new();
        public readonly List<RootRefGroup> RootReferenceBreakdown = new();
        public readonly List<LabelGroup> LabelBreakdown = new();
        public readonly Dictionary<int, int> AllocationToObjectMap = new();
        public readonly List<int> UnmappedAllocations = new();
        public readonly Dictionary<int, string[]> ResolvedCallstacks = new();
        public long TotalOverhead;
        public long TotalPadding;
        public long TotalUseful;
    }

    public class RegionTreeNode {
        public int RegionIndex;
        public string Name;
        public readonly List<RegionTreeNode> Children = new();
        public long TotalSize;
        public int AllocationCount;
        public long OverheadSize;
    }

    public class RootRefGroup {
        public long RootReferenceId;
        public string AreaName;
        public string ObjectName;
        public long TotalSize;
        public int AllocationCount;
        public readonly List<int> Allocations = new();
        public readonly List<int> LinkedNativeObjects = new();
    }

    public class LabelGroup {
        public string Name;
        public long TotalSize;
        public int AllocationCount;
    }
}
