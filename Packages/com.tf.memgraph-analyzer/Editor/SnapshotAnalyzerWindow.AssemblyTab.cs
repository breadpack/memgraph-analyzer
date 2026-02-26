using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private Vector2 _assemblyScrollPos;
        private string _assemblyFilter = "";
        private int _assemblyClassFilter; // 0 = All
        private int _selectedAssemblyRow = -1;

        private enum AssemblySortColumn { Name, Size, Types, Instances }
        private AssemblySortColumn _assemblySortColumn = AssemblySortColumn.Size;
        private bool _assemblySortAscending;

        // Track expanded assemblies and namespaces
        private readonly HashSet<string> _expandedAssemblies = new();
        private readonly HashSet<string> _expandedNamespaces = new();

        private static readonly string[] ClassFilterLabels = {
            "All", "User Code", "Unity Runtime", "Unity Editor", ".NET / Mono", "Third Party"
        };

        private void DrawAssemblyTab() {
            AnalyzerGuidance.DrawTabHeader("Browse managed assemblies by classification. Expand to see namespaces and types with size breakdown.");
            DrawAssemblyClassificationSummary();
            GUILayout.Space(4);

            // Filters
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _assemblyFilter = EditorGUILayout.TextField(_assemblyFilter, GUILayout.Width(150));
            GUILayout.Label("Classification:", GUILayout.Width(85));
            _assemblyClassFilter = EditorGUILayout.Popup(_assemblyClassFilter, ClassFilterLabels, GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Expand All", EditorStyles.miniButton, GUILayout.Width(70))) {
                foreach (var asm in _report.Assemblies)
                    _expandedAssemblies.Add(asm.Name);
            }
            if (GUILayout.Button("Collapse All", EditorStyles.miniButton, GUILayout.Width(75))) {
                _expandedAssemblies.Clear();
                _expandedNamespaces.Clear();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawAssemblySortButton("Name", AssemblySortColumn.Name, 0, true);
            DrawAssemblySortButton("Size", AssemblySortColumn.Size, 90);
            DrawAssemblySortButton("Types", AssemblySortColumn.Types, 60);
            DrawAssemblySortButton("Instances", AssemblySortColumn.Instances, 70);
            GUILayout.Label("Class", EditorStyles.toolbarButton, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            // Tree view
            var filtered = GetFilteredAssemblies();
            _assemblyScrollPos = EditorGUILayout.BeginScrollView(_assemblyScrollPos);

            foreach (var asm in filtered) {
                DrawAssemblyRow(asm);
            }

            EditorGUILayout.EndScrollView();

            // Footer
            GUILayout.Label(
                $"{filtered.Count} assemblies shown (of {_report.Assemblies.Count} total)",
                _mutedStyle);
        }

        private void DrawAssemblyClassificationSummary() {
            var sizes = _report.Summary.SizeByClassification;
            if (sizes.Count == 0) return;

            long totalSize = sizes.Values.Sum();
            if (totalSize <= 0) return;

            EditorGUILayout.BeginHorizontal();
            foreach (var kv in sizes.OrderByDescending(kv => kv.Value)) {
                float pct = (float)kv.Value / totalSize * 100f;
                var color = GetClassificationColor(kv.Key);
                var rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, color);
                GUILayout.Label(
                    $"{SnapshotLoader.GetClassificationDisplayName(kv.Key)} {pct:F0}%",
                    EditorStyles.miniLabel, GUILayout.Width(120));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssemblyRow(AssemblyInfo asm) {
            bool isExpanded = _expandedAssemblies.Contains(asm.Name);
            var color = GetClassificationColor(asm.Classification);

            var rect = EditorGUILayout.BeginHorizontal();

            // Foldout + Name
            if (GUILayout.Button(isExpanded ? "v" : ">", EditorStyles.miniLabel, GUILayout.Width(14))) {
                if (isExpanded) _expandedAssemblies.Remove(asm.Name);
                else _expandedAssemblies.Add(asm.Name);
            }

            var nameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = color } };
            GUILayout.Label(asm.Name, nameStyle, GUILayout.MinWidth(100));

            // Size bar
            DrawAssemblySizeBar(asm.TotalSize, 90);

            GUILayout.Label(asm.TypeCount.ToString("N0"), GUILayout.Width(60));
            GUILayout.Label(asm.InstanceCount.ToString("N0"), GUILayout.Width(70));
            GUILayout.Label(SnapshotLoader.GetClassificationDisplayName(asm.Classification),
                _mutedStyle, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();

            // Expanded: show namespaces
            if (isExpanded) {
                foreach (var ns in asm.Namespaces) {
                    DrawNamespaceRow(asm.Name, ns);
                }
            }
        }

        private void DrawNamespaceRow(string assemblyName, NamespaceInfo ns) {
            string nsKey = $"{assemblyName}::{ns.Name}";
            bool isExpanded = _expandedNamespaces.Contains(nsKey);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(24);

            if (GUILayout.Button(isExpanded ? "v" : ">", EditorStyles.miniLabel, GUILayout.Width(14))) {
                if (isExpanded) _expandedNamespaces.Remove(nsKey);
                else _expandedNamespaces.Add(nsKey);
            }

            GUILayout.Label(ns.Name, EditorStyles.label, GUILayout.MinWidth(76));
            DrawAssemblySizeBar(ns.TotalSize, 90);
            GUILayout.Label(ns.TypeCount.ToString("N0"), GUILayout.Width(60));
            GUILayout.Label(ns.InstanceCount.ToString("N0"), GUILayout.Width(70));
            GUILayout.Label("", GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            // Expanded: show types
            if (isExpanded) {
                foreach (var t in ns.Types) {
                    DrawTypeRow(t);
                }
            }
        }

        private void DrawTypeRow(TypeInfo t) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(52);

            GUILayout.Label(t.Name, EditorStyles.miniLabel, GUILayout.MinWidth(62));

            long size = t.TotalInstanceSize > 0 ? t.TotalInstanceSize : t.BaseSize;
            GUILayout.Label(VmmapParser.FormatSize(size), EditorStyles.miniLabel, GUILayout.Width(90));
            GUILayout.Label("", GUILayout.Width(60)); // types column (N/A for individual type)
            GUILayout.Label(t.InstanceCount > 0 ? t.InstanceCount.ToString("N0") : "-",
                EditorStyles.miniLabel, GUILayout.Width(70));

            string flags = "";
            if (t.IsValueType) flags += "[V]";
            if (t.IsArray) flags += "[A]";
            GUILayout.Label(flags, _mutedStyle, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssemblySizeBar(long size, float width) {
            long maxSize = _report.Assemblies.Count > 0 ? _report.Assemblies[0].TotalSize : 1;
            if (maxSize <= 0) maxSize = 1;

            EditorGUILayout.BeginHorizontal(GUILayout.Width(width));
            GUILayout.Label(VmmapParser.FormatSize(size), EditorStyles.miniLabel, GUILayout.Width(width));
            EditorGUILayout.EndHorizontal();
        }

        private List<AssemblyInfo> GetFilteredAssemblies() {
            var rows = _report.Assemblies.AsEnumerable();

            if (!string.IsNullOrEmpty(_assemblyFilter)) {
                rows = rows.Where(a =>
                    a.Name.IndexOf(_assemblyFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.Namespaces.Any(ns =>
                        ns.Name.IndexOf(_assemblyFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ns.Types.Any(t => t.Name.IndexOf(_assemblyFilter, StringComparison.OrdinalIgnoreCase) >= 0)));
            }

            if (_assemblyClassFilter > 0) {
                var target = (AssemblyClassification)(_assemblyClassFilter - 1);
                rows = rows.Where(a => a.Classification == target);
            }

            rows = _assemblySortColumn switch {
                AssemblySortColumn.Name => _assemblySortAscending
                    ? rows.OrderBy(a => a.Name) : rows.OrderByDescending(a => a.Name),
                AssemblySortColumn.Size => _assemblySortAscending
                    ? rows.OrderBy(a => a.TotalSize) : rows.OrderByDescending(a => a.TotalSize),
                AssemblySortColumn.Types => _assemblySortAscending
                    ? rows.OrderBy(a => a.TypeCount) : rows.OrderByDescending(a => a.TypeCount),
                AssemblySortColumn.Instances => _assemblySortAscending
                    ? rows.OrderBy(a => a.InstanceCount) : rows.OrderByDescending(a => a.InstanceCount),
                _ => rows,
            };

            return rows.ToList();
        }

        private void DrawAssemblySortButton(string label, AssemblySortColumn column, float width,
            bool expand = false) {
            var displayLabel = label;
            if (_assemblySortColumn == column)
                displayLabel += _assemblySortAscending ? " ^" : " v";

            bool clicked;
            if (expand)
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            else
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.Width(width));

            if (clicked) {
                if (_assemblySortColumn == column)
                    _assemblySortAscending = !_assemblySortAscending;
                else {
                    _assemblySortColumn = column;
                    _assemblySortAscending = false;
                }
            }
        }
    }
}
