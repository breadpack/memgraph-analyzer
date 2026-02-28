namespace Tools {
    internal enum AIDocumentType {
        Guide,
        Optimizations,
        Heap,
        Leaks,
        AllocationTrace,
        Unity,
        Vmmap,
        Comparison,
        HeapEvidence,
        UnityEvidence,
    }

    internal class AIDocumentInfo {
        public AIDocumentType Type;
        public string FileName;       // e.g. "02_Heap.md"
        public string DisplayName;    // e.g. "Heap Analysis"
        public bool IsAvailable;
        public int EstimatedTokens;
        public string Highlight;      // e.g. "3 patterns detected, 5 owner groups"
        public string CachedMarkdown;

        public static string GetFileName(AIDocumentType type) {
            return type switch {
                AIDocumentType.Guide           => "00_Guide.md",
                AIDocumentType.Optimizations   => "01_Optimizations.md",
                AIDocumentType.Heap            => "02_Heap.md",
                AIDocumentType.Leaks           => "03_Leaks.md",
                AIDocumentType.AllocationTrace => "04_AllocationTrace.md",
                AIDocumentType.Unity           => "05_Unity.md",
                AIDocumentType.Vmmap           => "06_Vmmap.md",
                AIDocumentType.Comparison      => "07_Comparison.md",
                AIDocumentType.HeapEvidence    => "02a_Heap_Evidence.md",
                AIDocumentType.UnityEvidence   => "05a_Unity_Evidence.md",
                _ => $"{(int)type:D2}_{type}.md",
            };
        }

        public static string GetDisplayName(AIDocumentType type) {
            return type switch {
                AIDocumentType.Guide           => "Guide (Index)",
                AIDocumentType.Optimizations   => "Optimization Recommendations",
                AIDocumentType.Heap            => "Heap Analysis",
                AIDocumentType.Leaks           => "Leak Analysis",
                AIDocumentType.AllocationTrace => "Allocation Trace Analysis",
                AIDocumentType.Unity           => "Unity-Specific Analysis",
                AIDocumentType.Vmmap           => "Virtual Memory Analysis",
                AIDocumentType.Comparison      => "Comparison Diff Analysis",
                AIDocumentType.HeapEvidence    => "Heap Evidence (Full Names)",
                AIDocumentType.UnityEvidence   => "Unity Evidence (Full Names)",
                _ => type.ToString(),
            };
        }
    }
}
