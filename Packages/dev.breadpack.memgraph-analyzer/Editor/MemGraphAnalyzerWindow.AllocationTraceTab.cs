using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private AllocationCategory _traceFilterCategory = (AllocationCategory)(-1); // -1 = All
        private Controllability _traceFilterControl = (Controllability)(-1);
        private AssetType _traceFilterAssetType = (AssetType)(-1);
        private int _traceSortMode; // 0=Size, 1=Count, 2=Category
        private readonly HashSet<int> _expandedTraceRows = new();
        private Vector2 _traceScrollPos;

        private static readonly Color BarAssetColor = new(0.4f, 0.75f, 0.95f, 0.8f);
        private static readonly Color BarGameLogicColor = new(0.3f, 0.85f, 0.4f, 0.8f);
        private static readonly Color BarEngineColor = new(0.95f, 0.7f, 0.3f, 0.8f);
        private static readonly Color BarSystemFwColor = new(0.6f, 0.6f, 0.6f, 0.7f);
        private static readonly Color BarGCHeapColor = new(0.85f, 0.4f, 0.85f, 0.8f);
        private static readonly Color TraceSeverityInfoColor = new(0.6f, 0.8f, 1f, 1f);

        // === Trace row cache ===
        private List<TracedAllocation> _cachedTraceRows;
        private (AllocationCategory cat, Controllability ctrl, AssetType asset, int sort) _cachedTraceKey;
        private string[] _traceRowSizeStr;
        private string[] _traceRowCallStr;
        private string[] _traceRowLabelStr;

        private void DrawAllocationTraceTab() {
            AnalyzerGuidance.DrawTabHeader(
                "Allocation trace analysis: classifies memory allocations by asset type, game logic, " +
                "and controllability using malloc_history callstack data.");

            var trace = _report?.AllocationTrace;
            if (trace == null || trace.Allocations.Count == 0) {
                EditorGUILayout.HelpBox(
                    "No allocation trace data available.\n" +
                    "This requires malloc_history -allBySize output. " +
                    "Re-run analysis or check that the .memgraph file contains allocation records.",
                    MessageType.Info);
                return;
            }

            DrawCategoryBar(trace);
            GUILayout.Space(4);
            DrawTraceFilters(trace);
            GUILayout.Space(4);
            DrawTraceList(trace);
        }

        private void DrawCategoryBar(AllocationTraceResult trace) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Category Breakdown", _headerStyle);

            var barRect = EditorGUILayout.GetControlRect(false, 28);
            if (Event.current.type == EventType.Repaint && trace.TotalAnalyzedBytes > 0) {
                EditorGUI.DrawRect(barRect, BarBgColor);

                float x = barRect.x;
                foreach (var cat in trace.CategoryBreakdown) {
                    float width = barRect.width * cat.Percentage / 100f;
                    if (width < 1f) continue;
                    var segRect = new Rect(x, barRect.y, width, barRect.height);
                    EditorGUI.DrawRect(segRect, GetCategoryColor(cat.Category));
                    x += width;
                }
            }

            // Category legend with sizes
            EditorGUILayout.BeginHorizontal();
            foreach (var cat in trace.CategoryBreakdown) {
                DrawCategoryLegendItem(cat);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawCategoryLegendItem(CategorySummary cat) {
            var color = GetCategoryColor(cat.Category);
            var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            rect.y += 2;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
            GUILayout.Label(
                $"{cat.Category}: {VmmapParser.FormatSize(cat.TotalBytes)} ({cat.Percentage:F1}%)",
                EditorStyles.miniLabel);
            GUILayout.Space(8);
        }

        private void DrawTraceFilters(AllocationTraceResult trace) {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Category filter
            GUILayout.Label("Category:", GUILayout.Width(58));
            var catOptions = new[] { "All", "Asset", "GameLogic", "EngineInternal", "SystemFramework", "GCHeap" };
            int catIdx = (int)_traceFilterCategory + 1; // -1 -> 0 (All)
            int newCatIdx = EditorGUILayout.Popup(catIdx, catOptions, EditorStyles.toolbarPopup, GUILayout.Width(100));
            _traceFilterCategory = (AllocationCategory)(newCatIdx - 1);

            GUILayout.Space(8);

            // Controllability filter
            GUILayout.Label("Control:", GUILayout.Width(48));
            var ctrlOptions = new[] { "All", "UserControllable", "Partially", "EngineOwned", "SystemOwned" };
            int ctrlIdx = (int)_traceFilterControl + 1;
            int newCtrlIdx = EditorGUILayout.Popup(ctrlIdx, ctrlOptions, EditorStyles.toolbarPopup, GUILayout.Width(110));
            _traceFilterControl = (Controllability)(newCtrlIdx - 1);

            GUILayout.Space(8);

            // AssetType filter
            GUILayout.Label("Asset:", GUILayout.Width(38));
            var assetOptions = new[] {
                "All", "None", "Shader", "Texture", "Mesh", "Audio", "Animation",
                "Font", "ScriptData", "WebView", "AssetBundle", "Prefab", "Other"
            };
            int assetIdx = (int)_traceFilterAssetType + 1;
            int newAssetIdx = EditorGUILayout.Popup(assetIdx, assetOptions, EditorStyles.toolbarPopup, GUILayout.Width(90));
            _traceFilterAssetType = (AssetType)(newAssetIdx - 1);

            GUILayout.FlexibleSpace();

            // Sort mode
            GUILayout.Label("Sort:", GUILayout.Width(32));
            var sortOptions = new[] { "Size", "Count", "Category" };
            _traceSortMode = EditorGUILayout.Popup(_traceSortMode, sortOptions, EditorStyles.toolbarPopup, GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTraceList(AllocationTraceResult trace) {
            var sorted = GetFilteredTraceRowsCached(trace);

            // Stats (use cached pre-computed sum)
            long filteredBytes = 0;
            foreach (var a in sorted)
                filteredBytes += a.TotalBytes;
            int filteredCount = sorted.Count;
            EditorGUILayout.LabelField(
                $"Showing {filteredCount} allocations ({VmmapParser.FormatSize(filteredBytes)}) " +
                $"of {trace.Allocations.Count} total ({VmmapParser.FormatSize(trace.TotalAnalyzedBytes)})",
                _mutedStyle);

            _traceScrollPos = EditorGUILayout.BeginScrollView(_traceScrollPos);

            for (int i = 0; i < sorted.Count; i++) {
                DrawTraceRow(sorted[i], i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTraceRow(TracedAllocation alloc, int index) {
            bool isExpanded = _expandedTraceRows.Contains(index);
            string severityIcon = GetTraceSeverityIcon(alloc.TotalBytes);
            var severityColor = GetTraceSeverityColor(alloc.TotalBytes);

            // Header row
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Expand toggle
            bool newExpanded = EditorGUILayout.Foldout(isExpanded, "", true, EditorStyles.foldout);
            if (newExpanded != isExpanded) {
                if (newExpanded) _expandedTraceRows.Add(index);
                else _expandedTraceRows.Remove(index);
            }

            // Severity icon
            var prevColor = GUI.contentColor;
            GUI.contentColor = severityColor;
            GUILayout.Label(severityIcon, EditorStyles.boldLabel, GUILayout.Width(24));
            GUI.contentColor = prevColor;

            // Main label - use pre-formatted string
            GUILayout.Label(_traceRowLabelStr[index], EditorStyles.boldLabel, GUILayout.MinWidth(200));

            GUILayout.FlexibleSpace();

            // Size (pre-formatted)
            GUILayout.Label(_traceRowSizeStr[index], GUILayout.Width(70));

            // Call count (pre-formatted)
            GUILayout.Label(_traceRowCallStr[index], _mutedStyle, GUILayout.Width(65));

            EditorGUILayout.EndHorizontal();

            // Category/Controllability tags
            if (!isExpanded) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(32);
                DrawCategoryTag(alloc.Category, alloc.AssetType);
                GUILayout.Space(4);
                DrawControllabilityTag(alloc.Controllability);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            // Expanded detail
            if (isExpanded) {
                DrawTraceDetail(alloc);
            }
        }

        private void DrawTraceDetail(TracedAllocation alloc) {
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Summary
            EditorGUILayout.LabelField("Summary", alloc.Summary ?? "(none)");

            // Category detail
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Category:", GUILayout.Width(80));
            DrawCategoryTag(alloc.Category, alloc.AssetType);
            GUILayout.Space(12);
            GUILayout.Label("Controllability:", GUILayout.Width(90));
            DrawControllabilityTag(alloc.Controllability);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Top functions
            if (!string.IsNullOrEmpty(alloc.TopUserFunction))
                EditorGUILayout.LabelField("User Function", alloc.TopUserFunction);
            if (!string.IsNullOrEmpty(alloc.TopEngineFunction))
                EditorGUILayout.LabelField("Engine Function",
                    CallTreeParser.FormatFunctionName(alloc.TopEngineFunction));

            // Stack frames
            if (alloc.Frames.Count > 0) {
                GUILayout.Space(4);
                GUILayout.Label("Call Stack:", EditorStyles.boldLabel);
                int maxFrames = Mathf.Min(alloc.Frames.Count, 20);
                for (int i = 0; i < maxFrames; i++) {
                    var frame = alloc.Frames[i];
                    bool isUser = CallTreeParser.IsUserCode(frame.FunctionName);
                    var style = isUser ? EditorStyles.boldLabel : _mutedStyle;
                    string funcDisplay = isUser
                        ? CallTreeParser.FormatFunctionName(frame.FunctionName)
                        : frame.FunctionName;
                    EditorGUILayout.LabelField(
                        $"  {i,2}  ({frame.Binary}) {funcDisplay}", style);
                }
                if (alloc.Frames.Count > maxFrames) {
                    EditorGUILayout.LabelField(
                        $"  ... +{alloc.Frames.Count - maxFrames} more frames", _mutedStyle);
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        private void DrawCategoryTag(AllocationCategory category, AssetType assetType) {
            var color = GetCategoryColor(category);
            string text = assetType != AssetType.None
                ? $"{category}/{assetType}"
                : category.ToString();

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = prevBg;
        }

        private void DrawControllabilityTag(Controllability ctrl) {
            var color = GetControllabilityColor(ctrl);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            string label = ctrl switch {
                Controllability.UserControllable => "User Controllable",
                Controllability.PartiallyControllable => "Partially Controllable",
                Controllability.EngineOwned => "Engine Owned",
                Controllability.SystemOwned => "System Owned",
                _ => ctrl.ToString(),
            };
            GUILayout.Label(label, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = prevBg;
        }

        #region Helpers (Cached)

        private List<TracedAllocation> GetFilteredTraceRowsCached(AllocationTraceResult trace) {
            var key = (_traceFilterCategory, _traceFilterControl, _traceFilterAssetType, _traceSortMode);
            if (_cachedTraceRows != null && _cachedTraceKey == key)
                return _cachedTraceRows;

            _cachedTraceKey = key;
            var filtered = GetFilteredAllocations(trace);
            _cachedTraceRows = SortAllocations(filtered);

            // Pre-format display strings
            int count = _cachedTraceRows.Count;
            _traceRowSizeStr = new string[count];
            _traceRowCallStr = new string[count];
            _traceRowLabelStr = new string[count];
            for (int i = 0; i < count; i++) {
                var alloc = _cachedTraceRows[i];
                _traceRowSizeStr[i] = VmmapParser.FormatSize(alloc.TotalBytes);
                _traceRowCallStr[i] = $"{alloc.CallCount} call{(alloc.CallCount != 1 ? "s" : "")}";
                _traceRowLabelStr[i] = !string.IsNullOrEmpty(alloc.TopUserFunction)
                    ? alloc.TopUserFunction
                    : !string.IsNullOrEmpty(alloc.TopEngineFunction)
                        ? CallTreeParser.FormatFunctionName(alloc.TopEngineFunction)
                        : GetFirstMeaningfulFunction(alloc);
            }

            _expandedTraceRows.Clear();
            return _cachedTraceRows;
        }

        private List<TracedAllocation> GetFilteredAllocations(AllocationTraceResult trace) {
            var result = new List<TracedAllocation>();
            foreach (var alloc in trace.Allocations) {
                if ((int)_traceFilterCategory >= 0 && alloc.Category != _traceFilterCategory)
                    continue;
                if ((int)_traceFilterControl >= 0 && alloc.Controllability != _traceFilterControl)
                    continue;
                if ((int)_traceFilterAssetType >= 0 && alloc.AssetType != _traceFilterAssetType)
                    continue;
                result.Add(alloc);
            }
            return result;
        }

        private List<TracedAllocation> SortAllocations(List<TracedAllocation> allocations) {
            var sorted = new List<TracedAllocation>(allocations);
            switch (_traceSortMode) {
                case 0: // Size
                    sorted.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
                    break;
                case 1: // Count
                    sorted.Sort((a, b) => b.CallCount.CompareTo(a.CallCount));
                    break;
                case 2: // Category
                    sorted.Sort((a, b) => {
                        int cmp = a.Category.CompareTo(b.Category);
                        return cmp != 0 ? cmp : b.TotalBytes.CompareTo(a.TotalBytes);
                    });
                    break;
            }
            return sorted;
        }

        private static string GetTraceSeverityIcon(long totalBytes) {
            if (totalBytes >= 50L * 1024 * 1024) return "[!!]";
            if (totalBytes >= 10L * 1024 * 1024) return "[!]";
            return "[i]";
        }

        private static Color GetTraceSeverityColor(long totalBytes) {
            if (totalBytes >= 50L * 1024 * 1024) return HealthCriticalColor;
            if (totalBytes >= 10L * 1024 * 1024) return HealthWarningColor;
            return TraceSeverityInfoColor;
        }

        private static Color GetCategoryColor(AllocationCategory category) {
            return category switch {
                AllocationCategory.Asset => BarAssetColor,
                AllocationCategory.GameLogic => BarGameLogicColor,
                AllocationCategory.EngineInternal => BarEngineColor,
                AllocationCategory.SystemFramework => BarSystemFwColor,
                AllocationCategory.GCHeap => BarGCHeapColor,
                _ => BarUnknownColor,
            };
        }

        private static Color GetControllabilityColor(Controllability ctrl) {
            return ctrl switch {
                Controllability.UserControllable => HealthGoodColor,
                Controllability.PartiallyControllable => HealthWarningColor,
                Controllability.EngineOwned => ControllabilityEngineColor,
                Controllability.SystemOwned => ControllabilitySystemColor,
                _ => Color.white,
            };
        }

        private static string GetFirstMeaningfulFunction(TracedAllocation alloc) {
            if (alloc.Frames == null || alloc.Frames.Count == 0) return "(unknown)";
            // Find first non-system frame
            foreach (var frame in alloc.Frames) {
                var binary = (frame.Binary ?? "").ToUpperInvariant();
                if (binary.Contains("LIBSYSTEM") || binary.Contains("LIBDISPATCH"))
                    continue;
                return CallTreeParser.FormatFunctionName(frame.FunctionName);
            }
            return CallTreeParser.FormatFunctionName(alloc.Frames[0].FunctionName);
        }

        #endregion
    }
}
