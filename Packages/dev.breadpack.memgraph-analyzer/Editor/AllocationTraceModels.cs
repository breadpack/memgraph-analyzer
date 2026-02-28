using System.Collections.Generic;

namespace Tools {
    public enum AllocationCategory {
        Asset,
        GameLogic,
        EngineInternal,
        SystemFramework,
        GCHeap,
    }

    public enum AssetType {
        None,
        Shader,
        Texture,
        Mesh,
        Audio,
        Animation,
        Font,
        ScriptData,
        WebView,
        AssetBundle,
        Prefab,
        Other,
    }

    public enum Controllability {
        UserControllable,
        PartiallyControllable,
        EngineOwned,
        SystemOwned,
    }

    public class TracedAllocation {
        public int CallCount;
        public long TotalBytes;
        public AllocationCategory Category;
        public AssetType AssetType;
        public Controllability Controllability;
        public string TopUserFunction;
        public string TopEngineFunction;
        public readonly List<StackFrame> Frames = new();
        public string Summary;
    }

    public class CategorySummary {
        public AllocationCategory Category;
        public long TotalBytes;
        public int AllocationCount;
        public float Percentage;
    }

    public class AllocationTraceResult {
        public readonly List<TracedAllocation> Allocations = new();
        public readonly List<CategorySummary> CategoryBreakdown = new();
        public long TotalAnalyzedBytes;
        public int TotalAnalyzedCount;
        public string RawOutput;
    }
}
