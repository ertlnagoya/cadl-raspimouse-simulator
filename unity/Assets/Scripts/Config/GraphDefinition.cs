using System.Collections.Generic;
using LineTrace;

namespace CADLConfig
{
    /// <summary>
    /// Centralized graph definition for the road network.
    /// Loads from CADL config JSON (simulatorConfig.graph section) if available,
    /// otherwise falls back to the hardcoded 11-node default.
    /// </summary>
    public static class GraphDefinition
    {
        // Cached values loaded from CADL or defaults
        private static int _numNodes = -1;
        private static List<RouteSearch.Path> _edges = null;

        /// <summary>
        /// Number of nodes in the graph.
        /// Loaded from CADL config or defaults to 11.
        /// </summary>
        public static int NumNodes
        {
            get
            {
                if (_numNodes < 0) LoadFromConfig();
                return _numNodes;
            }
        }

        /// <summary>
        /// Shared color names for robot display. Use instead of duplicating in each Pilot script.
        /// </summary>
        public static readonly string[] RMColor = {
            "Red", "Blue", "Green", "Yellow", "Purple",
            "Orange", "Pink", "Brown", "Gray", "White"
        };

        /// <summary>
        /// Generate a random goal node, excluding the specified node.
        /// Uses the standard "pick from N-1, skip excluded" algorithm:
        ///   rv in [0, NumNodes-2], then if rv >= excludeNode: rv++
        ///   Result: uniform over {0,...,NumNodes-1} \ {excludeNode}
        /// </summary>
        public static int RandomGoal(int excludeNode)
        {
            int n = NumNodes;
            int rv = UnityEngine.Random.Range(0, n - 1);
            if (rv >= excludeNode) rv++;
            return rv;
        }

        /// <summary>
        /// Returns the edge list for the graph.
        /// Loaded from CADL config or defaults to the hardcoded 17-edge graph.
        /// </summary>
        public static List<RouteSearch.Path> GetDefaultEdges()
        {
            if (_edges == null) LoadFromConfig();
            // Return a copy to prevent external mutation
            return new List<RouteSearch.Path>(_edges);
        }

        /// <summary>
        /// Check if a node ID is within the valid range [0, NumNodes).
        /// </summary>
        public static bool IsValidNode(int node)
        {
            return node >= 0 && node < NumNodes;
        }

        /// <summary>
        /// Clamp a node ID to the valid range [0, NumNodes).
        /// Logs a warning if clamping was necessary.
        /// </summary>
        public static int ClampNode(int node, string context = "")
        {
            if (node >= 0 && node < NumNodes) return node;
            int clamped = UnityEngine.Mathf.Clamp(node, 0, NumNodes - 1);
            UnityEngine.Debug.LogWarning(
                $"[GraphDefinition] Node {node} out of range [0,{NumNodes}). " +
                $"Clamped to {clamped}. {context}");
            return clamped;
        }

        /// <summary>
        /// Initialize a RouteSearch instance with the current graph.
        /// </summary>
        public static RouteSearch CreateRouteSearch()
        {
            var rs = new RouteSearch();
            rs.Init(NumNodes, GetDefaultEdges());
            return rs;
        }

        /// <summary>
        /// Force reload from CADL config (useful after config change at runtime).
        /// </summary>
        public static void Reload()
        {
            _numNodes = -1;
            _edges = null;
        }

        /// <summary>
        /// Load graph from CADL config JSON. Falls back to hardcoded defaults.
        /// </summary>
        private static void LoadFromConfig()
        {
            var config = SimulatorConfigurator.Config;
            var graph = config?.simulatorConfig?.graph;

            if (graph != null && graph.nodes != null && graph.nodes.Length > 0
                && graph.edges != null && graph.edges.Length > 0)
            {
                // Validate node IDs are contiguous 0..N-1
                // Sparse IDs (e.g., [1,3,5]) are NOT supported because the entire
                // codebase (RouteSearch, RandomGoal, IsValidNode, GraphVisualizer, etc.)
                // assumes all IDs in [0, NumNodes) are valid physical nodes.
                _numNodes = graph.nodes.Length;
                bool contiguous = true;
                var nodeSet = new System.Collections.Generic.HashSet<int>();
                foreach (int nodeId in graph.nodes)
                {
                    nodeSet.Add(nodeId);
                    if (nodeId < 0 || nodeId >= _numNodes) contiguous = false;
                }
                if (!contiguous || nodeSet.Count != _numNodes)
                {
                    UnityEngine.Debug.LogError(
                        $"[GraphDefinition] CADL node IDs must be contiguous 0..{_numNodes - 1}. " +
                        $"Got {nodeSet.Count} unique IDs, range violations detected. " +
                        $"Falling back to hardcoded defaults.");
                    _numNodes = 11;
                    _edges = GetHardcodedEdges();
                    return;
                }

                // Load edges with validation
                _edges = new List<RouteSearch.Path>(graph.edges.Length);
                int skipped = 0;
                foreach (var e in graph.edges)
                {
                    if (e.src < 0 || e.src >= _numNodes || e.dst < 0 || e.dst >= _numNodes)
                    {
                        UnityEngine.Debug.LogError(
                            $"[GraphDefinition] Edge ({e.src} -> {e.dst}) out of range [0,{_numNodes}). Skipping.");
                        skipped++;
                        continue;
                    }
                    _edges.Add(new RouteSearch.Path
                    {
                        Cross1 = e.src,
                        Cross2 = e.dst,
                        Length = e.length
                    });
                }
                UnityEngine.Debug.Log(
                    $"[GraphDefinition] Loaded from CADL config: {_numNodes} nodes, {_edges.Count} edges" +
                    (skipped > 0 ? $" ({skipped} invalid edges skipped)" : ""));
            }
            else
            {
                // Fallback to hardcoded defaults
                _numNodes = 11;
                _edges = GetHardcodedEdges();
                UnityEngine.Debug.Log(
                    $"[GraphDefinition] Using hardcoded defaults: {_numNodes} nodes, {_edges.Count} edges");
            }
        }

        /// <summary>
        /// Hardcoded 11-node, 17-edge default graph (backward compatibility).
        /// </summary>
        private static List<RouteSearch.Path> GetHardcodedEdges()
        {
            return new List<RouteSearch.Path>
            {
                new RouteSearch.Path { Cross1 = 0, Cross2 = 1, Length = 1f },
                new RouteSearch.Path { Cross1 = 1, Cross2 = 2, Length = 1f },
                new RouteSearch.Path { Cross1 = 2, Cross2 = 3, Length = 2f },
                new RouteSearch.Path { Cross1 = 3, Cross2 = 4, Length = 2.1f },
                new RouteSearch.Path { Cross1 = 4, Cross2 = 5, Length = 1.3f },
                new RouteSearch.Path { Cross1 = 5, Cross2 = 6, Length = 2f },
                new RouteSearch.Path { Cross1 = 6, Cross2 = 7, Length = 1.2f },
                new RouteSearch.Path { Cross1 = 7, Cross2 = 0, Length = 1f },
                new RouteSearch.Path { Cross1 = 7, Cross2 = 8, Length = 2.5f },
                new RouteSearch.Path { Cross1 = 0, Cross2 = 8, Length = 1f },
                new RouteSearch.Path { Cross1 = 1, Cross2 = 8, Length = 1f },
                new RouteSearch.Path { Cross1 = 8, Cross2 = 9, Length = 3f },
                new RouteSearch.Path { Cross1 = 6, Cross2 = 9, Length = 1f },
                new RouteSearch.Path { Cross1 = 5, Cross2 = 9, Length = 0.8f },
                new RouteSearch.Path { Cross1 = 4, Cross2 = 10, Length = 1.4f },
                new RouteSearch.Path { Cross1 = 3, Cross2 = 10, Length = 0.8f },
                new RouteSearch.Path { Cross1 = 2, Cross2 = 10, Length = 1.7f },
            };
        }
    }
}
