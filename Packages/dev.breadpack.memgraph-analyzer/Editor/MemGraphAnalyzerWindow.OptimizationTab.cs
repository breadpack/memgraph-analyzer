using System;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _optimizationScrollPos;
        private int _expandedOptimization = -1;

        private void DrawOptimizationGuide() {
            var opt = _report?.Optimizations;
            if (opt == null || opt.Recommendations.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Optimization Guide", _headerStyle);
            GUILayout.Label(
                $"{opt.Recommendations.Count} recommendations | " +
                $"Estimated total savings: {VmmapParser.FormatSize(opt.TotalEstimatedSavings)}",
                _mutedStyle);
            GUILayout.Space(4);

            int shown = Math.Min(opt.Recommendations.Count, 5);
            for (int i = 0; i < shown; i++) {
                DrawOptimizationRecommendationCompact(opt.Recommendations[i], i);
            }

            if (opt.Recommendations.Count > 5) {
                GUILayout.Label(
                    $"  ... and {opt.Recommendations.Count - 5} more (see full list in Summary tab details)",
                    _mutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawOptimizationRecommendationCompact(OptimizationRecommendation rec, int index) {
            var difficultyColor = rec.Difficulty switch {
                OptimizationDifficulty.Easy => HealthGoodColor,
                OptimizationDifficulty.Medium => HealthWarningColor,
                OptimizationDifficulty.Hard => HealthCriticalColor,
                _ => Color.white,
            };

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            var prevColor = GUI.contentColor;
            GUI.contentColor = difficultyColor;
            GUILayout.Label($"[{rec.Difficulty}]", EditorStyles.boldLabel, GUILayout.Width(70));
            GUI.contentColor = prevColor;

            GUILayout.Label(rec.Title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"~{VmmapParser.FormatSize(rec.EstimatedSavings)} savings",
                _mutedStyle, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            GUILayout.Label(rec.Description, EditorStyles.wordWrappedLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawFullOptimizationList() {
            var opt = _report?.Optimizations;
            if (opt == null || opt.Recommendations.Count == 0) {
                EditorGUILayout.HelpBox("No optimization recommendations. " +
                    "Memory patterns are within acceptable thresholds.", MessageType.Info);
                return;
            }

            // Summary bar
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Optimization Guide", _headerStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Total recommendations: {opt.Recommendations.Count}", GUILayout.Width(200));
            GUILayout.Label($"Estimated total savings: {VmmapParser.FormatSize(opt.TotalEstimatedSavings)}",
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Count by difficulty
            int easy = 0, medium = 0, hard = 0;
            foreach (var rec in opt.Recommendations) {
                switch (rec.Difficulty) {
                    case OptimizationDifficulty.Easy: easy++; break;
                    case OptimizationDifficulty.Medium: medium++; break;
                    case OptimizationDifficulty.Hard: hard++; break;
                }
            }
            EditorGUILayout.BeginHorizontal();
            var prevColor = GUI.contentColor;
            GUI.contentColor = HealthGoodColor;
            GUILayout.Label($"Easy: {easy}", GUILayout.Width(80));
            GUI.contentColor = HealthWarningColor;
            GUILayout.Label($"Medium: {medium}", GUILayout.Width(100));
            GUI.contentColor = HealthCriticalColor;
            GUILayout.Label($"Hard: {hard}", GUILayout.Width(80));
            GUI.contentColor = prevColor;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // Full recommendation list
            for (int i = 0; i < opt.Recommendations.Count; i++) {
                DrawOptimizationRecommendationFull(opt.Recommendations[i], i);
            }
        }

        private void DrawOptimizationRecommendationFull(OptimizationRecommendation rec, int index) {
            var difficultyColor = rec.Difficulty switch {
                OptimizationDifficulty.Easy => HealthGoodColor,
                OptimizationDifficulty.Medium => HealthWarningColor,
                OptimizationDifficulty.Hard => HealthCriticalColor,
                _ => Color.white,
            };

            var categoryColor = rec.Category switch {
                OptimizationCategory.Texture => new Color(0.4f, 0.7f, 1f, 1f),
                OptimizationCategory.Mesh => new Color(0.5f, 0.9f, 0.5f, 1f),
                OptimizationCategory.Audio => new Color(1f, 0.7f, 0.3f, 1f),
                OptimizationCategory.Shader => new Color(0.9f, 0.5f, 0.9f, 1f),
                OptimizationCategory.GPU => new Color(0.4f, 0.8f, 0.4f, 1f),
                _ => Color.white,
            };

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row
            EditorGUILayout.BeginHorizontal();

            var prevColor = GUI.contentColor;
            GUI.contentColor = difficultyColor;
            GUILayout.Label($"[{rec.Difficulty}]", EditorStyles.boldLabel, GUILayout.Width(70));
            GUI.contentColor = categoryColor;
            GUILayout.Label($"[{rec.Category}]", EditorStyles.miniLabel, GUILayout.Width(90));
            GUI.contentColor = prevColor;

            bool expanded = _expandedOptimization == index;
            if (GUILayout.Button(expanded ? "[-]" : "[+]", EditorStyles.miniButton, GUILayout.Width(28))) {
                _expandedOptimization = expanded ? -1 : index;
            }

            GUILayout.Label(rec.Title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"~{VmmapParser.FormatSize(rec.EstimatedSavings)}",
                EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            GUILayout.Label(rec.Description, EditorStyles.wordWrappedLabel);

            if (expanded) {
                GUILayout.Space(4);

                // Action steps
                if (rec.ActionSteps.Count > 0) {
                    GUILayout.Label("Action Steps:", EditorStyles.boldLabel);
                    foreach (var step in rec.ActionSteps) {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(16);
                        GUILayout.Label($"* {step}", EditorStyles.wordWrappedLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // Related allocations
                if (rec.RelatedAllocations.Count > 0) {
                    GUILayout.Space(2);
                    GUILayout.Label("Related Allocations:", EditorStyles.boldLabel);
                    foreach (var alloc in rec.RelatedAllocations) {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(16);
                        GUILayout.Label(alloc, _mutedStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
