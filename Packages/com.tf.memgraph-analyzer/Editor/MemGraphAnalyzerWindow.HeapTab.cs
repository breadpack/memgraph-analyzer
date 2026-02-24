using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _heapScrollPos;
        private string _heapFilter = "";
        private int _heapOwnerFilter;
        private int _heapTopN = 100;
        private bool _showHeapRaw;
        private int _selectedHeapRow = -1;

        private enum HeapSortColumn { Count, Bytes, Avg, Class, Owner }
        private HeapSortColumn _heapSortColumn = HeapSortColumn.Bytes;
        private bool _heapSortAscending;

        private static readonly string[] OwnerFilterLabels = {
            "All", "Unknown", "Unity", "Native Plugin", "System", "Thread Stack", "Graphics/GPU", "UnsafeUtility"
        };

        private static readonly Color DetailBgColor = new(0.18f, 0.18f, 0.22f, 0.9f);
        private static readonly Color BarFillColor = new(0.35f, 0.65f, 1f, 0.7f);
        private static readonly Color UserCodeColor = new(0.3f, 0.85f, 0.3f, 1f);

        private void DrawHeapTab() {
            DrawHeapSuspiciousPatterns();
            DrawHeapOwnerGroupSummary();
            GUILayout.Space(4);

            // Filters
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _heapFilter = EditorGUILayout.TextField(_heapFilter, GUILayout.Width(150));
            GUILayout.Label("Owner:", GUILayout.Width(45));
            _heapOwnerFilter = EditorGUILayout.Popup(_heapOwnerFilter, OwnerFilterLabels, GUILayout.Width(110));
            GUILayout.Label("Top N:", GUILayout.Width(45));
            _heapTopN = EditorGUILayout.IntSlider(_heapTopN, 10, 1000);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawHeapSortButton("Count", HeapSortColumn.Count, 60);
            DrawHeapSortButton("Bytes", HeapSortColumn.Bytes, 90);
            DrawHeapSortButton("Avg", HeapSortColumn.Avg, 70);
            DrawHeapSortButton("Class Name", HeapSortColumn.Class, 0, true);
            DrawHeapSortButton("Owner", HeapSortColumn.Owner, 100);
            GUILayout.Label("Action", EditorStyles.toolbarButton, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            // Data
            var rows = GetFilteredHeapRows();

            _heapScrollPos = EditorGUILayout.BeginScrollView(_heapScrollPos);

            for (int i = 0; i < rows.Count; i++) {
                var alloc = rows[i];
                bool isSelected = _selectedHeapRow == i;

                var rect = EditorGUILayout.BeginHorizontal();

                // Selection highlight
                if (isSelected && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, SelectionColor);

                // Click detection
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                    _selectedHeapRow = isSelected ? -1 : i;
                    Event.current.Use();
                    Repaint();
                }

                GUILayout.Label(alloc.Count.ToString("N0"), GUILayout.Width(60));
                GUILayout.Label(VmmapParser.FormatSize(alloc.TotalBytes), GUILayout.Width(90));
                GUILayout.Label(VmmapParser.FormatSize(alloc.AverageSize), GUILayout.Width(70));
                GUILayout.Label(alloc.ClassName, GUILayout.MinWidth(100));

                var ownerStyle = alloc.Owner switch {
                    MemoryOwner.Unity => _successStyle,
                    MemoryOwner.NativePlugin => _warningStyle,
                    MemoryOwner.UnsafeUtility => _errorStyle,
                    MemoryOwner.GraphicsDriver => _successStyle,
                    _ => EditorStyles.label,
                };
                GUILayout.Label(HeapParser.GetOwnerDisplayName(alloc.Owner), ownerStyle, GUILayout.Width(100));

                var actionability = HeapParser.GetActionability(alloc);
                var actStyle = new GUIStyle(EditorStyles.miniLabel) {
                    normal = { textColor = GetActionabilityColor(actionability) }
                };
                GUILayout.Label(HeapParser.GetActionabilityLabel(actionability), actStyle, GUILayout.Width(60));

                EditorGUILayout.EndHorizontal();

                // Detail panel inline below selected row
                if (isSelected) {
                    DrawHeapDetail(alloc);
                }
            }

            EditorGUILayout.EndScrollView();

            // Footer
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                $"{rows.Count} entries shown (of {_report.Heap.Allocations.Count} total) | " +
                $"Total: {VmmapParser.FormatSize(_report.Heap.TotalBytes)}",
                _mutedStyle);
            if (_report.Heap.ParseErrorCount > 0) {
                GUILayout.Label($"({_report.Heap.ParseErrorCount} parse errors)", _warningStyle);
            }
            EditorGUILayout.EndHorizontal();

            // Raw output foldout
            GUILayout.Space(8);
            _showHeapRaw = EditorGUILayout.Foldout(_showHeapRaw, "Show Raw Output");
            if (_showHeapRaw) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var raw = _report.Heap.RawOutput;
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

        private void DrawHeapDetail(HeapAllocation alloc) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                $">> {alloc.ClassName} -- {VmmapParser.FormatSize(alloc.TotalBytes)} Detail",
                _headerStyle);
            GUILayout.Space(4);

            DrawSizeDistribution(alloc);
            GUILayout.Space(6);
            DrawCallTreeCallers(alloc);
            GUILayout.Space(6);
            DrawAddressTraces(alloc);

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        private void DrawSizeDistribution(HeapAllocation alloc) {
            if (alloc.SizeDistribution == null || alloc.SizeDistribution.Count <= 1) return;

            int bucketCount = alloc.SizeDistribution.Count;
            GUILayout.Label($"[Size Distribution] ({bucketCount} buckets)", EditorStyles.boldLabel);

            const int maxDisplay = 10;
            long maxBytes = alloc.SizeDistribution.Count > 0 ? alloc.SizeDistribution[0].TotalBytes : 1;
            if (maxBytes <= 0) maxBytes = 1;

            int displayed = Math.Min(maxDisplay, alloc.SizeDistribution.Count);
            for (int i = 0; i < displayed; i++) {
                var bucket = alloc.SizeDistribution[i];
                float pct = alloc.TotalBytes > 0 ? (float)bucket.TotalBytes / alloc.TotalBytes * 100f : 0;
                float barRatio = (float)bucket.TotalBytes / maxBytes;

                EditorGUILayout.BeginHorizontal();

                // Size label
                GUILayout.Label(FormatBucketSize(bucket.Size), GUILayout.Width(70));

                // Bar
                var barRect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    var fillRect = new Rect(barRect.x, barRect.y,
                        barRect.width * Mathf.Clamp01(barRatio), barRect.height);
                    EditorGUI.DrawRect(fillRect, BarFillColor);
                }

                // Stats
                GUILayout.Label(
                    $"{VmmapParser.FormatSize(bucket.TotalBytes)} ({pct:F1}%) x{bucket.Count:N0}",
                    EditorStyles.miniLabel, GUILayout.Width(180));

                EditorGUILayout.EndHorizontal();
            }

            if (alloc.SizeDistribution.Count > maxDisplay) {
                GUILayout.Label(
                    $"    ... {alloc.SizeDistribution.Count - maxDisplay} more buckets",
                    _mutedStyle);
            }
        }

        private void DrawCallTreeCallers(HeapAllocation alloc) {
            if (_report.CallTree == null || _report.CallTree.Count == 0) return;

            // Extract keyword from class name for matching
            string searchKey = ExtractSearchKey(alloc.ClassName);
            if (string.IsNullOrEmpty(searchKey)) return;

            var callers = CallTreeParser.ExtractCallers(_report.CallTree, searchKey);
            if (callers.Count == 0) return;

            GUILayout.Label("[Allocation Callers] (malloc_history)", EditorStyles.boldLabel);

            // Collect user code entries
            var userCodeEntries = new List<CallTreeEntry>();

            const int maxCallers = 8;
            int displayed = Math.Min(maxCallers, callers.Count);
            for (int i = 0; i < displayed; i++) {
                var caller = callers[i];
                string displayName = CallTreeParser.FormatFunctionName(caller.FunctionName);
                bool isUser = CallTreeParser.IsUserCode(caller.FunctionName);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);

                if (isUser) {
                    var userStyle = new GUIStyle(EditorStyles.label) {
                        normal = { textColor = UserCodeColor }
                    };
                    GUILayout.Label(displayName, userStyle, GUILayout.MinWidth(200));
                    userCodeEntries.Add(caller);
                } else {
                    GUILayout.Label(displayName, GUILayout.MinWidth(200));
                }

                GUILayout.Label(VmmapParser.FormatSize(caller.TotalBytes), GUILayout.Width(80));

                if (!string.IsNullOrEmpty(caller.Binary)) {
                    GUILayout.Label($"({caller.Binary})", _mutedStyle, GUILayout.Width(140));
                }

                EditorGUILayout.EndHorizontal();

                // Show sub-callers for significant entries
                DrawSubCallers(caller, alloc.TotalBytes);
            }

            if (callers.Count > maxCallers) {
                GUILayout.Label(
                    $"    ... {callers.Count - maxCallers} more callers",
                    _mutedStyle);
            }

            // User code paths summary
            if (userCodeEntries.Count > 0) {
                GUILayout.Space(4);
                DrawUserCodePaths(userCodeEntries);
            }
        }

        private void DrawSubCallers(CallTreeEntry parent, long parentTotal) {
            if (parent.Children == null || parent.Children.Count == 0) return;

            var sorted = parent.Children.OrderByDescending(c => c.TotalBytes).Take(5).ToList();
            foreach (var child in sorted) {
                // Only show significant sub-callers (> 5% of parent)
                if (parentTotal > 0 && child.TotalBytes < parentTotal * 5 / 100) continue;

                string displayName = CallTreeParser.FormatFunctionName(child.FunctionName);
                bool isUser = CallTreeParser.IsUserCode(child.FunctionName);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(36);
                GUILayout.Label("->", _mutedStyle, GUILayout.Width(20));

                if (isUser) {
                    var userStyle = new GUIStyle(EditorStyles.label) {
                        normal = { textColor = UserCodeColor }
                    };
                    GUILayout.Label(displayName, userStyle, GUILayout.MinWidth(180));
                } else {
                    GUILayout.Label(displayName, GUILayout.MinWidth(180));
                }

                GUILayout.Label(VmmapParser.FormatSize(child.TotalBytes), GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawUserCodePaths(List<CallTreeEntry> userEntries) {
            GUILayout.Label("[C# Code Paths] (optimizable)", EditorStyles.boldLabel);

            foreach (var entry in userEntries.OrderByDescending(e => e.TotalBytes)) {
                string displayName = CallTreeParser.FormatFunctionName(entry.FunctionName);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);

                var codeStyle = new GUIStyle(EditorStyles.label) {
                    normal = { textColor = UserCodeColor }
                };
                GUILayout.Label("[C#]", codeStyle, GUILayout.Width(30));
                GUILayout.Label(displayName, codeStyle, GUILayout.MinWidth(200));
                GUILayout.Label($"= {VmmapParser.FormatSize(entry.TotalBytes)}", GUILayout.Width(100));

                EditorGUILayout.EndHorizontal();
            }
        }

        private static string ExtractSearchKey(string className) {
            if (string.IsNullOrEmpty(className)) return null;
            // For "memalign in MemoryManager::LowLevelAllocate", extract "LowLevelAllocate"
            int inIdx = className.IndexOf(" in ", StringComparison.Ordinal);
            if (inIdx >= 0) {
                var afterIn = className.Substring(inIdx + 4).Trim();
                int colonIdx = afterIn.LastIndexOf("::", StringComparison.Ordinal);
                return colonIdx >= 0 ? afterIn.Substring(colonIdx + 2) : afterIn;
            }
            // For simple class names, use as-is
            int lastColon = className.LastIndexOf("::", StringComparison.Ordinal);
            return lastColon >= 0 ? className.Substring(lastColon + 2) : className;
        }

        private static string FormatBucketSize(long size) {
            if (size >= 1024 * 1024) return $"{size / 1024.0 / 1024.0:F1} MB";
            if (size >= 1024) return $"{size / 1024.0:F1} KB";
            return $"{size} B";
        }

        #region Filtering and Sorting

        private List<HeapAllocation> GetFilteredHeapRows() {
            var rows = _report.Heap.Allocations.AsEnumerable();

            if (!string.IsNullOrEmpty(_heapFilter)) {
                rows = rows.Where(a => a.ClassName != null &&
                    a.ClassName.IndexOf(_heapFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_heapOwnerFilter > 0) {
                var targetOwner = (MemoryOwner)(_heapOwnerFilter - 1);
                rows = rows.Where(a => a.Owner == targetOwner);
            }

            rows = _heapSortColumn switch {
                HeapSortColumn.Count => _heapSortAscending
                    ? rows.OrderBy(a => a.Count) : rows.OrderByDescending(a => a.Count),
                HeapSortColumn.Bytes => _heapSortAscending
                    ? rows.OrderBy(a => a.TotalBytes) : rows.OrderByDescending(a => a.TotalBytes),
                HeapSortColumn.Avg => _heapSortAscending
                    ? rows.OrderBy(a => a.AverageSize) : rows.OrderByDescending(a => a.AverageSize),
                HeapSortColumn.Class => _heapSortAscending
                    ? rows.OrderBy(a => a.ClassName) : rows.OrderByDescending(a => a.ClassName),
                HeapSortColumn.Owner => _heapSortAscending
                    ? rows.OrderBy(a => a.Owner) : rows.OrderByDescending(a => a.Owner),
                _ => rows,
            };

            return rows.Take(_heapTopN).ToList();
        }

        private void DrawHeapSortButton(string label, HeapSortColumn column, float width, bool expand = false) {
            var displayLabel = label;
            if (_heapSortColumn == column)
                displayLabel += _heapSortAscending ? " ^" : " v";

            bool clicked;
            if (expand)
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            else
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.Width(width));

            if (clicked) {
                if (_heapSortColumn == column)
                    _heapSortAscending = !_heapSortAscending;
                else {
                    _heapSortColumn = column;
                    _heapSortAscending = false;
                }
            }
        }

        #endregion

        #region Suspicious Patterns

        private void DrawHeapSuspiciousPatterns() {
            var allocs = _report.Heap.Allocations;
            if (allocs.Count == 0) return;

            var largeAvg = allocs.Where(a => a.AverageSize > 10L * 1024 * 1024).ToList();
            var poolCandidates = allocs.Where(a =>
                a.Count > 1000 && a.AverageSize < 1024 && a.TotalBytes > 1024 * 1024).ToList();

            int singleCountTypes = allocs.Count(a => a.Count == 1);
            bool highFragmentation = allocs.Count > 10 && singleCountTypes > allocs.Count / 2;

            if (largeAvg.Count == 0 && poolCandidates.Count == 0 && !highFragmentation) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Suspicious Patterns", EditorStyles.boldLabel);

            if (largeAvg.Count > 0) {
                GUILayout.Label($"[!] Large Allocations ({largeAvg.Count} types with avg > 10 MB):", _warningStyle);
                foreach (var a in largeAvg.Take(3)) {
                    GUILayout.Label($"    {a.ClassName}: avg {VmmapParser.FormatSize(a.AverageSize)}, " +
                        $"total {VmmapParser.FormatSize(a.TotalBytes)}", EditorStyles.miniLabel);
                }
            }

            if (poolCandidates.Count > 0) {
                GUILayout.Label($"[i] Pooling Candidates ({poolCandidates.Count} types, many small allocs):", _warningStyle);
                foreach (var a in poolCandidates.Take(3)) {
                    GUILayout.Label($"    {a.ClassName}: {a.Count:N0} allocs, avg {VmmapParser.FormatSize(a.AverageSize)}",
                        EditorStyles.miniLabel);
                }
            }

            if (highFragmentation) {
                GUILayout.Label(
                    $"[!] Type Fragmentation: {singleCountTypes}/{allocs.Count} types have only 1 allocation. " +
                    "Excessive type diversity may indicate over-allocation.", _warningStyle);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        #endregion

        #region Owner Group Summary

        private void DrawHeapOwnerGroupSummary() {
            var owners = _report.Summary.OwnerBreakdowns;
            if (owners.Count == 0 || _report.Heap.TotalBytes <= 0) return;

            EditorGUILayout.BeginHorizontal();
            foreach (var kv in owners.OrderByDescending(kv => kv.Value)) {
                float pct = (float)kv.Value / _report.Heap.TotalBytes * 100f;
                var color = GetOwnerColor(kv.Key);
                var rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, color);
                GUILayout.Label($"{HeapParser.GetOwnerDisplayName(kv.Key)} {pct:F0}%",
                    EditorStyles.miniLabel, GUILayout.Width(110));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
