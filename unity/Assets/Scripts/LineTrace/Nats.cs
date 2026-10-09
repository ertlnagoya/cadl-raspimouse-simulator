using System;
using System.Collections.Concurrent;
using System.Text;
using NATS.Client;
using UnityEngine;
using CADLConfig;

namespace LineTrace
{
    /// <summary>
    /// Reply structure matching Go's DemandReply JSON.
    /// </summary>
    [Serializable]
    public struct DemandReplyJson
    {
        public int PermitState;
        public int ReCount;
    }

    public class Nats
    {
        private readonly IConnection connection;

        /// <summary>
        /// Thread-safe queue for dispatching async NATS subscription callbacks
        /// onto the Unity main thread.  Call FlushCallbacks() each Update().
        /// </summary>
        private readonly ConcurrentQueue<Action> pendingCallbacks =
            new ConcurrentQueue<Action>();

        /// <summary>True if NATS connection was established successfully.</summary>
        public bool IsConnected => connection != null && connection.State == ConnState.CONNECTED;

        /// <summary>
        /// Error return value for Send/Send_CSoS. Distinct from normal rejection (-1...-N)
        /// which means "retry N times". -999 signals a communication error (NATS timeout,
        /// connection lost, invalid subject). Callers should apply exponential backoff
        /// and halt after MaxCommErrors consecutive failures.
        /// </summary>
        public const int ErrorResult = -999;

        /// <summary>
        /// The ReCount value from the last Send_CSoS reply.
        /// Represents the arbitrator's suggested retry wait duration (in 1s ticks).
        /// 0 if not set (e.g. turn-back or plain-int reply).
        /// </summary>
        public int LastReCount { get; private set; } = 0;

        public Nats()
        {
            try
            {
                var opts = ConnectionFactory.GetDefaultOptions();
                // Read URL from CADL config, fall back to default
                opts.Url = SimulatorConfigurator.Config?.simulatorConfig?.environment?.nats_url
                           ?? "nats://localhost:4222";
                connection = new ConnectionFactory().CreateConnection(opts);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nats] Failed to connect: {e.Message}");
                connection = null;
            }
        }

        /// <summary>
        /// Send a D-SoS request and get the raw string response.
        /// </summary>
        public string SendRaw(string sub, Demand demand)
        {
            if (connection == null) return ErrorResult.ToString();
            try
            {
                var msg = connection.Request(
                    sub, Encoding.UTF8.GetBytes(JsonUtility.ToJson(demand)),
                    1000 * 2);  // 2s timeout (was 10s)
                return Encoding.UTF8.GetString(msg.Data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] Request '{sub}' failed: {e.GetType().Name}");
                return ErrorResult.ToString();
            }
        }

        /// <summary>
        /// Send a D-SoS request. Response is a plain integer (next node ID or negative for rejection).
        /// </summary>
        public int Send(string sub, Demand demand)
        {
            if (connection == null) return ErrorResult;
            string raw = SendRaw(sub, demand);
            if (int.TryParse(raw, out int result))
                return result;
            // Fallback: try parsing as JSON DemandReply
            try
            {
                var reply = JsonUtility.FromJson<DemandReplyJson>(raw);
                return reply.PermitState;
            }
            catch
            {
                Debug.LogError($"[Nats] Failed to parse response for '{sub}': {raw}");
                return -1;
            }
        }

        // ── Async subscription (task-arbitration broadcasts) ─────────────────

        /// <summary>
        /// Subscribe to a NATS subject asynchronously.
        /// The callback is queued and dispatched on the Unity main thread via
        /// FlushCallbacks().  Safe to call Unity APIs inside the callback.
        /// </summary>
        public void Subscribe(string sub, Action<string> onMessage)
        {
            if (connection == null) return;
            try
            {
                var asyncSub = connection.SubscribeAsync(sub);
                asyncSub.MessageHandler += (sender, args) =>
                {
                    string payload = Encoding.UTF8.GetString(args.Message.Data);
                    pendingCallbacks.Enqueue(() => onMessage(payload));
                };
                asyncSub.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] Subscribe '{sub}' failed: {e.GetType().Name}");
            }
        }

        /// <summary>
        /// Dispatch all pending subscription callbacks on the calling (main) thread.
        /// Call once per Update() from any MonoBehaviour that uses Subscribe().
        /// </summary>
        public void FlushCallbacks()
        {
            while (pendingCallbacks.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e)
                {
                    Debug.LogError($"[Nats] Callback exception: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Send a delivery-claim request (robot → arbitrator).
        /// Returns "1" if claim was received, "0" if ignored, -1 on error.
        /// </summary>
        public int SendDelivery(string sub, Demand_Delivery demand)
        {
            if (connection == null) return -1;
            try
            {
                var msg = connection.Request(
                    sub, Encoding.UTF8.GetBytes(JsonUtility.ToJson(demand)),
                    1000 * 2);  // 2s timeout
                return int.TryParse(Encoding.UTF8.GetString(msg.Data), out int r) ? r : -1;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] SendDelivery '{sub}' failed: {e.GetType().Name}");
                return -1;
            }
        }

        /// <summary>
        /// Request current edge/cross occupancy state from the arbitrator (resource subject).
        /// The arbitrator temporarily clears the requesting robot's own edge before replying,
        /// so the robot can route as if it does not occupy its current edge.
        /// Returns default(ReplyResource) on failure (Ok == false).
        /// </summary>
        public ReplyResource SendResource(string sub, DemandResource demand)
        {
            if (connection == null) return null;
            try
            {
                var msg = connection.Request(
                    sub, Encoding.UTF8.GetBytes(JsonUtility.ToJson(demand)),
                    1000 * 2);  // 2s timeout
                return JsonUtility.FromJson<ReplyResource>(Encoding.UTF8.GetString(msg.Data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] SendResource '{sub}' failed: {e.GetType().Name}");
                return null;
            }
        }

        /// <summary>
        /// Fire-and-forget publish — no reply expected (e.g. "goalcount").
        /// The arbitrator subscribes to these asynchronously.
        /// </summary>
        public void Publish(string sub, string payload)
        {
            if (connection == null) return;
            try
            {
                connection.Publish(sub, Encoding.UTF8.GetBytes(payload));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] Publish '{sub}' failed: {e.GetType().Name}");
            }
        }

        /// <summary>
        /// Send a C-SoS request and get the raw string response.
        /// </summary>
        public string SendRaw_CSoS(string sub, Demand_CSoS demand)
        {
            if (connection == null) return ErrorResult.ToString();
            try
            {
                var msg = connection.Request(
                    sub, Encoding.UTF8.GetBytes(JsonUtility.ToJson(demand)),
                    1000 * 2);  // 2s timeout (was 10s)
                return Encoding.UTF8.GetString(msg.Data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nats] C-SoS request '{sub}' failed: {e.GetType().Name}");
                return ErrorResult.ToString();
            }
        }

        /// <summary>
        /// Send a C-SoS request. Response is JSON DemandReply with PermitState.
        /// Sets LastReCount to the arbitrator's suggested retry wait duration.
        /// </summary>
        public int Send_CSoS(string sub, Demand_CSoS demand)
        {
            if (connection == null) return ErrorResult;
            string raw = SendRaw_CSoS(sub, demand);
            // Try plain integer first (for simple responses like "1")
            if (int.TryParse(raw, out int result))
            {
                LastReCount = 0;
                return result;
            }
            // Parse as JSON DemandReply
            try
            {
                var reply = JsonUtility.FromJson<DemandReplyJson>(raw);
                LastReCount = reply.ReCount;
                return reply.PermitState;
            }
            catch
            {
                Debug.LogError($"[Nats] Failed to parse C-SoS response for '{sub}': {raw}");
                LastReCount = 0;
                return -1;
            }
        }
    }
}
