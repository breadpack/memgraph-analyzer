using UnityEditor;
using UnityEngine;

namespace Tools {
    /// <summary>
    /// Shared guidance text constants and drawing utilities for analyzer windows.
    /// </summary>
    internal static class AnalyzerGuidance {
        private const string PrefKey = "MemGraphAnalyzer_ShowTabHeaders";

        internal static bool ShowTabHeaders {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        #region Tooltip Constants

        // --- Snapshot Toolbar ---
        internal static readonly GUIContent SnapshotBrowse =
            new("Browse...", "Select a Unity Memory Snapshot (.snap) file exported from the Profiler.");
        internal static readonly GUIContent SnapshotAnalyze =
            new("Analyze", "Run full analysis including heap crawl, retained sizes, and insights.");
        internal static readonly GUIContent SnapshotQuick =
            new("Quick", "Fast analysis without heap crawl. Skips instance counts and reference data.");
        internal static readonly GUIContent SnapshotCancel =
            new("Cancel", "Cancel the running analysis.");

        // --- MemGraph Toolbar ---
        internal static readonly GUIContent MemGraphBrowse =
            new("Browse...", "Select a .memgraph file exported from Xcode's Debug Memory Graph.");
        internal static readonly GUIContent MemGraphAnalyze =
            new("Analyze", "Run vmmap, heap, leaks, and malloc_history analysis on the selected file.");
        internal static readonly GUIContent MemGraphCancel =
            new("Cancel", "Cancel the running analysis.");
        internal static readonly GUIContent MemGraphSnapshot =
            new("Snapshot", "Open the Unity Snapshot Analyzer window for .snap file analysis.");

        // --- Export (shared) ---
        internal static readonly GUIContent ExportText =
            new("Export Text", "Export a detailed text report of the analysis to a .txt file.");
        internal static readonly GUIContent ExportCsv =
            new("Export CSV", "Export data as CSV for spreadsheet analysis.");
        internal static readonly GUIContent CopySummary =
            new("Copy Summary", "Copy a concise summary of the analysis to the clipboard.");
        internal static readonly GUIContent AIExport =
            new("AI Export", "Export current analysis as AI-optimized Markdown for Claude/GPT optimization requests.");

        // --- Show/Hide tab headers ---
        internal static readonly GUIContent ShowHeadersButton =
            new("?", "Show tab description headers.");

        #endregion

        #region Tab Header Drawing

        internal static void DrawTabHeader(string description) {
            if (!ShowTabHeaders) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16))) {
                ShowTabHeaders = false;
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Empty State Drawing

        private static GUIStyle _welcomeTitleStyle;
        private static GUIStyle _welcomeSubtitleStyle;
        private static GUIStyle _stepNumberStyle;
        private static GUIStyle _stepTextStyle;
        private static GUIStyle _tabNameStyle;
        private static GUIStyle _tabDescStyle;

        private static void InitEmptyStateStyles() {
            _welcomeTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
            };
            _welcomeSubtitleStyle ??= new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.gray },
            };
            _stepNumberStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
            };
            _stepTextStyle ??= new GUIStyle(EditorStyles.label) {
                wordWrap = true,
            };
            _tabNameStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 11,
            };
            _tabDescStyle ??= new GUIStyle(EditorStyles.label) {
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
            };
        }

        internal static void DrawWelcomeHeader(string title, string subtitle) {
            InitEmptyStateStyles();
            GUILayout.Space(20);
            EditorGUILayout.LabelField(title, _welcomeTitleStyle);
            GUILayout.Space(4);
            EditorGUILayout.LabelField(subtitle, _welcomeSubtitleStyle);
            GUILayout.Space(12);
        }

        internal static void DrawWorkflowSteps(string[] steps) {
            InitEmptyStateStyles();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Getting Started", EditorStyles.boldLabel);
            GUILayout.Space(4);

            for (int i = 0; i < steps.Length; i++) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1}", _stepNumberStyle, GUILayout.Width(24));
                EditorGUILayout.LabelField(steps[i], _stepTextStyle);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        internal static void DrawTabOverview((string name, string desc)[] tabs) {
            InitEmptyStateStyles();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Available Tabs", EditorStyles.boldLabel);
            GUILayout.Space(4);

            foreach (var (name, desc) in tabs) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(name, _tabNameStyle, GUILayout.Width(120));
                EditorGUILayout.LabelField(desc, _tabDescStyle);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(1);
            }

            EditorGUILayout.EndVertical();
        }

        internal static void DrawFileFormatHint(string title, string instruction) {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox($"{title}\n{instruction}", MessageType.Info);
        }

        #endregion

        #region Snapshot Empty State

        internal static void DrawSnapshotEmptyState() {
            DrawWelcomeHeader(
                "Snapshot Analyzer",
                "Analyze Unity Memory Snapshots (.snap) to find memory issues and optimization opportunities.");

            DrawWorkflowSteps(new[] {
                "Click Browse to select a .snap file.",
                "Click Analyze (full) or Quick (fast, no heap crawl).",
                "Explore the tabs to review memory usage.",
            });

            GUILayout.Space(4);

            DrawTabOverview(new[] {
                ("Summary", "Memory totals, classification breakdown, top types, and detected issues."),
                ("Assemblies", "Browse assemblies by classification, namespace, and type."),
                ("Native Objects", "Native Unity objects (textures, meshes, etc.) sorted by size."),
                ("References", "Search managed objects and trace reference chains to GC roots."),
                ("Insights", "Duplicate assets, unreferenced objects, and optimization hints."),
            });

            DrawFileFormatHint(
                "How to export a .snap file",
                "Unity Editor > Window > Analysis > Profiler > Memory > Take Snapshot > Export");

            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Quick vs Full Analysis\n" +
                "Quick is faster but skips heap crawl — no instance counts, reference chains, or retained sizes.\n" +
                "Use Full for detailed analysis including the References and Insights tabs.",
                MessageType.None);
        }

        #endregion

        #region MemGraph Empty State

        internal static void DrawMemGraphEmptyState() {
            DrawWelcomeHeader(
                "MemGraph Analyzer",
                "Analyze macOS/iOS .memgraph files using vmmap, heap, leaks, and malloc_history.");

            DrawWorkflowSteps(new[] {
                "Click Browse to select a .memgraph file.",
                "Click Analyze to run all diagnostic commands.",
                "Explore the tabs to review memory usage.",
            });

            GUILayout.Space(4);

            DrawTabOverview(new[] {
                ("Summary", "Health status, iOS device comparison, memory overview, optimization guide."),
                ("Virtual Memory", "vmmap region breakdown with fragmentation analysis."),
                ("Heap Analysis", "Heap allocations by class with size distribution and call stacks."),
                ("Leak Detection", "Leaked memory objects with stack traces and remediation guidance."),
                ("Unity-Specific", "Tracked vs untracked, plugin memory, GPU, thread stacks."),
                ("Asset & Logic", "Allocation trace by asset type and game logic with controllability."),
                ("Comparison", "Side-by-side diff of two .memgraph files."),
            });

            DrawFileFormatHint(
                "How to export a .memgraph file",
                "Xcode > Debug > Debug Memory Graph > File > Export Memory Graph");
        }

        #endregion
    }
}
