using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tools {
    public partial class MemGraphAnalyzerWindow {
        private readonly Dictionary<string, AddressTraceResult> _addressTraceCache = new();
        private static readonly Color TraceHeaderColor = new(0.9f, 0.75f, 0.3f, 1f);
        private static readonly Color FallbackHeaderColor = new(0.6f, 0.8f, 1f, 1f);

        private void RequestAddressTrace(HeapAllocation alloc) {
            if (alloc?.ClassName == null) return;

            var key = alloc.ClassName;
            if (_addressTraceCache.TryGetValue(key, out var existing) && existing.IsLoading)
                return; // already in progress

            var result = new AddressTraceResult { IsLoading = true, LoadingStep = 1 };
            _addressTraceCache[key] = result;
            Repaint();

            // Step 1: Get top addresses for this class
            var args = AddressTraceParser.BuildHeapAddressesCommand(key, _memGraphPath, 30);
            MemGraphCommandRunner.RunAsync("/bin/sh", args, cmdResult => {
                if (!string.IsNullOrEmpty(cmdResult.Output)) {
                    var addresses = AddressTraceParser.ParseHeapAddresses(cmdResult.Output);
                    if (addresses.Count > 0) {
                        // Attach vmmap context to addresses
                        if (_report?.Vmmap?.Regions != null) {
                            foreach (var (addr, _) in addresses) {
                                AddressTraceParser.FindContainingRegion(addr, _report.Vmmap.Regions);
                            }
                        }

                        // Pick top 5 largest
                        var top = addresses
                            .OrderByDescending(a => a.size)
                            .Take(5)
                            .Select(a => a.address)
                            .ToList();
                        RunMallocHistoryForAddresses(alloc, top, addresses);
                        return;
                    }
                }

                // No addresses found - run fallback
                RunFallbackAnalysis(alloc, null);
            });
        }

        private void RunMallocHistoryForAddresses(HeapAllocation alloc, List<string> addresses,
            List<(string address, long size)> allAddresses) {
            var key = alloc.ClassName;
            if (!_addressTraceCache.TryGetValue(key, out var result)) return;
            result.LoadingStep = 2;
            Repaint();

            var args = AddressTraceParser.BuildMallocHistoryCommand(_memGraphPath, addresses, 600);
            MemGraphCommandRunner.RunAsync("/bin/sh", args, cmdResult => {
                if (!string.IsNullOrEmpty(cmdResult.Output)) {
                    var traces = AddressTraceParser.ParseMallocHistory(cmdResult.Output);
                    // Attach vmmap context to each trace
                    if (_report?.Vmmap?.Regions != null) {
                        foreach (var trace in traces) {
                            trace.Context = AddressTraceParser.FindContainingRegion(
                                trace.Address, _report.Vmmap.Regions);
                        }
                    }
                    result.Traces.AddRange(traces);
                }

                if (result.Traces.Count == 0) {
                    // malloc_history failed, run fallback
                    RunFallbackAnalysis(alloc, allAddresses);
                } else {
                    result.IsLoading = false;
                    Repaint();
                }
            });
        }

        private void RunFallbackAnalysis(HeapAllocation alloc, List<(string address, long size)> addresses) {
            var key = alloc.ClassName;
            if (!_addressTraceCache.TryGetValue(key, out var result)) return;
            result.LoadingStep = 3;
            Repaint();

            var fallback = new FallbackAnalysis {
                HasMallocHistory = false,
                AnalysisMethod = "Zone + vmmap + call tree + pattern inference",
            };

            // Phase 4: vmmap address correlation (no additional command needed)
            if (addresses != null && _report?.Vmmap?.Regions != null) {
                foreach (var (addr, _) in addresses.Take(5)) {
                    var ctx = AddressTraceParser.FindContainingRegion(addr, _report.Vmmap.Regions);
                    if (ctx != null && !fallback.Contexts.Exists(c =>
                            c.VmmapRegionType == ctx.VmmapRegionType))
                        fallback.Contexts.Add(ctx);
                }
            }

            // Phase 5: call tree search
            if (_report?.CallTree != null && _report.CallTree.Count > 0) {
                var callers = CallTreeParser.ExtractCallers(_report.CallTree, alloc.ClassName, 4);
                foreach (var caller in callers.Take(10))
                    fallback.RelatedCallers.Add(caller);
            }

            // Phase 6: pattern inference
            var inferences = AddressTraceParser.InferAllocationOrigin(
                alloc.ClassName, alloc.Binary, alloc.AverageSize, _report?.Vmmap);
            fallback.Inferences.AddRange(inferences);

            // Phase 3: heap --zones (requires command)
            var zoneArgs = ZoneParser.BuildHeapZonesCommand(alloc.ClassName, _memGraphPath);
            MemGraphCommandRunner.RunAsync("/bin/sh", zoneArgs, zoneResult => {
                if (!string.IsNullOrEmpty(zoneResult.Output)) {
                    var zoneInfo = ZoneParser.ParseZoneForClass(zoneResult.Output, alloc.ClassName);
                    if (zoneInfo != null) {
                        fallback.Contexts.Insert(0, new AllocationContext {
                            ZoneName = zoneInfo.ZoneName,
                        });
                    }
                }

                result.Fallback = fallback;
                result.ErrorMessage = null;
                result.IsLoading = false;
                Repaint();
            });
        }

        private void DrawAddressTraces(HeapAllocation alloc) {
            if (alloc?.ClassName == null) return;
            var key = alloc.ClassName;

            GUILayout.Space(4);

            // Show trace button or results
            if (!_addressTraceCache.TryGetValue(key, out var traceResult)) {
                // Not yet traced - show button
                GUI.enabled = !MemGraphCommandRunner.IsRunning;
                if (GUILayout.Button("Trace Top Allocations (per-address call stacks)", GUILayout.Height(24))) {
                    RequestAddressTrace(alloc);
                }
                GUI.enabled = true;
                GUILayout.Label(
                    "Runs heap -addresses + malloc_history to get exact call stacks for individual allocations.",
                    _mutedStyle);
                return;
            }

            // Loading state
            if (traceResult.IsLoading) {
                string step = traceResult.LoadingStep switch {
                    1 => "Getting allocation addresses (heap -addresses)...",
                    2 => "Getting call stacks (malloc_history)...",
                    3 => "Running fallback analysis (zone, vmmap, call tree)...",
                    _ => "Loading...",
                };
                EditorGUILayout.HelpBox(step, MessageType.Info);
                return;
            }

            // Fallback analysis result
            if (traceResult.Fallback != null) {
                DrawFallbackAnalysis(traceResult.Fallback);
                return;
            }

            // Error state
            if (!string.IsNullOrEmpty(traceResult.ErrorMessage) && traceResult.Traces.Count == 0) {
                EditorGUILayout.HelpBox(traceResult.ErrorMessage, MessageType.Warning);
                return;
            }

            // Show results
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) {
                normal = { textColor = TraceHeaderColor }
            };
            GUILayout.Label(
                $"[Per-Address Call Stacks] ({traceResult.Traces.Count} allocations traced)",
                headerStyle);

            for (int t = 0; t < traceResult.Traces.Count; t++) {
                var trace = traceResult.Traces[t];
                GUILayout.Space(2);

                // Trace header
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"#{t + 1}", EditorStyles.boldLabel, GUILayout.Width(24));
                GUILayout.Label(trace.Address, _mutedStyle, GUILayout.Width(130));
                GUILayout.Label(VmmapParser.FormatSize(trace.Size), EditorStyles.boldLabel, GUILayout.Width(80));

                // Show the most important frame (first user code or first significant frame)
                string summary = GetTraceSummary(trace);
                if (!string.IsNullOrEmpty(summary)) {
                    var summaryStyle = new GUIStyle(EditorStyles.label) {
                        normal = { textColor = UserCodeColor }
                    };
                    GUILayout.Label(summary, summaryStyle);
                }
                EditorGUILayout.EndHorizontal();

                // Stack frames
                DrawTraceFrames(trace);
            }
        }

        private void DrawTraceFrames(AddressTrace trace) {
            var frames = AddressTraceParser.GetSignificantFrames(trace);
            if (frames.Count == 0) return;

            foreach (var frame in frames) {
                bool isUser = CallTreeParser.IsUserCode(frame.FunctionName);
                string displayName = CallTreeParser.FormatFunctionName(frame.FunctionName);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(48);

                if (isUser) {
                    var style = new GUIStyle(EditorStyles.label) {
                        normal = { textColor = UserCodeColor }
                    };
                    GUILayout.Label("[C#]", style, GUILayout.Width(30));
                    GUILayout.Label(displayName, style);
                } else {
                    GUILayout.Label("    ", GUILayout.Width(30));
                    bool isEngine = IsEngineFrame(frame);
                    GUILayout.Label(displayName, isEngine ? EditorStyles.label : _mutedStyle);
                }

                if (!string.IsNullOrEmpty(frame.Binary)) {
                    GUILayout.Label(frame.Binary, _mutedStyle, GUILayout.Width(120));
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static string GetTraceSummary(AddressTrace trace) {
            if (trace?.Frames == null) return null;

            // Find the first user code frame (most relevant for optimization)
            foreach (var frame in trace.Frames) {
                if (CallTreeParser.IsUserCode(frame.FunctionName))
                    return CallTreeParser.FormatFunctionName(frame.FunctionName);
            }

            // Fallback: find first significant non-allocator frame
            foreach (var frame in trace.Frames) {
                if (frame.FunctionName == null) continue;
                var upper = frame.FunctionName.ToUpperInvariant();
                if (upper.Contains("MEMORYMANAGER") || upper.Contains("MALLOC") ||
                    upper.Contains("MEMALIGN") || upper.Contains("CALLOC"))
                    continue;
                return frame.FunctionName;
            }

            return null;
        }

        private static bool IsEngineFrame(StackFrame frame) {
            if (frame?.Binary == null) return false;
            var bu = frame.Binary.ToUpperInvariant();
            return bu.Contains("UNITY") || bu.Contains("FMOD") || bu.Contains("SPINE");
        }

        private void DrawFallbackAnalysis(FallbackAnalysis fallback) {
            EditorGUILayout.HelpBox(
                "MallocStackLogging is not active in this memgraph.\n" +
                "Showing alternative analysis using zone, vmmap, call tree, and pattern data.",
                MessageType.Info);

            GUILayout.Space(4);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel) {
                normal = { textColor = FallbackHeaderColor }
            };

            // Allocation Context
            if (fallback.Contexts.Count > 0) {
                GUILayout.Label("[Allocation Context]", headerStyle);
                foreach (var ctx in fallback.Contexts) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(ctx.ZoneName))
                        parts.Add($"Zone: {ctx.ZoneName}");
                    if (!string.IsNullOrEmpty(ctx.VmmapRegionType))
                        parts.Add($"Region: {ctx.VmmapRegionType}");
                    if (!string.IsNullOrEmpty(ctx.VmmapProtection))
                        parts.Add($"Protection: {ctx.VmmapProtection}");
                    if (!string.IsNullOrEmpty(ctx.AllocatorType))
                        parts.Add($"Allocator: {ctx.AllocatorType}");
                    if (ctx.VmmapRegionSize > 0)
                        parts.Add($"RegionSize: {VmmapParser.FormatSize(ctx.VmmapRegionSize)}");
                    GUILayout.Label(string.Join(" | ", parts));
                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.Space(4);
            }

            // Related Callers
            if (fallback.RelatedCallers.Count > 0) {
                GUILayout.Label("[Related Callers] (from global call tree)", headerStyle);
                foreach (var caller in fallback.RelatedCallers) {
                    bool isUser = CallTreeParser.IsUserCode(caller.FunctionName);
                    string displayName = CallTreeParser.FormatFunctionName(caller.FunctionName);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    if (isUser) {
                        var style = new GUIStyle(EditorStyles.label) {
                            normal = { textColor = UserCodeColor }
                        };
                        GUILayout.Label("[C#]", style, GUILayout.Width(30));
                        GUILayout.Label(displayName, style);
                    } else {
                        GUILayout.Label("    ", GUILayout.Width(30));
                        GUILayout.Label(displayName);
                    }
                    GUILayout.Label(VmmapParser.FormatSize(caller.TotalBytes), EditorStyles.boldLabel,
                        GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.Space(4);
            }

            // Inferred Origins
            if (fallback.Inferences.Count > 0) {
                GUILayout.Label("[Inferred Origins]", headerStyle);
                foreach (var inference in fallback.Inferences) {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    GUILayout.Label($">> {inference}", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}
