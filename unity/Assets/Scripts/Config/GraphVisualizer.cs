using System.Collections.Generic;
using CADLConfig;
using Display;
using LineTrace;
using UnityEngine;

namespace CADLConfig
{
    /// <summary>
    /// Synchronizes scene visuals with the CADL graph definition.
    ///
    /// At runtime, finds all Cross, CrossDisp, and EdgeDisp objects in the scene
    /// and disables those whose node/edge IDs fall outside the current graph range.
    /// Roads (mesh objects named "N-M") are also hidden.
    ///
    /// Auto-initializes via [RuntimeInitializeOnLoadMethod] — no manual setup needed.
    /// </summary>
    public class GraphVisualizer : MonoBehaviour
    {
        private static GraphVisualizer Instance;

        [Header("Debug (read-only)")]
        [SerializeField] private int activeNodes;
        [SerializeField] private int hiddenNodes;
        [SerializeField] private int activeEdges;
        [SerializeField] private int hiddenEdges;

        // Set of valid edges from CADL config (both directions)
        private HashSet<(int, int)> validEdges = new HashSet<(int, int)>();

        /// <summary>
        /// Auto-create GraphVisualizer after scene loads.
        /// Uses AfterSceneLoad so all Cross/EdgeDisp objects exist.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance != null) return;
            var go = new GameObject("[GraphVisualizer]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GraphVisualizer>();
        }

        private void Start()
        {
            BuildValidEdgeSet();
            SyncCrossObjects();
            SyncEdgeObjects();

            Debug.Log($"[GraphVisualizer] Graph has {GraphDefinition.NumNodes} nodes. " +
                      $"Scene: {activeNodes} active / {hiddenNodes} hidden nodes, " +
                      $"{activeEdges} active / {hiddenEdges} hidden edges");
        }

        /// <summary>
        /// Build a set of valid edges (both directions) from GraphDefinition.
        /// </summary>
        private void BuildValidEdgeSet()
        {
            foreach (var e in GraphDefinition.GetDefaultEdges())
            {
                validEdges.Add((e.Cross1, e.Cross2));
                validEdges.Add((e.Cross2, e.Cross1));
            }
        }

        /// <summary>
        /// Find all Cross and CrossDisp objects, disable those outside graph range.
        /// </summary>
        private void SyncCrossObjects()
        {
            // Process Cross components (intersection triggers)
            foreach (var cross in FindObjectsOfType<Cross>(true))
            {
                bool valid = GraphDefinition.IsValidNode(cross.number);
                if (cross.gameObject.activeSelf != valid)
                {
                    cross.gameObject.SetActive(valid);
                }
                if (valid) activeNodes++;
                else hiddenNodes++;
            }

            // Process CrossDisp components (visual node markers)
            foreach (var disp in FindObjectsOfType<CrossDisp>(true))
            {
                int nodeNum = -1;
                int.TryParse(disp.name, out nodeNum);
                bool valid = GraphDefinition.IsValidNode(nodeNum);
                if (disp.gameObject.activeSelf != valid)
                {
                    disp.gameObject.SetActive(valid);
                }
            }
        }

        /// <summary>
        /// Find all EdgeDisp objects, disable those connecting nodes outside graph range
        /// or edges not present in the CADL graph.
        /// </summary>
        private void SyncEdgeObjects()
        {
            foreach (var edge in FindObjectsOfType<EdgeDisp>(true))
            {
                bool valid = GraphDefinition.IsValidNode(edge.num0)
                          && GraphDefinition.IsValidNode(edge.num1)
                          && validEdges.Contains((edge.num0, edge.num1));

                if (edge.gameObject.activeSelf != valid)
                {
                    edge.gameObject.SetActive(valid);
                }
                if (valid) activeEdges++;
                else hiddenEdges++;
            }

            // Also hide road mesh objects whose names match "N-M" pattern
            foreach (var renderer in FindObjectsOfType<MeshRenderer>(true))
            {
                string objName = renderer.gameObject.name;
                var parts = objName.Split('-');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int n0)
                    && int.TryParse(parts[1], out int n1))
                {
                    // Skip if already handled as EdgeDisp
                    if (renderer.GetComponent<EdgeDisp>() != null)
                        continue;

                    bool valid = GraphDefinition.IsValidNode(n0)
                              && GraphDefinition.IsValidNode(n1)
                              && validEdges.Contains((n0, n1));

                    if (renderer.gameObject.activeSelf != valid)
                    {
                        renderer.gameObject.SetActive(valid);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
