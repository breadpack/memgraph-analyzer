using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _comparisonScrollPos;
        private DiffFilterMode _diffFilterMode = DiffFilterMode.All;
        private DiffSortMode _diffSortMode = DiffSortMode.AbsoluteDelta;
        private string _diffHeapFilter = "";
        private int _diffOwnerFilter = -1; // -1 = All
        private bool _showVmmapDiff = true;
        private bool _showOwnerDiff = true;
        private bool _showPluginDiff = true;
        private bool _showLeakDiff = true;

        private static readonly Color DiffIncreaseColor = new(1f, 0.35f, 0.35f, 1f);
        private static readonly Color DiffDecreaseColor = new(0.35f, 0.85f, 0.35f, 1f);
        private static readonly Color DiffNewColor = new(1f, 0.65f, 0.2f, 1f);
        private static readonly Color DiffRemovedColor = new(0.6f, 0.6f, 0.6f, 1f);

        private void DrawComparisonTab() {
            // File selection toolbar
            DrawComparisonToolbar();

            // Progress / error
            if (_comparisonPhase != ComparisonPhase.Idle && _comparisonPhase != ComparisonPhase.Complete) {
                DrawComparisonProgress();
                return;
            }

            if (_comparisonPhase == ComparisonPhase.Error) {
                EditorGUILayout.HelpBox(_comparisonError ?? "Unknown error", MessageType.Error);
                return;
            }

            if (_diffResult == null) {
                EditorGUILayout.HelpBox(
                    "Select two .memgraph files and click Compare to see memory differences.",
                    MessageType.Info);
                return;
            }

            _comparisonScrollPos = EditorGUILayout.BeginScrollView(_comparisonScrollPos);

            DrawOverallDiff();
            GUILayout.Space(8);
            DrawHeapDiff();
            GUILayout.Space(8);
            DrawVmmapDiffSection();
            GUILayout.Space(8);
            DrawOwnerDiffSection();
            GUILayout.Space(8);
            DrawPluginDiffSection();
            GUILayout.Space(8);
            DrawLeakDiffSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawComparisonToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Baseline:", GUILayout.Width(55));
            var baseDisplay = string.IsNullOrEmpty(_baselinePath) ? "(none)" : Path.GetFileName(_baselinePath);
            GUILayout.Label(baseDisplay, EditorStyles.toolbarButton, GUILayout.MinWidth(80));
            if (GUILayout.Button("...", EditorStyles.toolbarButton, GUILayout.Width(24))) {
                var path = EditorUtility.OpenFilePanel("Select baseline .memgraph", "", "memgraph");
                if (!string.IsNullOrEmpty(path)) _baselinePath = path;
            }

            GUILayout.Space(8);

            GUILayout.Label("Current:", GUILayout.Width(50));
            var curDisplay = string.IsNullOrEmpty(_currentPath) ? "(none)" : Path.GetFileName(_currentPath);
            GUILayout.Label(curDisplay, EditorStyles.toolbarButton, GUILayout.MinWidth(80));
            if (GUILayout.Button("...", EditorStyles.toolbarButton, GUILayout.Width(24))) {
                var path = EditorUtility.OpenFilePanel("Select current .memgraph", "", "memgraph");
                if (!string.IsNullOrEmpty(path)) _currentPath = path;
            }

            GUILayout.Space(8);

            bool isRunning = _comparisonPhase != ComparisonPhase.Idle &&
                             _comparisonPhase != ComparisonPhase.Complete &&
                             _comparisonPhase != ComparisonPhase.Error;
            GUI.enabled = !isRunning && !string.IsNullOrEmpty(_baselinePath) && !string.IsNullOrEmpty(_currentPath);
            if (GUILayout.Button("Compare", EditorStyles.toolbarButton, GUILayout.Width(60))) {
                StartComparison();
            }
            GUI.enabled = !isRunning;
            if (GUILayout.Button("Swap", EditorStyles.toolbarButton, GUILayout.Width(40))) {
                (_baselinePath, _currentPath) = (_currentPath, _baselinePath);
                (_baselineReport, _currentReport) = (_currentReport, _baselineReport);
                if (_baselineReport != null && _currentReport != null)
                    _diffResult = MemGraphDiffCalculator.Calculate(_baselineReport, _currentReport);
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (_diffResult != null) {
                if (GUILayout.Button("Export Diff", EditorStyles.toolbarButton, GUILayout.Width(75))) {
                    ExportDiffReport();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawComparisonProgress() {
            string label = _comparisonPhase switch {
                ComparisonPhase.AnalyzingBaseline => "Analyzing baseline...",
                ComparisonPhase.AnalyzingCurrent => "Analyzing current...",
                ComparisonPhase.Calculating => "Calculating diff...",
                _ => "Processing...",
            };
            float progress = _comparisonPhase switch {
                ComparisonPhase.AnalyzingBaseline => 0.3f,
                ComparisonPhase.AnalyzingCurrent => 0.6f,
                ComparisonPhase.Calculating => 0.9f,
                _ => 0.5f,
            };
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress, label);
        }

        private void DrawOverallDiff() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Overall Comparison", _headerStyle);

            var d = _diffResult.Overall;
            DrawDiffMetricRow("Phys Footprint", d.BaselineFootprint, d.CurrentFootprint, d.FootprintDelta, d.FootprintDeltaPercent);
            DrawDiffMetricRow("Heap Total", d.BaselineHeapTotal, d.CurrentHeapTotal, d.HeapDelta, d.HeapDeltaPercent);
            DrawDiffMetricRow("Resident", d.BaselineResident, d.CurrentResident, d.ResidentDelta, d.ResidentDeltaPercent);
            DrawDiffMetricRow("Dirty", d.BaselineDirty, d.CurrentDirty, d.DirtyDelta, d.DirtyDeltaPercent);
            DrawDiffMetricRow("Virtual", d.BaselineVirtual, d.CurrentVirtual, d.VirtualDelta, d.VirtualDeltaPercent);

            // Leak delta
            var ld = _diffResult.Leaks;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Leak Count", GUILayout.Width(120));
            GUILayout.Label(ld.BaselineCount.ToString(), GUILayout.Width(100));
            GUILayout.Label(ld.CurrentCount.ToString(), GUILayout.Width(100));
            DrawDeltaLabel(ld.CountDelta, false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawDiffMetricRow(string label, long baseline, long current, long delta, float pct) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            GUILayout.Label(VmmapParser.FormatSize(baseline), GUILayout.Width(100));
            GUILayout.Label(VmmapParser.FormatSize(current), GUILayout.Width(100));
            DrawDeltaLabel(delta, true);
            DrawPercentLabel(pct);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDeltaLabel(long delta, bool formatAsSize) {
            Color c = delta > 0 ? DiffIncreaseColor : delta < 0 ? DiffDecreaseColor : Color.gray;
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
            string prefix = delta > 0 ? "+" : "";
            string text = formatAsSize ? $"{prefix}{VmmapParser.FormatSize(delta)}" : $"{prefix}{delta:N0}";
            GUILayout.Label(text, style, GUILayout.Width(100));
        }

        private static void DrawPercentLabel(float pct) {
            Color c = pct > 0 ? DiffIncreaseColor : pct < 0 ? DiffDecreaseColor : Color.gray;
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
            string prefix = pct > 0 ? "+" : "";
            GUILayout.Label($"{prefix}{pct:F1}%", style, GUILayout.Width(70));
        }

        private void DrawHeapDiff() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Heap Allocation Diff", _headerStyle);

            var heap = _diffResult.Heap;
            EditorGUILayout.BeginHorizontal();
            var prevColor = GUI.contentColor;
            GUI.contentColor = DiffNewColor;
            GUILayout.Label($"New: {heap.NewClassCount}", GUILayout.Width(80));
            GUI.contentColor = DiffRemovedColor;
            GUILayout.Label($"Removed: {heap.RemovedClassCount}", GUILayout.Width(100));
            GUI.contentColor = DiffIncreaseColor;
            GUILayout.Label($"Increased: {heap.IncreasedClassCount}", GUILayout.Width(110));
            GUI.contentColor = DiffDecreaseColor;
            GUILayout.Label($"Decreased: {heap.DecreasedClassCount}", GUILayout.Width(110));
            GUI.contentColor = prevColor;
            GUILayout.Label($"Unchanged: {heap.UnchangedClassCount}", _mutedStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Total Growth: +{VmmapParser.FormatSize(heap.TotalGrowth)}", _errorStyle, GUILayout.Width(200));
            GUILayout.Label($"Total Shrink: -{VmmapParser.FormatSize(heap.TotalShrink)}", _successStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Filters
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _diffFilterMode = (DiffFilterMode)EditorGUILayout.EnumPopup(_diffFilterMode, GUILayout.Width(110));
            GUILayout.Space(8);
            _diffHeapFilter = EditorGUILayout.TextField(_diffHeapFilter, GUILayout.MinWidth(120));
            GUILayout.Space(8);
            GUILayout.Label("Sort:", GUILayout.Width(30));
            _diffSortMode = (DiffSortMode)EditorGUILayout.EnumPopup(_diffSortMode, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Class Name", EditorStyles.boldLabel, GUILayout.MinWidth(150));
            GUILayout.Label("Baseline", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label("Current", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label("Delta", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label("%", EditorStyles.boldLabel, GUILayout.Width(60));
            GUILayout.Label("Owner", EditorStyles.boldLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            // Filter and sort
            var filtered = heap.Allocations.Where(a => MatchesDiffFilter(a)).ToList();
            switch (_diffSortMode) {
                case DiffSortMode.AbsoluteDelta:
                    filtered.Sort((a, b) => Math.Abs(b.BytesDelta).CompareTo(Math.Abs(a.BytesDelta)));
                    break;
                case DiffSortMode.PercentageDelta:
                    filtered.Sort((a, b) => Math.Abs(b.BytesDeltaPercent).CompareTo(Math.Abs(a.BytesDeltaPercent)));
                    break;
                case DiffSortMode.TotalSize:
                    filtered.Sort((a, b) => Math.Max(b.CurrentBytes, b.BaselineBytes)
                        .CompareTo(Math.Max(a.CurrentBytes, a.BaselineBytes)));
                    break;
                case DiffSortMode.ClassName:
                    filtered.Sort((a, b) => string.Compare(a.ClassName, b.ClassName, StringComparison.OrdinalIgnoreCase));
                    break;
            }

            int shown = Math.Min(filtered.Count, 100);
            for (int i = 0; i < shown; i++) {
                DrawHeapDiffRow(filtered[i]);
            }

            if (filtered.Count > 100) {
                GUILayout.Label($"  ... {filtered.Count - 100} more entries (use filter to narrow)", _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private bool MatchesDiffFilter(HeapAllocationDiff alloc) {
            // Direction filter
            switch (_diffFilterMode) {
                case DiffFilterMode.GrowthOnly when alloc.Direction != DiffDirection.Increased && alloc.Direction != DiffDirection.New:
                case DiffFilterMode.ShrinkOnly when alloc.Direction != DiffDirection.Decreased:
                case DiffFilterMode.NewOnly when alloc.Direction != DiffDirection.New:
                case DiffFilterMode.RemovedOnly when alloc.Direction != DiffDirection.Removed:
                    return false;
            }

            // Text filter
            if (!string.IsNullOrEmpty(_diffHeapFilter) &&
                (alloc.ClassName == null ||
                 alloc.ClassName.IndexOf(_diffHeapFilter, StringComparison.OrdinalIgnoreCase) < 0))
                return false;

            // Owner filter
            if (_diffOwnerFilter >= 0 && (int)alloc.Owner != _diffOwnerFilter)
                return false;

            return true;
        }

        private void DrawHeapDiffRow(HeapAllocationDiff alloc) {
            Color rowColor = alloc.Direction switch {
                DiffDirection.New => DiffNewColor,
                DiffDirection.Removed => DiffRemovedColor,
                DiffDirection.Increased => DiffIncreaseColor,
                DiffDirection.Decreased => DiffDecreaseColor,
                _ => Color.gray,
            };

            EditorGUILayout.BeginHorizontal();

            var nameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = rowColor } };
            GUILayout.Label(alloc.ClassName ?? "(unknown)", nameStyle, GUILayout.MinWidth(150));
            GUILayout.Label(VmmapParser.FormatSize(alloc.BaselineBytes), GUILayout.Width(90));
            GUILayout.Label(VmmapParser.FormatSize(alloc.CurrentBytes), GUILayout.Width(90));

            string prefix = alloc.BytesDelta > 0 ? "+" : "";
            var deltaStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = rowColor } };
            GUILayout.Label($"{prefix}{VmmapParser.FormatSize(alloc.BytesDelta)}", deltaStyle, GUILayout.Width(90));

            string pctPrefix = alloc.BytesDeltaPercent > 0 ? "+" : "";
            GUILayout.Label($"{pctPrefix}{alloc.BytesDeltaPercent:F1}%", nameStyle, GUILayout.Width(60));

            GUILayout.Label(HeapParser.GetOwnerDisplayName(alloc.Owner), _mutedStyle, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawVmmapDiffSection() {
            _showVmmapDiff = EditorGUILayout.Foldout(_showVmmapDiff, "Virtual Memory Region Diff", true);
            if (!_showVmmapDiff) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Region", EditorStyles.boldLabel, GUILayout.MinWidth(150));
            GUILayout.Label("Base Res.", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label("Cur Res.", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label("Delta", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label("%", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            var sorted = _diffResult.Vmmap.Regions
                .OrderByDescending(r => Math.Abs(r.ResidentDelta)).ToList();

            foreach (var region in sorted) {
                if (region.ResidentDelta == 0 && region.BaselineResident == 0 && region.CurrentResident == 0)
                    continue;

                Color c = region.Direction switch {
                    DiffDirection.Increased => DiffIncreaseColor,
                    DiffDirection.Decreased => DiffDecreaseColor,
                    DiffDirection.New => DiffNewColor,
                    DiffDirection.Removed => DiffRemovedColor,
                    _ => Color.gray,
                };
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(region.RegionType, GUILayout.MinWidth(150));
                GUILayout.Label(VmmapParser.FormatSize(region.BaselineResident), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(region.CurrentResident), GUILayout.Width(80));
                string prefix = region.ResidentDelta > 0 ? "+" : "";
                GUILayout.Label($"{prefix}{VmmapParser.FormatSize(region.ResidentDelta)}", style, GUILayout.Width(80));
                string pctPre = region.ResidentDeltaPercent > 0 ? "+" : "";
                GUILayout.Label($"{pctPre}{region.ResidentDeltaPercent:F1}%", style, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawOwnerDiffSection() {
            _showOwnerDiff = EditorGUILayout.Foldout(_showOwnerDiff, "Owner Breakdown Diff", true);
            if (!_showOwnerDiff) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foreach (var d in _diffResult.OwnerDiffs.OrderByDescending(x => Math.Abs(x.BytesDelta))) {
                Color c = d.Direction switch {
                    DiffDirection.Increased => DiffIncreaseColor,
                    DiffDirection.Decreased => DiffDecreaseColor,
                    _ => Color.gray,
                };
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(HeapParser.GetOwnerDisplayName(d.Key), GUILayout.Width(120));
                GUILayout.Label(VmmapParser.FormatSize(d.BaselineBytes), GUILayout.Width(90));
                GUILayout.Label(VmmapParser.FormatSize(d.CurrentBytes), GUILayout.Width(90));
                string prefix = d.BytesDelta > 0 ? "+" : "";
                GUILayout.Label($"{prefix}{VmmapParser.FormatSize(d.BytesDelta)}", style, GUILayout.Width(100));
                string pctPre = d.BytesDeltaPercent > 0 ? "+" : "";
                GUILayout.Label($"{pctPre}{d.BytesDeltaPercent:F1}%", style, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPluginDiffSection() {
            if (_diffResult.PluginDiffs.Count == 0) return;

            _showPluginDiff = EditorGUILayout.Foldout(_showPluginDiff, "Plugin Breakdown Diff", true);
            if (!_showPluginDiff) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foreach (var d in _diffResult.PluginDiffs.OrderByDescending(x => Math.Abs(x.BytesDelta))) {
                Color c = d.Direction switch {
                    DiffDirection.Increased => DiffIncreaseColor,
                    DiffDirection.Decreased => DiffDecreaseColor,
                    _ => Color.gray,
                };
                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(d.Key, GUILayout.Width(120));
                GUILayout.Label(VmmapParser.FormatSize(d.BaselineBytes), GUILayout.Width(90));
                GUILayout.Label(VmmapParser.FormatSize(d.CurrentBytes), GUILayout.Width(90));
                string prefix = d.BytesDelta > 0 ? "+" : "";
                GUILayout.Label($"{prefix}{VmmapParser.FormatSize(d.BytesDelta)}", style, GUILayout.Width(100));
                string pctPre = d.BytesDeltaPercent > 0 ? "+" : "";
                GUILayout.Label($"{pctPre}{d.BytesDeltaPercent:F1}%", style, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLeakDiffSection() {
            _showLeakDiff = EditorGUILayout.Foldout(_showLeakDiff, "Leak Diff", true);
            if (!_showLeakDiff) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var ld = _diffResult.Leaks;
            DrawDiffMetricRow("Leak Count", ld.BaselineCount, ld.CurrentCount, ld.CountDelta);
            DrawDiffMetricRow("Leak Bytes", ld.BaselineBytes, ld.CurrentBytes, ld.BytesDelta);

            EditorGUILayout.EndVertical();
        }

        private void DrawDiffMetricRow(string label, long baseline, long current, long delta) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            GUILayout.Label(VmmapParser.FormatSize(baseline), GUILayout.Width(100));
            GUILayout.Label(VmmapParser.FormatSize(current), GUILayout.Width(100));
            DrawDeltaLabel(delta, true);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDiffMetricRow(string label, int baseline, int current, int delta) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            GUILayout.Label(baseline.ToString("N0"), GUILayout.Width(100));
            GUILayout.Label(current.ToString("N0"), GUILayout.Width(100));
            Color c = delta > 0 ? DiffIncreaseColor : delta < 0 ? DiffDecreaseColor : Color.gray;
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
            string prefix = delta > 0 ? "+" : "";
            GUILayout.Label($"{prefix}{delta:N0}", style, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }
    }
}
