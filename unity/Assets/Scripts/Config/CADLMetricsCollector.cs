using System.Collections.Generic;
using System.IO;
using LineTrace;
using MCP;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Collects and evaluates metrics defined in CADL config at runtime.
    ///
    /// Supports the following metric IDs (matching cadl_config.json):
    ///   - goal_sum:       Total goal count across all agents
    ///   - goal_min:       Minimum goal count (fairness indicator)
    ///   - collision_rate: Collisions per minute per robot
    ///   - retry_rate:     Total retries / total routing requests
    ///
    /// Periodically logs metric values and evaluates them against CADL targets.
    /// Writes a metrics_report.txt on scene exit.
    ///
    /// Auto-initializes via [RuntimeInitializeOnLoadMethod] — no manual setup needed.
    /// </summary>
    public class CADLMetricsCollector : MonoBehaviour
    {
        private static CADLMetricsCollector Instance;

        [Header("Evaluation (read-only)")]
        [SerializeField] private int goalSum;
        [SerializeField] private int goalMin;
        [SerializeField] private float collisionRate;
        [SerializeField] private float retryRate;

        // Collision tracking
        private int totalCollisions = 0;
        private float lastCollisionLogTime = -1f;

        // Retry tracking
        private int totalRetries = 0;
        private int totalRequests = 0;

        // CADL metric targets (parsed from config)
        private readonly Dictionary<string, (string op, float value)> targets
            = new Dictionary<string, (string, float)>();

        // Evaluation interval
        private float evalInterval = 10f;
        private float evalTimer = 0f;

        // Per-robot goal counts snapshot (written once at end of report)
        private int[] perRobotGoals = null;

        // Time series for report
        private readonly List<(float time, int goalSum, int goalMin, float collRate, float retryRate)>
            history = new List<(float, int, int, float, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[CADLMetricsCollector]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CADLMetricsCollector>();
        }

        /// <summary>
        /// Called by collision detection code to increment collision counter.
        /// </summary>
        public static void RecordCollision()
        {
            if (Instance != null) Instance.totalCollisions++;
        }

        /// <summary>
        /// Called by routing code to track retry/request ratio.
        /// </summary>
        public static void RecordRoutingRequest(bool wasRetry)
        {
            if (Instance == null) return;
            Instance.totalRequests++;
            if (wasRetry) Instance.totalRetries++;
        }

        private void Start()
        {
            LoadTargetsFromConfig();
        }

        private void Update()
        {
            evalTimer += Time.deltaTime;
            if (evalTimer < evalInterval) return;
            evalTimer = 0f;

            CollectAndEvaluate();
        }

        private void CollectAndEvaluate()
        {
            // Collect goal metrics from all SoS modes
            bool collected = false;

            // Try MCP mode first (AgentRegistry)
            var registry = AgentRegistry.Instance;
            if (registry != null)
            {
                var agents = registry.GetAllAgents();
                if (agents.Count > 0)
                {
                    goalSum = 0;
                    goalMin = int.MaxValue;
                    foreach (var a in agents)
                    {
                        goalSum += a.Level;
                        if (a.Level < goalMin) goalMin = a.Level;
                    }
                    collected = true;
                }
            }

            // Fallback: collect from Pilot (D-SoS) and Pilot_CSoS (C-SoS) components
            if (!collected)
            {
                goalSum = 0;
                goalMin = int.MaxValue;
                int agentCount = 0;

                // D-SoS Pilots (only enabled — each robot has both Pilot and Pilot_CSoS,
                // but PilotSwitcher disables the inactive one)
                foreach (var pilot in Object.FindObjectsOfType<Pilot>())
                {
                    if (!pilot.enabled) continue;
                    goalSum += pilot.level;
                    if (pilot.level < goalMin) goalMin = pilot.level;
                    agentCount++;
                }

                // C-SoS Pilots (only enabled)
                // In task-arbitration (FCFS) mode, use deliveryCount (FCFS deliveries only)
                // rather than level (deliveries + wandering) to match thesis measurement.
                foreach (var pilot in Object.FindObjectsOfType<Pilot_CSoS>())
                {
                    if (!pilot.enabled) continue;
                    int count = pilot.TaskArbitrationEnabled ? pilot.DeliveryCount : pilot.level;
                    goalSum += count;
                    if (count < goalMin) goalMin = count;
                    agentCount++;
                }

                if (agentCount == 0)
                {
                    goalSum = 0;
                    goalMin = 0;
                }

                // Capture per-robot snapshot (sorted by id, updated every interval)
                CapturePerRobotGoals();
            }

            // Calculate rates
            float elapsedMinutes = Time.time / 60f;
            int numRobots = SimulatorConfigurator.GetNumRobots();

            collisionRate = (elapsedMinutes > 0 && numRobots > 0)
                ? totalCollisions / elapsedMinutes / numRobots
                : 0f;

            retryRate = totalRequests > 0
                ? (float)totalRetries / totalRequests
                : 0f;

            // Record history
            history.Add((Time.time, goalSum, goalMin, collisionRate, retryRate));

            // Evaluate against targets
            EvaluateTarget("goal_sum", goalSum);
            EvaluateTarget("goal_min", goalMin);
            EvaluateTarget("collision_rate", collisionRate);
            EvaluateTarget("retry_rate", retryRate);
        }

        private void EvaluateTarget(string metricId, float currentValue)
        {
            if (!targets.ContainsKey(metricId)) return;

            var (op, targetVal) = targets[metricId];
            bool met = false;

            switch (op)
            {
                case ">=": met = currentValue >= targetVal; break;
                case "<=": met = currentValue <= targetVal; break;
                case ">":  met = currentValue > targetVal;  break;
                case "<":  met = currentValue < targetVal;  break;
                case "==": met = Mathf.Approximately(currentValue, targetVal); break;
            }

            if (!met && Time.time > 30f) // Only warn after 30s warmup
            {
                Debug.LogWarning($"[CADLMetrics] {metricId}={currentValue:F2} " +
                                 $"does NOT meet target {op} {targetVal}");
            }
        }

        /// <summary>
        /// Parse metric targets from CADL config.
        /// Supports patterns like ">= 0", "<= 0.5", "< 1.0"
        /// </summary>
        private void LoadTargetsFromConfig()
        {
            var config = SimulatorConfigurator.Config;
            if (config?.metrics == null)
            {
                Debug.Log("[CADLMetrics] No metrics defined in CADL config");
                return;
            }

            foreach (var m in config.metrics)
            {
                if (string.IsNullOrEmpty(m.target)) continue;

                string trimmed = m.target.Trim();
                string op = null;
                string numStr = null;

                // Parse operator and value from strings like ">= 0", "<= 0.5"
                if (trimmed.StartsWith(">="))      { op = ">="; numStr = trimmed.Substring(2); }
                else if (trimmed.StartsWith("<="))  { op = "<="; numStr = trimmed.Substring(2); }
                else if (trimmed.StartsWith(">"))   { op = ">";  numStr = trimmed.Substring(1); }
                else if (trimmed.StartsWith("<"))    { op = "<";  numStr = trimmed.Substring(1); }
                else if (trimmed.StartsWith("=="))  { op = "=="; numStr = trimmed.Substring(2); }

                if (op != null && numStr != null &&
                    float.TryParse(numStr.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    targets[m.id] = (op, val);
                    Debug.Log($"[CADLMetrics] Target: {m.id} {op} {val}");
                }
            }
        }

        /// <summary>
        /// Capture current per-robot goal counts (sorted by robot id).
        /// </summary>
        private void CapturePerRobotGoals()
        {
            int n = SimulatorConfigurator.GetNumRobots();
            if (n <= 0) return;

            var counts = new int[n];

            foreach (var pilot in Object.FindObjectsOfType<Pilot>())
            {
                if (!pilot.enabled) continue;
                if (pilot.id >= 0 && pilot.id < n)
                    counts[pilot.id] = pilot.level;
            }
            foreach (var pilot in Object.FindObjectsOfType<Pilot_CSoS>())
            {
                if (!pilot.enabled) continue;
                if (pilot.id >= 0 && pilot.id < n)
                    counts[pilot.id] = pilot.TaskArbitrationEnabled ? pilot.DeliveryCount : pilot.level;
            }

            perRobotGoals = counts;
        }

        /// <summary>
        /// Force-write the metrics report to StreamingAssets immediately.
        /// Called by BatchRunner before Application.Quit() so the file is
        /// ready to copy before OnDestroy races with Application.quitting.
        /// </summary>
        public static void WriteReportNow()
        {
            if (Instance != null)
                Instance.WriteReport();
        }

        private void WriteReport()
        {
            if (history.Count == 0) return;

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "metrics_report.txt");
                using (var sw = new StreamWriter(new FileStream(path, FileMode.Create)))
                {
                    sw.WriteLine("# CADL Metrics Report");
                    sw.WriteLine($"# Generated at: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sw.WriteLine($"# Duration: {Time.time:F1}s");
                    sw.WriteLine($"# Robots: {SimulatorConfigurator.GetNumRobots()}");
                    sw.WriteLine("#");
                    sw.WriteLine("# time[s]\tgoal_sum\tgoal_min\tcollision_rate\tretry_rate");

                    foreach (var (time, gs, gm, cr, rr) in history)
                    {
                        sw.WriteLine($"{time:F1}\t{gs}\t{gm}\t{cr:F4}\t{rr:F4}");
                    }

                    sw.WriteLine("#");
                    sw.WriteLine("# Target Evaluation:");
                    foreach (var kvp in targets)
                    {
                        float val = GetMetricValue(kvp.Key);
                        bool pass = EvaluateTarget(kvp.Key, val, kvp.Value.op, kvp.Value.value);
                        sw.WriteLine($"#   {kvp.Key} = {val:F4} {kvp.Value.op} {kvp.Value.value} -> {(pass ? "PASS" : "FAIL")}");
                    }

                    // Per-robot goal counts (for FCFS bias analysis)
                    if (perRobotGoals != null && perRobotGoals.Length > 0)
                    {
                        sw.WriteLine("#");
                        sw.WriteLine("# Per-Robot Goal Counts (robot_id: count):");
                        for (int i = 0; i < perRobotGoals.Length; i++)
                            sw.WriteLine($"#   robot_{i}: {perRobotGoals[i]}");
                    }
                }
                Debug.Log($"[CADLMetrics] Report written to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CADLMetrics] Failed to write report: {e.Message}");
            }
        }

        private float GetMetricValue(string id)
        {
            if (history.Count == 0) return 0f;
            var last = history[history.Count - 1];
            return id switch
            {
                "goal_sum"       => last.goalSum,
                "goal_min"       => last.goalMin,
                "collision_rate" => last.collRate,
                "retry_rate"     => last.retryRate,
                _ => 0f
            };
        }

        private bool EvaluateTarget(string id, float val, string op, float threshold)
        {
            return op switch
            {
                ">=" => val >= threshold,
                "<=" => val <= threshold,
                ">"  => val > threshold,
                "<"  => val < threshold,
                "==" => Mathf.Approximately(val, threshold),
                _    => false
            };
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;

            // Write final report (may already be written by WriteReportNow)
            WriteReport();
        }

    }
}
