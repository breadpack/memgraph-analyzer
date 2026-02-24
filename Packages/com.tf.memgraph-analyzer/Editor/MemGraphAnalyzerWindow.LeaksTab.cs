using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _leaksScrollPos;
        private int _selectedLeakIndex = -1;
        private bool _showLeaksRaw;
        private bool _showLeaksGrouped;
        private List<LeakGroup> _cachedLeakGroups;

        private void DrawLeaksTab() {
            if (_report.Leaks.TotalLeakCount == 0 && _report.Leaks.Leaks.Count == 0) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("No memory leaks detected.", _successStyle);
                GUILayout.Label("The leaks tool found no leaked memory in this memgraph.", _mutedStyle);
                EditorGUILayout.EndVertical();

                DrawLeaksRawOutput();
                return;
            }

            // Summary
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                $"{_report.Leaks.TotalLeakCount} leaks detected | " +
                $"Total: {VmmapParser.FormatSize(_report.Leaks.TotalLeakBytes)}",
                _errorStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // View toggle
            EditorGUILayout.BeginHorizontal();
            _showLeaksGrouped = GUILayout.Toggle(_showLeaksGrouped, "Group by Type",
                EditorStyles.toolbarButton, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (_showLeaksGrouped) {
                DrawLeaksGroupedView();
            } else {
                DrawLeaksFlatView();
            }

            // Next Steps
            DrawLeaksNextSteps();

            if (_report.Leaks.ParseErrorCount > 0) {
                GUILayout.Label($"({_report.Leaks.ParseErrorCount} entries could not be parsed)", _mutedStyle);
            }

            DrawLeaksRawOutput();
        }

        private void DrawLeaksGroupedView() {
            if (_cachedLeakGroups == null)
                _cachedLeakGroups = LeaksParser.GroupLeaks(_report.Leaks);

            _leaksScrollPos = EditorGUILayout.BeginScrollView(_leaksScrollPos);

            foreach (var group in _cachedLeakGroups) {
                var severityColor = GetSeverityColor(group.Severity);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var prevColor = GUI.contentColor;
                GUI.contentColor = severityColor;
                GUILayout.Label(GetSeverityIcon(group.Severity), GUILayout.Width(30));
                GUI.contentColor = prevColor;

                GUILayout.Label(group.TypeOrZone, EditorStyles.boldLabel, GUILayout.MinWidth(150));
                GUILayout.Label($"{group.Entries.Count} leaks", GUILayout.Width(70));
                GUILayout.Label(VmmapParser.FormatSize(group.TotalBytes), GUILayout.Width(80));
                GUILayout.Label(HeapParser.GetOwnerDisplayName(group.Owner), _mutedStyle, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLeaksFlatView() {
            _leaksScrollPos = EditorGUILayout.BeginScrollView(_leaksScrollPos);

            for (int i = 0; i < _report.Leaks.Leaks.Count; i++) {
                var leak = _report.Leaks.Leaks[i];
                bool isSelected = i == _selectedLeakIndex;

                var rowRect = EditorGUILayout.BeginHorizontal();

                if (Event.current.type == EventType.Repaint && isSelected) {
                    EditorGUI.DrawRect(rowRect, SelectionColor);
                }

                GUILayout.Label(leak.Address, GUILayout.Width(130));
                GUILayout.Label(VmmapParser.FormatSize(leak.Size), GUILayout.Width(80));

                var ownerStyle = leak.Owner switch {
                    MemoryOwner.Unity => _warningStyle,
                    MemoryOwner.NativePlugin => _warningStyle,
                    MemoryOwner.UnsafeUtility => _errorStyle,
                    _ => EditorStyles.label,
                };
                GUILayout.Label(HeapParser.GetOwnerDisplayName(leak.Owner), ownerStyle, GUILayout.Width(100));
                GUILayout.Label(leak.TypeOrZone ?? "", GUILayout.MinWidth(100));

                EditorGUILayout.EndHorizontal();

                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition)) {
                    _selectedLeakIndex = isSelected ? -1 : i;
                    Event.current.Use();
                    Repaint();
                }

                // Show detail if selected
                if (isSelected) {
                    DrawLeakDetail(leak);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLeaksNextSteps() {
            var leaks = _report.Leaks.Leaks;
            if (leaks.Count == 0) return;

            bool hasUnity = leaks.Any(l => l.Owner == MemoryOwner.Unity);
            bool hasPlugin = leaks.Any(l => l.Owner == MemoryOwner.NativePlugin);
            bool hasUnsafe = leaks.Any(l => l.Owner == MemoryOwner.UnsafeUtility);

            if (!hasUnity && !hasPlugin && !hasUnsafe) return;

            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Next Steps", EditorStyles.boldLabel);

            if (hasUnity) {
                GUILayout.Label(
                    ">> Unity-owned leaks: Check for unreleased textures, meshes, or assets. " +
                    "Verify Destroy() calls match Instantiate().", _warningStyle);
            }
            if (hasPlugin) {
                GUILayout.Label(
                    ">> Plugin leaks: Report to vendor with stack traces. " +
                    "Check plugin version updates for fixes.", _warningStyle);
            }
            if (hasUnsafe) {
                GUILayout.Label(
                    ">> UnsafeUtility leaks: Verify UnsafeUtility.Malloc/Free pairs. " +
                    "Check NativeArray/NativeList disposal.", _errorStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLeakDetail(LeakEntry leak) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Address", leak.Address);
            EditorGUILayout.LabelField("Size", VmmapParser.FormatSize(leak.Size));
            EditorGUILayout.LabelField("Type/Zone", leak.TypeOrZone ?? "(unknown)");
            EditorGUILayout.LabelField("Owner", HeapParser.GetOwnerDisplayName(leak.Owner));

            if (!string.IsNullOrEmpty(leak.StackTrace)) {
                GUILayout.Space(4);
                GUILayout.Label("Stack Trace:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(leak.StackTrace, EditorStyles.miniLabel,
                    GUILayout.MinHeight(80), GUILayout.ExpandHeight(false));
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Detail", GUILayout.Width(100))) {
                var text = $"Leak: {leak.Address} size={leak.Size} type={leak.TypeOrZone}\n" +
                           $"Owner: {HeapParser.GetOwnerDisplayName(leak.Owner)}\n" +
                           $"Stack:\n{leak.StackTrace}";
                GUIUtility.systemCopyBuffer = text;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawLeaksRawOutput() {
            GUILayout.Space(8);
            _showLeaksRaw = EditorGUILayout.Foldout(_showLeaksRaw, "Show Raw Output");
            if (_showLeaksRaw) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var raw = _report.Leaks.RawOutput;
                if (string.IsNullOrEmpty(raw)) {
                    GUILayout.Label("(no raw output available)", _mutedStyle);
                } else {
                    var display = raw.Length > 5000 ? raw.Substring(0, 5000) + "\n...(truncated)" : raw;
                    EditorGUILayout.SelectableLabel(display, EditorStyles.miniLabel,
                        GUILayout.MinHeight(200), GUILayout.ExpandHeight(true));
                }
                EditorGUILayout.EndVertical();
            }
        }
    }
}
