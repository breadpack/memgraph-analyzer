using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private int _allocTabViewMode; // 0=Regions, 1=RootReferences, 2=Labels
        private readonly Dictionary<int, bool> _allocRegionFoldouts = new();
        private Vector2 _allocRootRefScrollPos;
        private int _allocSelectedAllocation = -1;
        private string _allocRootRefFilter = "";
        private int _allocRootRefTopN = 50;

        private static readonly string[] AllocViewModeLabels = {
            "Region Hierarchy", "Root References", "Labels"
        };

        private static readonly Color RegionBarColor = new(0.4f, 0.7f, 0.9f, 0.7f);
        private static readonly Color RootRefBarColor = new(0.5f, 0.8f, 0.5f, 0.7f);
        private static readonly Color LabelBarColor = new(0.8f, 0.6f, 0.4f, 0.7f);

        private void DrawNativeAllocationsTab() {
            AnalyzerGuidance.DrawTabHeader(
                "Native memory allocation regions, subsystem breakdown, and call stacks. " +
                "Shows how memory is distributed across allocator regions and which subsystems own allocations.");

            if (_report.NativeAllocations.Count == 0) {
                EditorGUILayout.HelpBox(
                    "No native allocation data available in this snapshot. " +
                    "This data may not be present in older snapshot formats.",
                    MessageType.Info);
                return;
            }

            DrawAllocSummaryBar();
            GUILayout.Space(4);

            _allocTabViewMode = GUILayout.Toolbar(_allocTabViewMode, AllocViewModeLabels);
            GUILayout.Space(4);

            switch (_allocTabViewMode) {
                case 0: DrawRegionHierarchyView(); break;
                case 1: DrawRootReferenceView(); break;
                case 2: DrawLabelView(); break;
            }

            GUILayout.Space(4);
            DrawAllocationDetail();
        }

        #region Summary Bar

        private void DrawAllocSummaryBar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            GUILayout.Label($"Allocations: {_report.NativeAllocations.Count:N0}", EditorStyles.boldLabel);
            GUILayout.Label($"Total: {VmmapParser.FormatSize(_report.Summary.TotalNativeAllocationSize)}",
                GUILayout.Width(120));
            GUILayout.Label($"Overhead: {VmmapParser.FormatSize(_report.Summary.TotalNativeOverheadSize)}",
                _warningStyle, GUILayout.Width(130));
            GUILayout.Label($"Padding: {VmmapParser.FormatSize(_report.Summary.TotalNativePaddingSize)}",
                _mutedStyle, GUILayout.Width(120));

            if (_report.Summary.TotalNativeAllocationSize > 0) {
                float overheadPct = (float)_report.Summary.TotalNativeOverheadSize /
                                    _report.Summary.TotalNativeAllocationSize * 100f;
                GUILayout.Label($"Overhead: {overheadPct:F1}%", _mutedStyle, GUILayout.Width(100));
            }

            GUILayout.Label($"Regions: {_report.NativeMemoryRegions.Count}", _mutedStyle, GUILayout.Width(80));
            GUILayout.Label($"Root Refs: {_report.NativeRootReferences.Count}", _mutedStyle, GUILayout.Width(100));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Region Hierarchy View

        private void DrawRegionHierarchyView() {
            var analysis = _report.NativeAllocationAnalysis;
            if (analysis == null || analysis.RegionTree.Count == 0) {
                EditorGUILayout.HelpBox("No region hierarchy data available.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Allocator Region Hierarchy", _headerStyle);

            long maxSize = analysis.RegionTree.Count > 0 ? analysis.RegionTree[0].TotalSize : 1;

            foreach (var root in analysis.RegionTree) {
                DrawRegionNode(root, 0, maxSize);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRegionNode(RegionTreeNode node, int depth, long maxSize) {
            if (node.TotalSize <= 0 && node.Children.Count == 0) return;

            bool hasChildren = node.Children.Count > 0;
            int key = node.RegionIndex;

            if (!_allocRegionFoldouts.TryGetValue(key, out bool expanded))
                expanded = depth < 1;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 16);

            if (hasChildren) {
                bool newExpanded = EditorGUILayout.Foldout(expanded, "", true);
                if (newExpanded != expanded)
                    _allocRegionFoldouts[key] = newExpanded;
                expanded = newExpanded;
            } else {
                GUILayout.Space(18);
            }

            GUILayout.Label(node.Name, GUILayout.MinWidth(150));

            // Size bar
            var barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.MinWidth(80));
            if (Event.current.type == EventType.Repaint && maxSize > 0) {
                EditorGUI.DrawRect(barRect, BarBgColor);
                float ratio = (float)node.TotalSize / maxSize;
                var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                EditorGUI.DrawRect(fillRect, RegionBarColor);
            }

            GUILayout.Label(VmmapParser.FormatSize(node.TotalSize), GUILayout.Width(80));
            GUILayout.Label($"{node.AllocationCount:N0} allocs", _mutedStyle, GUILayout.Width(80));

            if (node.OverheadSize > 0) {
                GUILayout.Label($"OH: {VmmapParser.FormatSize(node.OverheadSize)}", _warningStyle, GUILayout.Width(90));
            }

            EditorGUILayout.EndHorizontal();

            if (hasChildren && expanded) {
                foreach (var child in node.Children) {
                    DrawRegionNode(child, depth + 1, maxSize);
                }
            }
        }

        #endregion

        #region Root Reference View

        private void DrawRootReferenceView() {
            var analysis = _report.NativeAllocationAnalysis;
            if (analysis == null || analysis.RootReferenceBreakdown.Count == 0) {
                EditorGUILayout.HelpBox("No root reference data available.", MessageType.Info);
                return;
            }

            // Filter bar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _allocRootRefFilter = EditorGUILayout.TextField(_allocRootRefFilter, GUILayout.Width(200));
            GUILayout.Label("Top N:", GUILayout.Width(45));
            _allocRootRefTopN = EditorGUILayout.IntSlider(_allocRootRefTopN, 10, 500);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Group by AreaName
            var filtered = analysis.RootReferenceBreakdown.AsEnumerable();
            if (!string.IsNullOrEmpty(_allocRootRefFilter)) {
                filtered = filtered.Where(g =>
                    g.AreaName.IndexOf(_allocRootRefFilter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    g.ObjectName.IndexOf(_allocRootRefFilter, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var groups = filtered.Take(_allocRootRefTopN).ToList();

            // Area summary
            var areaSizes = new Dictionary<string, long>();
            foreach (var g in analysis.RootReferenceBreakdown) {
                if (!areaSizes.TryGetValue(g.AreaName, out long size))
                    size = 0;
                areaSizes[g.AreaName] = size + g.TotalSize;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Subsystem Areas (Top 10)", _headerStyle);

            long areaMax = areaSizes.Count > 0 ? areaSizes.Values.Max() : 1;
            foreach (var kv in areaSizes.OrderByDescending(kv => kv.Value).Take(10)) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key, GUILayout.Width(200));

                var barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.MinWidth(80));
                if (Event.current.type == EventType.Repaint && areaMax > 0) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = (float)kv.Value / areaMax;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, RootRefBarColor);
                }

                GUILayout.Label(VmmapParser.FormatSize(kv.Value), GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // Detailed list
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Root Reference Details", _headerStyle);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Area", EditorStyles.toolbarButton, GUILayout.Width(150));
            GUILayout.Label("Object", EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            GUILayout.Label("Size", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("Allocs", EditorStyles.toolbarButton, GUILayout.Width(60));
            GUILayout.Label("Objects", EditorStyles.toolbarButton, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            _allocRootRefScrollPos = EditorGUILayout.BeginScrollView(_allocRootRefScrollPos,
                GUILayout.MaxHeight(400));

            for (int i = 0; i < groups.Count; i++) {
                var g = groups[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(g.AreaName, GUILayout.Width(150));
                GUILayout.Label(g.ObjectName, GUILayout.MinWidth(100));
                GUILayout.Label(VmmapParser.FormatSize(g.TotalSize), GUILayout.Width(80));
                GUILayout.Label(g.AllocationCount.ToString("N0"), _mutedStyle, GUILayout.Width(60));
                GUILayout.Label(g.LinkedNativeObjects.Count.ToString("N0"), _mutedStyle, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            GUILayout.Label(
                $"{groups.Count} entries shown (of {analysis.RootReferenceBreakdown.Count} total)",
                _mutedStyle);
        }

        #endregion

        #region Label View

        private void DrawLabelView() {
            var analysis = _report.NativeAllocationAnalysis;
            if (analysis == null || analysis.LabelBreakdown.Count == 0) {
                EditorGUILayout.HelpBox(
                    "No label data available. Labels require allocation site data in the snapshot.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Memory Labels", _headerStyle);

            long maxSize = analysis.LabelBreakdown.Count > 0 ? analysis.LabelBreakdown[0].TotalSize : 1;

            foreach (var label in analysis.LabelBreakdown) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(label.Name, GUILayout.Width(200));

                var barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.MinWidth(80));
                if (Event.current.type == EventType.Repaint && maxSize > 0) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = (float)label.TotalSize / maxSize;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, LabelBarColor);
                }

                GUILayout.Label(VmmapParser.FormatSize(label.TotalSize), GUILayout.Width(80));
                GUILayout.Label($"{label.AllocationCount:N0} allocs", _mutedStyle, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Allocation Detail

        private void DrawAllocationDetail() {
            if (_allocSelectedAllocation < 0 || _allocSelectedAllocation >= _report.NativeAllocations.Count)
                return;

            var alloc = _report.NativeAllocations[_allocSelectedAllocation];
            var analysis = _report.NativeAllocationAnalysis;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"Allocation #{_allocSelectedAllocation}", _headerStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Address:", GUILayout.Width(100));
            GUILayout.Label($"0x{alloc.Address:X}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(100));
            GUILayout.Label(VmmapParser.FormatSize((long)alloc.Size), EditorStyles.boldLabel);
            GUILayout.Label($"({alloc.Size:N0} bytes)", _mutedStyle);
            EditorGUILayout.EndHorizontal();

            if (alloc.OverheadSize > 0) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Overhead:", GUILayout.Width(100));
                GUILayout.Label(VmmapParser.FormatSize((long)alloc.OverheadSize), _warningStyle);
                EditorGUILayout.EndHorizontal();
            }

            if (alloc.PaddingSize > 0) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Padding:", GUILayout.Width(100));
                GUILayout.Label(VmmapParser.FormatSize((long)alloc.PaddingSize), _mutedStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Region
            if (alloc.MemoryRegionIndex >= 0 && alloc.MemoryRegionIndex < _report.NativeMemoryRegions.Count) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Region:", GUILayout.Width(100));
                GUILayout.Label(_report.NativeMemoryRegions[alloc.MemoryRegionIndex].Name);
                EditorGUILayout.EndHorizontal();
            }

            // Linked native object
            if (analysis != null && analysis.AllocationToObjectMap.TryGetValue(_allocSelectedAllocation, out int objIdx)) {
                if (objIdx >= 0 && objIdx < _report.NativeObjects.Count) {
                    var obj = _report.NativeObjects[objIdx];
                    GUILayout.Space(2);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Linked Object:", GUILayout.Width(100));
                    GUILayout.Label($"{obj.NativeTypeName}: {obj.Name}", _successStyle);
                    EditorGUILayout.EndHorizontal();
                }
            }

            // Callstack
            if (analysis != null && alloc.AllocationSiteId >= 0 &&
                analysis.ResolvedCallstacks.TryGetValue(alloc.AllocationSiteId, out string[] frames)) {
                GUILayout.Space(4);
                GUILayout.Label("Call Stack:", EditorStyles.boldLabel);
                foreach (string frame in frames) {
                    GUILayout.Label($"  {frame}", _mutedStyle);
                }
            }

            EditorGUILayout.EndVertical();
        }

        #endregion
    }
}
