using System.IO;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Loads a CADL-generated JSON config at runtime and applies it to
    /// configure the simulation parameters.
    ///
    /// Auto-initializes before any scene loads via [RuntimeInitializeOnLoadMethod].
    /// No manual scene placement required.
    ///
    /// Config file is loaded from:
    ///   StreamingAssets/cadl_config.json  (default)
    ///   or a custom path set via configFileName.
    ///
    /// Usage in other scripts:
    ///   var config = SimulatorConfigurator.Config;
    ///   string algo = config.GetAlgorithm("ROBOT");
    ///   float beta = config.Governance.beta;
    /// </summary>
    public class SimulatorConfigurator : MonoBehaviour
    {
        [Header("Config File")]
        [Tooltip("JSON file name in StreamingAssets/")]
        public string configFileName = "cadl_config.json";

        [Header("Loaded Config (read-only)")]
        [SerializeField] private string loadedSoSName = "";
        [SerializeField] private string loadedSoSType = "";
        [SerializeField] private float loadedAlpha = 0f;
        [SerializeField] private float loadedBeta = 0f;

        /// <summary>Singleton instance.</summary>
        public static SimulatorConfigurator Instance { get; private set; }

        /// <summary>The loaded CADL config. Null if not loaded.</summary>
        public static CADLSimConfig Config { get; private set; }

        /// <summary>True after config is successfully loaded.</summary>
        public static bool IsLoaded => Config != null;

        /// <summary>
        /// Auto-create SimulatorConfigurator before any scene loads.
        /// This ensures CADL config is available to all Awake() methods.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[SimulatorConfigurator]");
            DontDestroyOnLoad(go);
            go.AddComponent<SimulatorConfigurator>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LoadConfig();
        }

        /// <summary>
        /// Load the CADL config JSON from StreamingAssets.
        /// </summary>
        public void LoadConfig()
        {
            string path = Path.Combine(Application.streamingAssetsPath, configFileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SimulatorConfigurator] Config not found: {path}. Using defaults.");
                Config = null;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                Config = JsonUtility.FromJson<CADLSimConfig>(json);

                if (Config?.simulatorConfig != null)
                {
                    loadedSoSName = Config.simulatorConfig.name;
                    loadedSoSType = Config.SoSType;
                    loadedAlpha = Config.Governance?.alpha ?? 0f;
                    loadedBeta = Config.Governance?.beta ?? 0f;

                    // random_seed: -1 (or absent/default) = do NOT call InitState()
                    //   → Unity uses time-based default initialization (matches thesis behaviour).
                    // random_seed >= 0 = call InitState(seed) for reproducible runs.
                    int seed = Config.simulatorConfig.environment?.random_seed ?? -1;
                    if (seed >= 0)
                    {
                        UnityEngine.Random.InitState(seed);
                        Debug.Log($"[SimulatorConfigurator] Loaded: {loadedSoSName} ({loadedSoSType}), alpha={loadedAlpha}, beta={loadedBeta}, random_seed={seed} (explicit)");
                    }
                    else
                    {
                        Debug.Log($"[SimulatorConfigurator] Loaded: {loadedSoSName} ({loadedSoSType}), alpha={loadedAlpha}, beta={loadedBeta}, random_seed=unset (thesis-faithful: no InitState)");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SimulatorConfigurator] Failed to parse config: {e.Message}");
                Config = null;
            }
        }

        /// <summary>
        /// Get the routing algorithm name for a given agent template.
        /// Falls back to "NaiveDijkstra" if config is not loaded.
        /// </summary>
        public static string GetRoutingAlgorithm(string templateId = "ROBOT")
        {
            return Config?.GetAlgorithm(templateId) ?? "NaiveDijkstra";
        }

        /// <summary>
        /// Get the path mode for MCP-SoS ("static" or "dynamic").
        /// </summary>
        public static string GetPathMode()
        {
            return Config?.simulatorConfig?.environment?.path_mode ?? "static";
        }

        /// <summary>
        /// Get the configured start node for a robot by ID.
        /// Returns -1 if not set (caller should fall back to RandomGoal).
        /// </summary>
        public static int GetStartNode(int robotId)
        {
            var nodes = Config?.simulatorConfig?.environment?.start_nodes;
            if (nodes != null && robotId < nodes.Length)
                return nodes[robotId];
            return -1;
        }

        /// <summary>
        /// Get the number of robots from config.
        /// Source of truth: agentTemplates[templateId="ROBOT"].count
        /// (environment.num_robots is informational only and NOT used here)
        /// </summary>
        public static int GetNumRobots()
        {
            if (Config?.agentTemplates == null) return 5;
            var robot = Config.FindTemplate("ROBOT");
            return robot?.count ?? 5;
        }

        /// <summary>
        /// Get the same-direction routing penalty from CADL environment.
        /// </summary>
        public static float GetSameDirectionPenalty(float defaultValue = 1.5f)
        {
            return Config?.simulatorConfig?.environment?.same_direction_penalty ?? defaultValue;
        }

        /// <summary>
        /// Get the opposite-direction routing penalty from CADL environment.
        /// </summary>
        public static float GetOppositeDirectionPenalty(float defaultValue = 7.0f)
        {
            return Config?.simulatorConfig?.environment?.opposite_direction_penalty ?? defaultValue;
        }

        /// <summary>
        /// Get the intersection detection threshold distance from CADL environment.
        /// </summary>
        public static float GetIntersectionThreshold(float defaultValue = 0.375f)
        {
            return Config?.simulatorConfig?.environment?.intersection_threshold ?? defaultValue;
        }

        /// <summary>
        /// Get NATS subjects configuration from CADL.
        /// Returns a NatsSubjectsData with defaults if config is not loaded.
        /// </summary>
        public static NatsSubjectsData GetNatsSubjects()
        {
            return Config?.communicationSetup?.nats_subjects ?? new NatsSubjectsData();
        }

        /// <summary>
        /// Get a specific NATS subject by operation name.
        /// </summary>
        public static string GetNatsSubject(string operation)
        {
            var subjects = GetNatsSubjects();
            switch (operation)
            {
                case "init": return subjects.init;
                case "next": return subjects.next;
                case "ret":  return subjects.ret;
                case "fin":  return subjects.fin;
                case "disp": return subjects.disp;
                case "resource": return subjects.resource;
                case "goalcount":        return subjects.goalcount;
                case "stop":            return subjects.stop;
                case "delivery_request": return subjects.delivery_request;
                case "goal_claim":      return subjects.goal_claim;
                case "goal_request":    return subjects.goal_request;
                case "task_end":        return subjects.task_end;
                case "task_end_result": return subjects.task_end_result;
                case "task_retire":     return subjects.task_retire;
                default: return operation; // fallback to operation name itself
            }
        }

        /// <summary>
        /// Get a governance parameter by name.
        /// </summary>
        public static float GetGovernanceParam(string param)
        {
            if (Config?.Governance == null) return 0.5f;
            switch (param.ToLower())
            {
                case "alpha": return Config.Governance.alpha;
                case "beta":  return Config.Governance.beta;
                case "lambda": return Config.Governance.lambda;
                default: return 0.5f;
            }
        }

        /// <summary>
        /// Get max_retries from COLLISION_RECOVERY protocol timing.
        /// Falls back to defaultValue if config is not loaded.
        /// </summary>
        public static int GetMaxRetries(int defaultValue = 3)
        {
            var protocol = Config?.FindProtocol("COLLISION_RECOVERY");
            if (protocol?.timing != null && protocol.timing.max_retries > 0)
                return protocol.timing.max_retries;
            return defaultValue;
        }

        /// <summary>
        /// Get back_duration from COLLISION_RECOVERY protocol timing.
        /// Parses string like "2s" to float seconds.
        /// Falls back to defaultValue if config is not loaded.
        /// </summary>
        public static float GetBackDuration(float defaultValue = 2f)
        {
            var protocol = Config?.FindProtocol("COLLISION_RECOVERY");
            return ParseDuration(protocol?.timing?.back_duration, defaultValue);
        }

        /// <summary>
        /// Get max_response timeout from a named protocol.
        /// Falls back to defaultValue if config is not loaded.
        /// </summary>
        public static float GetMaxResponse(string protocolId, float defaultValue = 10f)
        {
            var protocol = Config?.FindProtocol(protocolId);
            return ParseDuration(protocol?.timing?.max_response, defaultValue);
        }

        /// <summary>
        /// Parse a duration string like "10s", "2.5s", "500ms" to float seconds.
        /// </summary>
        private static float ParseDuration(string durationStr, float defaultValue)
        {
            if (string.IsNullOrEmpty(durationStr)) return defaultValue;

            string trimmed = durationStr.Trim();
            if (trimmed.EndsWith("ms"))
            {
                if (float.TryParse(trimmed.Substring(0, trimmed.Length - 2), out float ms))
                    return ms / 1000f;
            }
            else if (trimmed.EndsWith("s"))
            {
                if (float.TryParse(trimmed.Substring(0, trimmed.Length - 1), out float s))
                    return s;
            }
            else
            {
                if (float.TryParse(trimmed, out float val))
                    return val;
            }

            return defaultValue;
        }
    }
}
