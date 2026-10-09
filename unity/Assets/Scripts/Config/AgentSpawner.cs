using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Activates or deactivates robot GameObjects based on CADL config num_robots.
    ///
    /// Auto-initializes via [RuntimeInitializeOnLoadMethod] — no manual setup needed.
    /// Finds ALL robots matching naming convention "m0", "m1", "m2", ... in the scene,
    /// including inactive objects and non-contiguous numbering.
    ///
    /// Robots with index < num_robots are enabled; others are disabled.
    /// This allows the scene to contain the maximum number of robots
    /// while CADL controls how many are actually active.
    /// </summary>
    public class AgentSpawner : MonoBehaviour
    {
        private static AgentSpawner Instance;

        // Pattern to match robot naming convention "m0", "m1", ..., "m19"
        private static readonly Regex RobotNamePattern = new Regex(@"^m(\d+)$");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[AgentSpawner]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AgentSpawner>();
        }

        private void Start()
        {
            int numRobots = SimulatorConfigurator.GetNumRobots();

            // Find ALL robot GameObjects including inactive ones
            // Use Resources.FindObjectsOfTypeAll to find inactive objects too
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            var robotsByIndex = new SortedDictionary<int, GameObject>();

            foreach (var t in allTransforms)
            {
                // Skip scene-root objects (prefabs, editor objects)
                if (t.gameObject.scene.name == null) continue;

                var match = RobotNamePattern.Match(t.gameObject.name);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
                {
                    // Only consider root-level or direct children (avoid nested matches)
                    robotsByIndex[idx] = t.gameObject;
                }
            }

            int totalRobots = robotsByIndex.Count;
            int enabledCount = 0;
            int disabledCount = 0;

            foreach (var kvp in robotsByIndex)
            {
                bool shouldBeActive = kvp.Key < numRobots;

                if (kvp.Value.activeSelf != shouldBeActive)
                {
                    kvp.Value.SetActive(shouldBeActive);
                }

                if (shouldBeActive)
                    enabledCount++;
                else
                    disabledCount++;
            }

            Debug.Log($"[AgentSpawner] CADL num_robots={numRobots}: " +
                      $"enabled {enabledCount}, disabled {disabledCount} of {totalRobots} robots");

            if (numRobots > totalRobots)
            {
                Debug.LogWarning($"[AgentSpawner] CADL requests {numRobots} robots " +
                                 $"but scene only has {totalRobots}. " +
                                 $"Add more robot prefabs to the scene.");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
