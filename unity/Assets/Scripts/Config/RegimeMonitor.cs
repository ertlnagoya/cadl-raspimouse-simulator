using System;
using MCP;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Runtime regime (operating mode) of the SoS simulation.
    /// </summary>
    public enum Regime
    {
        Normal,
        Congested
    }

    /// <summary>
    /// Monitors occupancy levels and triggers regime transitions
    /// as defined in CADL regimeTransitions config.
    ///
    /// CADL defines:
    ///   NORMAL -> CONGESTED when occupied_edges > total_edges * 0.6
    ///   CONGESTED -> NORMAL when occupied_edges <= total_edges * 0.4
    ///
    /// Hysteresis (0.4/0.6 thresholds) prevents rapid oscillation.
    ///
    /// Auto-initializes via [RuntimeInitializeOnLoadMethod] — no manual setup needed.
    /// </summary>
    public class RegimeMonitor : MonoBehaviour
    {
        public static RegimeMonitor Instance { get; private set; }

        [Header("Current State (read-only)")]
        [SerializeField] private string currentRegimeDisplay = "Normal";
        [SerializeField] private float occupancyRatio = 0f;

        /// <summary>Current regime.</summary>
        public Regime CurrentRegime { get; private set; } = Regime.Normal;

        /// <summary>
        /// Fired when regime changes. Args: (oldRegime, newRegime).
        /// Subscribe from Pilot_MCP, RouteSearch, etc. to adapt behavior.
        /// </summary>
        public event Action<Regime, Regime> OnRegimeChanged;

        // Thresholds from CADL (parsed from condition strings)
        private float congestThreshold = 0.6f;
        private float normalThreshold = 0.4f;
        private int totalEdges = 17;

        // Evaluation interval (seconds) to avoid per-frame overhead
        private float evalInterval = 1f;
        private float evalTimer = 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[RegimeMonitor]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RegimeMonitor>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            LoadThresholdsFromConfig();
        }

        private void Update()
        {
            evalTimer += Time.deltaTime;
            if (evalTimer < evalInterval) return;
            evalTimer = 0f;

            EvaluateRegime();
        }

        /// <summary>
        /// Evaluate current occupancy and trigger regime transition if needed.
        /// </summary>
        private void EvaluateRegime()
        {
            var occupancy = OccupancyManager.Instance;
            if (occupancy == null) return;

            int occupied = occupancy.OccupiedEdgeCount;
            occupancyRatio = totalEdges > 0 ? (float)occupied / totalEdges : 0f;

            Regime previousRegime = CurrentRegime;

            switch (CurrentRegime)
            {
                case Regime.Normal:
                    if (occupied > totalEdges * congestThreshold)
                    {
                        CurrentRegime = Regime.Congested;
                    }
                    break;

                case Regime.Congested:
                    if (occupied <= totalEdges * normalThreshold)
                    {
                        CurrentRegime = Regime.Normal;
                    }
                    break;
            }

            if (CurrentRegime != previousRegime)
            {
                currentRegimeDisplay = CurrentRegime.ToString();
                Debug.Log($"[RegimeMonitor] Regime transition: {previousRegime} -> {CurrentRegime} " +
                          $"(occupied={occupied}/{totalEdges}, ratio={occupancyRatio:F2})");
                OnRegimeChanged?.Invoke(previousRegime, CurrentRegime);
            }
        }

        /// <summary>
        /// Parse regime transition thresholds from CADL config.
        /// Supports conditions like "occupied_edges > total_edges * 0.6"
        /// </summary>
        private void LoadThresholdsFromConfig()
        {
            var config = SimulatorConfigurator.Config;
            if (config?.simulatorConfig?.environment != null)
            {
                totalEdges = config.simulatorConfig.environment.num_edges;
            }

            if (config?.regimeTransitions == null) return;

            foreach (var rt in config.regimeTransitions)
            {
                float? threshold = ParseThreshold(rt.condition);
                if (threshold == null) continue;

                if (rt.fromRegime == "NORMAL" && rt.toRegime == "CONGESTED")
                {
                    congestThreshold = threshold.Value;
                    Debug.Log($"[RegimeMonitor] NORMAL->CONGESTED threshold: {congestThreshold}");
                }
                else if (rt.fromRegime == "CONGESTED" && rt.toRegime == "NORMAL")
                {
                    normalThreshold = threshold.Value;
                    Debug.Log($"[RegimeMonitor] CONGESTED->NORMAL threshold: {normalThreshold}");
                }
            }
        }

        /// <summary>
        /// Parse threshold from condition string like "occupied_edges > total_edges * 0.6"
        /// Returns the multiplier (e.g., 0.6).
        /// </summary>
        private static float? ParseThreshold(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return null;

            // Look for "* X.X" pattern at end of condition
            int starIdx = condition.LastIndexOf('*');
            if (starIdx < 0) return null;

            string numStr = condition.Substring(starIdx + 1).Trim();
            if (float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }

            return null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
