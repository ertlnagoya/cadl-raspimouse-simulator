using System.Collections.Generic;
using System;
using UnityEngine;

namespace LineTrace
{
    public struct RouteSearch
    {
        /// <summary>No-edge sentinel value. Edges[i][j] >= this means no connection.</summary>
        public const float NoEdge = 100000f;

        /// <summary>
        /// Effective same-direction penalty scaled by governance alpha.
        /// Base value is read from CADL config (environment.same_direction_penalty).
        /// Higher alpha (more autonomy) = lower penalties (agents rely on local judgment).
        /// </summary>
        public float SameDirectionPenalty
        {
            get
            {
                float basePenalty = CADLConfig.SimulatorConfigurator.GetSameDirectionPenalty(1.5f);
                float alpha = CADLConfig.SimulatorConfigurator.GetGovernanceParam("alpha");
                return basePenalty * (1f - alpha);
            }
        }

        /// <summary>
        /// Effective opposite-direction penalty scaled by governance alpha.
        /// Base value is read from CADL config (environment.opposite_direction_penalty).
        /// </summary>
        public float OppositeDirectionPenalty
        {
            get
            {
                float basePenalty = CADLConfig.SimulatorConfigurator.GetOppositeDirectionPenalty(7.0f);
                float alpha = CADLConfig.SimulatorConfigurator.GetGovernanceParam("alpha");
                return basePenalty * (1f - alpha);
            }
        }

        public int Size;
        public float[][] Edges;
        public bool[][] EdgeFlags;
        public bool[] CrossFlags;

        public bool Available(int src, int dst)
        {
            return !(EdgeFlags[src][dst] || EdgeFlags[dst][src] || CrossFlags[src]);
        }

        public void MakeEdge(int i, int j, float length)
        {
            Edges[i][j] = Edges[j][i] = length;
        }

        public void SetZero()
        {
            Array.Clear(CrossFlags, 0, Size);
            for (int i = 0; i < Size; i++)
            {
                Array.Clear(EdgeFlags[i], 0, Size);
            }
        }

        public struct Path
        {
            public int Cross1;
            public int Cross2;
            public float Length;
        }

        public void Init(int size, List<Path> edges)
        {
            Size = size;
            Edges = new float[size][];
            EdgeFlags = new bool[size][];
            CrossFlags = new bool[size];

            for (int i = 0; i < size; i++)
            {
                Edges[i] = new float[size];
                EdgeFlags[i] = new bool[size];
                Array.Fill(Edges[i], NoEdge);
            }

            foreach (var edge in edges)
            {
                MakeEdge(edge.Cross1, edge.Cross2, edge.Length);
            }
        }

        public float DirectionWeight(int i, int j)
        {
            float weight = Edges[i][j];
            weight += EdgeFlags[i][j] ? SameDirectionPenalty : 0;
            weight += EdgeFlags[j][i] ? OppositeDirectionPenalty : 0;
            weight += CrossFlags[j] ? 1f : 0;
            return weight;
        }

        public float FlagWeight(int i, int j)
        {
            float weight = Edges[i][j];
            weight += (EdgeFlags[i][j] || EdgeFlags[j][i]) ? OppositeDirectionPenalty : 0;
            weight += CrossFlags[j] ? 1f : 0;
            return weight;
        }

        public float Length(int i, int j)
        {
            return Edges[i][j];
        }

        public int NaiveDijkstra(int start, int end) => Dijkstra(start, end, Length);
        public int FlagDijkstra(int start, int end) => Dijkstra(start, end, FlagWeight);
        public int DirectionDijkstra(int start, int end) => Dijkstra(start, end, DirectionWeight);

        public int RandomDijkstra(int start)
        {
            List<int> neighbors = new List<int>();
            for (int i = 0; i < Size; i++)
            {
                if (Edges[start][i] < NoEdge) neighbors.Add(i);
            }
            if (neighbors.Count == 0) return start; // No neighbors — stay in place
            return neighbors[UnityEngine.Random.Range(0, neighbors.Count)];
        }

        public int Dijkstra(int start, int end, Func<int, int, float> lengthFunc)
        {
            float[] costs = new float[Size];
            int[] precs = new int[Size];
            bool[] checks = new bool[Size];

            Array.Fill(costs, float.MaxValue / 2);
            costs[start] = 0;

            for (int l = 0; l < Size; l++)
            {
                float minCost = float.MaxValue;
                int currentNode = -1;

                for (int k = 0; k < Size; k++)
                {
                    if (!checks[k] && costs[k] < minCost)
                    {
                        minCost = costs[k];
                        currentNode = k;
                    }
                }

                if (currentNode == -1) break;
                checks[currentNode] = true;

                for (int j = 0; j < Size; j++)
                {
                    float newCost = costs[currentNode] + lengthFunc(currentNode, j);
                    if (newCost < costs[j])
                    {
                        costs[j] = newCost;
                        precs[j] = currentNode;
                    }
                }
            }

            // Trace back from end to find the first hop from start
            int tmp = end;
            HashSet<int> visited = new HashSet<int>();
            while (start != precs[tmp])
            {
                if (visited.Contains(tmp) || tmp < 0 || tmp >= Size)
                {
                    // Unreachable or cycle — no valid path
                    Debug.LogWarning($"[RouteSearch] No path from {start} to {end}");
                    return start;
                }
                visited.Add(tmp);
                tmp = precs[tmp];
            }
            return tmp;
        }

        public void Output()
        {
            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                {
                    if (EdgeFlags[i][j])
                    {
                        // Debug.Log(i + " -> " + j);
                    }
                }
            }
        }
    }
}
