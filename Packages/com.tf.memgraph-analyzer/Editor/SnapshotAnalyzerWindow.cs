using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class SnapshotAnalyzerWindow : EditorWindow {
        [MenuItem("Tools/Snapshot Analyzer")]
        public static void ShowWindow() {
            GetWindow<SnapshotAnalyzerWindow>("Snapshot Analyzer");
        }

        private string _snapPath = "";
        private SnapshotReport _report;
        private int _selectedTab;
        private bool _stylesInitialized;

        private GUIStyle _headerStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _successStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _mutedStyle;

        private static readonly GUIContent[] TabLabels = {
            new("Summary", "Memory totals, classification breakdown, top types, and detected issues"),
            new("Assemblies", "Browse assemblies by classification, namespace, and type"),
            new("Native Objects", "Native Unity objects (textures, meshes, etc.) sorted by size"),
            new("References", "Search managed objects and trace reference chains to GC roots"),
            new("Insights", "Duplicate assets, unreferenced objects, and optimization hints"),
        };

        // Colors for assembly classifications
        private static readonly Color BarBgColor = new(0.2f, 0.2f, 0.2f, 0.5f);
        private static readonly Color SelectionColor = new(0.24f, 0.48f, 0.9f, 0.3f);
        private static readonly Color UserCodeColor = new(0.3f, 0.85f, 0.3f, 0.8f);
        private static readonly Color UnityRuntimeColor = new(0.3f, 0.6f, 1f, 0.8f);
        private static readonly Color UnityEditorColor = new(0.6f, 0.4f, 0.8f, 0.8f);
        private static readonly Color DotNetColor = new(0.5f, 0.5f, 0.5f, 0.8f);
        private static readonly Color ThirdPartyColor = new(1f, 0.6f, 0.2f, 0.8f);
        private static readonly Color BarFillColor = new(0.35f, 0.65f, 1f, 0.7f);

        private void InitStyles() {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _errorStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) }
            };
            _successStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(0.4f, 0.8f, 0.4f) }
            };
            _warningStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = new Color(1f, 0.8f, 0.2f) }
            };
            _mutedStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = Color.gray }
            };
        }

        private void OnGUI() {
            InitStyles();
            DrawToolbar();
            DrawProgressBar();

            if (_report != null && _report.Phase == SnapshotAnalysisPhase.Complete) {
                _selectedTab = GUILayout.Toolbar(_selectedTab, TabLabels);
                GUILayout.Space(4);

                switch (_selectedTab) {
                    case 0: DrawSummaryTab(); break;
                    case 1: DrawAssemblyTab(); break;
                    case 2: DrawNativeTab(); break;
                    case 3: DrawReferencesTab(); break;
                    case 4: DrawInsightsTab(); break;
                }
            } else if (_report != null && _report.Phase == SnapshotAnalysisPhase.Error) {
                EditorGUILayout.HelpBox($"Analysis failed: {_report.ErrorMessage}", MessageType.Error);
            } else if (_report == null || _report.Phase == SnapshotAnalysisPhase.Idle) {
                AnalyzerGuidance.DrawSnapshotEmptyState();
            }
        }

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("File:", GUILayout.Width(30));
            var displayPath = string.IsNullOrEmpty(_snapPath) ? "(none)" : Path.GetFileName(_snapPath);
            GUILayout.Label(displayPath, EditorStyles.toolbarButton, GUILayout.MinWidth(100));

            if (GUILayout.Button(AnalyzerGuidance.SnapshotBrowse, EditorStyles.toolbarButton, GUILayout.Width(70))) {
                var path = EditorUtility.OpenFilePanel("Select .snap file", "", "snap");
                if (!string.IsNullOrEmpty(path)) {
                    _snapPath = path;
                }
            }

            bool isRunning = _report != null &&
                             _report.Phase != SnapshotAnalysisPhase.Idle &&
                             _report.Phase != SnapshotAnalysisPhase.Complete &&
                             _report.Phase != SnapshotAnalysisPhase.Error;

            GUI.enabled = !string.IsNullOrEmpty(_snapPath) && !isRunning;
            if (GUILayout.Button(AnalyzerGuidance.SnapshotAnalyze, EditorStyles.toolbarButton, GUILayout.Width(60))) {
                StartAnalysis(false);
            }
            if (GUILayout.Button(AnalyzerGuidance.SnapshotQuick, EditorStyles.toolbarButton, GUILayout.Width(45))) {
                StartAnalysis(true);
            }
            GUI.enabled = true;

            if (isRunning) {
                if (GUILayout.Button(AnalyzerGuidance.SnapshotCancel, EditorStyles.toolbarButton, GUILayout.Width(55))) {
                    if (_report != null) {
                        _report.Phase = SnapshotAnalysisPhase.Error;
                        _report.ErrorMessage = "Cancelled by user";
                    }
                }
            }

            GUILayout.FlexibleSpace();

            if (_report != null && _report.Phase == SnapshotAnalysisPhase.Complete) {
                DrawSnapshotExportButtons();
            }

            if (!AnalyzerGuidance.ShowTabHeaders) {
                if (GUILayout.Button(AnalyzerGuidance.ShowHeadersButton, EditorStyles.toolbarButton, GUILayout.Width(22))) {
                    AnalyzerGuidance.ShowTabHeaders = true;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawProgressBar() {
            if (_report == null) return;
            var phase = _report.Phase;
            if (phase == SnapshotAnalysisPhase.Idle || phase == SnapshotAnalysisPhase.Complete ||
                phase == SnapshotAnalysisPhase.Error)
                return;

            float progress = phase switch {
                SnapshotAnalysisPhase.Loading => 0.1f,
                SnapshotAnalysisPhase.ExtractingTypes => 0.25f,
                SnapshotAnalysisPhase.CrawlingHeap => 0.4f,
                SnapshotAnalysisPhase.BuildingAssemblyTree => 0.6f,
                SnapshotAnalysisPhase.CalculatingRetained => 0.75f,
                SnapshotAnalysisPhase.LinkingNativeManaged => 0.85f,
                SnapshotAnalysisPhase.GeneratingInsights => 0.93f,
                _ => 0f,
            };
            string label = phase switch {
                SnapshotAnalysisPhase.Loading => "Loading .snap file...",
                SnapshotAnalysisPhase.ExtractingTypes => "Extracting type information...",
                SnapshotAnalysisPhase.CrawlingHeap => "Crawling managed heap...",
                SnapshotAnalysisPhase.BuildingAssemblyTree => "Building assembly tree...",
                SnapshotAnalysisPhase.CalculatingRetained => "Calculating retained sizes...",
                SnapshotAnalysisPhase.LinkingNativeManaged => "Linking native and managed objects...",
                SnapshotAnalysisPhase.GeneratingInsights => "Generating insights...",
                _ => "Processing...",
            };
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress, label);
        }

        private void StartAnalysis(bool skipCrawl) {
            if (!ValidateSnapFile()) return;

            ResetTabState();

            _report = new SnapshotReport {
                FilePath = _snapPath,
                AnalysisTime = DateTime.Now,
                Phase = SnapshotAnalysisPhase.Loading,
                SkipCrawl = skipCrawl,
            };
            Repaint();

            EditorApplication.delayCall += RunLoadPhase;
        }

        private void ResetTabState() {
            _selectedTab = 0;
            _selectedAssemblyRow = -1;
            _selectedNativeRow = -1;
            _refSearchText = "";
            _refSearchResults = null;
            _selectedRefObject = -1;
            _insightCategoryFilter = 0;
            _insightScrollPos = Vector2.zero;
        }

        private bool ValidateSnapFile() {
            if (string.IsNullOrEmpty(_snapPath)) {
                EditorUtility.DisplayDialog("Error", "Please select a .snap file.", "OK");
                return false;
            }
            if (!File.Exists(_snapPath)) {
                EditorUtility.DisplayDialog("Error", $"File not found:\n{_snapPath}", "OK");
                return false;
            }
            if (!_snapPath.EndsWith(".snap", StringComparison.OrdinalIgnoreCase)) {
                EditorUtility.DisplayDialog("Error", "File must have .snap extension.", "OK");
                return false;
            }
            return true;
        }

        #region Analysis Pipeline

        private void RunLoadPhase() {
            try {
                _report = SnapshotLoader.Load(_snapPath);
                if (_report.Phase == SnapshotAnalysisPhase.Error) {
                    Repaint();
                    return;
                }

                _report.Phase = SnapshotAnalysisPhase.BuildingAssemblyTree;
                Repaint();
                EditorApplication.delayCall += RunBuildAssemblyTree;
            }
            catch (Exception ex) {
                OnPipelineError($"Load failed: {ex.Message}");
            }
        }

        private void RunBuildAssemblyTree() {
            try {
                var assemblies = AssemblyAnalyzer.Analyze(_report.Types, _report.NativeObjects, null);
                _report.Assemblies.Clear();
                _report.Assemblies.AddRange(assemblies);
                AssemblyAnalyzer.BuildNativeTypeSummaries(_report);

                // Build classification summary
                foreach (var asm in assemblies) {
                    if (!_report.Summary.SizeByClassification.ContainsKey(asm.Classification))
                        _report.Summary.SizeByClassification[asm.Classification] = 0;
                    _report.Summary.SizeByClassification[asm.Classification] += asm.TotalSize;

                    if (!_report.Summary.TypeCountByClassification.ContainsKey(asm.Classification))
                        _report.Summary.TypeCountByClassification[asm.Classification] = 0;
                    _report.Summary.TypeCountByClassification[asm.Classification] += asm.TypeCount;
                }

                if (_report.SkipCrawl) {
                    _report.Phase = SnapshotAnalysisPhase.LinkingNativeManaged;
                    Repaint();
                    EditorApplication.delayCall += RunLinkNativeManaged;
                    return;
                }

                _report.Phase = SnapshotAnalysisPhase.CrawlingHeap;
                Repaint();
                EditorApplication.delayCall += RunCrawlHeap;
            }
            catch (Exception ex) {
                OnPipelineError($"Assembly analysis failed: {ex.Message}");
            }
        }

        private void RunCrawlHeap() {
            try {
                var reader = new ManagedHeapReader(_report.ManagedHeapSections, _report.PointerSize);
                var crawlerResult = ManagedHeapCrawler.Crawl(
                    reader, _report.Types, _report.Fields,
                    _report.GcHandleTargets, _report.PointerSize, _report.ObjectHeaderSize);

                _report.CrawlerResult = crawlerResult;
                _report.Summary.TotalManagedObjectCount = crawlerResult.TotalCrawled;

                // Merge instance data back to assemblies
                AssemblyAnalyzer.MergeInstanceData(_report.Assemblies, _report.Types);

                _report.Phase = SnapshotAnalysisPhase.CalculatingRetained;
                Repaint();
                EditorApplication.delayCall += RunCalculateRetained;
            }
            catch (Exception ex) {
                Debug.LogWarning($"[SnapshotAnalyzer] Heap crawling failed: {ex.Message}. Continuing without instance data.");
                _report.Phase = SnapshotAnalysisPhase.LinkingNativeManaged;
                Repaint();
                EditorApplication.delayCall += RunLinkNativeManaged;
            }
        }

        private void RunCalculateRetained() {
            try {
                if (_report.CrawlerResult != null) {
                    _report.RetainedSizes = RetainedSizeCalculator.Calculate(_report.CrawlerResult);
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"[SnapshotAnalyzer] Retained size calculation failed: {ex.Message}. Continuing without retained sizes.");
            }

            _report.Phase = SnapshotAnalysisPhase.LinkingNativeManaged;
            Repaint();
            EditorApplication.delayCall += RunLinkNativeManaged;
        }

        private void RunLinkNativeManaged() {
            try {
                _report.LinkResult = _report.CrawlerResult != null
                    ? NativeManagedLinker.Link(_report)
                    : NativeManagedLinker.LinkQuick(_report);
            }
            catch (Exception ex) {
                Debug.LogWarning($"[SnapshotAnalyzer] Native-managed linking failed: {ex.Message}. Continuing without link data.");
            }

            _report.Phase = SnapshotAnalysisPhase.GeneratingInsights;
            Repaint();
            EditorApplication.delayCall += RunGenerateInsights;
        }

        private void RunGenerateInsights() {
            try {
                _report.InsightResult = SnapshotInsightGenerator.Analyze(_report, _report.LinkResult);
            }
            catch (Exception ex) {
                Debug.LogWarning($"[SnapshotAnalyzer] Insight generation failed: {ex.Message}. Continuing without insights.");
            }

            _report.Phase = SnapshotAnalysisPhase.Complete;
            Repaint();
        }

        private void OnPipelineError(string message) {
            _report.Phase = SnapshotAnalysisPhase.Error;
            _report.ErrorMessage = message;
            Repaint();
        }

        #endregion

        #region Utility

        internal static Color GetClassificationColor(AssemblyClassification classification) {
            return classification switch {
                AssemblyClassification.UserCode => UserCodeColor,
                AssemblyClassification.UnityRuntime => UnityRuntimeColor,
                AssemblyClassification.UnityEditor => UnityEditorColor,
                AssemblyClassification.DotNet => DotNetColor,
                AssemblyClassification.ThirdParty => ThirdPartyColor,
                _ => DotNetColor,
            };
        }

        internal static Color GetNativeCategoryColor(NativeTypeCategory category) {
            return category switch {
                NativeTypeCategory.Texture => new Color(0.9f, 0.4f, 0.4f, 0.8f),
                NativeTypeCategory.Mesh => new Color(0.4f, 0.8f, 0.4f, 0.8f),
                NativeTypeCategory.Material => new Color(0.4f, 0.6f, 0.9f, 0.8f),
                NativeTypeCategory.Shader => new Color(0.8f, 0.6f, 0.9f, 0.8f),
                NativeTypeCategory.AnimationClip => new Color(1f, 0.8f, 0.3f, 0.8f),
                NativeTypeCategory.AudioClip => new Color(0.3f, 0.8f, 0.8f, 0.8f),
                NativeTypeCategory.Font => new Color(0.7f, 0.7f, 0.5f, 0.8f),
                NativeTypeCategory.ScriptableObject => new Color(0.9f, 0.5f, 0.3f, 0.8f),
                NativeTypeCategory.GameObject => new Color(0.5f, 0.9f, 0.5f, 0.8f),
                NativeTypeCategory.Component => new Color(0.6f, 0.7f, 0.8f, 0.8f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.6f),
            };
        }

        #endregion
    }
}
