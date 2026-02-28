using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        // === Shared GUIStyles (reused instead of `new GUIStyle()` in loops) ===
        private GUIStyle _actionabilityFixableStyle;
        private GUIStyle _actionabilityMonitorStyle;
        private GUIStyle _actionabilitySystemStyle;
        private GUIStyle _userCodeStyle;
        private GUIStyle _densityLabelStyle;
        private GUIStyle _metricHealthStyle;
        private bool _tabStylesInitialized;

        private void InitTabStyles() {
            if (_tabStylesInitialized) return;
            _tabStylesInitialized = true;

            _actionabilityFixableStyle = new GUIStyle(EditorStyles.miniLabel) {
                normal = { textColor = GetActionabilityColor(Actionability.Fixable) }
            };
            _actionabilityMonitorStyle = new GUIStyle(EditorStyles.miniLabel) {
                normal = { textColor = GetActionabilityColor(Actionability.Monitor) }
            };
            _actionabilitySystemStyle = new GUIStyle(EditorStyles.miniLabel) {
                normal = { textColor = GetActionabilityColor(Actionability.SystemOwned) }
            };
            _userCodeStyle = new GUIStyle(EditorStyles.label) {
                normal = { textColor = UserCodeColor }
            };
            _densityLabelStyle = new GUIStyle(EditorStyles.boldLabel);
            _metricHealthStyle = new GUIStyle(EditorStyles.boldLabel);
        }

        private GUIStyle GetCachedActionabilityStyle(Actionability actionability) {
            return actionability switch {
                Actionability.Fixable => _actionabilityFixableStyle,
                Actionability.Monitor => _actionabilityMonitorStyle,
                Actionability.SystemOwned => _actionabilitySystemStyle,
                _ => EditorStyles.miniLabel,
            };
        }

        // === Cache invalidation ===

        private void InvalidateAllCaches() {
            // HeapTab
            _cachedHeapRows = null;
            _cachedHeapKey = default;
            _cachedLargeAvg = null;
            _cachedPoolCandidates = null;
            _cachedSingleCountTypes = -1;

            // UnityTab
            _unityTabCacheBuilt = false;
            _cachedPluginsSorted = null;
            _cachedPluginAllocations = null;
            _cachedUnsafeAllocs = null;
            _cachedStackRegions = null;
            _cachedStackAllocs = null;
            _cachedGpuRegions = null;
            _cachedGpuAllocs = null;

            // VmmapTab
            _cachedVmmapRows = null;
            _cachedVmmapKey = default;
            _cachedVmmapDetailedRegions = null;
            _cachedVmmapDetailedFilter = null;

            // AllocationTraceTab
            _cachedTraceRows = null;
            _cachedTraceKey = default;
            _expandedTraceRows.Clear();

            // LeaksTab
            _cachedLeakGroups = null;
            _leaksNextStepsCached = false;

            // SummaryTab
            _cachedTopCategories = null;
            _cachedOwnersSorted = null;
        }
    }
}
