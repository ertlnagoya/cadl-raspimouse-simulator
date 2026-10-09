using System.Collections.Generic;
using System.Text.RegularExpressions;
using LineTrace;
using MCP;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Auto-enables the correct Pilot component based on CADL sosType.
    ///
    /// Maps sosType to Pilot components:
    ///   "directed"      → Pilot (D-SoS)
    ///   "collaborative" → Pilot_CSoS (C-SoS)
    ///   "acknowledged"  → Pilot_MCP (MCP-SoS)
    ///
    /// For each robot (m0, m1, ...), finds all Pilot variants and enables
    /// only the matching one. Uses Resources.FindObjectsOfTypeAll to discover
    /// inactive and non-contiguous robots (same pattern as AgentSpawner).
    ///
    /// Auto-initializes via [RuntimeInitializeOnLoadMethod] — no manual setup needed.
    /// </summary>
    public class PilotSwitcher : MonoBehaviour
    {
        private static PilotSwitcher Instance;

        // Pattern to match robot naming convention "m0", "m1", ..., "m19"
        private static readonly Regex RobotNamePattern = new Regex(@"^m(\d+)$");

        [Header("Debug (read-only)")]
        [SerializeField] private string detectedSoSType;
        [SerializeField] private int switchedRobots;
        [SerializeField] private int warningRobots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[PilotSwitcher]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PilotSwitcher>();
        }

        private void Start()
        {
            string sosType = SimulatorConfigurator.Config?.SoSType ?? "unknown";
            detectedSoSType = sosType;

            if (sosType == "unknown")
            {
                Debug.Log("[PilotSwitcher] No CADL sosType configured — skipping auto-switch");
                return;
            }

            Debug.Log($"[PilotSwitcher] CADL sosType: {sosType}");

            // Find ALL robot objects including inactive and non-contiguous
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            var robots = new SortedDictionary<int, GameObject>();
            foreach (var t in allTransforms)
            {
                if (t.gameObject.scene.name == null) continue;
                var match = RobotNamePattern.Match(t.gameObject.name);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
                {
                    robots[idx] = t.gameObject;
                }
            }

            foreach (var kvp in robots)
            {
                SwitchPilot(kvp.Value, sosType);
            }

            Debug.Log($"[PilotSwitcher] Switched {switchedRobots} robots, {warningRobots} warnings");
        }

        private void SwitchPilot(GameObject robot, string sosType)
        {
            // Get all Pilot variants (including disabled components)
            var pilot = robot.GetComponent<Pilot>();
            var pilotCSoS = robot.GetComponent<Pilot_CSoS>();
            var pilotMCP = robot.GetComponent<Pilot_MCP>();

            bool hasPilot = pilot != null;
            bool hasCSoS = pilotCSoS != null;
            bool hasMCP = pilotMCP != null;

            // Determine target component
            switch (sosType)
            {
                case "directed":
                    if (hasPilot)
                    {
                        pilot.enabled = true;
                        if (hasCSoS) pilotCSoS.enabled = false;
                        if (hasMCP) pilotMCP.enabled = false;
                        switchedRobots++;
                    }
                    else
                    {
                        Debug.LogWarning($"[PilotSwitcher] {robot.name}: sosType=directed but no Pilot component");
                        warningRobots++;
                    }
                    break;

                case "collaborative":
                    if (hasCSoS)
                    {
                        pilotCSoS.enabled = true;
                        if (hasPilot) pilot.enabled = false;
                        if (hasMCP) pilotMCP.enabled = false;
                        switchedRobots++;
                    }
                    else
                    {
                        Debug.LogWarning($"[PilotSwitcher] {robot.name}: sosType=collaborative but no Pilot_CSoS component");
                        warningRobots++;
                    }
                    break;

                case "acknowledged":
                    if (hasMCP)
                    {
                        pilotMCP.enabled = true;
                        if (hasPilot) pilot.enabled = false;
                        if (hasCSoS) pilotCSoS.enabled = false;
                        switchedRobots++;
                    }
                    else
                    {
                        Debug.LogWarning($"[PilotSwitcher] {robot.name}: sosType=acknowledged but no Pilot_MCP component");
                        warningRobots++;
                    }
                    break;

                default:
                    Debug.LogWarning($"[PilotSwitcher] Unknown sosType: {sosType}");
                    warningRobots++;
                    break;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
