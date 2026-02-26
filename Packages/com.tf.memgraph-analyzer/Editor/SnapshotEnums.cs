namespace Tools {
    public enum SnapshotAnalysisPhase {
        Idle,
        Loading,
        ExtractingTypes,
        CrawlingHeap,
        BuildingAssemblyTree,
        CalculatingRetained,
        Complete,
        Error,
    }

    public enum AssemblyClassification {
        UserCode,
        UnityRuntime,
        UnityEditor,
        DotNet,
        ThirdParty,
    }

    public enum NativeTypeCategory {
        Texture,
        Mesh,
        Material,
        Shader,
        AnimationClip,
        AudioClip,
        Font,
        ScriptableObject,
        GameObject,
        Component,
        Other,
    }
}
