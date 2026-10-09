using System;
using UnityEngine;

namespace LineTrace
{
    public class Cross : MonoBehaviour
    {
        [HideInInspector] public int number;

        public int[] nexts = {-1, -1, -1, -1};
        private Vector3[] _pivs = new Vector3[4];

        private void Awake()
        {
            // Parse "crossN" format — extract N from name
            if (name.Length > 5 && name.StartsWith("cross"))
                number = int.Parse(name.Substring(5));
            else
                int.TryParse(name, out number);

            for (int i = 0; i < 4; i++)
            {
                if (nexts[i] == -1) continue;
                var child = transform.Find($"dir{nexts[i]}");
                if (child != null)
                    _pivs[i] = child.position;
                else
                    Debug.LogWarning($"[Cross] Node {number}: child 'dir{nexts[i]}' not found");
            }
        }

        public Vector3 GetDir(int next)
        {
            for (int i = 0; i < 4; i++)
            {
                if (nexts[i] == next)
                {
                    return _pivs[i];
                }
            }

            throw new Exception($"{number} -/-> {next} (available: [{string.Join(",", nexts)}])");
        }
    }
}