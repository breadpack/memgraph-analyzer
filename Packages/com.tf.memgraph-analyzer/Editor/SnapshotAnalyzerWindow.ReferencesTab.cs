using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private Vector2 _refScrollPos;
        private string _refSearchText = "";
        private List<ManagedObjectInstance> _refSearchResults;
        private int _selectedRefObject = -1;
        private ReferenceChain _currentChain;
        private List<ReferenceEdge> _currentIncoming;
        private List<ReferenceEdge> _currentOutgoing;

        private void DrawReferencesTab() {
            if (_report.CrawlerResult == null) {
                EditorGUILayout.HelpBox(
                    "Heap crawling was skipped or not completed. " +
                    "Re-analyze with full mode (not Quick) to enable reference analysis.",
                    MessageType.Warning);
                return;
            }

            DrawRefSearch();
            GUILayout.Space(4);
            DrawTopRetainedObjects();
            GUILayout.Space(4);

            if (_selectedRefObject >= 0)
                DrawRefObjectDetail();
        }

        private void DrawRefSearch() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Search Type:", GUILayout.Width(80));
            _refSearchText = EditorGUILayout.TextField(_refSearchText, GUILayout.Width(200));
            if (GUILayout.Button("Search", GUILayout.Width(60))) {
                PerformRefSearch();
            }
            EditorGUILayout.EndHorizontal();

            if (_refSearchResults != null && _refSearchResults.Count > 0) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"Found {_refSearchResults.Count} objects:", EditorStyles.boldLabel);

                int shown = Math.Min(_refSearchResults.Count, 20);
                for (int i = 0; i < shown; i++) {
                    var obj = _refSearchResults[i];
                    string typeName = GetTypeName(obj.TypeIndex);
                    long retained = GetRetainedSize(obj.ObjectIndex);

                    var rect = EditorGUILayout.BeginHorizontal();

                    bool isSelected = _selectedRefObject == obj.ObjectIndex;
                    if (isSelected && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(rect, SelectionColor);

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                        SelectRefObject(obj.ObjectIndex);
                        Event.current.Use();
                        Repaint();
                    }

                    GUILayout.Label(typeName, GUILayout.MinWidth(150));
                    GUILayout.Label($"Size: {VmmapParser.FormatSize(obj.Size)}", GUILayout.Width(100));
                    GUILayout.Label($"Retained: {VmmapParser.FormatSize(retained)}", GUILayout.Width(120));
                    if (obj.IsGcRoot) GUILayout.Label("[GC Root]", _successStyle, GUILayout.Width(70));
                    GUILayout.Label($"@0x{obj.Address:X}", _mutedStyle, GUILayout.Width(140));

                    EditorGUILayout.EndHorizontal();
                }

                if (_refSearchResults.Count > 20) {
                    GUILayout.Label($"... and {_refSearchResults.Count - 20} more", _mutedStyle);
                }

                EditorGUILayout.EndVertical();
            } else if (_refSearchResults != null) {
                GUILayout.Label("No objects found matching the search.", _mutedStyle);
            }
        }

        private void DrawTopRetainedObjects() {
            if (_report.RetainedSizes == null || _report.RetainedSizes.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Top 15 Objects by Retained Size", EditorStyles.boldLabel);

            var topRetained = _report.RetainedSizes
                .Where(kv => kv.Key >= 0 && kv.Key < _report.CrawlerResult.Objects.Count)
                .OrderByDescending(kv => kv.Value)
                .Take(15)
                .ToList();

            foreach (var kv in topRetained) {
                var obj = _report.CrawlerResult.Objects[kv.Key];
                string typeName = GetTypeName(obj.TypeIndex);

                var rect = EditorGUILayout.BeginHorizontal();

                bool isSelected = _selectedRefObject == obj.ObjectIndex;
                if (isSelected && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, SelectionColor);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                    SelectRefObject(obj.ObjectIndex);
                    Event.current.Use();
                    Repaint();
                }

                GUILayout.Label(typeName, GUILayout.MinWidth(150));
                GUILayout.Label($"Own: {VmmapParser.FormatSize(obj.Size)}", GUILayout.Width(100));
                GUILayout.Label($"Retained: {VmmapParser.FormatSize(kv.Value)}", EditorStyles.boldLabel,
                    GUILayout.Width(120));
                if (obj.IsGcRoot) GUILayout.Label("[GC Root]", _successStyle, GUILayout.Width(70));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRefObjectDetail() {
            if (_selectedRefObject < 0 || _selectedRefObject >= _report.CrawlerResult.Objects.Count)
                return;

            var obj = _report.CrawlerResult.Objects[_selectedRefObject];
            string typeName = GetTypeName(obj.TypeIndex);

            _refScrollPos = EditorGUILayout.BeginScrollView(_refScrollPos);

            // Object info
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"Selected: {typeName}", _headerStyle);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Address:", GUILayout.Width(80));
            GUILayout.Label($"0x{obj.Address:X}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Own Size:", GUILayout.Width(80));
            GUILayout.Label(VmmapParser.FormatSize(obj.Size), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            long retained = GetRetainedSize(obj.ObjectIndex);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Retained:", GUILayout.Width(80));
            GUILayout.Label(VmmapParser.FormatSize(retained), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (obj.IsGcRoot) {
                GUILayout.Label("This object is a GC Root", _successStyle);
            }
            if (obj.GcHandleIndex >= 0) {
                GUILayout.Label($"GC Handle Index: {obj.GcHandleIndex}", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);

            // Reference chain
            if (_currentChain != null && _currentChain.IsValid) {
                DrawReferenceChain();
            }

            GUILayout.Space(4);

            // Incoming references
            if (_currentIncoming != null && _currentIncoming.Count > 0) {
                DrawIncomingReferences();
            }

            GUILayout.Space(4);

            // Outgoing references
            if (_currentOutgoing != null && _currentOutgoing.Count > 0) {
                DrawOutgoingReferences();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawReferenceChain() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Reference Chain (GC Root -> Object)", EditorStyles.boldLabel);

            for (int i = 0; i < _currentChain.Nodes.Count; i++) {
                var node = _currentChain.Nodes[i];

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(i * 16);

                if (node.IsGcRoot) {
                    GUILayout.Label("[ROOT]", _successStyle, GUILayout.Width(50));
                } else {
                    GUILayout.Label("->", _mutedStyle, GUILayout.Width(50));
                }

                GUILayout.Label(node.TypeName, GUILayout.MinWidth(150));

                if (!string.IsNullOrEmpty(node.FieldName)) {
                    GUILayout.Label($".{node.FieldName}", _warningStyle, GUILayout.Width(120));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawIncomingReferences() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"Referenced By ({_currentIncoming.Count})", EditorStyles.boldLabel);

            int shown = Math.Min(_currentIncoming.Count, 20);
            for (int i = 0; i < shown; i++) {
                var edge = _currentIncoming[i];
                if (edge.FromObjectIndex < 0 || edge.FromObjectIndex >= _report.CrawlerResult.Objects.Count)
                    continue;

                var fromObj = _report.CrawlerResult.Objects[edge.FromObjectIndex];
                string typeName = GetTypeName(fromObj.TypeIndex);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(typeName, GUILayout.MinWidth(150));
                if (!string.IsNullOrEmpty(edge.FieldName))
                    GUILayout.Label($".{edge.FieldName}", _warningStyle, GUILayout.Width(120));
                GUILayout.Label(VmmapParser.FormatSize(fromObj.Size), _mutedStyle, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }

            if (_currentIncoming.Count > 20) {
                GUILayout.Label($"    ... and {_currentIncoming.Count - 20} more", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawOutgoingReferences() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"References To ({_currentOutgoing.Count})", EditorStyles.boldLabel);

            int shown = Math.Min(_currentOutgoing.Count, 20);
            for (int i = 0; i < shown; i++) {
                var edge = _currentOutgoing[i];
                if (edge.ToObjectIndex < 0 || edge.ToObjectIndex >= _report.CrawlerResult.Objects.Count)
                    continue;

                var toObj = _report.CrawlerResult.Objects[edge.ToObjectIndex];
                string typeName = GetTypeName(toObj.TypeIndex);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                if (!string.IsNullOrEmpty(edge.FieldName))
                    GUILayout.Label($".{edge.FieldName}", _warningStyle, GUILayout.Width(120));
                GUILayout.Label(typeName, GUILayout.MinWidth(150));
                GUILayout.Label(VmmapParser.FormatSize(toObj.Size), _mutedStyle, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }

            if (_currentOutgoing.Count > 20) {
                GUILayout.Label($"    ... and {_currentOutgoing.Count - 20} more", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        #region Reference Helpers

        private void PerformRefSearch() {
            if (_report.CrawlerResult == null || string.IsNullOrEmpty(_refSearchText)) {
                _refSearchResults = null;
                return;
            }

            _refSearchResults = new List<ManagedObjectInstance>();
            foreach (var obj in _report.CrawlerResult.Objects) {
                string typeName = GetTypeName(obj.TypeIndex);
                if (typeName.IndexOf(_refSearchText, StringComparison.OrdinalIgnoreCase) >= 0) {
                    _refSearchResults.Add(obj);
                }
            }

            // Sort by retained size descending
            _refSearchResults.Sort((a, b) => {
                long ra = GetRetainedSize(a.ObjectIndex);
                long rb = GetRetainedSize(b.ObjectIndex);
                return rb.CompareTo(ra);
            });
        }

        private void SelectRefObject(int objectIndex) {
            _selectedRefObject = objectIndex;
            _currentChain = RetainedSizeCalculator.FindReferenceChain(
                objectIndex, _report.CrawlerResult, _report.Types);
            _currentIncoming = RetainedSizeCalculator.GetIncomingReferences(
                objectIndex, _report.CrawlerResult);
            _currentOutgoing = RetainedSizeCalculator.GetOutgoingReferences(
                objectIndex, _report.CrawlerResult);
        }

        private string GetTypeName(int typeIndex) {
            if (typeIndex < 0 || typeIndex >= _report.Types.Length)
                return "(unknown)";
            var type = _report.Types[typeIndex];
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
        }

        private long GetRetainedSize(int objectIndex) {
            if (_report.RetainedSizes != null &&
                _report.RetainedSizes.TryGetValue(objectIndex, out long retained))
                return retained;
            if (objectIndex >= 0 && objectIndex < _report.CrawlerResult.Objects.Count)
                return _report.CrawlerResult.Objects[objectIndex].Size;
            return 0;
        }

        #endregion
    }
}
