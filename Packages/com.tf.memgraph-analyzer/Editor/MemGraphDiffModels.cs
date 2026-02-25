using System.Collections.Generic;

namespace Tools {
    public enum DiffDirection { Unchanged, Increased, Decreased, New, Removed }
    public enum DiffFilterMode { All, GrowthOnly, ShrinkOnly, NewOnly, RemovedOnly }
    public enum DiffSortMode { AbsoluteDelta, PercentageDelta, TotalSize, ClassName }

    public class MemGraphDiffResult {
        public MemGraphReport Baseline;
        public MemGraphReport Current;
        public OverallDiff Overall = new();
        public HeapDiff Heap = new();
        public VmmapDiff Vmmap = new();
        public FootprintDiff Footprint = new();
        public LeakDiff Leaks = new();
        public readonly List<BreakdownDiff<MemoryOwner>> OwnerDiffs = new();
        public readonly List<BreakdownDiff<string>> PluginDiffs = new();
    }

    public class OverallDiff {
        public long BaselineFootprint;
        public long CurrentFootprint;
        public long FootprintDelta;
        public float FootprintDeltaPercent;

        public long BaselineResident;
        public long CurrentResident;
        public long ResidentDelta;
        public float ResidentDeltaPercent;

        public long BaselineHeapTotal;
        public long CurrentHeapTotal;
        public long HeapDelta;
        public float HeapDeltaPercent;

        public long BaselineDirty;
        public long CurrentDirty;
        public long DirtyDelta;
        public float DirtyDeltaPercent;

        public long BaselineVirtual;
        public long CurrentVirtual;
        public long VirtualDelta;
        public float VirtualDeltaPercent;
    }

    public class HeapAllocationDiff {
        public string ClassName;
        public MemoryOwner Owner;
        public DiffDirection Direction;

        public int BaselineCount;
        public long BaselineBytes;
        public int CurrentCount;
        public long CurrentBytes;

        public long BytesDelta;
        public float BytesDeltaPercent;
        public int CountDelta;
    }

    public class HeapDiff {
        public readonly List<HeapAllocationDiff> Allocations = new();
        public int NewClassCount;
        public int RemovedClassCount;
        public int IncreasedClassCount;
        public int DecreasedClassCount;
        public int UnchangedClassCount;
        public long TotalGrowth;
        public long TotalShrink;
    }

    public class VmmapRegionDiff {
        public string RegionType;
        public DiffDirection Direction;

        public long BaselineResident;
        public long CurrentResident;
        public long ResidentDelta;
        public float ResidentDeltaPercent;

        public long BaselineDirty;
        public long CurrentDirty;
        public long DirtyDelta;

        public long BaselineVirtual;
        public long CurrentVirtual;
        public long VirtualDelta;
    }

    public class VmmapDiff {
        public readonly List<VmmapRegionDiff> Regions = new();
    }

    public class FootprintDiff {
        public long BaselinePhysFootprint;
        public long CurrentPhysFootprint;
        public long PhysFootprintDelta;

        public long BaselineDirty;
        public long CurrentDirty;
        public long DirtyDelta;

        public long BaselineClean;
        public long CurrentClean;
        public long CleanDelta;

        public long BaselineReclaimable;
        public long CurrentReclaimable;
        public long ReclaimableDelta;
    }

    public class LeakDiff {
        public int BaselineCount;
        public int CurrentCount;
        public int CountDelta;

        public long BaselineBytes;
        public long CurrentBytes;
        public long BytesDelta;
    }

    public class BreakdownDiff<TKey> {
        public TKey Key;
        public DiffDirection Direction;
        public long BaselineBytes;
        public long CurrentBytes;
        public long BytesDelta;
        public float BytesDeltaPercent;
    }
}
