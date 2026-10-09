using System.Collections.Generic;
using UnityEngine;

namespace MCP
{
    /// <summary>
    /// Manages edge and node occupancy for collision avoidance.
    /// Singleton - replaces Go arbitrator's StateTable/EdgeFlags/CrossFlags.
    /// </summary>
    public class OccupancyManager : MonoBehaviour
    {
        public static OccupancyManager Instance { get; private set; }

        // Edge occupancy: (src, dst) -> agentId
        private Dictionary<(int, int), int> edgeOccupancy = new Dictionary<(int, int), int>();

        // Node (cross) occupancy: set of occupied node IDs
        private HashSet<int> crossOccupancy = new HashSet<int>();

        // Agent state tracking: agentId -> (src, dst)
        private Dictionary<int, (int src, int dst)> agentEdges = new Dictionary<int, (int, int)>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Debug.Log("[OccupancyManager] Initialized");
        }

        /// <summary>
        /// Check if edge (src -> dst) is available for movement.
        /// Mirrors RouteSearch.Available() and Go arbitrator's Available() logic:
        ///   1. Edge src→dst not occupied
        ///   2. Reverse edge dst→src not occupied
        ///   3. Source node src not occupied by another crossing agent
        /// </summary>
        public bool IsAvailable(int src, int dst)
        {
            // Check if edge is occupied (either direction)
            if (edgeOccupancy.ContainsKey((src, dst)))
                return false;
            if (edgeOccupancy.ContainsKey((dst, src)))
                return false;

            // Check if source node is occupied (matches CrossFlags[src] in Go arbitrator)
            if (crossOccupancy.Contains(src))
                return false;

            return true;
        }

        /// <summary>
        /// Attempt to occupy edge (src -> dst) for agent.
        /// Returns true if successful, false if blocked.
        /// </summary>
        public bool TryOccupy(int agentId, int src, int dst)
        {
            if (!IsAvailable(src, dst))
                return false;

            // Occupy the edge
            edgeOccupancy[(src, dst)] = agentId;

            // Occupy destination node
            crossOccupancy.Add(dst);

            // Track agent's current edge
            agentEdges[agentId] = (src, dst);

            Debug.Log($"[OccupancyManager] Agent {agentId} occupied edge {src}->{dst}");
            return true;
        }

        /// <summary>
        /// Release edge occupation when agent leaves a node.
        /// Called when agent arrives at next node.
        /// </summary>
        public void Release(int agentId, int fromNode)
        {
            // Find and remove edges starting from this node for this agent
            var toRemove = new List<(int, int)>();
            foreach (var kv in edgeOccupancy)
            {
                if (kv.Value == agentId && kv.Key.Item1 == fromNode)
                {
                    toRemove.Add(kv.Key);
                }
            }

            foreach (var key in toRemove)
            {
                edgeOccupancy.Remove(key);
                // Also release the destination node (agent has arrived there)
                crossOccupancy.Remove(key.Item2);
                Debug.Log($"[OccupancyManager] Agent {agentId} released edge {key.Item1}->{key.Item2}");
            }

            // Release the source node (agent has left it)
            crossOccupancy.Remove(fromNode);

            // Update agent edges
            agentEdges.Remove(agentId);
        }

        /// <summary>
        /// Fully release all occupancy for an agent (on destroy/goal).
        /// </summary>
        public void ReleaseAll(int agentId)
        {
            // Remove all edges for this agent
            var toRemove = new List<(int, int)>();
            foreach (var kv in edgeOccupancy)
            {
                if (kv.Value == agentId)
                    toRemove.Add(kv.Key);
            }

            foreach (var key in toRemove)
            {
                edgeOccupancy.Remove(key);
                crossOccupancy.Remove(key.Item2);
            }

            agentEdges.Remove(agentId);
            Debug.Log($"[OccupancyManager] Agent {agentId} fully released");
        }

        /// <summary>
        /// Number of currently occupied edges.
        /// </summary>
        public int OccupiedEdgeCount => edgeOccupancy.Count;

        /// <summary>
        /// Get current edge flags for MCP query.
        /// </summary>
        public Dictionary<string, int> GetEdgeFlags()
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in edgeOccupancy)
            {
                result[$"{kv.Key.Item1}->{kv.Key.Item2}"] = kv.Value;
            }
            return result;
        }

        /// <summary>
        /// Get occupied nodes for MCP query.
        /// </summary>
        public List<int> GetCrossFlags()
        {
            return new List<int>(crossOccupancy);
        }

        /// <summary>
        /// Get agent's current edge, if any.
        /// </summary>
        public (int src, int dst)? GetAgentEdge(int agentId)
        {
            if (agentEdges.TryGetValue(agentId, out var edge))
                return edge;
            return null;
        }

        /// <summary>
        /// Reset all occupancy (for scene reload).
        /// </summary>
        public void Clear()
        {
            edgeOccupancy.Clear();
            crossOccupancy.Clear();
            agentEdges.Clear();
            Debug.Log("[OccupancyManager] Cleared all occupancy");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
