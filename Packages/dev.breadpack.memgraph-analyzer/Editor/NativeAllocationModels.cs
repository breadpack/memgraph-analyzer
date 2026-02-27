namespace Tools {
    public class NativeAllocationInfo {
        public ulong Address;
        public ulong Size;
        public ulong OverheadSize;
        public ulong PaddingSize;
        public int MemoryRegionIndex;
        public int RootReferenceId;
        public int AllocationSiteId;
    }

    public class NativeMemoryRegionInfo {
        public string Name;
        public int ParentIndex;
        public ulong AddressBase;
        public ulong AddressSize;
        public int FirstAllocationIndex;
        public int NumAllocations;
    }

    public class NativeMemoryLabelInfo {
        public string Name;
    }

    public class NativeRootReferenceInfo {
        public long Id;
        public string AreaName;
        public string ObjectName;
        public long AccumulatedSize;
    }

    public class NativeAllocationSiteInfo {
        public int Id;
        public int MemoryLabelIndex;
        public int[] CallstackSymbolIndices;
    }

    public class NativeCallstackSymbolInfo {
        public ulong Symbol;
        public string ReadableStackTrace;
    }
}
