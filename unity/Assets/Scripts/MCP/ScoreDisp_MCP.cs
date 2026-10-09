using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MCP
{
    /// <summary>
    /// Score display using AgentRegistry instead of Pilot[].
    /// MCP version of Display.ScoreDisp.
    /// </summary>
    public class ScoreDisp_MCP : MonoBehaviour
    {
        private TextMeshProUGUI text;

        private float t;
        private int preSum = -1;
        private int preMin = -1;

        private readonly List<(float, int)> sums = new List<(float, int)>();
        private readonly List<(float, int)> mins = new List<(float, int)>();

        private void Start()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void Update()
        {
            if (AgentRegistry.Instance == null) return;

            var agents = AgentRegistry.Instance.GetAllAgents();
            if (agents.Count == 0) return;

            var sum = agents.Sum(a => a.Level);
            var min = agents.Min(a => a.Level);
            text.text = $"Sum: {sum}, Min: {min}";

            if (preSum != sum)
            {
                preSum = sum;
                sums.Add((t, sum));
            }

            if (preMin != min)
            {
                preMin = min;
                mins.Add((t, min));
            }

            t += Time.deltaTime;
        }

        private void OnDestroy()
        {
            if (preSum < 0) return; // No data recorded

            sums.Add((t, preSum));
            mins.Add((t, preMin));

            var agentCount = AgentRegistry.Instance?.Count ?? 5;

            var sumPath = Application.streamingAssetsPath + "/" + "sum.txt";
            var sumSw = new StreamWriter(new FileStream(sumPath, FileMode.Create));
            foreach (var (time, sum) in sums)
            {
                sumSw.WriteLine($"{time} {sum / (float)agentCount}");
            }
            sumSw.Close();

            var minPath = Application.streamingAssetsPath + "/" + "min.txt";
            var minSw = new StreamWriter(new FileStream(minPath, FileMode.Create));
            foreach (var (time, min) in mins)
            {
                minSw.WriteLine($"{time} {min}");
            }
            minSw.Close();
        }
    }
}
