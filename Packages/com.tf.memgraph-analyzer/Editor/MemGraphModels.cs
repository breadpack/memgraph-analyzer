using System;
using System.Collections.Generic;

namespace Tools {
    public enum MemoryOwner {
        Unknown,
        Unity,
        NativePlugin,
        System,
        ThreadStack,
        GraphicsDriver,
        UnsafeUtility,
    }

    public enum AnalysisPhase {
        Idle,
        RunningFootprint,
        RunningVmmapSummary,
        RunningVmmap,
        RunningHeap,
        RunningLeaks,
        RunningCallTree,
        Categorizing,
        Complete,
        Error,
    }

    public enum MemoryHealthStatus { Good, Warning, Critical }

    public enum InsightSeverity { Info, Warning, Critical }

    public enum InsightCategory {
        MemoryPressure,
        Leaks,
        NativePlugin,
        UnsafeUtility,
        Fragmentation,
        Untracked,
        ThreadStack,
        Graphics,
    }

    public enum Actionability { Fixable, Monitor, SystemOwned }

    public class MemoryInsight {
        public InsightSeverity Severity;
        public string Title;
        public string Description;
        public InsightCategory Category;
        public string Recommendation;
        public int Priority;
    }

    public class LeakGroup {
        public string TypeOrZone;
        public MemoryOwner Owner;
        public readonly List<LeakEntry> Entries = new();
        public long TotalBytes;
        public InsightSeverity Severity;
    }

    public class iOSDeviceProfile {
        public string Name;
        public long TotalRAM;
        public long JetsamLimit;
        public long WarningThreshold;
    }

    public static class iOSDeviceProfiles {
        public static readonly iOSDeviceProfile[] All = {
            new() { Name = "iPhone SE/8 (3GB)", TotalRAM = 3L * 1024 * 1024 * 1024, JetsamLimit = 1395L * 1024 * 1024, WarningThreshold = 1228L * 1024 * 1024 },
            new() { Name = "iPhone 11/XR (3GB)", TotalRAM = 3L * 1024 * 1024 * 1024, JetsamLimit = 2048L * 1024 * 1024, WarningThreshold = 1740L * 1024 * 1024 },
            new() { Name = "iPhone 12/13/14 (4GB)", TotalRAM = 4L * 1024 * 1024 * 1024, JetsamLimit = 2764L * 1024 * 1024, WarningThreshold = 2355L * 1024 * 1024 },
            new() { Name = "iPhone 14 Pro/15 Pro (6GB)", TotalRAM = 6L * 1024 * 1024 * 1024, JetsamLimit = 3993L * 1024 * 1024, WarningThreshold = 3481L * 1024 * 1024 },
            new() { Name = "iPhone 16 Pro (8GB)", TotalRAM = 8L * 1024 * 1024 * 1024, JetsamLimit = 5530L * 1024 * 1024, WarningThreshold = 4915L * 1024 * 1024 },
            new() { Name = "iPad (4GB)", TotalRAM = 4L * 1024 * 1024 * 1024, JetsamLimit = 2764L * 1024 * 1024, WarningThreshold = 2355L * 1024 * 1024 },
            new() { Name = "iPad Pro 11\" (8GB)", TotalRAM = 8L * 1024 * 1024 * 1024, JetsamLimit = 5530L * 1024 * 1024, WarningThreshold = 4915L * 1024 * 1024 },
            new() { Name = "iPad Pro 12.9\" (16GB)", TotalRAM = 16L * 1024 * 1024 * 1024, JetsamLimit = 11264L * 1024 * 1024, WarningThreshold = 10240L * 1024 * 1024 },
        };

        public static int DefaultIndex => 2; // iPhone 12/13/14
    }

    public class MemGraphReport {
        public string FilePath;
        public DateTime AnalysisTime;
        public AnalysisPhase Phase = AnalysisPhase.Idle;
        public string ErrorMessage;

        public FootprintResult Footprint = new();
        public VmmapResult Vmmap = new();
        public HeapResult Heap = new();
        public LeaksResult Leaks = new();
        public MemorySummary Summary = new();
        public readonly List<MemoryInsight> Insights = new();
        public List<CallTreeEntry> CallTree = new();
    }

    public class FootprintResult {
        public string RawOutput = "";
        public long PhysFootprint;
        public long PhysFootprintPeak;
        public long TotalDirty;
        public long TotalClean;
        public long TotalReclaimable;
        public readonly List<FootprintCategory> Categories = new();
    }

    public class FootprintCategory {
        public string Name;
        public long DirtySize;
        public long CleanSize;
        public long ReclaimableSize;
        public int RegionCount;
    }

    public class MemorySummary {
        public long TotalVirtual;
        public long TotalResident;
        public long TotalDirty;
        public long TotalSwapped;

        public long TrackedByUnity;
        public long UntrackedByUnity;

        public MemoryHealthStatus OverallHealth;

        public readonly List<MemoryCategoryBreakdown> CategoryBreakdowns = new();
        public readonly Dictionary<MemoryOwner, long> OwnerBreakdowns = new();
        public readonly Dictionary<string, long> PluginBreakdowns = new();
    }

    public class MemoryCategoryBreakdown {
        public string Category;
        public long Size;
        public float Percentage;
    }

    public class VmmapResult {
        public string RawOutput = "";
        public string DetailedRawOutput = "";
        public readonly List<VmmapSummaryRow> Summary = new();
        public readonly List<VmmapRegion> Regions = new();
        public VmmapSummaryRow Total;
        public int ParseErrorCount;
    }

    public class VmmapSummaryRow {
        public string RegionType;
        public long VirtualSize;
        public long ResidentSize;
        public long DirtySize;
        public long SwappedSize;
        public int RegionCount;
    }

    public class VmmapRegion {
        public string AddressStart;
        public string AddressEnd;
        public long Size;
        public string Protection;
        public string ShareMode;
        public string RegionType;
        public string Detail;
    }

    public class HeapResult {
        public string RawOutput = "";
        public readonly List<HeapAllocation> Allocations = new();
        public long TotalBytes;
        public int TotalCount;
        public int ParseErrorCount;
    }

    public class HeapSizeBucket {
        public long Size;
        public int Count;
        public long TotalBytes;
    }

    public class HeapAllocation {
        public int Count;
        public long TotalBytes;
        public long AverageSize;
        public string ClassName;
        public string Binary;
        public MemoryOwner Owner;
        public List<HeapSizeBucket> SizeDistribution;
    }

    public class CallTreeEntry {
        public string FunctionName;
        public string Binary;
        public int Count;
        public long TotalBytes;
        public int Depth;
        public List<CallTreeEntry> Children = new();
    }

    public class StackFrame {
        public string FunctionName;
        public string Binary;
        public string Address;
    }

    public class AddressTrace {
        public string Address;
        public long Size;
        public readonly List<StackFrame> Frames = new();
    }

    public class AddressTraceResult {
        public readonly List<AddressTrace> Traces = new();
        public bool IsLoading;
        public int LoadingStep; // 0=idle, 1=heap -addresses, 2=malloc_history
        public string ErrorMessage;
    }

    public class LeaksResult {
        public string RawOutput = "";
        public int TotalLeakCount;
        public long TotalLeakBytes;
        public readonly List<LeakEntry> Leaks = new();
        public int ParseErrorCount;
    }

    public class LeakEntry {
        public string Address;
        public long Size;
        public string TypeOrZone;
        public string StackTrace;
        public MemoryOwner Owner;
    }

    public class CommandResult {
        public bool Success;
        public string Output;
        public string Error;
        public int ExitCode;
    }
}
