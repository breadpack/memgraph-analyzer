using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private Vector2 _nativeScrollPos;
        private string _nativeFilter = "";
        private int _nativeCategoryFilter; // 0 = All
        private int _nativeTopN = 100;
        private int _selectedNativeRow = -1;

        private enum NativeSortColumn { Name, Type, Size }
        private NativeSortColumn _nativeSortColumn = NativeSortColumn.Size;
        private bool _nativeSortAscending;

        private static readonly string[] NativeCategoryFilterLabels = {
            "All", "Textures", "Meshes", "Materials", "Shaders", "Animations",
            "Audio", "Fonts", "ScriptableObjects", "GameObjects", "Components", "Other"
        };

        private void DrawNativeTab() {
            AnalyzerGuidance.DrawTabHeader("All native Unity objects (Texture2D, Mesh, Material, etc.) sorted by size. Click a row for details.");
            DrawNativeCategorySummary();
            GUILayout.Space(4);
            DrawNativeCategoryBarChart();
            GUILayout.Space(4);
            DrawNativeObjectList();
        }

        private void DrawNativeCategorySummary() {
            if (_report.NativeTypeSummaries.Count == 0) return;

            // Aggregate by category
            var categorySizes = new Dictionary<NativeTypeCategory, long>();
            var categoryCounts = new Dictionary<NativeTypeCategory, int>();

            foreach (var summary in _report.NativeTypeSummaries) {
                if (!categorySizes.ContainsKey(summary.Category))
                    categorySizes[summary.Category] = 0;
                categorySizes[summary.Category] += summary.TotalSize;

                if (!categoryCounts.ContainsKey(summary.Category))
                    categoryCounts[summary.Category] = 0;
                categoryCounts[summary.Category] += summary.ObjectCount;
            }

            EditorGUILayout.BeginHorizontal();
            foreach (var kv in categorySizes.OrderByDescending(kv => kv.Value).Take(6)) {
                var color = GetNativeCategoryColor(kv.Key);
                var rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, color);

                int count = categoryCounts.TryGetValue(kv.Key, out var c) ? c : 0;
                GUILayout.Label(
                    $"{SnapshotLoader.GetNativeCategoryDisplayName(kv.Key)} {VmmapParser.FormatSize(kv.Value)} ({count})",
                    EditorStyles.miniLabel, GUILayout.Width(180));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNativeCategoryBarChart() {
            if (_report.NativeTypeSummaries.Count == 0) return;

            var categorySizes = new Dictionary<NativeTypeCategory, long>();
            foreach (var summary in _report.NativeTypeSummaries) {
                if (!categorySizes.ContainsKey(summary.Category))
                    categorySizes[summary.Category] = 0;
                categorySizes[summary.Category] += summary.TotalSize;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Native Memory by Category", EditorStyles.boldLabel);

            var sorted = categorySizes.OrderByDescending(kv => kv.Value).ToList();
            long maxSize = sorted.Count > 0 ? sorted[0].Value : 1;

            foreach (var kv in sorted) {
                EditorGUILayout.BeginHorizontal();

                var swatchRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatchRect, GetNativeCategoryColor(kv.Key));

                GUILayout.Label(SnapshotLoader.GetNativeCategoryDisplayName(kv.Key), GUILayout.Width(120));

                var barRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.MinWidth(100));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = maxSize > 0 ? (float)kv.Value / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, GetNativeCategoryColor(kv.Key));
                }

                GUILayout.Label(VmmapParser.FormatSize(kv.Value), GUILayout.Width(80));
                float pct = _report.Summary.TotalNativeSize > 0
                    ? (float)kv.Value / _report.Summary.TotalNativeSize * 100f : 0;
                GUILayout.Label($"{pct:F1}%", _mutedStyle, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawNativeObjectList() {
            // Filters
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _nativeFilter = EditorGUILayout.TextField(_nativeFilter, GUILayout.Width(150));
            GUILayout.Label("Category:", GUILayout.Width(60));
            _nativeCategoryFilter = EditorGUILayout.Popup(_nativeCategoryFilter,
                NativeCategoryFilterLabels, GUILayout.Width(120));
            GUILayout.Label("Top N:", GUILayout.Width(45));
            _nativeTopN = EditorGUILayout.IntSlider(_nativeTopN, 10, 1000);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawNativeSortButton("Name", NativeSortColumn.Name, 0, true);
            DrawNativeSortButton("Type", NativeSortColumn.Type, 120);
            DrawNativeSortButton("Size", NativeSortColumn.Size, 90);
            EditorGUILayout.EndHorizontal();

            // Data
            var rows = GetFilteredNativeObjects();
            _nativeScrollPos = EditorGUILayout.BeginScrollView(_nativeScrollPos);

            for (int i = 0; i < rows.Count; i++) {
                var obj = rows[i];
                bool isSelected = _selectedNativeRow == i;

                var rect = EditorGUILayout.BeginHorizontal();

                if (isSelected && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, SelectionColor);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                    _selectedNativeRow = isSelected ? -1 : i;
                    Event.current.Use();
                    Repaint();
                }

                GUILayout.Label(obj.Name, GUILayout.MinWidth(100));

                var catColor = GetNativeCategoryColor(obj.Category);
                var catStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = catColor } };
                GUILayout.Label(obj.NativeTypeName, catStyle, GUILayout.Width(120));

                GUILayout.Label(VmmapParser.FormatSize(obj.Size), GUILayout.Width(90));

                EditorGUILayout.EndHorizontal();

                if (isSelected) {
                    DrawNativeDetail(obj);
                }
            }

            EditorGUILayout.EndScrollView();

            // Footer
            GUILayout.Label(
                $"{rows.Count} objects shown (of {_report.NativeObjects.Count} total) | " +
                $"Total: {VmmapParser.FormatSize(_report.Summary.TotalNativeSize)}",
                _mutedStyle);
        }

        private void DrawNativeDetail(NativeObjectInfo obj) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($">> {obj.NativeTypeName}: {obj.Name}", _headerStyle);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Instance ID:", GUILayout.Width(100));
            GUILayout.Label(obj.InstanceId.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Size:", GUILayout.Width(100));
            GUILayout.Label(VmmapParser.FormatSize(obj.Size), EditorStyles.boldLabel);
            GUILayout.Label($"({obj.Size:N0} bytes)", _mutedStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Category:", GUILayout.Width(100));
            GUILayout.Label(SnapshotLoader.GetNativeCategoryDisplayName(obj.Category));
            EditorGUILayout.EndHorizontal();

            if (obj.GcHandleIndex >= 0) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("GC Handle:", GUILayout.Width(100));
                GUILayout.Label($"Index {obj.GcHandleIndex}", _successStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Managed Owner display
            DrawNativeManagedOwner(obj);

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private List<NativeObjectInfo> GetFilteredNativeObjects() {
            var rows = _report.NativeObjects.AsEnumerable();

            if (!string.IsNullOrEmpty(_nativeFilter)) {
                rows = rows.Where(o =>
                    (o.Name != null && o.Name.IndexOf(_nativeFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (o.NativeTypeName != null && o.NativeTypeName.IndexOf(_nativeFilter, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_nativeCategoryFilter > 0) {
                var target = (NativeTypeCategory)(_nativeCategoryFilter - 1);
                rows = rows.Where(o => o.Category == target);
            }

            rows = _nativeSortColumn switch {
                NativeSortColumn.Name => _nativeSortAscending
                    ? rows.OrderBy(o => o.Name) : rows.OrderByDescending(o => o.Name),
                NativeSortColumn.Type => _nativeSortAscending
                    ? rows.OrderBy(o => o.NativeTypeName) : rows.OrderByDescending(o => o.NativeTypeName),
                NativeSortColumn.Size => _nativeSortAscending
                    ? rows.OrderBy(o => o.Size) : rows.OrderByDescending(o => o.Size),
                _ => rows,
            };

            return rows.Take(_nativeTopN).ToList();
        }

        private void DrawNativeSortButton(string label, NativeSortColumn column, float width,
            bool expand = false) {
            var displayLabel = label;
            if (_nativeSortColumn == column)
                displayLabel += _nativeSortAscending ? " ^" : " v";

            bool clicked;
            if (expand)
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            else
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.Width(width));

            if (clicked) {
                if (_nativeSortColumn == column)
                    _nativeSortAscending = !_nativeSortAscending;
                else {
                    _nativeSortColumn = column;
                    _nativeSortAscending = false;
                }
            }
        }

        private void DrawNativeManagedOwner(NativeObjectInfo obj) {
            if (_report.LinkResult == null) return;
            int nativeIdx = obj.NativeObjectListIndex;

            if (!_report.LinkResult.NativeToManaged.TryGetValue(nativeIdx, out int managedIdx)) return;
            if (_report.CrawlerResult == null || managedIdx < 0 || managedIdx >= _report.CrawlerResult.Objects.Count) return;

            var managedObj = _report.CrawlerResult.Objects[managedIdx];
            string typeName = "Unknown";
            if (managedObj.TypeIndex >= 0 && managedObj.TypeIndex < _report.Types.Length)
                typeName = _report.Types[managedObj.TypeIndex].Name;

            GUILayout.Space(4);
            GUILayout.Label("Managed Owner", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Type:", GUILayout.Width(100));
            GUILayout.Label(typeName, _successStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Managed Size:", GUILayout.Width(100));
            GUILayout.Label(VmmapParser.FormatSize(managedObj.Size));
            EditorGUILayout.EndHorizontal();

            if (managedObj.IsGcRoot) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("GC Root:", GUILayout.Width(100));
                GUILayout.Label("Yes", _warningStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Show reference chain if available
            DrawOwnerReferenceChain(managedIdx);
        }

        private void DrawOwnerReferenceChain(int managedIdx) {
            if (_report.CrawlerResult == null) return;

            // Build incoming reference chain up to GC root (max depth 10)
            var chain = new System.Collections.Generic.List<string>();
            var visited = new System.Collections.Generic.HashSet<int>();
            int current = managedIdx;

            for (int depth = 0; depth < 10; depth++) {
                if (visited.Contains(current)) break;
                visited.Add(current);

                if (current < 0 || current >= _report.CrawlerResult.Objects.Count) break;
                var obj = _report.CrawlerResult.Objects[current];

                string typeName = "?";
                if (obj.TypeIndex >= 0 && obj.TypeIndex < _report.Types.Length)
                    typeName = _report.Types[obj.TypeIndex].Name;

                if (obj.IsGcRoot) {
                    chain.Add($"[Root] {typeName}");
                    break;
                }

                chain.Add(typeName);

                // Find first incoming reference
                bool found = false;
                foreach (var edge in _report.CrawlerResult.References) {
                    if (edge.ToObjectIndex == current) {
                        chain[chain.Count - 1] = $"{typeName} (via .{edge.FieldName})";
                        current = edge.FromObjectIndex;
                        found = true;
                        break;
                    }
                }
                if (!found) break;
            }

            if (chain.Count <= 1) return;

            GUILayout.Space(2);
            GUILayout.Label("Owner Chain:", _mutedStyle);
            chain.Reverse();
            GUILayout.Label("  " + string.Join(" -> ", chain), _mutedStyle);
        }
    }
}
