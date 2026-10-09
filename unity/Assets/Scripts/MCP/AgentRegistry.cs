using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MCP
{
    /// <summary>
    /// Agent status for MCP queries.
    /// </summary>
    public enum AgentStatus
    {
        Idle,       // At node, waiting for command
        Moving,     // Traversing edge
        Waiting,    // Blocked, waiting for clearance (Dynamic mode)
        Collision,  // Physical collision, reversed, waiting for LLM set_path
        Arrived,    // Reached goal
        Yielding,   // Yielding to another agent
        Error       // Error state
    }

    /// <summary>
    /// Agent state data structure for MCP queries.
    /// </summary>
    public class AgentState
    {
        public int Id;
        public int CurrentNode;      // Current node (-1 if on edge)
        public int CurrentEdgeSrc;   // Edge source (if moving)
        public int CurrentEdgeDst;   // Edge destination (if moving)
        public int Goal;             // Goal node
        public float Progress;       // Edge progress 0.0-1.0
        public AgentStatus Status;
        public int Level;            // Goal completion count
        public List<int> PlannedPath; // Remaining planned path (Static mode)
    }

    /// <summary>
    /// Registry for all MCP-controlled agents.
    /// Singleton - provides agent lookup for MCP tools.
    /// </summary>
    public class AgentRegistry : MonoBehaviour
    {
        public static AgentRegistry Instance { get; private set; }

        private Dictionary<int, Pilot_MCP> pilots = new Dictionary<int, Pilot_MCP>();
        private Dictionary<int, AgentState> states = new Dictionary<int, AgentState>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Debug.Log("[AgentRegistry] Initialized");
        }

        /// <summary>
        /// Register an agent (called from Pilot_MCP.Awake).
        /// </summary>
        public void Register(int id, Pilot_MCP pilot)
        {
            pilots[id] = pilot;
            states[id] = new AgentState
            {
                Id = id,
                CurrentNode = -1,
                CurrentEdgeSrc = -1,
                CurrentEdgeDst = -1,
                Goal = -1,
                Progress = 0f,
                Status = AgentStatus.Idle,
                Level = 0
            };
            Debug.Log($"[AgentRegistry] Registered agent {id}");
        }

        /// <summary>
        /// Unregister an agent (called from Pilot_MCP.OnDestroy).
        /// </summary>
        public void Unregister(int id)
        {
            pilots.Remove(id);
            states.Remove(id);
            Debug.Log($"[AgentRegistry] Unregistered agent {id}");
        }

        /// <summary>
        /// Update agent state (called from Pilot_MCP).
        /// </summary>
        public void UpdateState(int id, AgentState state)
        {
            if (states.ContainsKey(id))
            {
                states[id] = state;
            }
        }

        /// <summary>
        /// Get agent state by ID.
        /// </summary>
        public AgentState GetAgent(int id)
        {
            return states.TryGetValue(id, out var state) ? state : null;
        }

        /// <summary>
        /// Get Pilot_MCP component by ID.
        /// </summary>
        public Pilot_MCP GetPilot(int id)
        {
            return pilots.TryGetValue(id, out var pilot) ? pilot : null;
        }

        /// <summary>
        /// Get all agent states.
        /// </summary>
        public List<AgentState> GetAllAgents()
        {
            return states.Values.ToList();
        }

        /// <summary>
        /// Get all agent IDs.
        /// </summary>
        public List<int> GetAllIds()
        {
            return pilots.Keys.ToList();
        }

        /// <summary>
        /// Get agent count.
        /// </summary>
        public int Count => pilots.Count;

        /// <summary>
        /// Find agents at a specific node.
        /// </summary>
        public List<AgentState> GetAgentsAtNode(int node)
        {
            return states.Values.Where(s => s.CurrentNode == node).ToList();
        }

        /// <summary>
        /// Find agents on a specific edge.
        /// </summary>
        public List<AgentState> GetAgentsOnEdge(int src, int dst)
        {
            return states.Values.Where(s =>
                s.CurrentEdgeSrc == src && s.CurrentEdgeDst == dst
            ).ToList();
        }

        /// <summary>
        /// Clear all registrations (for scene reload).
        /// </summary>
        public void Clear()
        {
            pilots.Clear();
            states.Clear();
            Debug.Log("[AgentRegistry] Cleared all agents");
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
