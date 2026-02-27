using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow {
        private Vector2 _insightScrollPos;
        private int _insightCategoryFilter; // 0 = All

        private static readonly string[] InsightCategoryFilterLabels = {
            "All", "Duplicates", "Unreferenced", "Static Retention",
            "Empty GameObjects", "Type Explosion", "Large Ownership"
        };

        private void DrawInsightsTab() {
            AnalyzerGuidance.DrawTabHeader("Automated issue detection: duplicate assets, unreferenced objects, large static retention, and more.");
            if (_report.InsightResult == null) {
                EditorGUILayout.HelpBox("No insight data available. Run analysis to generate insights.", MessageType.Info);
                return;
            }

            _insightScrollPos = EditorGUILayout.BeginScrollView(_insightScrollPos);

            DrawInsightSummaryBanner();
            GUILayout.Space(8);
            DrawInsightCategoryFilter();
            GUILayout.Space(4);
            DrawInsightList();
            GUILayout.Space(8);
            DrawDuplicateAssetsSection();
            GUILayout.Space(8);
            DrawUnreferencedAssetsSection();
            GUILayout.Space(8);
            DrawMemoryAttributionRanking();

            EditorGUILayout.EndScrollView();
        }

        private void DrawInsightSummaryBanner() {
            var result = _report.InsightResult;
            int critical = 0, warning = 0, info = 0;
            foreach (var insight in result.Insights) {
                switch (insight.Severity) {
                    case SnapshotInsightSeverity.Critical: critical++; break;
                    case SnapshotInsightSeverity.Warning: warning++; break;
                    case SnapshotInsightSeverity.Info: info++; break;
                }
            }

            Color bannerColor;
            if (critical > 0) bannerColor = new Color(1f, 0.3f, 0.3f, 0.15f);
            else if (warning > 0) bannerColor = new Color(1f, 0.8f, 0.2f, 0.15f);
            else bannerColor = new Color(0.4f, 0.8f, 0.4f, 0.15f);

            var rect = EditorGUILayout.GetControlRect(false, 36);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, bannerColor);

            string savings = result.TotalEstimatedSavings > 0
                ? $"Estimated savings: {VmmapParser.FormatSize(result.TotalEstimatedSavings)}"
                : "No significant savings identified";

            var labelText = $"  {result.Insights.Count} Issues Found  |  " +
                            $"Critical: {critical}  Warning: {warning}  Info: {info}  |  {savings}";
            GUI.Label(rect, labelText, _headerStyle);
        }

        private void DrawInsightCategoryFilter() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Category:", GUILayout.Width(60));
            _insightCategoryFilter = GUILayout.Toolbar(_insightCategoryFilter, InsightCategoryFilterLabels);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInsightList() {
            var insights = GetFilteredInsights();
            if (insights.Count == 0) {
                EditorGUILayout.HelpBox("No issues in this category.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"Issues ({insights.Count})", EditorStyles.boldLabel);

            foreach (var insight in insights) {
                DrawSingleInsight(insight);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSingleInsight(SnapshotInsight insight) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header: severity icon + title + savings
            EditorGUILayout.BeginHorizontal();
            var severityColor = GetSnapshotSeverityColor(insight.Severity);
            string icon = GetSnapshotSeverityIcon(insight.Severity);

            var prevColor = GUI.contentColor;
            GUI.contentColor = severityColor;
            GUILayout.Label($"{icon} {insight.Title}", EditorStyles.boldLabel);
            GUI.contentColor = prevColor;

            if (insight.EstimatedSavings > 0) {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"~{VmmapParser.FormatSize(insight.EstimatedSavings)}", _warningStyle, GUILayout.Width(100));
            }
            EditorGUILayout.EndHorizontal();

            // Description
            GUILayout.Label(insight.Description, EditorStyles.wordWrappedLabel);

            // Recommendation
            GUILayout.Label($">> {insight.Recommendation}", _mutedStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawDuplicateAssetsSection() {
            var duplicates = _report.InsightResult.DuplicateAssets;
            if (duplicates.Count == 0) return;
            if (_insightCategoryFilter != 0 && _insightCategoryFilter != 1) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            long totalWasted = duplicates.Sum(d => d.TotalWastedSize);
            GUILayout.Label($"Duplicate Assets ({duplicates.Count} groups, wasting {VmmapParser.FormatSize(totalWasted)})",
                EditorStyles.boldLabel);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Name", EditorStyles.toolbarButton, GUILayout.MinWidth(150));
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.Width(100));
            GUILayout.Label("Count", EditorStyles.toolbarButton, GUILayout.Width(50));
            GUILayout.Label("Each", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("Wasted", EditorStyles.toolbarButton, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            int shown = Mathf.Min(duplicates.Count, 30);
            for (int i = 0; i < shown; i++) {
                var dup = duplicates[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(dup.Name, GUILayout.MinWidth(150));
                GUILayout.Label(dup.NativeTypeName, _mutedStyle, GUILayout.Width(100));
                GUILayout.Label(dup.Count.ToString(), GUILayout.Width(50));
                GUILayout.Label(VmmapParser.FormatSize(dup.IndividualSize), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(dup.TotalWastedSize), _warningStyle, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }

            if (duplicates.Count > 30) {
                GUILayout.Label($"  ... and {duplicates.Count - 30} more groups", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnreferencedAssetsSection() {
            var unreferenced = _report.InsightResult.UnreferencedAssets;
            if (unreferenced.Count == 0) return;
            if (_insightCategoryFilter != 0 && _insightCategoryFilter != 2) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            long totalSize = unreferenced.Sum(u => u.Size);
            GUILayout.Label($"Unreferenced Native Assets ({unreferenced.Count}, total {VmmapParser.FormatSize(totalSize)})",
                EditorStyles.boldLabel);

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Name", EditorStyles.toolbarButton, GUILayout.MinWidth(150));
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.Width(100));
            GUILayout.Label("Size", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("Reason", EditorStyles.toolbarButton, GUILayout.MinWidth(150));
            EditorGUILayout.EndHorizontal();

            int shown = Mathf.Min(unreferenced.Count, 30);
            for (int i = 0; i < shown; i++) {
                var asset = unreferenced[i];
                string typeName = "";
                if (asset.NativeObjectIndex >= 0 && asset.NativeObjectIndex < _report.NativeObjects.Count)
                    typeName = _report.NativeObjects[asset.NativeObjectIndex].NativeTypeName;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(asset.Name, GUILayout.MinWidth(150));
                GUILayout.Label(typeName, _mutedStyle, GUILayout.Width(100));
                GUILayout.Label(VmmapParser.FormatSize(asset.Size), GUILayout.Width(80));
                GUILayout.Label(asset.Reason, _mutedStyle, GUILayout.MinWidth(150));
                EditorGUILayout.EndHorizontal();
            }

            if (unreferenced.Count > 30) {
                GUILayout.Label($"  ... and {unreferenced.Count - 30} more assets", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMemoryAttributionRanking() {
            if (_report.LinkResult == null || _report.CrawlerResult == null) return;
            if (_report.LinkResult.NativeRetainedByManaged.Count == 0) return;
            if (_insightCategoryFilter != 0 && _insightCategoryFilter != 5) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Top 20 Managed Objects by Total Memory (own + native)", EditorStyles.boldLabel);

            // Build ranking: managed size + owned native size
            var ranking = new List<(int managedIdx, long ownSize, long nativeSize, long total, string typeName)>();
            foreach (var kv in _report.LinkResult.NativeRetainedByManaged) {
                int managedIdx = kv.Key;
                long nativeSize = kv.Value;
                if (managedIdx < 0 || managedIdx >= _report.CrawlerResult.Objects.Count) continue;

                var obj = _report.CrawlerResult.Objects[managedIdx];
                long ownSize = obj.Size;
                long retained = 0;
                if (_report.RetainedSizes != null && _report.RetainedSizes.TryGetValue(managedIdx, out long retSize))
                    retained = retSize;

                long total = retained + nativeSize;
                string typeName = "Unknown";
                if (obj.TypeIndex >= 0 && obj.TypeIndex < _report.Types.Length)
                    typeName = _report.Types[obj.TypeIndex].Name;

                ranking.Add((managedIdx, ownSize, nativeSize, total, typeName));
            }

            ranking.Sort((a, b) => b.total.CompareTo(a.total));

            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.MinWidth(150));
            GUILayout.Label("Own Size", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("Native Owned", EditorStyles.toolbarButton, GUILayout.Width(90));
            GUILayout.Label("Total", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("", GUILayout.MinWidth(60));
            EditorGUILayout.EndHorizontal();

            long maxTotal = ranking.Count > 0 ? ranking[0].total : 1;
            int shown = Mathf.Min(ranking.Count, 20);

            for (int i = 0; i < shown; i++) {
                var item = ranking[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(item.typeName, GUILayout.MinWidth(150));
                GUILayout.Label(VmmapParser.FormatSize(item.ownSize), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(item.nativeSize), _warningStyle, GUILayout.Width(90));
                GUILayout.Label(VmmapParser.FormatSize(item.total), EditorStyles.boldLabel, GUILayout.Width(80));

                // Bar
                var barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.MinWidth(60));
                if (Event.current.type == EventType.Repaint) {
                    EditorGUI.DrawRect(barRect, BarBgColor);
                    float ratio = maxTotal > 0 ? (float)item.total / maxTotal : 0;
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, BarFillColor);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private List<SnapshotInsight> GetFilteredInsights() {
            if (_report.InsightResult == null) return new List<SnapshotInsight>();
            if (_insightCategoryFilter == 0) return _report.InsightResult.Insights;

            var targetCategory = _insightCategoryFilter switch {
                1 => SnapshotInsightCategory.DuplicateAsset,
                2 => SnapshotInsightCategory.UnreferencedNative,
                3 => SnapshotInsightCategory.LargeStaticRetention,
                4 => SnapshotInsightCategory.EmptyGameObjects,
                5 => SnapshotInsightCategory.TypeExplosion,
                6 => SnapshotInsightCategory.LargeNativeOwnership,
                _ => SnapshotInsightCategory.DuplicateAsset,
            };

            return _report.InsightResult.Insights
                .Where(i => i.Category == targetCategory)
                .ToList();
        }

        private static Color GetSnapshotSeverityColor(SnapshotInsightSeverity severity) {
            return severity switch {
                SnapshotInsightSeverity.Critical => new Color(1f, 0.3f, 0.3f),
                SnapshotInsightSeverity.Warning => new Color(1f, 0.8f, 0.2f),
                SnapshotInsightSeverity.Info => new Color(0.6f, 0.8f, 1f),
                _ => Color.white,
            };
        }

        private static string GetSnapshotSeverityIcon(SnapshotInsightSeverity severity) {
            return severity switch {
                SnapshotInsightSeverity.Critical => "[!!]",
                SnapshotInsightSeverity.Warning => "[!]",
                SnapshotInsightSeverity.Info => "[i]",
                _ => "[-]",
            };
        }
    }
}
