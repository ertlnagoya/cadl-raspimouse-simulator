using System.Collections.Generic;
using System.Linq;
using CADLConfig;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Alias for cleaner code
using Response = MCPForUnity.Editor.Helpers.SuccessResponse;
using ErrorResponse = MCPForUnity.Editor.Helpers.ErrorResponse;

namespace MCP.Tools
{
    /// <summary>
    /// MCP tools for querying world state (GET operations).
    /// </summary>
    [McpForUnityTool("world_state", Description = @"Query robot swarm simulation state. Use execute_custom_tool with tool_name='world_state'.

ACTIONS (pass as 'action' parameter):
- get_all: Get all agents' state (id, node, goal, status, progress)
- get_agent: Get single agent by 'id' parameter
- get_nearby: Get agents within 'distance' hops from agent 'id'
- get_occupancy: Get edge/node occupation map (which edges are blocked)
- get_graph: Get static graph structure (11 nodes, 17 edges)
- get_config: Get simulation config (path_mode: static/dynamic)

EXAMPLE: {""action"":""get_all""} or {""action"":""get_config""}")]
    public static class WorldStateTools
    {
        /// <summary>
        /// Parameters for world_state tool
        /// </summary>
        public class Parameters
        {
            [ToolParameter("Action: get_all|get_agent|get_nearby|get_occupancy|get_graph|get_config")]
            public string action { get; set; }

            [ToolParameter("Agent ID (for get_agent, get_nearby)", Required = false)]
            public int? id { get; set; }

            [ToolParameter("Max graph distance for get_nearby (default: 2)", Required = false)]
            public int? distance { get; set; }
        }

        // Graph data sourced from centralized GraphDefinition (no more duplication)

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLower() ?? "get_all";

            switch (action)
            {
                case "get_all":
                    return GetAllAgents();

                case "get_agent":
                    int agentId = @params["id"]?.ToObject<int>() ?? -1;
                    return GetAgent(agentId);

                case "get_nearby":
                    int nearbyId = @params["id"]?.ToObject<int>() ?? -1;
                    int distance = @params["distance"]?.ToObject<int>() ?? 2;
                    return GetNearby(nearbyId, distance);

                case "get_occupancy":
                    return GetOccupancy();

                case "get_graph":
                    return GetGraph();

                case "get_config":
                    return GetConfig();

                default:
                    return new ErrorResponse($"Unknown action: {action}. Valid: get_all, get_agent, get_nearby, get_occupancy, get_graph, get_config");
            }
        }

        private static object GetAllAgents()
        {
            var registry = AgentRegistry.Instance;
            if (registry == null)
            {
                return new ErrorResponse("AgentRegistry not initialized. Ensure MCP managers are in scene.");
            }

            var agents = registry.GetAllAgents().Select(a => new
            {
                id = a.Id,
                node = a.CurrentNode,
                edge_src = a.CurrentEdgeSrc,
                edge_dst = a.CurrentEdgeDst,
                goal = a.Goal,
                status = a.Status.ToString().ToLower(),
                progress = a.Progress,
                level = a.Level
            }).ToList();

            return new SuccessResponse($"{agents.Count} agents", new
            {
                count = agents.Count,
                agents = agents,
                time = Time.time
            });
        }

        private static object GetAgent(int id)
        {
            var registry = AgentRegistry.Instance;
            if (registry == null)
            {
                return new ErrorResponse("AgentRegistry not initialized");
            }

            var agent = registry.GetAgent(id);
            if (agent == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            return new SuccessResponse($"Agent {id}", new
            {
                id = agent.Id,
                node = agent.CurrentNode,
                edge_src = agent.CurrentEdgeSrc,
                edge_dst = agent.CurrentEdgeDst,
                goal = agent.Goal,
                status = agent.Status.ToString().ToLower(),
                progress = agent.Progress,
                level = agent.Level
            });
        }

        private static object GetNearby(int id, int maxDistance)
        {
            var registry = AgentRegistry.Instance;
            if (registry == null)
            {
                return new ErrorResponse("AgentRegistry not initialized");
            }

            var agent = registry.GetAgent(id);
            if (agent == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            int sourceNode = agent.CurrentNode >= 0 ? agent.CurrentNode : agent.CurrentEdgeDst;
            if (sourceNode < 0)
            {
                return new ErrorResponse($"Agent {id} has no valid position");
            }

            // Build adjacency list
            var adjacency = BuildAdjacencyList();

            // BFS to find nearby agents
            var distances = new Dictionary<int, int> { [sourceNode] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(sourceNode);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int currentDist = distances[current];

                if (currentDist >= maxDistance) continue;

                if (adjacency.TryGetValue(current, out var neighbors))
                {
                    foreach (int neighbor in neighbors)
                    {
                        if (!distances.ContainsKey(neighbor))
                        {
                            distances[neighbor] = currentDist + 1;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            // Find agents at these nodes
            var nearbyAgents = new List<object>();
            foreach (var state in registry.GetAllAgents())
            {
                if (state.Id == id) continue; // Skip self

                int agentNode = state.CurrentNode >= 0 ? state.CurrentNode : state.CurrentEdgeDst;
                if (agentNode >= 0 && distances.TryGetValue(agentNode, out int dist))
                {
                    nearbyAgents.Add(new
                    {
                        id = state.Id,
                        node = agentNode,
                        distance = dist,
                        status = state.Status.ToString().ToLower()
                    });
                }
            }

            return new SuccessResponse($"{nearbyAgents.Count} nearby agents within distance {maxDistance}", new
            {
                source_agent = id,
                source_node = sourceNode,
                max_distance = maxDistance,
                nearby = nearbyAgents
            });
        }

        private static object GetOccupancy()
        {
            var occupancy = OccupancyManager.Instance;
            if (occupancy == null)
            {
                return new ErrorResponse("OccupancyManager not initialized");
            }

            return new SuccessResponse("Occupancy map", new
            {
                edge_flags = occupancy.GetEdgeFlags(),
                cross_flags = occupancy.GetCrossFlags()
            });
        }

        private static object GetGraph()
        {
            var defaultEdges = GraphDefinition.GetDefaultEdges();
            var adjacency = BuildAdjacencyList(defaultEdges);

            var nodes = Enumerable.Range(0, GraphDefinition.NumNodes).Select(n => new
            {
                id = n,
                neighbors = adjacency.TryGetValue(n, out var list) ? list : new List<int>()
            }).ToList();

            var edges = defaultEdges.Select(e => new
            {
                src = e.Cross1,
                dst = e.Cross2,
                length = e.Length
            }).ToList();

            return new SuccessResponse("Graph structure", new
            {
                node_count = GraphDefinition.NumNodes,
                edge_count = defaultEdges.Count,
                nodes = nodes,
                edges = edges
            });
        }

        private static object GetConfig()
        {
            var registry = AgentRegistry.Instance;
            if (registry == null)
            {
                return new ErrorResponse("AgentRegistry not initialized");
            }

            // Get pathMode from first registered agent (ID-agnostic)
            var allIds = registry.GetAllIds();
            if (allIds.Count == 0)
            {
                return new ErrorResponse("No agents found");
            }

            var pilot = registry.GetPilot(allIds[0]);
            if (pilot == null)
            {
                return new ErrorResponse("No agents found");
            }

            return new SuccessResponse("Simulation config", new
            {
                path_mode = pilot.pathMode.ToString().ToLower(),
                agent_count = allIds.Count
            });
        }

        private static Dictionary<int, List<int>> BuildAdjacencyList(
            List<LineTrace.RouteSearch.Path> edges = null)
        {
            edges = edges ?? GraphDefinition.GetDefaultEdges();
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var e in edges)
            {
                int src = e.Cross1, dst = e.Cross2;
                if (!adjacency.ContainsKey(src))
                    adjacency[src] = new List<int>();
                if (!adjacency.ContainsKey(dst))
                    adjacency[dst] = new List<int>();

                adjacency[src].Add(dst);
                adjacency[dst].Add(src);
            }
            return adjacency;
        }
    }
}
