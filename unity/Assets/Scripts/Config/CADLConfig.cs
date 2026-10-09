using System;
using System.Collections.Generic;

namespace CADLConfig
{
    /// <summary>
    /// Data classes for deserializing CADL-generated Unity config JSON.
    /// Maps to the output of: cadl sim-gen example.cadl --target unity
    /// </summary>

    [Serializable]
    public class EdgeData
    {
        public int src;
        public int dst;
        public float length = 1f;
    }

    [Serializable]
    public class GraphData
    {
        public int[] nodes;
        public EdgeData[] edges;
    }

    [Serializable]
    public class SimulatorConfigData
    {
        public string name;
        public string sosType;
        public string description;
        public EnvironmentData environment;
        public GraphData graph;
    }

    [Serializable]
    public class EnvironmentData
    {
        public int num_nodes = 11;
        public int num_edges = 17;
        public int num_robots = 5;
        public string nats_url = "nats://localhost:4222";
        public float edge_length_min = 0.8f;
        public float edge_length_max = 3.0f;
        public float same_direction_penalty = 1.5f;
        public float opposite_direction_penalty = 7.0f;
        public float intersection_threshold = 0.375f;
        public bool discrete_time_sync = false;
        public string path_mode = "static";
        public string mcp_server = "";
        public int random_seed = -1;  // -1 = no InitState (thesis-faithful); >= 0 = explicit seed
        public int[] start_nodes = null;  // per-robot start node; null = RandomGoal()
    }

    [Serializable]
    public class PlannerData
    {
        public string type;       // "central" or "local"
        public string algorithm;  // e.g. "NaiveDijkstra", "DirectionDijkstra"
    }

    [Serializable]
    public class AgentTemplateData
    {
        public string templateId;
        public string prefab;
        public string role;
        public string autonomy;
        public int count = 1;
        public string[] capabilities;
        public PlannerData planner;
        public string[] inputs;
        public string[] outputs;
    }

    [Serializable]
    public class ChannelData
    {
        public string channelName;
        public string fromTemplate;
        public string toTemplate;
    }

    [Serializable]
    public class NatsSubjectsData
    {
        public string init = "init";
        public string next = "next";
        public string ret = "ret";
        public string fin = "fin";
        public string disp = "disp";
        public string resource = "resource";
        public string goalcount = "goalcount";
        public string stop = "stop";
        // Task-arbitration subjects (FCFS delivery system)
        public string delivery_request = "Delivery_Request";
        public string goal_claim       = "goal_claim";
        public string goal_request     = "goal_request";
        public string task_end         = "end";
        public string task_end_result  = "end_result";
        public string task_retire      = "task_retire";
    }

    [Serializable]
    public class GovernanceData
    {
        public string decisionHolder;
        public float alpha = 0.5f;
        public float beta = 0.5f;
        public float lambda = 0.5f;
    }

    [Serializable]
    public class CommunicationSetupData
    {
        public ChannelData[] channels;
        public NatsSubjectsData nats_subjects;
        public GovernanceData governance;
    }

    [Serializable]
    public class StepData
    {
        public string stepType;
        public string sender;
        public string receiver;
        public string content;
        public string condition;
    }

    [Serializable]
    public class TimingData
    {
        public string max_response;
        public string max_total;
        public string max_detection;
        public string max_resolution;
        public string back_duration;
        public string retry_delay_base;
        public int max_retries = 0;
    }

    [Serializable]
    public class ProtocolData
    {
        public string protocolId;
        public string trigger;
        public StepData[] steps;
        public TimingData timing;
        public string precondition;
        public string postcondition;
    }

    [Serializable]
    public class RegimeTransitionData
    {
        public string fromRegime;
        public string toRegime;
        public string condition;
        public string triggerProtocol;
    }

    [Serializable]
    public class MetricData
    {
        public string id;
        public string formula;
        public string target;
    }

    /// <summary>
    /// Motivation governance config (read by motivation-aware arbitrator and C-SoS robots).
    /// </summary>
    [Serializable]
    public class MotivationConfigData
    {
        public bool enabled;
        public string model;
        public float rho;
        public float kappa;
        public int budgetBase;
        public float waitScale;
        public float[] agentMotivation;
        public string profile;
        /// <summary>
        /// Per-robot delivery cap for C-SoS P1-P4 patterns.
        /// null (or empty) means no limit (standard C-SoS Reference).
        /// </summary>
        public int[] maxDeliveries;

        /// <summary>
        /// Wandering goal selection mode for free-time navigation between deliveries.
        /// "random" (default) = RandomGoal() — picks a random non-current node.
        /// "select" = SelectGoal() — deterministic sequence from wanderingGoalList,
        ///            matching the thesis (卒論2) Pilot_CSoS GoalType.Select behaviour.
        /// </summary>
        public string wanderingGoalMode = "random";

        /// <summary>
        /// Goal-list used by SelectGoal() when wanderingGoalMode == "select".
        /// null or empty → falls back to the thesis hardcoded list:
        ///   { 9, 8, 3, 2, 5, 10, 7, 8, 0, 4, 6, 1 }
        /// </summary>
        public int[] wanderingGoalList = null;

        /// <summary>Returns the delivery cap for a given robot id, or -1 if no cap.</summary>
        public int GetMaxDeliveries(int robotId)
        {
            if (maxDeliveries == null || maxDeliveries.Length == 0) return -1;
            if (robotId < 0 || robotId >= maxDeliveries.Length) return -1;
            return maxDeliveries[robotId];
        }
    }

    /// <summary>
    /// Task-level arbitration config (FCFS delivery task assignment).
    /// </summary>
    [Serializable]
    public class TaskArbitrationData
    {
        public bool enabled = false;
        public string protocol = "fcfs";
        /// <summary>max delay before claiming (motivation=0 → maxClaimDelaySec, motivation=1 → 0).</summary>
        public float maxClaimDelaySec = 3.0f;
        public float deliveryIntervalSec = 5.0f;
        public int[] goalSequence = new int[] { 9, 8, 3, 2, 5, 10, 7, 8, 0, 4, 6, 1 };

        // ── Deadlock recovery ────────────────────────────────────────────────
        /// <summary>
        /// Enable deadlock detection and recovery.
        /// false (default) = thesis-faithful behaviour (no recovery).
        /// true            = D-3 claim timeout + D-4 position-progress watchdog.
        /// </summary>
        public bool deadlockRecoveryEnabled = false;
        /// <summary>
        /// Seconds without arriving at a new node before declaring a deadlock
        /// and forcing a new goal / re-route (Unity-side watchdog, D-4).
        /// Only active when deadlockRecoveryEnabled = true.
        /// </summary>
        public float deadlockDetectionSec = 15.0f;
    }

    /// <summary>
    /// Root config object matching the CADL Unity JSON output.
    /// </summary>
    [Serializable]
    public class CADLSimConfig
    {
        public SimulatorConfigData simulatorConfig;
        public AgentTemplateData[] agentTemplates;
        public CommunicationSetupData communicationSetup;
        public ProtocolData[] protocols;
        public RegimeTransitionData[] regimeTransitions;
        public MetricData[] metrics;
        public MotivationConfigData motivationConfig;
        public TaskArbitrationData taskArbitration;

        /// <summary>
        /// Find an agent template by ID.
        /// </summary>
        public AgentTemplateData FindTemplate(string templateId)
        {
            if (agentTemplates == null) return null;
            foreach (var t in agentTemplates)
            {
                if (t.templateId == templateId) return t;
            }
            return null;
        }

        /// <summary>
        /// Get the SoS type as an enum-like string: "directed", "collaborative", "acknowledged", "virtual".
        /// </summary>
        public string SoSType => simulatorConfig?.sosType ?? "unknown";

        /// <summary>
        /// Get the routing algorithm for a given template.
        /// </summary>
        public string GetAlgorithm(string templateId)
        {
            var template = FindTemplate(templateId);
            return template?.planner?.algorithm ?? "NaiveDijkstra";
        }

        /// <summary>
        /// Get governance parameters.
        /// </summary>
        public GovernanceData Governance => communicationSetup?.governance;

        /// <summary>
        /// Find a protocol by ID.
        /// </summary>
        public ProtocolData FindProtocol(string protocolId)
        {
            if (protocols == null) return null;
            foreach (var p in protocols)
            {
                if (p.protocolId == protocolId) return p;
            }
            return null;
        }
    }
}
