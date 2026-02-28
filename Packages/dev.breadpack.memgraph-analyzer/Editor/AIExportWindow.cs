using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tools {
    /// <summary>
    /// Current view state passed from the main analyzer window.
    /// </summary>
    internal struct AIExportViewState {
        public List<HeapAllocation> FilteredHeapRows;
        public string HeapFilter;
        public int HeapOwnerFilter;
        public List<TracedAllocation> FilteredTraceRows;
        public AllocationCategory TraceFilterCat;
        public Controllability TraceFilterCtrl;
        public AssetType TraceFilterAsset;
    }

    /// <summary>
    /// Multi-document AI Export window.
    /// Auto-generates analysis-first documents with no manual settings.
    /// </summary>
    internal class AIExportWindow : EditorWindow {
        private MemGraphReport _report;
        private MemGraphDiffResult _diffResult;
        private AIExportViewState _viewState;

        private AIDocumentInfo[] _documents;
        private bool[] _selected;
        private int _previewIndex;
        private Vector2 _listScroll;
        private Vector2 _previewScroll;

        public static void Show(MemGraphReport report, int selectedTab,
            MemGraphDiffResult diffResult, AIExportViewState viewState) {
            var window = GetWindow<AIExportWindow>(true, "AI Export", true);
            window._report = report;
            window._diffResult = diffResult;
            window._viewState = viewState;
            window.RebuildDocuments();
            window.minSize = new Vector2(620, 520);
            window.Show();
        }

        private void RebuildDocuments() {
            var docs = new List<AIDocumentInfo>();

            // Build each detail document
            docs.Add(BuildDoc(AIDocumentType.Optimizations, () =>
                AIExportMarkdownBuilder.BuildOptimizations(_report),
                BuildOptHighlight));

            docs.Add(BuildDoc(AIDocumentType.Heap, () =>
                AIExportMarkdownBuilder.BuildHeapAnalysis(
                    _report, _viewState.FilteredHeapRows,
                    _viewState.HeapFilter, _viewState.HeapOwnerFilter),
                BuildHeapHighlight));

            docs.Add(BuildDoc(AIDocumentType.Leaks, () =>
                AIExportMarkdownBuilder.BuildLeakAnalysis(_report),
                BuildLeakHighlight));

            docs.Add(BuildDoc(AIDocumentType.AllocationTrace, () =>
                AIExportMarkdownBuilder.BuildAllocationTraceAnalysis(
                    _report, _viewState.FilteredTraceRows,
                    _viewState.TraceFilterCat, _viewState.TraceFilterCtrl,
                    _viewState.TraceFilterAsset),
                BuildTraceHighlight));

            docs.Add(BuildDoc(AIDocumentType.Unity, () =>
                AIExportMarkdownBuilder.BuildUnityAnalysis(_report),
                BuildUnityHighlight));

            docs.Add(BuildDoc(AIDocumentType.Vmmap, () =>
                AIExportMarkdownBuilder.BuildVmmapAnalysis(_report),
                BuildVmmapHighlight));

            docs.Add(BuildDoc(AIDocumentType.Comparison, () =>
                _diffResult != null
                    ? AIExportMarkdownBuilder.BuildComparisonAnalysis(_report, _diffResult)
                    : "",
                BuildComparisonHighlight));

            // Build Guide last (needs doc inventory)
            var guideDoc = new AIDocumentInfo {
                Type = AIDocumentType.Guide,
                FileName = AIDocumentInfo.GetFileName(AIDocumentType.Guide),
                DisplayName = AIDocumentInfo.GetDisplayName(AIDocumentType.Guide),
                IsAvailable = true,
            };
            // Insert Guide at position 0
            docs.Insert(0, guideDoc);

            _documents = docs.ToArray();

            // Build guide markdown with full document list
            guideDoc.CachedMarkdown = AIExportGuideBuilder.Build(_report, _documents, _diffResult);
            guideDoc.EstimatedTokens = EstimateTokens(guideDoc.CachedMarkdown);
            guideDoc.Highlight = BuildGuideHighlight();

            // Select all available by default
            _selected = new bool[_documents.Length];
            for (int i = 0; i < _documents.Length; i++)
                _selected[i] = _documents[i].IsAvailable;
            _previewIndex = 0;
        }

        private AIDocumentInfo BuildDoc(AIDocumentType type, Func<string> builder,
            Func<string> highlightBuilder) {
            var doc = new AIDocumentInfo {
                Type = type,
                FileName = AIDocumentInfo.GetFileName(type),
                DisplayName = AIDocumentInfo.GetDisplayName(type),
            };
            doc.CachedMarkdown = builder();
            doc.IsAvailable = !string.IsNullOrEmpty(doc.CachedMarkdown);
            doc.EstimatedTokens = EstimateTokens(doc.CachedMarkdown);
            doc.Highlight = doc.IsAvailable ? highlightBuilder() : "N/A";
            return doc;
        }

        #region Highlight Builders

        private string BuildGuideHighlight() {
            int issueCount = _report.Insights?.Count ?? 0;
            int docCount = 0;
            foreach (var d in _documents)
                if (d.IsAvailable && d.Type != AIDocumentType.Guide) docCount++;
            return $"{issueCount} issues, {docCount} detail docs";
        }

        private string BuildOptHighlight() {
            var opts = _report.Optimizations;
            if (opts == null) return "N/A";
            return $"{opts.Recommendations.Count} recommendations, ~{AIExportMarkdownBuilder.Fmt(opts.TotalEstimatedSavings)} savings";
        }

        private string BuildHeapHighlight() {
            var allocs = _viewState.FilteredHeapRows ?? _report.Heap.Allocations;
            int largeCount = 0;
            int poolCount = 0;
            foreach (var a in allocs) {
                if (a.AverageSize > 10L * 1024 * 1024) largeCount++;
                if (a.Count > 1000 && a.AverageSize < 1024 && a.TotalBytes > 1024 * 1024) poolCount++;
            }
            var ownerCount = new HashSet<MemoryOwner>();
            foreach (var a in allocs) ownerCount.Add(a.Owner);
            return $"{largeCount} large, {poolCount} poolable, {ownerCount.Count} owners";
        }

        private string BuildLeakHighlight() {
            var leaks = _report.Leaks;
            if (leaks == null || leaks.TotalLeakCount == 0) return "No leaks";
            var groups = LeaksParser.GroupLeaks(leaks);
            return $"{leaks.TotalLeakCount} leaks in {groups.Count} groups, {AIExportMarkdownBuilder.Fmt(leaks.TotalLeakBytes)}";
        }

        private string BuildTraceHighlight() {
            var trace = _report.AllocationTrace;
            if (trace == null || trace.Allocations.Count == 0) return "N/A";
            var source = _viewState.FilteredTraceRows ?? trace.Allocations;
            int userCount = 0;
            foreach (var a in source)
                if (a.Controllability == Controllability.UserControllable) userCount++;
            return $"{source.Count} allocations, {userCount} user-controllable";
        }

        private string BuildUnityHighlight() {
            long tracked = _report.Summary.TrackedByUnity;
            long untracked = _report.Summary.UntrackedByUnity;
            long total = _report.Heap.TotalBytes;
            int pluginCount = _report.Summary.PluginBreakdowns.Count;
            string pct = total > 0 ? AIExportMarkdownBuilder.Pct(untracked, total) : "?";
            return $"{pct} untracked, {pluginCount} plugins";
        }

        private string BuildVmmapHighlight() {
            int regionCount = _report.Vmmap.Summary.Count;
            return $"{regionCount} region types";
        }

        private string BuildComparisonHighlight() {
            if (_diffResult == null) return "N/A";
            var d = _diffResult.Overall;
            string direction = d.FootprintDelta > 0 ? "increased" : d.FootprintDelta < 0 ? "decreased" : "unchanged";
            return $"Footprint {direction} by {AIExportMarkdownBuilder.Fmt(Math.Abs(d.FootprintDelta))}";
        }

        #endregion

        private void OnGUI() {
            if (_report == null) {
                EditorGUILayout.HelpBox("No report data. Open AI Export from the analyzer toolbar.", MessageType.Warning);
                return;
            }

            if (_documents == null) RebuildDocuments();

            // Total token estimate
            int totalTokens = 0;
            for (int i = 0; i < _documents.Length; i++)
                if (_selected[i]) totalTokens += _documents[i].EstimatedTokens;
            EditorGUILayout.LabelField($"Selected: ~{totalTokens:N0} tokens", EditorStyles.boldLabel);
            GUILayout.Space(2);

            // Split: left = document list, right = preview
            EditorGUILayout.BeginHorizontal();

            // Document list (left, ~40%)
            EditorGUILayout.BeginVertical(GUILayout.Width(240));
            DrawDocumentList();
            EditorGUILayout.EndVertical();

            // Preview (right, ~60%)
            EditorGUILayout.BeginVertical();
            DrawPreview();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            DrawButtons();
        }

        private void DrawDocumentList() {
            EditorGUILayout.LabelField("Documents", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < _documents.Length; i++) {
                var doc = _documents[i];
                GUI.enabled = doc.IsAvailable;

                EditorGUILayout.BeginHorizontal(
                    i == _previewIndex ? "selectionRect" : EditorStyles.helpBox);

                _selected[i] = EditorGUILayout.Toggle(_selected[i], GUILayout.Width(16));

                EditorGUILayout.BeginVertical();
                if (GUILayout.Button(doc.FileName, EditorStyles.linkLabel)) {
                    _previewIndex = i;
                }
                var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                EditorGUILayout.LabelField($"~{doc.EstimatedTokens:N0} tok | {doc.Highlight}", style);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview() {
            if (_previewIndex < 0 || _previewIndex >= _documents.Length) return;
            var doc = _documents[_previewIndex];

            EditorGUILayout.LabelField($"Preview: {doc.FileName}", EditorStyles.boldLabel);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));

            string content = doc.CachedMarkdown ?? "(empty)";
            EditorGUILayout.SelectableLabel(content, EditorStyles.textArea,
                GUILayout.ExpandHeight(true),
                GUILayout.MinHeight(Mathf.Max(200, content.Split('\n').Length * 14)));

            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons() {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy Guide", GUILayout.Height(28))) {
                var guide = Array.Find(_documents, d => d.Type == AIDocumentType.Guide);
                if (guide != null) {
                    GUIUtility.systemCopyBuffer = guide.CachedMarkdown;
                    Debug.Log($"[MemGraphAnalyzer] Guide copied to clipboard (~{guide.EstimatedTokens:N0} tokens).");
                    ShowNotification(new GUIContent("Guide copied!"));
                }
            }

            if (GUILayout.Button("Copy Selected", GUILayout.Height(28))) {
                var sb = new StringBuilder();
                for (int i = 0; i < _documents.Length; i++) {
                    if (!_selected[i] || !_documents[i].IsAvailable) continue;
                    if (sb.Length > 0) sb.AppendLine("\n---\n");
                    sb.Append(_documents[i].CachedMarkdown);
                }
                GUIUtility.systemCopyBuffer = sb.ToString();
                int tokens = EstimateTokens(sb.ToString());
                Debug.Log($"[MemGraphAnalyzer] Selected documents copied to clipboard (~{tokens:N0} tokens).");
                ShowNotification(new GUIContent("Selected docs copied!"));
            }

            if (GUILayout.Button("Save Folder", GUILayout.Height(28))) {
                SaveToFolder();
            }

            if (GUILayout.Button("Close", GUILayout.Height(28))) {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SaveToFolder() {
            var defaultFolder = $"memgraph_ai_{_report.AnalysisTime:yyyyMMdd_HHmmss}";
            var basePath = EditorUtility.SaveFolderPanel("Save AI Export Folder", "", defaultFolder);
            if (string.IsNullOrEmpty(basePath)) return;

            int saved = 0;
            for (int i = 0; i < _documents.Length; i++) {
                if (!_selected[i] || !_documents[i].IsAvailable) continue;
                var filePath = Path.Combine(basePath, _documents[i].FileName);
                File.WriteAllText(filePath, _documents[i].CachedMarkdown);
                saved++;
            }

            Debug.Log($"[MemGraphAnalyzer] Saved {saved} documents to: {basePath}");
            EditorUtility.RevealInFinder(basePath);
            ShowNotification(new GUIContent($"Saved {saved} files!"));
        }

        private static int EstimateTokens(string text) {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length / 4;
        }
    }
}
