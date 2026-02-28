using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private Vector2 _vmmapScrollPos;
        private string _vmmapFilter = "";
        private bool _showVmmapDetailed;
        private bool _showVmmapRaw;

        private enum VmmapSortColumn { Type, Virtual, Resident, Dirty, Swapped, Count }
        private VmmapSortColumn _vmmapSortColumn = VmmapSortColumn.Resident;
        private bool _vmmapSortAscending;

        // === Vmmap cache ===
        private List<VmmapSummaryRow> _cachedVmmapRows;
        private (string filter, VmmapSortColumn sortCol, bool sortAsc) _cachedVmmapKey;
        private List<VmmapRegion> _cachedVmmapDetailedRegions;
        private string _cachedVmmapDetailedFilter;

        private static readonly Dictionary<string, string> RegionDescriptions = new() {
            { "__TEXT", "Executable code (read-only). Shared between processes. Usually not a concern." },
            { "__DATA", "Global/static variables. Writable. Includes ObjC metadata." },
            { "__DATA_CONST", "Read-only initialized data. Shared where possible." },
            { "__DATA_DIRTY", "Data pages that have been written. Cannot be reclaimed." },
            { "__LINKEDIT", "Linker metadata (symbols, relocations). Read-only, shared." },
            { "MALLOC", "Heap allocated memory. Primary optimization target." },
            { "MALLOC_LARGE", "Large heap allocations (>1 page). Check for oversized buffers." },
            { "MALLOC_NANO", "Small object heap (nano allocator). Very small allocations." },
            { "MALLOC_TINY", "Tiny heap allocations. Frequent small allocs." },
            { "MALLOC_SMALL", "Small heap allocations. Standard alloc sizes." },
            { "IOKit", "Kernel/GPU shared memory. Metal textures/buffers." },
            { "IOSurface", "GPU surface memory. Render targets and display buffers." },
            { "VM_ALLOCATE", "Direct VM allocations. May be from system or plugins." },
            { "Stack", "Thread stacks. Each thread reserves 512KB-1MB." },
            { "STACK GUARD", "Guard pages between thread stacks. Virtual only, not resident." },
            { "mapped file", "Memory-mapped files. May be shared/read-only." },
            { "__FONT_DATA", "Font data. Read-only, shared." },
            { "CG image", "CoreGraphics image buffers. Bitmaps in system memory." },
        };

        private void DrawVmmapTab() {
            AnalyzerGuidance.DrawTabHeader("vmmap output: virtual/resident/dirty/swapped by region type. Hover region names for descriptions.");
            // Filter
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(40));
            _vmmapFilter = EditorGUILayout.TextField(_vmmapFilter, GUILayout.Width(200));
            GUILayout.FlexibleSpace();
            _showVmmapDetailed = GUILayout.Toggle(_showVmmapDetailed, "Show Detailed Regions",
                EditorStyles.toolbarButton, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Fragmentation summary
            DrawVmmapFragmentationSummary();
            GUILayout.Space(4);

            if (!_showVmmapDetailed) {
                DrawVmmapSummaryTable();
            } else {
                DrawVmmapDetailedTable();
            }

            // Raw output foldout
            GUILayout.Space(8);
            _showVmmapRaw = EditorGUILayout.Foldout(_showVmmapRaw, "Show Raw Output");
            if (_showVmmapRaw) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var rawText = _showVmmapDetailed
                    ? _report.Vmmap.DetailedRawOutput
                    : _report.Vmmap.RawOutput;
                if (string.IsNullOrEmpty(rawText)) {
                    GUILayout.Label("(no raw output available)", _mutedStyle);
                } else {
                    // Show first 5000 chars to avoid performance issues
                    var display = rawText.Length > 5000 ? rawText.Substring(0, 5000) + "\n...(truncated)" : rawText;
                    EditorGUILayout.SelectableLabel(display, EditorStyles.miniLabel,
                        GUILayout.MinHeight(200), GUILayout.ExpandHeight(true));
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawVmmapSummaryTable() {
            // Column headers
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawVmmapSortButton("Region Type", VmmapSortColumn.Type, 0, true);
            DrawVmmapSortButton("Virtual", VmmapSortColumn.Virtual, 80);
            DrawVmmapSortButton("Resident", VmmapSortColumn.Resident, 80);
            DrawVmmapSortButton("Dirty", VmmapSortColumn.Dirty, 80);
            DrawVmmapSortButton("Swapped", VmmapSortColumn.Swapped, 80);
            DrawVmmapSortButton("Count", VmmapSortColumn.Count, 50);
            EditorGUILayout.EndHorizontal();

            // Data (cached)
            var rows = GetFilteredVmmapRowsCached();

            _vmmapScrollPos = EditorGUILayout.BeginScrollView(_vmmapScrollPos);

            foreach (var row in rows) {
                EditorGUILayout.BeginHorizontal();

                // Region type with tooltip
                string regionLabel = row.RegionType;
                string tooltip = GetRegionTooltip(row.RegionType);
                if (tooltip != null)
                    regionLabel += " (?)";
                GUILayout.Label(new GUIContent(regionLabel, tooltip), GUILayout.MinWidth(100));

                GUILayout.Label(VmmapParser.FormatSize(row.VirtualSize), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(row.ResidentSize), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(row.DirtySize), GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(row.SwappedSize), GUILayout.Width(80));
                GUILayout.Label(row.RegionCount.ToString(), GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }

            // Total row
            if (_report.Vmmap.Total != null) {
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
                var total = _report.Vmmap.Total;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("TOTAL", EditorStyles.boldLabel, GUILayout.MinWidth(100));
                GUILayout.Label(VmmapParser.FormatSize(total.VirtualSize), EditorStyles.boldLabel, GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(total.ResidentSize), EditorStyles.boldLabel, GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(total.DirtySize), EditorStyles.boldLabel, GUILayout.Width(80));
                GUILayout.Label(VmmapParser.FormatSize(total.SwappedSize), EditorStyles.boldLabel, GUILayout.Width(80));
                GUILayout.Label(total.RegionCount.ToString(), EditorStyles.boldLabel, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (_report.Vmmap.ParseErrorCount > 0) {
                GUILayout.Label($"({_report.Vmmap.ParseErrorCount} lines could not be parsed)", _mutedStyle);
            }
        }

        private void DrawVmmapDetailedTable() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.Width(120));
            GUILayout.Label("Address Range", EditorStyles.toolbarButton, GUILayout.Width(200));
            GUILayout.Label("Size", EditorStyles.toolbarButton, GUILayout.Width(80));
            GUILayout.Label("Protection", EditorStyles.toolbarButton, GUILayout.Width(70));
            GUILayout.Label("SM", EditorStyles.toolbarButton, GUILayout.Width(50));
            GUILayout.Label("Detail", EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            EditorGUILayout.EndHorizontal();

            _vmmapScrollPos = EditorGUILayout.BeginScrollView(_vmmapScrollPos);

            var regions = GetFilteredVmmapDetailedCached();

            foreach (var region in regions) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(region.RegionType ?? "", GUILayout.Width(120));
                GUILayout.Label($"{region.AddressStart}-{region.AddressEnd}", GUILayout.Width(200));
                GUILayout.Label(VmmapParser.FormatSize(region.Size), GUILayout.Width(80));
                GUILayout.Label(region.Protection ?? "", GUILayout.Width(70));
                GUILayout.Label(region.ShareMode ?? "", GUILayout.Width(50));
                GUILayout.Label(region.Detail ?? "", GUILayout.MinWidth(100));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Label($"{regions.Count} regions displayed", _mutedStyle);
        }

        #region Caching

        private List<VmmapSummaryRow> GetFilteredVmmapRowsCached() {
            var key = (_vmmapFilter, _vmmapSortColumn, _vmmapSortAscending);
            if (_cachedVmmapRows != null && _cachedVmmapKey == key)
                return _cachedVmmapRows;

            _cachedVmmapKey = key;
            _cachedVmmapRows = GetFilteredVmmapRows();
            return _cachedVmmapRows;
        }

        private List<VmmapSummaryRow> GetFilteredVmmapRows() {
            var rows = _report.Vmmap.Summary.AsEnumerable();

            if (!string.IsNullOrEmpty(_vmmapFilter)) {
                rows = rows.Where(r => r.RegionType != null &&
                    r.RegionType.IndexOf(_vmmapFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            rows = _vmmapSortColumn switch {
                VmmapSortColumn.Type => _vmmapSortAscending
                    ? rows.OrderBy(r => r.RegionType)
                    : rows.OrderByDescending(r => r.RegionType),
                VmmapSortColumn.Virtual => _vmmapSortAscending
                    ? rows.OrderBy(r => r.VirtualSize)
                    : rows.OrderByDescending(r => r.VirtualSize),
                VmmapSortColumn.Resident => _vmmapSortAscending
                    ? rows.OrderBy(r => r.ResidentSize)
                    : rows.OrderByDescending(r => r.ResidentSize),
                VmmapSortColumn.Dirty => _vmmapSortAscending
                    ? rows.OrderBy(r => r.DirtySize)
                    : rows.OrderByDescending(r => r.DirtySize),
                VmmapSortColumn.Swapped => _vmmapSortAscending
                    ? rows.OrderBy(r => r.SwappedSize)
                    : rows.OrderByDescending(r => r.SwappedSize),
                VmmapSortColumn.Count => _vmmapSortAscending
                    ? rows.OrderBy(r => r.RegionCount)
                    : rows.OrderByDescending(r => r.RegionCount),
                _ => rows,
            };

            return rows.ToList();
        }

        private List<VmmapRegion> GetFilteredVmmapDetailedCached() {
            if (_cachedVmmapDetailedRegions != null && _cachedVmmapDetailedFilter == _vmmapFilter)
                return _cachedVmmapDetailedRegions;

            _cachedVmmapDetailedFilter = _vmmapFilter;

            if (string.IsNullOrEmpty(_vmmapFilter)) {
                _cachedVmmapDetailedRegions = _report.Vmmap.Regions;
            } else {
                _cachedVmmapDetailedRegions = _report.Vmmap.Regions.Where(r =>
                    (r.RegionType != null && r.RegionType.IndexOf(_vmmapFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (r.Detail != null && r.Detail.IndexOf(_vmmapFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
            }

            return _cachedVmmapDetailedRegions;
        }

        #endregion

        private void DrawVmmapSortButton(string label, VmmapSortColumn column, float width, bool expand = false) {
            var displayLabel = label;
            if (_vmmapSortColumn == column)
                displayLabel += _vmmapSortAscending ? " ▲" : " ▼";

            bool clicked;
            if (expand)
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.MinWidth(100));
            else
                clicked = GUILayout.Button(displayLabel, EditorStyles.toolbarButton, GUILayout.Width(width));

            if (clicked) {
                if (_vmmapSortColumn == column)
                    _vmmapSortAscending = !_vmmapSortAscending;
                else {
                    _vmmapSortColumn = column;
                    _vmmapSortAscending = false;
                }
            }
        }

        private void DrawVmmapFragmentationSummary() {
            if (_report.Vmmap.Total == null) return;

            long totalVirtual = _report.Vmmap.Total.VirtualSize;
            long totalResident = _report.Vmmap.Total.ResidentSize;
            if (totalVirtual <= 0) return;

            float density = (float)totalResident / totalVirtual * 100f;
            string densityLabel;
            Color densityColor;

            if (density > 50f) {
                densityLabel = "Dense";
                densityColor = HealthGoodColor;
            } else if (density > 20f) {
                densityLabel = "Moderate";
                densityColor = HealthWarningColor;
            } else {
                densityLabel = "Sparse (high fragmentation)";
                densityColor = HealthCriticalColor;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Memory Density:", EditorStyles.boldLabel, GUILayout.Width(120));
            _densityLabelStyle.normal.textColor = densityColor;
            GUILayout.Label($"{densityLabel} ({density:F1}%)", _densityLabelStyle, GUILayout.Width(200));
            GUILayout.Label(
                $"Resident {VmmapParser.FormatSize(totalResident)} / Virtual {VmmapParser.FormatSize(totalVirtual)}",
                _mutedStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static string GetRegionTooltip(string regionType) {
            if (string.IsNullOrEmpty(regionType)) return null;

            // Exact match first
            if (RegionDescriptions.TryGetValue(regionType, out string desc))
                return desc;

            // Partial match for common patterns
            foreach (var kv in RegionDescriptions) {
                if (regionType.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            }

            return null;
        }
    }
}
