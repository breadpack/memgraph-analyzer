using System.Collections.Generic;
using System.Linq;

namespace Tools {
    /// <summary>
    /// Classifies parsed allocations by category, asset type, and controllability.
    /// Uses callstack analysis to determine the source and nature of each allocation.
    /// </summary>
    public static class AllocationClassifier {
        public static AllocationTraceResult Classify(List<TracedAllocation> raw) {
            var result = new AllocationTraceResult();
            if (raw == null || raw.Count == 0) return result;

            foreach (var alloc in raw) {
                alloc.Category = DetermineCategory(alloc.Frames);
                alloc.AssetType = DetermineAssetType(alloc.Frames);
                alloc.Controllability = DetermineControllability(alloc.Category, alloc.Frames);
                alloc.TopUserFunction = FindTopUserFunction(alloc.Frames);
                alloc.TopEngineFunction = FindTopEngineFunction(alloc.Frames);
                alloc.Summary = BuildSummary(alloc);

                result.Allocations.Add(alloc);
                result.TotalAnalyzedBytes += alloc.TotalBytes;
                result.TotalAnalyzedCount += alloc.CallCount;
            }

            BuildCategoryBreakdown(result);
            return result;
        }

        static AllocationCategory DetermineCategory(List<StackFrame> frames) {
            if (frames == null || frames.Count == 0) return AllocationCategory.SystemFramework;

            bool hasUserCode = false;
            bool hasUnityEngine = false;
            bool hasAssetLoading = false;

            foreach (var frame in frames) {
                var func = frame.FunctionName ?? "";
                var binary = frame.Binary ?? "";
                var funcUpper = func.ToUpperInvariant();
                var binaryUpper = binary.ToUpperInvariant();

                // GC heap detection
                if (funcUpper.Contains("GC_EXPAND_HP") || funcUpper.Contains("GC_ALLOC") ||
                    funcUpper.Contains("IL2CPP_GC") || funcUpper.Contains("BDWGC") ||
                    funcUpper.Contains("GC_MALLOC") || funcUpper.Contains("GC_GCOLLECT"))
                    return AllocationCategory.GCHeap;

                if (CallTreeParser.IsUserCode(func))
                    hasUserCode = true;

                if (binaryUpper.Contains("UNITYFRAMEWORK") || binaryUpper.Contains("UNITY"))
                    hasUnityEngine = true;

                if (IsAssetLoadingFunction(funcUpper))
                    hasAssetLoading = true;
            }

            if (hasAssetLoading)
                return AllocationCategory.Asset;

            if (hasUserCode)
                return AllocationCategory.GameLogic;

            if (hasUnityEngine)
                return AllocationCategory.EngineInternal;

            return AllocationCategory.SystemFramework;
        }

        static AssetType DetermineAssetType(List<StackFrame> frames) {
            if (frames == null || frames.Count == 0) return AssetType.None;

            foreach (var frame in frames) {
                var func = (frame.FunctionName ?? "").ToUpperInvariant();

                // Shader
                if (func.Contains("SHADER") && (func.Contains("AWAKEFROMLOAD") || func.Contains("SUBPROGRAM") ||
                    func.Contains("COMPILE") || func.Contains("PARSE")))
                    return AssetType.Shader;
                if (func.Contains("SUBPROGRAM") || func.Contains("SHADER_VARIANT"))
                    return AssetType.Shader;

                // Texture
                if (func.Contains("TEXTURE2D") || func.Contains("RENDERTEXTURE") ||
                    func.Contains("IMAGEDECODER") || func.Contains("UPLOADTEXTURE") ||
                    func.Contains("TEXTUREDATA"))
                    return AssetType.Texture;

                // Mesh
                if ((func.Contains("MESH") && !func.Contains("MESSAGE")) ||
                    func.Contains("VERTEXDATA") || func.Contains("INDEXBUFFER"))
                    return AssetType.Mesh;

                // Audio
                if (func.Contains("AUDIOCLIP") || func.Contains("FMOD") ||
                    func.Contains("AUDIO_LOAD") || func.Contains("AUDIODATA"))
                    return AssetType.Audio;

                // Animation
                if (func.Contains("ANIMATIONCLIP") || func.Contains("ANIMATION_LOAD") ||
                    func.Contains("ANIMATORCONTROLLER"))
                    return AssetType.Animation;

                // Font
                if (func.Contains("FONT") || func.Contains("TMP_FONTASSET") ||
                    func.Contains("TEXTMESHPRO") || func.Contains("FONTATLAS"))
                    return AssetType.Font;

                // ScriptData
                if (func.Contains("SCRIPTDATABASE") || func.Contains("SCRIPTBASE") ||
                    func.Contains("SCRIPTDATA"))
                    return AssetType.ScriptData;

                // WebView
                if (func.Contains("WEBVIEW") || func.Contains("WEBKIT") ||
                    func.Contains("WKWEBVIEW") || func.Contains("UNIWEBVIEW"))
                    return AssetType.WebView;

                // AssetBundle
                if (func.Contains("ASSETBUNDLE") || func.Contains("ADDRESSABLE") ||
                    func.Contains("BUNDLELOAD"))
                    return AssetType.AssetBundle;

                // Prefab
                if (func.Contains("PREFAB") || func.Contains("INSTANTIATE"))
                    return AssetType.Prefab;
            }

            // If category is Asset but no specific type matched
            return AssetType.None;
        }

        static Controllability DetermineControllability(AllocationCategory category, List<StackFrame> frames) {
            switch (category) {
                case AllocationCategory.GameLogic:
                    return Controllability.UserControllable;

                case AllocationCategory.Asset:
                    // If user code is in the callstack, user can control asset loading
                    if (frames != null) {
                        foreach (var frame in frames) {
                            if (CallTreeParser.IsUserCode(frame.FunctionName))
                                return Controllability.UserControllable;
                        }
                    }
                    return Controllability.PartiallyControllable;

                case AllocationCategory.GCHeap:
                    // GC heap may be influenced by user code
                    if (frames != null) {
                        foreach (var frame in frames) {
                            if (CallTreeParser.IsUserCode(frame.FunctionName))
                                return Controllability.UserControllable;
                        }
                    }
                    return Controllability.PartiallyControllable;

                case AllocationCategory.EngineInternal:
                    return Controllability.EngineOwned;

                case AllocationCategory.SystemFramework:
                    return Controllability.SystemOwned;

                default:
                    return Controllability.SystemOwned;
            }
        }

        static string FindTopUserFunction(List<StackFrame> frames) {
            if (frames == null) return null;
            // Search from bottom (caller) to top (callee) - last user code frame is the entry point
            for (int i = frames.Count - 1; i >= 0; i--) {
                if (CallTreeParser.IsUserCode(frames[i].FunctionName))
                    return CallTreeParser.FormatFunctionName(frames[i].FunctionName);
            }
            return null;
        }

        static string FindTopEngineFunction(List<StackFrame> frames) {
            if (frames == null) return null;
            foreach (var frame in frames) {
                var binaryUpper = (frame.Binary ?? "").ToUpperInvariant();
                if (binaryUpper.Contains("UNITYFRAMEWORK") || binaryUpper.Contains("UNITY"))
                    return frame.FunctionName;
            }
            return null;
        }

        static string BuildSummary(TracedAllocation alloc) {
            var size = VmmapParser.FormatSize(alloc.TotalBytes);
            var parts = new List<string>();

            parts.Add($"{alloc.CallCount} call{(alloc.CallCount != 1 ? "s" : "")}");

            if (alloc.AssetType != AssetType.None) {
                parts.Add(alloc.AssetType.ToString());
            }

            if (!string.IsNullOrEmpty(alloc.TopUserFunction)) {
                parts.Add($"via {alloc.TopUserFunction}");
            } else if (!string.IsNullOrEmpty(alloc.TopEngineFunction)) {
                var formatted = CallTreeParser.FormatFunctionName(alloc.TopEngineFunction);
                parts.Add($"via {formatted}");
            }

            return $"{size} ({string.Join(", ", parts)})";
        }

        static void BuildCategoryBreakdown(AllocationTraceResult result) {
            var catMap = new Dictionary<AllocationCategory, (long bytes, int count)>();

            foreach (var alloc in result.Allocations) {
                if (!catMap.ContainsKey(alloc.Category))
                    catMap[alloc.Category] = (0, 0);

                var existing = catMap[alloc.Category];
                catMap[alloc.Category] = (existing.bytes + alloc.TotalBytes, existing.count + alloc.CallCount);
            }

            long totalBytes = result.TotalAnalyzedBytes > 0 ? result.TotalAnalyzedBytes : 1;

            foreach (var kv in catMap.OrderByDescending(x => x.Value.bytes)) {
                result.CategoryBreakdown.Add(new CategorySummary {
                    Category = kv.Key,
                    TotalBytes = kv.Value.bytes,
                    AllocationCount = kv.Value.count,
                    Percentage = (float)kv.Value.bytes / totalBytes * 100f,
                });
            }
        }

        static bool IsAssetLoadingFunction(string funcUpper) {
            return funcUpper.Contains("SHADER") && funcUpper.Contains("AWAKEFROMLOAD") ||
                   funcUpper.Contains("TEXTURE2D") ||
                   funcUpper.Contains("RENDERTEXTURE") ||
                   funcUpper.Contains("IMAGEDECODER") ||
                   funcUpper.Contains("MESH") && funcUpper.Contains("LOAD") ||
                   funcUpper.Contains("AUDIOCLIP") ||
                   funcUpper.Contains("ANIMATIONCLIP") ||
                   funcUpper.Contains("ASSETBUNDLE") ||
                   funcUpper.Contains("ADDRESSABLE") ||
                   funcUpper.Contains("FONTASSET") ||
                   funcUpper.Contains("SCRIPTDATABASE") ||
                   funcUpper.Contains("WEBVIEW") || funcUpper.Contains("WEBKIT") ||
                   funcUpper.Contains("FMOD") && funcUpper.Contains("LOAD");
        }
    }
}
