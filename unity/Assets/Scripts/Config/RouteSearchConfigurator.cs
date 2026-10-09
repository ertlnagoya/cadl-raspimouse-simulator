using System;
using LineTrace;

namespace CADLConfig
{
    /// <summary>
    /// Bridges CADL config algorithm names to RouteSearch method calls.
    /// Use this in Pilot/Pilot_CSoS scripts to select the routing algorithm
    /// based on the CADL config instead of hardcoding.
    ///
    /// Governance parameters influence routing:
    ///   - alpha: Scales occupancy penalties in DirectionDijkstra (applied to RouteSearch)
    ///   - lambda: Exploration probability — with probability lambda, a random
    ///             neighbor is chosen instead of the optimal route
    ///
    /// Example usage in Pilot_CSoS.cs:
    ///   int nextNode = RouteSearchConfigurator.Route(routeSearch, src, goal);
    /// </summary>
    public static class RouteSearchConfigurator
    {
        /// <summary>
        /// Route from src to goal using the algorithm specified in CADL config.
        /// Applies governance lambda (exploration probability).
        /// Falls back to DirectionDijkstra if config is not loaded.
        /// </summary>
        public static int Route(RouteSearch rs, int src, int goal, string templateId = "ROBOT")
        {
            return Route(rs, src, goal, templateId, explore: true);
        }

        /// <summary>
        /// Route with explicit control over lambda exploration.
        /// Set explore=false for deterministic path planning (e.g. CalculateFullPath).
        /// </summary>
        public static int Route(RouteSearch rs, int src, int goal, string templateId, bool explore)
        {
            // Governance lambda: exploration probability (only for per-step decisions)
            if (explore)
            {
                float lambda = SimulatorConfigurator.GetGovernanceParam("lambda");
                if (lambda > 0f && UnityEngine.Random.value < lambda)
                {
                    return rs.RandomDijkstra(src);
                }
            }

            string algo = SimulatorConfigurator.GetRoutingAlgorithm(templateId);
            return RouteByName(rs, src, goal, algo);
        }

        /// <summary>
        /// Route using a named algorithm (no governance exploration applied).
        /// </summary>
        public static int RouteByName(RouteSearch rs, int src, int goal, string algorithmName)
        {
            switch (algorithmName)
            {
                case "NaiveDijkstra":
                    return rs.NaiveDijkstra(src, goal);

                case "FlagDijkstra":
                    return rs.FlagDijkstra(src, goal);

                case "DirectionDijkstra":
                    return rs.DirectionDijkstra(src, goal);

                case "RandomDijkstra":
                    return rs.RandomDijkstra(src);

                // MCP-SoS uses NaiveDijkstra locally
                case "occupancy_aware_routing":
                case "LLM_Dijkstra":
                    return rs.NaiveDijkstra(src, goal);

                default:
                    return rs.DirectionDijkstra(src, goal);
            }
        }
    }
}
