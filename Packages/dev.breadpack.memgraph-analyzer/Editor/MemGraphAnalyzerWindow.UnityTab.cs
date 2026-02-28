using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _unityScrollPos;

        // === Unity tab caches ===
        private bool _unityTabCacheBuilt;
        private List<KeyValuePair<string, long>> _cachedPluginsSorted;
        private Dictionary<string, List<HeapAllocation>> _cachedPluginAllocations;
        private List<HeapAllocation> _cachedUnsafeAllocs;
        private long _cachedUnsafeTotal;
        private List<VmmapSummaryRow> _cachedStackRegions;
        private List<HeapAllocation> _cachedStackAllocs;
        private long _cachedStackTotal;
        private List<VmmapSummaryRow> _cachedGpuRegions;
        private List<HeapAllocation> _cachedGpuAllocs;
        private long _cachedGpuTotal;

        private void RebuildUnityTabCaches() {
            if (_unityTabCacheBuilt) return;
            _unityTabCacheBuilt = true;

            // Plugin breakdown: single O(N) scan to build plugin->allocations map
            var plugins = _report.Summary.PluginBreakdowns;
            _cachedPluginsSorted = plugins.OrderByDescending(kv => kv.Value).ToList();

            _cachedPluginAllocations = new Dictionary<string, List<HeapAllocation>>();
            foreach (var plugin in _cachedPluginsSorted)
                _cachedPluginAllocations[plugin.Key] = new List<HeapAllocation>();

            // Single pass over all heap allocations for plugin mapping
            foreach (var alloc in _report.Heap.Allocations) {
                var pluginName = HeapParser.DetectPluginName(alloc.ClassName, alloc.Binary);
                if (pluginName != null && _cachedPluginAllocations.TryGetValue(pluginName, out var list))
                    list.Add(alloc);
            }

            // Sort each plugin's allocations and keep top 5
            foreach (var kv in _cachedPluginAllocations) {
                kv.Value.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
                if (kv.Value.Count > 5)
                    kv.Value.RemoveRange(5, kv.Value.Count - 5);
            }

            // Unsafe allocations
            _cachedUnsafeAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.UnsafeUtility)
                .OrderByDescending(a => a.TotalBytes)
                .ToList();
            _cachedUnsafeTotal = 0;
            foreach (var a in _cachedUnsafeAllocs)
                _cachedUnsafeTotal += a.TotalBytes;

            // Stack regions/allocs
            _cachedStackRegions = _report.Vmmap.Summary
                .Where(r => r.RegionType != null &&
                    (r.RegionType.Contains("Stack") || r.RegionType.Contains("STACK")))
                .ToList();
            _cachedStackAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.ThreadStack)
                .ToList();
            _cachedStackTotal = 0;
            foreach (var a in _cachedStackAllocs)
                _cachedStackTotal += a.TotalBytes;

            // GPU regions/allocs
            _cachedGpuRegions = _report.Vmmap.Summary
                .Where(r => r.RegionType != null &&
                    (r.RegionType.Contains("IOKit") || r.RegionType.Contains("IOSurface") ||
                     r.RegionType.Contains("GPU") || r.RegionType.Contains("CG")))
                .ToList();
            _cachedGpuAllocs = _report.Heap.Allocations
                .Where(a => a.Owner == MemoryOwner.GraphicsDriver)
                .OrderByDescending(a => a.TotalBytes)
                .ToList();
            _cachedGpuTotal = 0;
            foreach (var a in _cachedGpuAllocs)
                _cachedGpuTotal += a.TotalBytes;
        }

        private void DrawUnityTab() {
            AnalyzerGuidance.DrawTabHeader("Tracked vs untracked memory, native plugin breakdown, UnsafeUtility, thread stacks, and GPU memory.");
            RebuildUnityTabCaches();

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
            if (_cachedPluginsSorted == null || _cachedPluginsSorted.Count == 0) {
                GUILayout.Label("Native Plugins", _headerStyle);
                GUILayout.Label("No native plugin allocations detected in heap.", _mutedStyle);
                return;
            }

            GUILayout.Label("Native Plugin Memory", _headerStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            long maxSize = _cachedPluginsSorted[0].Value;

            foreach (var kv in _cachedPluginsSorted) {
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

            // List top allocations per plugin (from cache)
            GUILayout.Space(4);
            foreach (var plugin in _cachedPluginsSorted) {
                if (!_cachedPluginAllocations.TryGetValue(plugin.Key, out var allocations) || allocations.Count == 0)
                    continue;

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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_cachedUnsafeAllocs.Count == 0) {
                GUILayout.Label("No UnsafeUtility allocations detected.", _successStyle);
            } else {
                GUILayout.Label(
                    $"{_cachedUnsafeAllocs.Count} allocation types | Total: {VmmapParser.FormatSize(_cachedUnsafeTotal)}",
                    _warningStyle);

                GUILayout.Space(4);
                int displayed = Mathf.Min(20, _cachedUnsafeAllocs.Count);
                for (int i = 0; i < displayed; i++) {
                    var alloc = _cachedUnsafeAllocs[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(alloc.ClassName, GUILayout.MinWidth(150));
                    GUILayout.Label(VmmapParser.FormatSize(alloc.TotalBytes), GUILayout.Width(80));
                    GUILayout.Label($"{alloc.Count} allocs", _mutedStyle, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }

                if (_cachedUnsafeAllocs.Count > 20) {
                    GUILayout.Label($"  ... and {_cachedUnsafeAllocs.Count - 20} more", _mutedStyle);
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_cachedStackRegions.Count > 0) {
                GUILayout.Label("vmmap Regions:", EditorStyles.miniLabel);
                foreach (var region in _cachedStackRegions) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(region.RegionType, GUILayout.Width(150));
                    GUILayout.Label($"Virtual: {VmmapParser.FormatSize(region.VirtualSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Resident: {VmmapParser.FormatSize(region.ResidentSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Count: {region.RegionCount}", GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_cachedStackAllocs.Count > 0) {
                GUILayout.Label("Heap Allocations:", EditorStyles.miniLabel);
                GUILayout.Label($"  Total: {VmmapParser.FormatSize(_cachedStackTotal)}", _mutedStyle);
            }

            if (_cachedStackRegions.Count == 0 && _cachedStackAllocs.Count == 0) {
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_cachedGpuRegions.Count > 0) {
                GUILayout.Label("vmmap Regions:", EditorStyles.miniLabel);
                foreach (var region in _cachedGpuRegions) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(region.RegionType, GUILayout.Width(150));
                    GUILayout.Label($"Virtual: {VmmapParser.FormatSize(region.VirtualSize)}", GUILayout.Width(120));
                    GUILayout.Label($"Resident: {VmmapParser.FormatSize(region.ResidentSize)}", GUILayout.Width(120));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_cachedGpuAllocs.Count > 0) {
                GUILayout.Label($"Heap Allocations: {VmmapParser.FormatSize(_cachedGpuTotal)}", EditorStyles.miniLabel);

                int displayed = Mathf.Min(10, _cachedGpuAllocs.Count);
                for (int i = 0; i < displayed; i++) {
                    var alloc = _cachedGpuAllocs[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(alloc.ClassName, GUILayout.MinWidth(150));
                    GUILayout.Label(VmmapParser.FormatSize(alloc.TotalBytes), GUILayout.Width(80));
                    GUILayout.Label($"{alloc.Count} allocs", _mutedStyle, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (_cachedGpuRegions.Count == 0 && _cachedGpuAllocs.Count == 0) {
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
