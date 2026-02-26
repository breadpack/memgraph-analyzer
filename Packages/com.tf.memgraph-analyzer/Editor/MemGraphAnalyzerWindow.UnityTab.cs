using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _unityScrollPos;

        private void DrawUnityTab() {
            AnalyzerGuidance.DrawTabHeader("Tracked vs untracked memory, native plugin breakdown, UnsafeUtility, thread stacks, and GPU memory.");
            _unityScrollPos = EditorGUILayout.BeginScrollView(_unityScrollPos);

            DrawTrackedVsUntrackedSection();
            GUILayout.Space(8);
            DrawPluginBreakdownSection();
            GUILayout.Space(8);
            DrawUnsafeUtilitySection();
            GUILayout.Space(8);
            DrawThreadStackSection();
            GUILayout.Space(8);
            DrawGraphicsMemorySection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTrackedVsUntrackedSection() {
            GUILayout.Label("Unity Tracked vs Untracked", _headerStyle);
            GUILayout.Label(
                "Tracked = Unity Profiler-visible allocations. " +
                "Untracked = native plugin, system, or unsafe code allocations invisible to Unity Profiler.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            long tracked = _report.Summary.TrackedByUnity;
            long untracked = _report.Summary.UntrackedByUnity;
            long total = tracked + untracked;

            DrawMetricRow("Unity Tracked (heap)", tracked);
            DrawMetricRow("Untracked (heap)", untracked);

            if (total > 0) {
                GUILayout.Space(4);

                // Stacked bar
                var barRect = EditorGUILayout.GetControlRect(false, 24);
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);

                    float trackedRatio = (float)tracked / total;
                    var trackedRect = new Rect(barRect.x, barRect.y, barRect.width * trackedRatio, barRect.height);
                    EditorGUI.DrawRect(trackedRect, BarUnityColor);

                    var untrackedRect = new Rect(barRect.x + trackedRect.width, barRect.y,
                        barRect.width - trackedRect.width, barRect.height);
                    EditorGUI.DrawRect(untrackedRect, BarUnsafeColor);
                }

                EditorGUILayout.BeginHorizontal();
                DrawColorLegend(BarUnityColor, $"Tracked: {VmmapParser.FormatSize(tracked)} ({(float)tracked / total * 100:F1}%)");
                DrawColorLegend(BarUnsafeColor, $"Untracked: {VmmapParser.FormatSize(untracked)} ({(float)untracked / total * 100:F1}%)");
                EditorGUILayout.EndHorizontal();
            }

            if (untracked > tracked) {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "Untracked memory exceeds Unity-tracked memory. " +
                    "Check native plugins, UnsafeUtility allocations, and system memory.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPluginBreakdownSection() {
            var plugins = _report.Summary.PluginBreakdowns;
            if (plugins.Count == 0) {
                GUILayout.Label("Native Plugins", _headerStyle);
                GUILayout.Label("No native plugin allocations detected in heap.", _mutedStyle);
                return;
            }

            GUILayout.Label("Native Plugin Memory", _headerStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var sorted = plugins.OrderByDescending(kv => kv.Value).ToList();
            long maxSize = sorted[0].Value;

            foreach (var kv in sorted) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key, GUILayout.Width(120));

                var barRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.MinWidth(100));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = maxSize > 0 ? (float)kv.Value / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, BarPluginColor);
                }

                GUILayout.Label(VmmapParser.FormatSize(kv.Value), GUILayout.Width(80));

                // Plugin health indicator
                string healthLabel;
                GUIStyle healthStyle;
                if (kv.Value > 100L * 1024 * 1024) {
                    healthLabel = "Critical";
                    healthStyle = _errorStyle;
                } else if (kv.Value > 50L * 1024 * 1024) {
                    healthLabel = "Concerning";
                    healthStyle = _warningStyle;
                } else {
                    healthLabel = "Healthy";
                    healthStyle = _successStyle;
                }
                GUILayout.Label(healthLabel, healthStyle, GUILayout.Width(70));

                EditorGUILayout.EndHorizontal();
            }

            // List top allocations per plugin
            GUILayout.Space(4);
            foreach (var plugin in sorted) {
                var allocations = _report.Heap.Allocations
                    .Where(a => HeapParser.DetectPluginName(a.ClassName, a.Binary) == plugin.Key)
                    .OrderByDescending(a => a.TotalBytes)
                    .Take(5)
                    .ToList();

                if (allocations.Count == 0) continue;

                GUILayout.Label($"  Top allocations for {plugin.Key}:", EditorStyles.miniLabel);
                foreach (var alloc in allocations) {
                    GUILayout.Label(
                        $"    {alloc.ClassName}: {VmmapParser.FormatSize(alloc.TotalBytes)} ({alloc.Count} allocs)",
                        EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnsafeUtilitySection() {
            GUILayout.Label("UnsafeUtility Allocations", _headerStyle);
            GUILayout.Label(
                "Manual native memory via UnsafeUtility.Malloc. Requires explicit Free(). " +
                "Leaks here grow unbounded without manual cleanup.",
                EditorStyles.wordWrappedMiniLabel);

            var unsafeAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.UnsafeUtility)
                .OrderByDescending(a => a.TotalBytes)
                .ToList();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (unsafeAllocs.Count == 0) {
                GUILayout.Label("No UnsafeUtility allocations detected.", _successStyle);
            } else {
                long totalUnsafe = unsafeAllocs.Sum(a => a.TotalBytes);
                GUILayout.Label(
                    $"{unsafeAllocs.Count} allocation types | Total: {VmmapParser.FormatSize(totalUnsafe)}",
                    _warningStyle);

                GUILayout.Space(4);
                foreach (var alloc in unsafeAllocs.Take(20)) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(alloc.ClassName, GUILayout.MinWidth(150));
                    GUILayout.Label(VmmapParser.FormatSize(alloc.TotalBytes), GUILayout.Width(80));
                    GUILayout.Label($"{alloc.Count} allocs", _mutedStyle, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }

                if (unsafeAllocs.Count > 20) {
                    GUILayout.Label($"  ... and {unsafeAllocs.Count - 20} more", _mutedStyle);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawThreadStackSection() {
            GUILayout.Label("Thread Stack Memory", _headerStyle);
            GUILayout.Label(
                "Each thread reserves 512KB-1MB of virtual memory for its stack. " +
                "Check thread count if virtual memory is unexpectedly high.",
                EditorStyles.wordWrappedMiniLabel);

            // From vmmap summary
            var stackRegions = _report.Vmmap.Summary
                .Where(r => r.RegionType != null &&
                    (r.RegionType.Contains("Stack") || r.RegionType.Contains("STACK")))
                .ToList();

            // From heap
            var stackAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.ThreadStack)
                .ToList();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (stackRegions.Count > 0) {
                GUILayout.Label("vmmap Regions:", EditorStyles.miniLabel);
                foreach (var region in stackRegions) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(region.RegionType, GUILayout.Width(150));
                    GUILayout.Label($"Virtual: {VmmapParser.FormatSize(region.VirtualSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Resident: {VmmapParser.FormatSize(region.ResidentSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Count: {region.RegionCount}", GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (stackAllocs.Count > 0) {
                GUILayout.Label("Heap Allocations:", EditorStyles.miniLabel);
                long totalStack = stackAllocs.Sum(a => a.TotalBytes);
                GUILayout.Label($"  Total: {VmmapParser.FormatSize(totalStack)}", _mutedStyle);
            }

            if (stackRegions.Count == 0 && stackAllocs.Count == 0) {
                GUILayout.Label("No thread stack data found.", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGraphicsMemorySection() {
            GUILayout.Label("Graphics / GPU Memory", _headerStyle);
            GUILayout.Label(
                "GPU memory = Metal textures, render targets, buffers. " +
                "Reduce via texture compression, resolution scaling, render target pooling.",
                EditorStyles.wordWrappedMiniLabel);

            // From vmmap
            var gpuRegions = _report.Vmmap.Summary
                .Where(r => r.RegionType != null &&
                    (r.RegionType.Contains("IOKit") || r.RegionType.Contains("IOSurface") ||
                     r.RegionType.Contains("GPU") || r.RegionType.Contains("CG")))
                .ToList();

            // From heap
            var gpuAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.GraphicsDriver)
                .OrderByDescending(a => a.TotalBytes)
                .ToList();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (gpuRegions.Count > 0) {
                GUILayout.Label("vmmap Regions:", EditorStyles.miniLabel);
                foreach (var region in gpuRegions) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(region.RegionType, GUILayout.Width(150));
                    GUILayout.Label($"Virtual: {VmmapParser.FormatSize(region.VirtualSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Resident: {VmmapParser.FormatSize(region.ResidentSize)}", GUILayout.Width(120));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (gpuAllocs.Count > 0) {
                long totalGpu = gpuAllocs.Sum(a => a.TotalBytes);
                GUILayout.Label($"Heap Allocations: {VmmapParser.FormatSize(totalGpu)}", EditorStyles.miniLabel);

                foreach (var alloc in gpuAllocs.Take(10)) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(alloc.ClassName, GUILayout.MinWidth(150));
                    GUILayout.Label(VmmapParser.FormatSize(alloc.TotalBytes), GUILayout.Width(80));
                    GUILayout.Label($"{alloc.Count} allocs", _mutedStyle, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (gpuRegions.Count == 0 && gpuAllocs.Count == 0) {
                GUILayout.Label("No graphics/GPU memory data found.", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawColorLegend(Color color, string label) {
            var rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            if (Event.current.type == EventType.Repaint) {
                EditorGUI.DrawRect(rect, color);
            }
            GUILayout.Label(label, GUILayout.Width(200));
        }
    }
}
