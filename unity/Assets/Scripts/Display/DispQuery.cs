using LineTrace;
using UnityEngine;

namespace Display
{
    /// <summary>
    /// Shared state of the "disp" occupancy query used by EdgeDisp and CrossDisp.
    /// Scenes without an arbitrator that answers "disp" (e.g. MCP-SoS) would otherwise
    /// log one failed request per edge and intersection; the first failure turns the
    /// query off for the whole run and is reported once.
    /// </summary>
    public static class DispQuery
    {
        private static bool unavailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            unavailable = false;
        }

        /// <summary>
        /// Ask the arbitrator whether an edge (src, dst) or an intersection (src, -1)
        /// is occupied. Returns false when the query is unavailable.
        /// </summary>
        public static bool TryQuery(Nats nats, int src, int dst, out bool occupied)
        {
            occupied = false;
            if (unavailable) return false;

            var n = nats.Send("disp", new Demand {Src = src, Dst = dst}, logFailure: false);
            if (n == Nats.ErrorResult || n == -1)
            {
                unavailable = true;
                Debug.Log("[Disp] Nothing answers the 'disp' subject; " +
                          "edge and intersection occupancy colours are off for this run.");
                return false;
            }
            occupied = n == 1;
            return true;
        }
    }
}
