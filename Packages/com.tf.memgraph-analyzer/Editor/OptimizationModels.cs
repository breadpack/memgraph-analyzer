using System.Collections.Generic;

namespace Tools {
    public enum OptimizationDifficulty { Easy, Medium, Hard }

    public enum OptimizationCategory {
        Texture,
        Mesh,
        Audio,
        Shader,
        Font,
        Animation,
        AssetBundle,
        AllocationPattern,
        GPU,
        VirtualMemory,
        General,
    }

    public class OptimizationRecommendation {
        public string Title;
        public string Description;
        public long EstimatedSavings;
        public OptimizationDifficulty Difficulty;
        public OptimizationCategory Category;
        public List<string> ActionSteps = new();
        public List<string> RelatedAllocations = new();
        public int Priority;
    }

    public class OptimizationResult {
        public readonly List<OptimizationRecommendation> Recommendations = new();
        public long TotalEstimatedSavings;
    }
}
