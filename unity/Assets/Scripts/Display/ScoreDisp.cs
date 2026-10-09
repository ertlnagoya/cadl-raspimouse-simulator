using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LineTrace;
using TMPro;
using UnityEngine;
using CADLConfig;

namespace Display
{
    public class ScoreDisp : MonoBehaviour
    {
        public Pilot[] pilots;
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
            if (pilots == null || pilots.Length == 0) return;
            var sum = pilots.Sum(dik => dik.level);
            var min = pilots.Min(dik => dik.level);
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
            if (pilots == null || pilots.Length == 0) return;
            // Use actual array length (more reliable than config value)
            int numRobots = pilots.Length;

            sums.Add((t, preSum));
            mins.Add((t, preMin));

            try
            {
                var sumPath = Path.Combine(Application.streamingAssetsPath, "sum.txt");
                using (var sumSw = new StreamWriter(new FileStream(sumPath, FileMode.Create)))
                {
                    foreach (var (time, sum) in sums)
                    {
                        sumSw.WriteLine($"{time} {sum / (float)numRobots}");
                    }
                }

                var minPath = Path.Combine(Application.streamingAssetsPath, "min.txt");
                using (var minSw = new StreamWriter(new FileStream(minPath, FileMode.Create)))
                {
                    foreach (var (time, min) in mins)
                    {
                        minSw.WriteLine($"{time} {min}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScoreDisp] Failed to write metrics: {e.Message}");
            }
        }
    }
}
