using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _summaryScrollPos;
        private int _selectedDeviceIndex = iOSDeviceProfiles.DefaultIndex;

        private void DrawSummaryTab() {
            _summaryScrollPos = EditorGUILayout.BeginScrollView(_summaryScrollPos);

            // Health Badge
            DrawHealthBadge();
            GUILayout.Space(8);

            // iOS Device Comparison
            DrawDeviceComparisonBar();
            GUILayout.Space(8);

            // Top Issues
            DrawTopIssues();
            GUILayout.Space(8);

            GUILayout.Label("Memory Overview", _headerStyle);
            GUILayout.Space(4);

            // Key metrics with health colors
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var fp = _report.Footprint;
            if (fp.PhysFootprint > 0) {
                DrawMetricRowWithHealth("Physical Footprint (Jetsam)", fp.PhysFootprint,
                    1228L * 1024 * 1024, 1536L * 1024 * 1024);
                if (fp.PhysFootprintPeak > 0)
                    DrawMetricRow("Physical Footprint (peak)", fp.PhysFootprintPeak);
                DrawMetricRow("Footprint Dirty", fp.TotalDirty);
                DrawMetricRow("Footprint Clean", fp.TotalClean);
                DrawMetricRow("Footprint Reclaimable", fp.TotalReclaimable);
            }
            DrawMetricRow("Total Virtual (vmmap)", _report.Summary.TotalVirtual);
            DrawMetricRowWithHealth("Total Resident (vmmap)", _report.Vmmap.Total?.ResidentSize ?? 0,
                1228L * 1024 * 1024, 1536L * 1024 * 1024);
            DrawMetricRow("Total Dirty (vmmap)", _report.Vmmap.Total?.DirtySize ?? 0);
            DrawMetricRow("Total Swapped (vmmap)", _report.Summary.TotalSwapped);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // Heap totals
            if (_report.Heap.TotalBytes > 0) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Heap Summary", EditorStyles.boldLabel);
                DrawMetricRow("Total Heap Bytes", _report.Heap.TotalBytes);
                DrawMetricRow("Total Allocations", _report.Heap.TotalCount);
                DrawMetricRow("Unity Tracked (est.)", _report.Summary.TrackedByUnity);
                DrawMetricRow("Untracked (est.)", _report.Summary.UntrackedByUnity);
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(8);

            // Category bar chart
            DrawCategoryBarChart();

            GUILayout.Space(8);

            // Owner breakdown bar chart
            DrawOwnerBarChart();

            GUILayout.Space(8);

            // Leak summary
            DrawLeakSummaryBox();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHealthBadge() {
            var health = _report.Summary.OverallHealth;
            var color = GetHealthColor(health);
            long footprint = _report.Footprint.PhysFootprint > 0
                ? _report.Footprint.PhysFootprint : _report.Summary.TotalResident;

            string icon = health switch {
                MemoryHealthStatus.Good => "OK",
                MemoryHealthStatus.Warning => "!",
                MemoryHealthStatus.Critical => "!!",
                _ => "?",
            };

            var bgColor = new Color(color.r, color.g, color.b, 0.15f);
            var rect = EditorGUILayout.GetControlRect(false, 32);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, bgColor);

            var prevColor = GUI.contentColor;
            GUI.contentColor = color;
            var label = _report.Footprint.PhysFootprint > 0 ? "Footprint" : "Resident";
            var text = $"[{icon}] Memory Health: {health.ToString().ToUpper()}    {label}: {VmmapParser.FormatSize(footprint)}";
            GUI.Label(rect, text, _healthBadgeStyle);
            GUI.contentColor = prevColor;
        }

        private void DrawDeviceComparisonBar() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("iOS Device Comparison", EditorStyles.boldLabel);

            var deviceNames = new string[iOSDeviceProfiles.All.Length];
            for (int i = 0; i < iOSDeviceProfiles.All.Length; i++)
                deviceNames[i] = iOSDeviceProfiles.All[i].Name;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Device:", GUILayout.Width(50));
            _selectedDeviceIndex = EditorGUILayout.Popup(_selectedDeviceIndex, deviceNames, GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();

            var device = iOSDeviceProfiles.All[_selectedDeviceIndex];
            long resident = _report.Footprint.PhysFootprint > 0
                ? _report.Footprint.PhysFootprint : _report.Summary.TotalResident;

            GUILayout.Space(4);

            // Bar: usage vs limit
            var barRect = EditorGUILayout.GetControlRect(false, 24);
            if (Event.current.type == EventType.Repaint) {
                EditorGUI.DrawRect(barRect, BarBgColor);

                float usageRatio = device.JetsamLimit > 0 ? Mathf.Clamp01((float)resident / device.JetsamLimit) : 0;
                Color barColor = resident > device.JetsamLimit ? HealthCriticalColor
                    : resident > device.WarningThreshold ? HealthWarningColor
                    : HealthGoodColor;

                var fillRect = new Rect(barRect.x, barRect.y, barRect.width * usageRatio, barRect.height);
                EditorGUI.DrawRect(fillRect, barColor);

                // Warning threshold marker
                float warnRatio = device.JetsamLimit > 0
                    ? Mathf.Clamp01((float)device.WarningThreshold / device.JetsamLimit) : 0;
                float markerX = barRect.x + barRect.width * warnRatio;
                EditorGUI.DrawRect(new Rect(markerX - 1, barRect.y, 2, barRect.height), HealthWarningColor);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Usage: {VmmapParser.FormatSize(resident)}", GUILayout.Width(160));
            GUILayout.Label($"Warning: {VmmapParser.FormatSize(device.WarningThreshold)}", _warningStyle, GUILayout.Width(160));
            GUILayout.Label($"Jetsam Limit: {VmmapParser.FormatSize(device.JetsamLimit)}", _errorStyle);
            EditorGUILayout.EndHorizontal();

            if (resident > device.JetsamLimit) {
                EditorGUILayout.HelpBox(
                    $"App WILL be killed by Jetsam on {device.Name}! " +
                    $"Over limit by {VmmapParser.FormatSize(resident - device.JetsamLimit)}.",
                    MessageType.Error);
            } else if (resident > device.WarningThreshold) {
                EditorGUILayout.HelpBox(
                    $"Approaching Jetsam limit on {device.Name}. " +
                    $"Headroom: {VmmapParser.FormatSize(device.JetsamLimit - resident)}.",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTopIssues() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Top Issues", EditorStyles.boldLabel);

            if (_report.Insights.Count == 0) {
                GUILayout.Label("No issues detected.", _successStyle);
            } else {
                int shown = Math.Min(_report.Insights.Count, 5);
                for (int i = 0; i < shown; i++) {
                    var insight = _report.Insights[i];
                    var color = GetSeverityColor(insight.Severity);

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    var prevContentColor = GUI.contentColor;
                    GUI.contentColor = color;
                    GUILayout.Label($"{GetSeverityIcon(insight.Severity)} {insight.Title}", EditorStyles.boldLabel);
                    GUI.contentColor = prevContentColor;

                    GUILayout.Label(insight.Description, EditorStyles.wordWrappedLabel);
                    GUILayout.Label($">> {insight.Recommendation}", _mutedStyle);
                    EditorGUILayout.EndVertical();
                }

                if (_report.Insights.Count > 5) {
                    GUILayout.Label($"  ... and {_report.Insights.Count - 5} more issues", _mutedStyle);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMetricRow(string label, long value) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            if (value > 1024) {
                GUILayout.Label(VmmapParser.FormatSize(value), EditorStyles.boldLabel, GUILayout.Width(120));
                GUILayout.Label($"({value:N0} bytes)", _mutedStyle);
            } else {
                GUILayout.Label(value.ToString("N0"), EditorStyles.boldLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMetricRowWithHealth(string label, long value, long warnThreshold, long critThreshold) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            if (value > 1024) {
                var style = new GUIStyle(EditorStyles.boldLabel);
                if (value > critThreshold)
                    style.normal.textColor = HealthCriticalColor;
                else if (value > warnThreshold)
                    style.normal.textColor = HealthWarningColor;
                GUILayout.Label(VmmapParser.FormatSize(value), style, GUILayout.Width(120));
                GUILayout.Label($"({value:N0} bytes)", _mutedStyle);
            } else {
                GUILayout.Label(value.ToString("N0"), EditorStyles.boldLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMetricRow(string label, int value) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            GUILayout.Label(value.ToString("N0"), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryBarChart() {
            var categories = _report.Summary.CategoryBreakdowns;
            if (categories.Count == 0) return;

            GUILayout.Label("Resident Memory by Category (vmmap)", EditorStyles.boldLabel);

            // Top 10 categories by size
            var topCategories = categories.OrderByDescending(c => c.Size).Take(10).ToList();
            long maxSize = topCategories.Count > 0 ? topCategories[0].Size : 1;

            foreach (var cat in topCategories) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(cat.Category, GUILayout.Width(180));

                var barRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.MinWidth(100));

                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);

                    float ratio = maxSize > 0 ? (float)cat.Size / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, BarUnityColor);
                }

                GUILayout.Label(VmmapParser.FormatSize(cat.Size), GUILayout.Width(80));
                GUILayout.Label($"{cat.Percentage:F1}%", _mutedStyle, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawOwnerBarChart() {
            var owners = _report.Summary.OwnerBreakdowns;
            if (owners.Count == 0) return;

            GUILayout.Label("Heap Memory by Owner", EditorStyles.boldLabel);

            var sorted = owners.OrderByDescending(kv => kv.Value).ToList();
            long maxSize = sorted.Count > 0 ? sorted[0].Value : 1;

            foreach (var kv in sorted) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(HeapParser.GetOwnerDisplayName(kv.Key), GUILayout.Width(130));

                var barRect = EditorGUILayout.GetControlRect(false, 16, GUILayout.MinWidth(100));

                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);

                    float ratio = maxSize > 0 ? (float)kv.Value / maxSize : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, GetOwnerColor(kv.Key));
                }

                GUILayout.Label(VmmapParser.FormatSize(kv.Value), GUILayout.Width(80));

                float pct = _report.Heap.TotalBytes > 0 ? (float)kv.Value / _report.Heap.TotalBytes * 100f : 0;
                GUILayout.Label($"{pct:F1}%", _mutedStyle, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawLeakSummaryBox() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Leak Detection", EditorStyles.boldLabel);

            if (_report.Leaks.TotalLeakCount == 0) {
                GUILayout.Label("No leaks detected.", _successStyle);
            } else {
                GUILayout.Label(
                    $"{_report.Leaks.TotalLeakCount} leaks detected " +
                    $"({VmmapParser.FormatSize(_report.Leaks.TotalLeakBytes)})",
                    _errorStyle);
            }
            EditorGUILayout.EndVertical();
        }
    }
}
