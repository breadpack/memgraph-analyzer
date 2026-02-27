using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools {
    public static class OptimizationGuide {
        public static OptimizationResult Analyze(MemGraphReport report) {
            var result = new OptimizationResult();
            if (report == null) return result;

            AnalyzeUnityAssetPatterns(report, result);
            AnalyzeAllocationPatterns(report, result);
            AnalyzeVmmapPatterns(report, result);
            AnalyzeFootprintPatterns(report, result);
            RankRecommendations(result);
            return result;
        }

        private static void AnalyzeUnityAssetPatterns(MemGraphReport report, OptimizationResult result) {
            var allocations = report.Heap.Allocations;
            if (allocations == null || allocations.Count == 0) return;

            // Texture pattern
            var textureAllocs = new List<string>();
            long textureBytes = 0;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("TEXTURE2D") || upper.Contains("RENDERTEXTURE") ||
                    upper.Contains("CUBEMAP") || upper.Contains("TEXTURE_")) {
                    textureBytes += alloc.TotalBytes;
                    textureAllocs.Add(alloc.ClassName);
                }
            }
            if (textureBytes > 100L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Texture Memory Optimization",
                    Description = $"Textures are using {VmmapParser.FormatSize(textureBytes)}. " +
                                  "ASTC compression and mipmap optimization can significantly reduce this.",
                    EstimatedSavings = textureBytes / 2,
                    Difficulty = OptimizationDifficulty.Easy,
                    Category = OptimizationCategory.Texture,
                    ActionSteps = new List<string> {
                        "Switch texture compression to ASTC (4x4 for quality, 6x6 for balanced, 8x8 for size)",
                        "Enable mipmaps only for 3D scene textures; disable for UI textures",
                        "Reduce max texture size where visual quality permits (2048 -> 1024)",
                        "Use Sprite Atlas for UI elements to reduce draw calls and waste",
                        "Audit RenderTextures - release unused ones after post-processing",
                    },
                    RelatedAllocations = textureAllocs.Take(10).ToList(),
                });
            }

            // Mesh pattern
            var meshAllocs = new List<string>();
            long meshBytes = 0;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("MESH") || upper.Contains("VERTEXBUFFER") || upper.Contains("INDEXBUFFER")) {
                    meshBytes += alloc.TotalBytes;
                    meshAllocs.Add(alloc.ClassName);
                }
            }
            if (meshBytes > 50L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Mesh Memory Optimization",
                    Description = $"Meshes are using {VmmapParser.FormatSize(meshBytes)}. " +
                                  "LOD and mesh compression can reduce memory usage.",
                    EstimatedSavings = meshBytes * 30 / 100,
                    Difficulty = OptimizationDifficulty.Medium,
                    Category = OptimizationCategory.Mesh,
                    ActionSteps = new List<string> {
                        "Implement LOD groups for distant objects",
                        "Enable Mesh Compression (Low/Medium/High) in import settings",
                        "Remove unused vertex attributes (tangents, UV2) via import settings",
                        "Use 16-bit index buffers for meshes with <65k vertices",
                        "Consider mesh instancing for repeated geometry",
                    },
                    RelatedAllocations = meshAllocs.Take(10).ToList(),
                });
            }

            // Audio pattern
            var audioAllocs = new List<string>();
            long audioBytes = 0;
            bool hasFmod = false;
            bool hasWwise = false;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("AUDIOCLIP") || upper.Contains("AUDIO_")) {
                    audioBytes += alloc.TotalBytes;
                    audioAllocs.Add(alloc.ClassName);
                }
                if (upper.Contains("FMOD")) hasFmod = true;
                if (upper.Contains("WWISE")) hasWwise = true;
            }
            // Also count FMOD/Wwise plugin memory
            foreach (var kv in report.Summary.PluginBreakdowns) {
                var ku = kv.Key.ToUpperInvariant();
                if (ku.Contains("FMOD") || ku.Contains("WWISE"))
                    audioBytes += kv.Value;
            }
            if (audioBytes > 30L * 1024 * 1024) {
                var steps = new List<string> {
                    "Use streaming for music and long audio clips (> 200KB)",
                    "Compress short SFX to ADPCM or Vorbis (Quality 50-70%)",
                    "Force Mono for non-spatial audio (halves memory)",
                    "Reduce sample rate to 22050Hz for voice/SFX",
                };
                if (hasFmod) {
                    steps.Add("FMOD: Reduce loaded bank count; use bank loading/unloading per scene");
                    steps.Add("FMOD: Set streaming threshold in FMOD settings");
                }
                if (hasWwise) {
                    steps.Add("Wwise: Optimize sound bank granularity");
                    steps.Add("Wwise: Enable streaming for large audio assets");
                }
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Audio Memory Optimization",
                    Description = $"Audio is using {VmmapParser.FormatSize(audioBytes)}. " +
                                  "Streaming and compression can free significant memory.",
                    EstimatedSavings = audioBytes * 60 / 100,
                    Difficulty = OptimizationDifficulty.Easy,
                    Category = OptimizationCategory.Audio,
                    ActionSteps = steps,
                    RelatedAllocations = audioAllocs.Take(10).ToList(),
                });
            }

            // Shader pattern
            var shaderAllocs = new List<string>();
            long shaderBytes = 0;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("SHADER") || upper.Contains("SHADERVARIANT") || upper.Contains("MATERIAL")) {
                    shaderBytes += alloc.TotalBytes;
                    shaderAllocs.Add(alloc.ClassName);
                }
            }
            if (shaderBytes > 20L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Shader/Material Memory Optimization",
                    Description = $"Shaders and materials are using {VmmapParser.FormatSize(shaderBytes)}. " +
                                  "Shader variant stripping can reduce this significantly.",
                    EstimatedSavings = shaderBytes * 40 / 100,
                    Difficulty = OptimizationDifficulty.Hard,
                    Category = OptimizationCategory.Shader,
                    ActionSteps = new List<string> {
                        "Enable shader variant stripping in Player Settings",
                        "Use IPreprocessShaders to strip unused shader keywords",
                        "Reduce material count by sharing materials where possible",
                        "Use shader_feature instead of multi_compile for optional features",
                        "Collect shader variants via ShaderVariantCollection and strip the rest",
                    },
                    RelatedAllocations = shaderAllocs.Take(10).ToList(),
                });
            }

            // Font pattern
            var fontAllocs = new List<string>();
            long fontBytes = 0;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("TMP_FONTASSET") || upper.Contains("FONT") ||
                    upper.Contains("__FONT_DATA") || upper.Contains("FONTDATA")) {
                    fontBytes += alloc.TotalBytes;
                    fontAllocs.Add(alloc.ClassName);
                }
            }
            if (fontBytes > 10L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Font Memory Optimization",
                    Description = $"Fonts are using {VmmapParser.FormatSize(fontBytes)}. " +
                                  "Atlas size reduction and character set limiting can help.",
                    EstimatedSavings = fontBytes / 2,
                    Difficulty = OptimizationDifficulty.Easy,
                    Category = OptimizationCategory.Font,
                    ActionSteps = new List<string> {
                        "Reduce TMP font atlas size (4096 -> 2048 or 1024)",
                        "Include only required character sets (ASCII + target language)",
                        "Use SDF font rendering to reduce atlas resolution needs",
                        "Share font assets across UI elements instead of duplicating",
                        "Unload unused font assets when switching scenes",
                    },
                    RelatedAllocations = fontAllocs.Take(10).ToList(),
                });
            }

            // Animation pattern
            var animAllocs = new List<string>();
            long animBytes = 0;
            foreach (var alloc in allocations) {
                var upper = (alloc.ClassName ?? "").ToUpperInvariant();
                if (upper.Contains("ANIMATIONCLIP") || upper.Contains("ANIMATION_") ||
                    (upper.Contains("ANIMATION") && !upper.Contains("ANIMATOR"))) {
                    animBytes += alloc.TotalBytes;
                    animAllocs.Add(alloc.ClassName);
                }
            }
            if (animBytes > 30L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Animation Memory Optimization",
                    Description = $"Animations are using {VmmapParser.FormatSize(animBytes)}. " +
                                  "Compression and curve reduction can lower memory usage.",
                    EstimatedSavings = animBytes * 40 / 100,
                    Difficulty = OptimizationDifficulty.Medium,
                    Category = OptimizationCategory.Animation,
                    ActionSteps = new List<string> {
                        "Enable Animation Compression in import settings (Keyframe Reduction)",
                        "Reduce animation precision (Optimal compression)",
                        "Remove constant curves and scale curves if unused",
                        "Use Animation Rigging for procedural animations where possible",
                        "Unload animation clips when characters leave the scene",
                    },
                    RelatedAllocations = animAllocs.Take(10).ToList(),
                });
            }
        }

        private static void AnalyzeAllocationPatterns(MemGraphReport report, OptimizationResult result) {
            var allocations = report.Heap.Allocations;
            if (allocations == null || allocations.Count == 0) return;

            // Small-many allocation pattern: count>1000, avgSize<1KB, total>1MB
            var poolingCandidates = new List<string>();
            long poolingTotal = 0;
            foreach (var alloc in allocations) {
                if (alloc.Count > 1000 && alloc.AverageSize < 1024 && alloc.TotalBytes > 1024 * 1024) {
                    poolingCandidates.Add($"{alloc.ClassName} ({alloc.Count}x, avg {VmmapParser.FormatSize(alloc.AverageSize)})");
                    poolingTotal += alloc.TotalBytes;
                }
            }
            if (poolingCandidates.Count > 0 && poolingTotal > 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Object Pooling Opportunity",
                    Description = $"{poolingCandidates.Count} classes have >1000 small allocations " +
                                  $"totaling {VmmapParser.FormatSize(poolingTotal)}. " +
                                  "Pooling can reduce allocation overhead and fragmentation.",
                    EstimatedSavings = poolingTotal * 30 / 100,
                    Difficulty = OptimizationDifficulty.Medium,
                    Category = OptimizationCategory.AllocationPattern,
                    ActionSteps = new List<string> {
                        "Implement object pooling for frequently allocated/deallocated types",
                        "Use Unity's ObjectPool<T> or NativeArray for temporary buffers",
                        "Consider using struct types to avoid heap allocations",
                        "Review allocation-heavy hot paths with Unity Profiler",
                    },
                    RelatedAllocations = poolingCandidates.Take(10).ToList(),
                });
            }

            // Large single allocation pattern: avgSize>1MB, fixable owner
            var streamingCandidates = new List<string>();
            long streamingTotal = 0;
            foreach (var alloc in allocations) {
                if (alloc.AverageSize > 1024 * 1024 &&
                    HeapParser.GetActionability(alloc) == Actionability.Fixable) {
                    streamingCandidates.Add($"{alloc.ClassName} (avg {VmmapParser.FormatSize(alloc.AverageSize)})");
                    streamingTotal += alloc.TotalBytes;
                }
            }
            if (streamingCandidates.Count > 0 && streamingTotal > 5L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "Large Allocation Streaming",
                    Description = $"{streamingCandidates.Count} classes have large average allocations (>1MB) " +
                                  $"totaling {VmmapParser.FormatSize(streamingTotal)}. " +
                                  "Streaming or lazy loading can reduce peak memory.",
                    EstimatedSavings = streamingTotal * 20 / 100,
                    Difficulty = OptimizationDifficulty.Medium,
                    Category = OptimizationCategory.AllocationPattern,
                    ActionSteps = new List<string> {
                        "Use Addressables async loading to spread load over time",
                        "Implement asset streaming for scene transitions",
                        "Unload assets that are not visible or needed",
                        "Consider splitting large assets into smaller chunks",
                    },
                    RelatedAllocations = streamingCandidates.Take(10).ToList(),
                });
            }
        }

        private static void AnalyzeVmmapPatterns(MemGraphReport report, OptimizationResult result) {
            if (report.Vmmap.Summary == null || report.Vmmap.Summary.Count == 0) return;

            // GPU memory pattern: IOKit/IOSurface resident > 200MB
            long gpuBytes = 0;
            var gpuRegions = new List<string>();
            foreach (var row in report.Vmmap.Summary) {
                var upper = (row.RegionType ?? "").ToUpperInvariant();
                if (upper.Contains("IOKIT") || upper.Contains("IOSURFACE")) {
                    gpuBytes += row.ResidentSize;
                    gpuRegions.Add($"{row.RegionType} ({VmmapParser.FormatSize(row.ResidentSize)})");
                }
            }
            if (gpuBytes > 200L * 1024 * 1024) {
                result.Recommendations.Add(new OptimizationRecommendation {
                    Title = "GPU Memory Optimization",
                    Description = $"GPU-related memory (IOKit/IOSurface) is {VmmapParser.FormatSize(gpuBytes)}. " +
                                  "Reducing render target count and resolution can help.",
                    EstimatedSavings = gpuBytes * 30 / 100,
                    Difficulty = OptimizationDifficulty.Medium,
                    Category = OptimizationCategory.GPU,
                    ActionSteps = new List<string> {
                        "Reduce render target resolution (use dynamic resolution scaling)",
                        "Minimize the number of active render targets / camera stacking",
                        "Use R8/R16 formats instead of RGBA32 for depth/shadow maps",
                        "Reduce post-processing chain complexity",
                        "Disable unused camera render features (MSAA, HDR if not needed)",
                    },
                    RelatedAllocations = gpuRegions,
                });
            }

            // Excessive threads pattern: Stack region count > 50
            foreach (var row in report.Vmmap.Summary) {
                if ((row.RegionType ?? "").ToUpperInvariant().Contains("STACK") && row.RegionCount > 50) {
                    long excessCount = row.RegionCount - 50;
                    long savings = excessCount * 512 * 1024; // 512KB per stack
                    result.Recommendations.Add(new OptimizationRecommendation {
                        Title = "Excessive Thread Stacks",
                        Description = $"{row.RegionCount} thread stack regions detected. " +
                                      $"Each stack consumes ~512KB. {excessCount} excess threads " +
                                      $"waste ~{VmmapParser.FormatSize(savings)}.",
                        EstimatedSavings = savings,
                        Difficulty = OptimizationDifficulty.Hard,
                        Category = OptimizationCategory.VirtualMemory,
                        ActionSteps = new List<string> {
                            "Audit thread creation in native plugins (FMOD, networking libs)",
                            "Use thread pools instead of dedicated threads",
                            "Reduce Unity Job System worker thread count if possible",
                            "Check for plugin thread leaks across scene loads",
                        },
                        RelatedAllocations = new List<string> {
                            $"Stack regions: {row.RegionCount} (virtual: {VmmapParser.FormatSize(row.VirtualSize)})"
                        },
                    });
                    break;
                }
            }
        }

        private static void AnalyzeFootprintPatterns(MemGraphReport report, OptimizationResult result) {
            var fp = report.Footprint;
            if (fp == null || fp.Categories.Count == 0) return;

            // Look for large reclaimable categories
            long totalReclaimable = fp.TotalReclaimable;
            if (totalReclaimable > 50L * 1024 * 1024) {
                var largeCats = new List<string>();
                foreach (var cat in fp.Categories) {
                    if (cat.ReclaimableSize > 10L * 1024 * 1024)
                        largeCats.Add($"{cat.Name} ({VmmapParser.FormatSize(cat.ReclaimableSize)})");
                }
                if (largeCats.Count > 0) {
                    result.Recommendations.Add(new OptimizationRecommendation {
                        Title = "Reclaimable Memory Cleanup",
                        Description = $"{VmmapParser.FormatSize(totalReclaimable)} of memory is marked reclaimable. " +
                                      "Triggering cleanup before memory-intensive operations can help.",
                        EstimatedSavings = totalReclaimable / 2,
                        Difficulty = OptimizationDifficulty.Easy,
                        Category = OptimizationCategory.General,
                        ActionSteps = new List<string> {
                            "Call Resources.UnloadUnusedAssets() at scene transitions",
                            "Trigger GC.Collect() before loading large scenes",
                            "Review asset lifecycle - ensure assets are unloaded when done",
                        },
                        RelatedAllocations = largeCats,
                    });
                }
            }
        }

        private static void RankRecommendations(OptimizationResult result) {
            long totalSavings = 0;
            foreach (var rec in result.Recommendations) {
                float difficultyWeight = rec.Difficulty switch {
                    OptimizationDifficulty.Easy => 3.0f,
                    OptimizationDifficulty.Medium => 2.0f,
                    OptimizationDifficulty.Hard => 1.0f,
                    _ => 1.0f,
                };
                rec.Priority = -(int)(rec.EstimatedSavings / (1024.0 * 1024) * difficultyWeight);
                totalSavings += rec.EstimatedSavings;
            }
            result.TotalEstimatedSavings = totalSavings;
            result.Recommendations.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}
