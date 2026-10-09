using LineTrace;
using LineTrace.Handlers;
using RasPiMouse;
using System.Collections.Generic;
using UnityEngine;
using CADLConfig;

namespace MCP
{
    /// <summary>
    /// Path calculation mode for autonomous navigation.
    /// </summary>
    public enum PathMode
    {
        Static,   // Calculate full path once when goal is set (deterministic, no occupancy)
        Dynamic   // Recalculate at each node (considers occupancy, may deadlock)
    }

    /// <summary>
    /// MCP-controlled pilot for robot navigation.
    /// Supports autonomous navigation with MCP goal override.
    /// </summary>
    public class Pilot_MCP : MonoBehaviour
    {
        private const int MaxPathLength = 20;
        /// <summary>
        /// Max retries scaled by governance beta (centralization).
        /// Higher beta = more retries (more patient with central coordination).
        /// Formula: base_retries * (1 + beta), so beta=0.3 → 130% of base.
        /// </summary>
        private static int MaxRetryCount
        {
            get
            {
                int baseRetries = SimulatorConfigurator.GetMaxRetries(3);
                float beta = SimulatorConfigurator.GetGovernanceParam("beta");
                return Mathf.Max(1, Mathf.RoundToInt(baseRetries * (1f + beta)));
            }
        }

        [Header("Agent Configuration")]
        public int id;
        public int src;
        public int dst;
        public int goal;

        [Header("Runtime State")]
        public int level;
        public AgentStatus status = AgentStatus.Idle;

        [Header("Autonomous Mode")]
        public bool autonomousMode = true; // Enable autonomous navigation
        public PathMode pathMode = PathMode.Static; // Static = predictable, Dynamic = occupancy-aware
        public int retryCount = 0;
        private float retryTimer = 0f;

        // Planned path for Static mode (list of nodes from current to goal)
        private List<int> plannedPath = new List<int>();

        // Internal components
        private Mouse mouse;
        private Handler handler;
        private Cross preCross = null;
        private bool reted = true;
        private float progress = 0f;
        private Vector3 lastPosition;

        // Route calculation
        private RouteSearch routeSearch;

        // Edge traversal tracking
        private float edgeStartTime;
        private float estimatedEdgeTime = 2f;

        private static string[] RMColor => CADLConfig.GraphDefinition.RMColor;

        #region Unity Lifecycle

        private void Awake()
        {
            mouse = GetComponent<Mouse>();
            handler = new Handler(mouse, RMColor[id % RMColor.Length], mcpMode: true);
            lastPosition = transform.position;

            // Initialize route search from shared graph definition
            routeSearch = GraphDefinition.CreateRouteSearch();

            // Apply path mode from CADL config if available
            string configPathMode = SimulatorConfigurator.GetPathMode();
            if (configPathMode == "dynamic")
                pathMode = PathMode.Dynamic;
            else
                pathMode = PathMode.Static;
        }

        private void Start()
        {
            // Register with AgentRegistry (in Start to ensure AgentRegistry.Awake runs first)
            if (AgentRegistry.Instance != null)
            {
                AgentRegistry.Instance.Register(id, this);
            }
            else
            {
                Debug.LogWarning($"[Pilot_MCP] AgentRegistry not found. Agent {id} not registered.");
            }

            // Validate initial node positions against graph size
            src = GraphDefinition.ClampNode(src, $"Agent {id} src");
            dst = GraphDefinition.ClampNode(dst, $"Agent {id} dst");

            // Always assign random goal at start (like D-SoS original)
            // Scene file may have goal=0, which causes deadlock
            goal = RandomGoal();

            // Calculate initial path for Static mode
            if (pathMode == PathMode.Static)
            {
                CalculateFullPath(src, goal);
            }

            UpdateRegistryState();
            Debug.Log($"[Pilot_MCP] Agent {id} initialized at node {src}, goal {goal}, path mode {pathMode}");
        }

        private void Update()
        {
            // Check for collision back completed - enter Collision status
            if (handler.CollisionBackCompleted && status != AgentStatus.Collision)
            {
                status = AgentStatus.Collision;
                Debug.Log($"[Pilot_MCP] Agent {id} entered Collision status, waiting for LLM set_path");
                UpdateRegistryState();
                return;
            }

            // If in Collision status, do nothing (wait for LLM set_path)
            if (status == AgentStatus.Collision)
            {
                handler.Handle(); // Keep handler running (robot stopped)
                return;
            }

            // Handle retry in autonomous mode
            if (autonomousMode && retryCount > 0)
            {
                mouse.Stop();
                retryTimer += Time.deltaTime;
                if (retryTimer > 1f)
                {
                    retryCount--;
                    retryTimer = 0f;
                    TryAutonomousMove();
                }
                return;
            }

            // Update progress based on movement
            if (status == AgentStatus.Moving)
            {
                float elapsed = Time.time - edgeStartTime;
                progress = Mathf.Clamp01(elapsed / estimatedEdgeTime);
            }

            // Handle movement
            handler.Handle();

            // Auto-release when leaving a node
            if (!reted && preCross != null)
            {
                float dist = (preCross.transform.position - transform.position).magnitude;
                if (dist > CADLConfig.SimulatorConfigurator.GetIntersectionThreshold())
                {
                    reted = true;
                    OccupancyManager.Instance?.Release(id, src);
                }
            }

            // Update registry state
            UpdateRegistryState();
        }

        private void OnTriggerEnter(Collider other)
        {
            var cross = other.GetComponent<Cross>();
            if (cross == null || preCross == cross) return;

            // Ignore Cross nodes outside the current graph range
            if (!GraphDefinition.IsValidNode(cross.number))
            {
                Debug.LogWarning($"[Pilot_MCP] Agent {id}: Ignoring Cross {cross.number} (outside graph range [0,{GraphDefinition.NumNodes}))");
                return;
            }

            preCross = cross;

            // Release previous edge
            if (!reted)
            {
                OccupancyManager.Instance?.Release(id, src);
                reted = true;
            }

            // Validate arrival
            if (cross.number != dst)
            {
                Debug.LogWarning($"[Pilot_MCP] Agent {id}: Expected {dst}, arrived at {cross.number}");
                dst = cross.number;
            }

            // Update position
            src = cross.number;
            status = AgentStatus.Idle;
            progress = 0f;

            // Check goal
            if (cross.number == goal)
            {
                level++;
                Debug.Log($"[Goal] RM: {RMColor[id % RMColor.Length]} Goal count: {level} time[s]: {Time.time}");
                goal = RandomGoal(); // Auto-assign new goal

                // Recalculate path for Static mode
                if (pathMode == PathMode.Static)
                {
                    CalculateFullPath(src, goal);
                }
            }

            // Autonomous navigation
            if (autonomousMode)
            {
                TryAutonomousMove();
            }

            UpdateRegistryState();
        }

        /// <summary>
        /// Try to move autonomously using planned path (Static) or dijkstra (Dynamic).
        /// </summary>
        private void TryAutonomousMove()
        {
            if (preCross == null) return;

            int nextNode;

            if (pathMode == PathMode.Static)
            {
                // Static mode: follow planned path, no occupancy check
                if (plannedPath.Count == 0)
                {
                    // No path — recalculate if we haven't reached goal
                    if (src != goal)
                    {
                        CalculateFullPath(src, goal);
                        if (plannedPath.Count == 0)
                        {
                            status = AgentStatus.Idle;
                            return;
                        }
                    }
                    else
                    {
                        status = AgentStatus.Idle;
                        return;
                    }
                }
                nextNode = plannedPath[0];

                // Skip if nextNode == src (e.g., recovering from mid-edge collision)
                while (nextNode == src && plannedPath.Count > 1)
                {
                    plannedPath.RemoveAt(0);
                    nextNode = plannedPath[0];
                }

                // Verify nextNode is adjacent to src
                if (!IsAdjacent(src, nextNode))
                {
                    Debug.LogWarning($"[Pilot_MCP] Agent {id}: Path mismatch at {src}, next {nextNode} not adjacent. Recalculating.");
                    CalculateFullPath(src, goal);
                    if (plannedPath.Count == 0)
                    {
                        status = AgentStatus.Idle;
                        return;
                    }
                    nextNode = plannedPath[0];
                    if (!IsAdjacent(src, nextNode))
                    {
                        Debug.LogError($"[Pilot_MCP] Agent {id}: Still not adjacent after recalc: {src}->{nextNode}");
                        status = AgentStatus.Idle;
                        return;
                    }
                }

                plannedPath.RemoveAt(0);
            }
            else
            {
                // Dynamic mode: recalculate from current position
                nextNode = RouteSearchConfigurator.Route(routeSearch, src, goal);

                // Verify adjacency
                if (!IsAdjacent(src, nextNode))
                {
                    Debug.LogWarning($"[Pilot_MCP] Agent {id}: Dynamic route {src}->{nextNode} not adjacent, using fallback");
                    nextNode = GetRandomAdjacentNode(src);
                    if (nextNode == -1)
                    {
                        status = AgentStatus.Idle;
                        return;
                    }
                }

                // Check occupancy
                if (OccupancyManager.Instance != null && !OccupancyManager.Instance.TryOccupy(id, src, nextNode))
                {
                    retryCount = MaxRetryCount;
                    status = AgentStatus.Waiting;
                    CADLConfig.CADLMetricsCollector.RecordRoutingRequest(true);
                    Debug.Log($"[Pilot_MCP] Agent {id} blocked at {src}, waiting to move to {nextNode}");
                    return;
                }
            }

            // Record successful routing request
            CADLConfig.CADLMetricsCollector.RecordRoutingRequest(false);

            // Execute move
            if (src == nextNode)
            {
                handler.SetBack();
                OccupancyManager.Instance?.Release(id, src);
            }
            else
            {
                Vector3 dir = preCross.GetDir(nextNode);
                handler.SetCross(preCross.transform.position, dir);
                dst = nextNode;
                status = AgentStatus.Moving;
                reted = false;
                edgeStartTime = Time.time;
                progress = 0f;
            }
        }

        /// <summary>
        /// Check if two nodes are directly connected (adjacent).
        /// </summary>
        private bool IsAdjacent(int from, int to)
        {
            if (from < 0 || to < 0 || from >= routeSearch.Size || to >= routeSearch.Size) return false;
            return routeSearch.Edges[from][to] < RouteSearch.NoEdge;
        }

        /// <summary>
        /// Get a random adjacent node as fallback.
        /// </summary>
        private int GetRandomAdjacentNode(int node)
        {
            var neighbors = new System.Collections.Generic.List<int>();
            for (int i = 0; i < routeSearch.Size; i++)
            {
                if (routeSearch.Edges[node][i] < RouteSearch.NoEdge) neighbors.Add(i);
            }
            if (neighbors.Count == 0) return -1;
            return neighbors[Random.Range(0, neighbors.Count)];
        }

        /// <summary>
        /// Calculate full path from start to end using the configured algorithm.
        /// Stores result in plannedPath (excluding start, including end).
        /// </summary>
        private void CalculateFullPath(int start, int end)
        {
            plannedPath.Clear();

            if (start == end) return;

            // Build full path by repeatedly calling the configured algorithm
            int current = start;
            HashSet<int> visited = new HashSet<int> { start };

            while (current != end)
            {
                int next = RouteSearchConfigurator.Route(routeSearch, current, end, "ROBOT", explore: false);
                if (next == current || visited.Contains(next))
                {
                    Debug.LogError($"[Pilot_MCP] Agent {id}: Path calculation stuck at {current}");
                    break;
                }
                plannedPath.Add(next);
                visited.Add(next);
                current = next;

                // Safety limit
                if (plannedPath.Count > MaxPathLength)
                {
                    Debug.LogError($"[Pilot_MCP] Agent {id}: Path too long, aborting");
                    break;
                }
            }

            Debug.Log($"[Pilot_MCP] Agent {id}: Planned path {start} -> [{string.Join(",", plannedPath)}]");
        }

        private void OnDestroy()
        {
            OccupancyManager.Instance?.ReleaseAll(id);
            AgentRegistry.Instance?.Unregister(id);
        }

        #endregion

        #region MCP Control Interface

        /// <summary>
        /// Set path (called from MCP ControlTools).
        /// Overwrites plannedPath and resumes movement.
        /// Use this to recover from Collision status or redirect on edge.
        /// </summary>
        public void SetPath(List<int> path)
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogWarning($"[Pilot_MCP] Agent {id}: SetPath called with empty path");
                return;
            }

            plannedPath.Clear();
            plannedPath.AddRange(path);
            int nextNode = path[0];

            Debug.Log($"[Pilot_MCP] Agent {id} path set: [{string.Join(",", plannedPath)}]");

            // Check if at node or on edge
            if (IsAtNode())
            {
                // At node: resume and trigger autonomous move
                if (status == AgentStatus.Collision || status == AgentStatus.Yielding)
                {
                    handler.ResumeFromWait();
                }
                status = AgentStatus.Idle;
                UpdateRegistryState();

                if (autonomousMode && preCross != null)
                {
                    TryAutonomousMove();
                }
            }
            else
            {
                // On edge: determine direction based on status
                int facingNode;
                if (status == AgentStatus.Collision)
                {
                    // After collision reverse, facing src direction
                    facingNode = src;
                }
                else
                {
                    // Yielding or other: was moving toward dst
                    facingNode = dst;
                }

                int oppositeNode = (facingNode == src) ? dst : src;

                if (nextNode == facingNode)
                {
                    // Same direction: just resume Go
                    handler.ResumeFromWait();
                    Debug.Log($"[Pilot_MCP] Agent {id} resuming toward {facingNode}");
                }
                else if (nextNode == oppositeNode)
                {
                    // Opposite direction: reverse and go
                    handler.ReverseAndGo();
                    Debug.Log($"[Pilot_MCP] Agent {id} reversing toward {oppositeNode}");
                }
                else
                {
                    // Invalid nextNode (not on this edge): proceed toward facing direction
                    handler.ResumeFromWait();
                    Debug.LogWarning($"[Pilot_MCP] Agent {id}: nextNode {nextNode} not on edge {src}-{dst}, proceeding toward {facingNode}");
                }

                status = AgentStatus.Moving;
                UpdateRegistryState();
            }
        }

        /// <summary>
        /// Move to next node (called from MCP ControlTools).
        /// Assumes occupancy already checked/acquired.
        /// </summary>
        public bool MoveTo(int nextNode)
        {
            if (preCross == null)
            {
                Debug.LogError($"[Pilot_MCP] Agent {id}: Cannot move, no current cross reference");
                return false;
            }

            // Get direction to next node
            try
            {
                Vector3 dir = preCross.GetDir(nextNode);
                handler.SetCross(preCross.transform.position, dir);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Pilot_MCP] Agent {id}: Invalid move {src}->{nextNode}: {ex.Message}");
                return false;
            }

            dst = nextNode;
            status = AgentStatus.Moving;
            reted = false;
            edgeStartTime = Time.time;
            progress = 0f;

            Debug.Log($"[Pilot_MCP] Agent {id} moving from {src} to {nextNode}");
            UpdateRegistryState();
            return true;
        }

        /// <summary>
        /// Yield to another agent (stop and wait).
        /// </summary>
        public void Yield()
        {
            handler.SetWait();
            status = AgentStatus.Yielding;
            Debug.Log($"[Pilot_MCP] Agent {id} yielding");
            UpdateRegistryState();
        }

        /// <summary>
        /// Resume from yield state.
        /// </summary>
        public void Resume()
        {
            if (status == AgentStatus.Yielding)
            {
                handler.ResumeFromWait();

                if (IsAtNode())
                {
                    // At node: trigger autonomous move
                    status = AgentStatus.Idle;
                    Debug.Log($"[Pilot_MCP] Agent {id} resumed at node");
                    UpdateRegistryState();

                    if (autonomousMode && preCross != null)
                    {
                        TryAutonomousMove();
                    }
                }
                else
                {
                    // On edge: continue moving (GoHandler will take over)
                    status = AgentStatus.Moving;
                    Debug.Log($"[Pilot_MCP] Agent {id} resumed on edge, continuing toward {dst}");
                    UpdateRegistryState();
                }
            }
        }

        /// <summary>
        /// Emergency stop.
        /// </summary>
        public void EmergencyStop()
        {
            mouse.Stop();
            status = AgentStatus.Error;
            Debug.LogWarning($"[Pilot_MCP] Agent {id} emergency stopped");
            UpdateRegistryState();
        }

        /// <summary>
        /// Get current node (src if at node, -1 if on edge).
        /// </summary>
        public int CurrentNode => status == AgentStatus.Moving ? -1 : src;

        /// <summary>
        /// Get current state for registry.
        /// </summary>
        public AgentState GetState()
        {
            return new AgentState
            {
                Id = id,
                CurrentNode = CurrentNode,
                CurrentEdgeSrc = status == AgentStatus.Moving ? src : -1,
                CurrentEdgeDst = status == AgentStatus.Moving ? dst : -1,
                Goal = goal,
                Progress = progress,
                Status = status,
                Level = level,
                PlannedPath = new List<int>(plannedPath) // Copy of remaining path
            };
        }

        #endregion

        #region Private Methods

        private void UpdateRegistryState()
        {
            AgentRegistry.Instance?.UpdateState(id, GetState());
        }

        private int RandomGoal() => CADLConfig.GraphDefinition.RandomGoal(dst);

        /// <summary>
        /// Check if robot is physically at a node (near preCross position).
        /// </summary>
        private bool IsAtNode()
        {
            if (preCross == null) return false;
            float dist = (preCross.transform.position - transform.position).magnitude;
            return dist < 0.25f;
        }

        #endregion
    }
}
