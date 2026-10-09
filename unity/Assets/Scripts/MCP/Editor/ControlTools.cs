using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Alias for cleaner code
using SuccessResponse = MCPForUnity.Editor.Helpers.SuccessResponse;
using ErrorResponse = MCPForUnity.Editor.Helpers.ErrorResponse;

namespace MCP.Tools
{
    /// <summary>
    /// MCP tools for controlling agents (SET operations).
    /// </summary>
    [McpForUnityTool("control", Description = @"Control robot agent movement. Use execute_custom_tool with tool_name='control'.

ACTIONS (pass as 'action' parameter):
- set_path: Set path for agent. Params: id (agent), path (list of node IDs). Use to recover from Collision status.
- yield: Stop agent and yield to others. Params: id
- resume: Resume yielded agent. Params: id
- emergency_stop: Emergency stop. Params: id
- set_path_mode: Set path calculation mode for ALL agents. Params: mode ('static' or 'dynamic'). Call BEFORE play.

WORKFLOW: 1) get world_state 2) find Collision status agents 3) compute path with dijkstra 4) set_path
EXAMPLE: {""action"":""set_path_mode"",""mode"":""dynamic""}")]
    public static class ControlTools
    {
        /// <summary>
        /// Parameters for control tool
        /// </summary>
        public class Parameters
        {
            [ToolParameter("Action: set_path|yield|resume|emergency_stop|set_path_mode")]
            public string action { get; set; }

            [ToolParameter("Agent ID (0-9)", Required = false)]
            public int id { get; set; }

            [ToolParameter("Path for set_path (list of node IDs to traverse)", Required = false)]
            public List<int> path { get; set; }

            [ToolParameter("Path mode for set_path_mode: 'static' or 'dynamic'", Required = false)]
            public string mode { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLower();

            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action required. Valid: set_path, yield, resume, emergency_stop");
            }

            switch (action)
            {
                case "set_path":
                    var pathToken = @params["path"];
                    List<int> path = null;
                    if (pathToken != null && pathToken.Type == JTokenType.Array)
                    {
                        path = pathToken.ToObject<List<int>>();
                    }
                    return SetPath(
                        @params["id"]?.ToObject<int>() ?? -1,
                        path
                    );

                case "yield":
                    return Yield(@params["id"]?.ToObject<int>() ?? -1);

                case "resume":
                    return Resume(@params["id"]?.ToObject<int>() ?? -1);

                case "emergency_stop":
                    return EmergencyStop(@params["id"]?.ToObject<int>() ?? -1);

                case "set_path_mode":
                    return SetPathMode(@params["mode"]?.ToString());

                default:
                    return new ErrorResponse($"Unknown action: {action}. Valid: set_path, yield, resume, emergency_stop, set_path_mode");
            }
        }

        private static object SetPath(int id, List<int> path)
        {
            if (id < 0)
            {
                return new ErrorResponse("'id' parameter required");
            }

            if (path == null || path.Count == 0)
            {
                return new ErrorResponse("'path' parameter required (list of node IDs)");
            }

            var registry = AgentRegistry.Instance;
            var pilot = registry?.GetPilot(id);

            if (pilot == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            var state = registry.GetAgent(id);
            string previousStatus = state?.Status.ToString() ?? "unknown";

            pilot.SetPath(path);

            return new SuccessResponse("Path set", new
            {
                id = id,
                path = path,
                previous_status = previousStatus,
                status = "idle"
            });
        }

        private static object Yield(int id)
        {
            if (id < 0)
            {
                return new ErrorResponse("'id' parameter required");
            }

            var registry = AgentRegistry.Instance;
            var pilot = registry?.GetPilot(id);

            if (pilot == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            pilot.Yield();
            return new SuccessResponse($"Agent {id} yielding", new
            {
                id = id,
                status = "yielding"
            });
        }

        private static object Resume(int id)
        {
            if (id < 0)
            {
                return new ErrorResponse("'id' parameter required");
            }

            var registry = AgentRegistry.Instance;
            var pilot = registry?.GetPilot(id);

            if (pilot == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            pilot.Resume();
            return new SuccessResponse($"Agent {id} resumed", new
            {
                id = id,
                status = "idle"
            });
        }

        private static object EmergencyStop(int id)
        {
            if (id < 0)
            {
                return new ErrorResponse("'id' parameter required");
            }

            var registry = AgentRegistry.Instance;
            var pilot = registry?.GetPilot(id);

            if (pilot == null)
            {
                return new ErrorResponse($"Agent {id} not found");
            }

            pilot.EmergencyStop();
            return new SuccessResponse($"Agent {id} emergency stopped", new
            {
                id = id,
                status = "error"
            });
        }

        private static object SetPathMode(string mode)
        {
            if (string.IsNullOrEmpty(mode))
            {
                return new ErrorResponse("'mode' parameter required: 'static' or 'dynamic'");
            }

            PathMode targetMode;
            switch (mode.ToLower())
            {
                case "static":
                    targetMode = PathMode.Static;
                    break;
                case "dynamic":
                    targetMode = PathMode.Dynamic;
                    break;
                default:
                    return new ErrorResponse($"Invalid mode: {mode}. Valid: 'static', 'dynamic'");
            }

            var registry = AgentRegistry.Instance;
            if (registry == null)
            {
                return new ErrorResponse("AgentRegistry not initialized");
            }

            // Set pathMode for all registered agents
            int count = 0;
            foreach (int id in registry.GetAllIds())
            {
                var pilot = registry.GetPilot(id);
                if (pilot != null)
                {
                    pilot.pathMode = targetMode;
                    count++;
                }
            }

            if (count == 0)
            {
                return new ErrorResponse("No agents found");
            }

            return new SuccessResponse($"Path mode set to {mode} for {count} agents", new
            {
                mode = mode.ToLower(),
                agent_count = count
            });
        }
    }
}
