using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private Vector2 _summaryScrollPos;

        private void DrawSummaryTab() {
            _summaryScrollPos = EditorGUILayout.BeginScrollView(_summaryScrollPos);

            DrawSnapshotFileInfo();
            GUILayout.Space(8);
            DrawVmInfo();
            GUILayout.Space(8);
            DrawCountSummary();
            GUILayout.Space(8);
            DrawMemorySummary();
            GUILayout.Space(8);
            DrawClassificationBarChart();
            GUILayout.Space(8);
            DrawTopNativeTypes();
            GUILayout.Space(8);
            DrawTopManagedTypes();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSnapshotFileInfo() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Snapshot Info", _headerStyle);
            DrawSnapshotMetricRow("File", System.IO.Path.GetFileName(_report.FilePath));
            DrawSnapshotMetricRow("Path", _report.FilePath);
            DrawSnapshotMetricRow("Analysis Time", _report.AnalysisTime.ToString("yyyy-MM-dd HH:mm:ss"));
            DrawSnapshotMetricRow("Mode", _report.SkipCrawl ? "Quick (no heap crawl)" : "Full Analysis");
            EditorGUILayout.EndVertical();
        }

        private void DrawVmInfo() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Virtual Machine Information", EditorStyles.boldLabel);
            DrawSnapshotMetricRow("Pointer Size", $"{_report.PointerSize} bytes");
            DrawSnapshotMetricRow("Object Header Size", $"{_report.ObjectHeaderSize} bytes");
            EditorGUILayout.EndVertical();
        }

        private void DrawCountSummary() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Count Summary", EditorStyles.boldLabel);
            DrawSnapshotMetricRowInt("Type Descriptions", _report.TypeCount);
            DrawSnapshotMetricRowInt("Field Descriptions", _report.FieldCount);
            DrawSnapshotMetricRowInt("Native Objects", _report.NativeObjectCount);
            DrawSnapshotMetricRowInt("GC Handles", _report.GcHandleCount);
            DrawSnapshotMetricRowInt("Managed Heap Sections", _report.ManagedHeapSectionCount);
            DrawSnapshotMetricRowInt("Connections", _report.ConnectionCount);
            if (_report.CrawlerResult != null) {
                DrawSnapshotMetricRowInt("Crawled Managed Objects", _report.CrawlerResult.TotalCrawled);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMemorySummary() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Memory Summary", EditorStyles.boldLabel);
            DrawSnapshotMetricRowSize("Native Objects Total", _report.Summary.TotalNativeSize);
            DrawSnapshotMetricRowSize("Managed Heap Total", _report.Summary.TotalManagedHeapSize);
            DrawSnapshotMetricRowSize("Combined", _report.Summary.TotalNativeSize + _report.Summary.TotalManagedHeapSize);
            EditorGUILayout.EndVertical();
        }

        private void DrawClassificationBarChart() {
            var classificationSizes = _report.Summary.SizeByClassification;
            if (classificationSizes.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Assembly Classification (by type base size)", EditorStyles.boldLabel);

            var sorted = classificationSizes.OrderByDescending(kv => kv.Value).ToList();
            long maxSize = sorted.Count > 0 ? sorted[0].Value : 1;
            long totalSize = sorted.Sum(kv => kv.Value);

            foreach (var kv in sorted) {
                EditorGUILayout.BeginHorizontal();

                // Color swatch
                var swatchRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatchRect, GetClassificationColor(kv.Key));

                GUILayout.Label(SnapshotLoader.GetClassificationDisplayName(kv.Key), GUILayout.Width(120));

                var barRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.MinWidth(100));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = maxSize > 0 ? (float)kv.Value / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, GetClassificationColor(kv.Key));
                }

                GUILayout.Label(VmmapParser.FormatSize(kv.Value), GUILayout.Width(80));
                float pct = totalSize > 0 ? (float)kv.Value / totalSize * 100f : 0;
                GUILayout.Label($"{pct:F1}%", _mutedStyle, GUILayout.Width(50));

                int typeCount = 0;
                _report.Summary.TypeCountByClassification.TryGetValue(kv.Key, out typeCount);
                GUILayout.Label($"{typeCount} types", _mutedStyle, GUILayout.Width(70));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTopNativeTypes() {
            if (_report.NativeTypeSummaries.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Top 10 Native Types (by size)", EditorStyles.boldLabel);

            var top = _report.NativeTypeSummaries
                .OrderByDescending(t => t.TotalSize)
                .Take(10)
                .ToList();

            long maxSize = top.Count > 0 ? top[0].TotalSize : 1;

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.MinWidth(120));
            GUILayout.Label("Count", EditorStyles.toolbarButton, GUILayout.Width(60));
            GUILayout.Label("Total Size", EditorStyles.toolbarButton, GUILayout.Width(90));
            GUILayout.Label("", GUILayout.MinWidth(80));
            EditorGUILayout.EndHorizontal();

            foreach (var summary in top) {
                EditorGUILayout.BeginHorizontal();

                var swatchRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatchRect, GetNativeCategoryColor(summary.Category));

                GUILayout.Label(summary.TypeName, GUILayout.MinWidth(108));
                GUILayout.Label(summary.ObjectCount.ToString("N0"), GUILayout.Width(60));
                GUILayout.Label(VmmapParser.FormatSize(summary.TotalSize), GUILayout.Width(90));

                var barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.MinWidth(80));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = maxSize > 0 ? (float)summary.TotalSize / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, GetNativeCategoryColor(summary.Category));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTopManagedTypes() {
            if (_report.Types == null || _report.Types.Length == 0) return;

            var typesWithInstances = _report.CrawlerResult != null
                ? _report.Types.Where(t => t.InstanceCount > 0)
                    .OrderByDescending(t => t.TotalInstanceSize)
                    .Take(10)
                    .ToList()
                : _report.Types
                    .Where(t => t.BaseSize > 0)
                    .OrderByDescending(t => (long)t.BaseSize)
                    .Take(10)
                    .ToList();

            if (typesWithInstances.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string title = _report.CrawlerResult != null
                ? "Top 10 Managed Types (by instance size)"
                : "Top 10 Managed Types (by base size, no crawl data)";
            GUILayout.Label(title, EditorStyles.boldLabel);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.MinWidth(150));
            GUILayout.Label("Assembly", EditorStyles.toolbarButton, GUILayout.Width(120));
            if (_report.CrawlerResult != null) {
                GUILayout.Label("Instances", EditorStyles.toolbarButton, GUILayout.Width(70));
                GUILayout.Label("Total Size", EditorStyles.toolbarButton, GUILayout.Width(90));
            } else {
                GUILayout.Label("Base Size", EditorStyles.toolbarButton, GUILayout.Width(90));
            }
            EditorGUILayout.EndHorizontal();

            foreach (var t in typesWithInstances) {
                EditorGUILayout.BeginHorizontal();

                var classification = SnapshotLoader.ClassifyAssembly(t.Assembly);
                var style = classification == AssemblyClassification.UserCode ? _successStyle : EditorStyles.label;
                GUILayout.Label(t.Name, style, GUILayout.MinWidth(150));
                GUILayout.Label(t.Assembly, _mutedStyle, GUILayout.Width(120));

                if (_report.CrawlerResult != null) {
                    GUILayout.Label(t.InstanceCount.ToString("N0"), GUILayout.Width(70));
                    GUILayout.Label(VmmapParser.FormatSize(t.TotalInstanceSize), GUILayout.Width(90));
                } else {
                    GUILayout.Label(VmmapParser.FormatSize(t.BaseSize), GUILayout.Width(90));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        #region Metric Helpers

        private void DrawSnapshotMetricRow(string label, string value) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            GUILayout.Label(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSnapshotMetricRowInt(string label, int value) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            GUILayout.Label(value.ToString("N0"), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSnapshotMetricRowSize(string label, long bytes) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            GUILayout.Label(VmmapParser.FormatSize(bytes), EditorStyles.boldLabel, GUILayout.Width(120));
            GUILayout.Label($"({bytes:N0} bytes)", _mutedStyle);
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
