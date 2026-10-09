using LineTrace.Handlers;
using RasPiMouse;
using System;
using UnityEngine;
using Random = UnityEngine.Random;
using CADLConfig;

namespace LineTrace
{
    /// <summary>
    /// C-SoS Pilot: autonomous robot that proposes its own routes and asks the
    /// arbitrator to verify edge availability.
    ///
    /// Two operating modes (selected by CADL taskArbitration.enabled):
    ///
    ///   false (default) — "Random Goal" mode
    ///     Robots pick random goals and compete for edges (edge-conflict protocol).
    ///     This is the current baseline used in v2 experiments.
    ///
    ///   true — "FCFS Task" mode
    ///     The arbitrator broadcasts delivery tasks.  Robots claim the task based
    ///     on motivation-weighted delay (high motivation → claim sooner → FCFS
    ///     advantage).  The first robot whose claim arrives wins the task.
    ///     Edge navigation uses the same edge-conflict protocol as above.
    /// </summary>
    public class Pilot_CSoS : MonoBehaviour
    {
        private static int MaxRetryCount => SimulatorConfigurator.GetMaxRetries(3);

        public int id;
        public int src;
        public int dst;
        private int next;
        public int re;
        public int goal;
        public int level;

        private Mouse mouse;
        private Handler handler;
        private Nats nats;
        private RouteSearch routeSearch;
        private Cross preCross = null;
        private float radius = 0f;
        private bool reted = true;
        private float reTime;

        // NATS communication error tracking
        private int commErrorCount = 0;
        private const int MaxCommErrors = 10;
        private bool commFailed = false;

        // NATS subject names (loaded from CADL)
        private string subInit;
        private string subNext;
        private string subRet;
        private string subGoalcount;
        private string subResource;

        // Periodic resource fetch (matches thesis ResourceGetType.Periodic, 1s interval)
        private float resourcePeriodicTimer = 0f;
        private const float ResourcePeriodicInterval = 1f;

        // C-SoS P1-P4 delivery cap
        private int maxDeliveries = -1;
        private int deliveryCount = 0;   // counts only FCFS deliveries (not return-home trips)
        private bool retired = false;

        // ── SelectGoal (thesis GoalType.Select) ─────────────────────────────────
        /// <summary>
        /// True when wanderingGoalMode == "select" (thesis-faithful deterministic wandering).
        /// False (default) = RandomGoal() as before.
        /// </summary>
        private bool selectGoalMode = false;

        /// <summary>
        /// Goal-list for SelectGoal().
        /// Thesis hardcoded: { 9, 8, 3, 2, 5, 10, 7, 8, 0, 4, 6, 1 }.
        /// Can be overridden via CADL motivationConfig.wanderingGoalList.
        /// </summary>
        private int[] wanderingGoalList = { 9, 8, 3, 2, 5, 10, 7, 8, 0, 4, 6, 1 };

        /// <summary>
        /// Number of FCFS deliveries completed by this robot.
        /// Used by CADLMetricsCollector to report delivery-only counts (not wandering).
        /// </summary>
        public int DeliveryCount => deliveryCount;

        /// <summary>
        /// True when task-arbitration (FCFS) mode is active.
        /// Used by CADLMetricsCollector to select deliveryCount vs level.
        /// </summary>
        public bool TaskArbitrationEnabled => taskArbitrationEnabled;

        /// <summary>
        /// True between winning a delivery (OnGoalAssigned) and reaching
        /// its goal (CompleteGoal). Read-only — exposed for the SoS-DSL
        /// Pilot↔ContractRuntime bridge.
        /// </summary>
        public bool HasActiveDelivery => hasActiveDelivery;

        private string[] RMColor => CADLConfig.GraphDefinition.RMColor;

        // ── Task-arbitration (FCFS mode) ──────────────────────────────────────

        private bool taskArbitrationEnabled = false;
        private float maxClaimDelaySec = 3.0f;
        private float agentMotivation = 0.5f;  // from CADL motivationConfig

        private string subDeliveryRequest;
        private string subGoalClaim;
        private string subGoalRequest;
        private string subTaskEnd;
        private string subTaskRetire;

        /// <summary>True while this robot is executing a delivery task.</summary>
        private bool hasActiveDelivery = false;
        /// <summary>A delivery request arrived; waiting for claim-delay timer.</summary>
        private bool pendingClaim = false;
        private int  pendingDeliveryID = -1;
        /// <summary>Goal to return to after delivery is complete.</summary>
        private int  returnGoal = -1;

        /// <summary>Countdown until we send goal_claim to the arbitrator.</summary>
        private float claimTimer = -1f;

        /// <summary>Configured home/start node (set in Awake, used in Start for teleport).</summary>
        private int _homeNode = -1;

        // ── Deadlock detection ──────────────────────────────────────────────────
        /// <summary>Time.time when the robot last arrived at a NEW intersection node.</summary>
        private float lastNodeProgressTime = 0f;
        /// <summary>Node number of the last completed move (to detect no-progress).</summary>
        private int   lastProgressNodeNum  = -1;
        /// <summary>Whether deadlock recovery is enabled (loaded from CADL taskArbitration).</summary>
        private bool  deadlockRecoveryEnabled = false;
        /// <summary>Seconds without node progress before triggering recovery (from CADL).</summary>
        private float deadlockTimeoutSec = 15f;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (!enabled) return;
            mouse = GetComponent<Mouse>();
            handler = new Handler(mouse, RMColor[id]);
            nats = new Nats();

            subInit      = SimulatorConfigurator.GetNatsSubject("init");
            subNext      = SimulatorConfigurator.GetNatsSubject("next");
            subRet       = SimulatorConfigurator.GetNatsSubject("ret");
            subGoalcount = SimulatorConfigurator.GetNatsSubject("goalcount");
            subResource  = SimulatorConfigurator.GetNatsSubject("resource");

            // Delivery cap (P1-P4) and wandering-goal mode
            var motCfg = CADLConfig.SimulatorConfigurator.Config?.motivationConfig;
            if (motCfg != null)
            {
                maxDeliveries = motCfg.GetMaxDeliveries(id);
                if (motCfg.agentMotivation != null && id < motCfg.agentMotivation.Length)
                    agentMotivation = motCfg.agentMotivation[id];

                // SelectGoal mode (thesis GoalType.Select)
                if (!string.IsNullOrEmpty(motCfg.wanderingGoalMode) &&
                    motCfg.wanderingGoalMode.ToLower() == "select")
                {
                    selectGoalMode = true;
                    if (motCfg.wanderingGoalList != null && motCfg.wanderingGoalList.Length > 0)
                        wanderingGoalList = motCfg.wanderingGoalList;
                    // else: keep default thesis hardcoded list
                    Debug.Log($"[Pilot_CSoS {id}] SelectGoal mode: list length={wanderingGoalList.Length}");
                }
            }

            // Task-arbitration mode
            var ta = CADLConfig.SimulatorConfigurator.Config?.taskArbitration;
            if (ta != null && ta.enabled)
            {
                taskArbitrationEnabled   = true;
                maxClaimDelaySec         = ta.maxClaimDelaySec;
                deadlockRecoveryEnabled  = ta.deadlockRecoveryEnabled;
                deadlockTimeoutSec       = ta.deadlockDetectionSec > 0f ? ta.deadlockDetectionSec : 15f;
                subDeliveryRequest = SimulatorConfigurator.GetNatsSubject("delivery_request");
                subGoalClaim       = SimulatorConfigurator.GetNatsSubject("goal_claim");
                subGoalRequest     = SimulatorConfigurator.GetNatsSubject("goal_request");
                subTaskEnd         = SimulatorConfigurator.GetNatsSubject("task_end");
                subTaskRetire      = SimulatorConfigurator.GetNatsSubject("task_retire");
            }

            src = GraphDefinition.ClampNode(src, $"CSoS Agent {id} src");
            dst = GraphDefinition.ClampNode(dst, $"CSoS Agent {id} dst");
            routeSearch = GraphDefinition.CreateRouteSearch();
            int configStart = SimulatorConfigurator.GetStartNode(id);
            goal = (configStart >= 0) ? configStart : RandomGoal();
            _homeNode = configStart;  // saved for Start()-time teleportation

            nats.Send_CSoS(subInit, new Demand_CSoS { Id = id, Src = src, Dst = dst });
        }

        private void Start()
        {
            // Teleport to home node in Start() (not Awake) so that ALL Cross objects
            // have been initialised (all Awake() calls done) before we search for them.
            if (taskArbitrationEnabled && _homeNode >= 0)
            {
                Cross[] allCrosses = FindObjectsOfType<Cross>();
                foreach (var c in allCrosses)
                {
                    if (c.number == _homeNode)
                    {
                        // Small per-robot lateral offset to avoid exact overlap
                        Vector3 offset = new Vector3((id - 2) * 0.12f, 0f, 0f);
                        transform.position = c.transform.position + offset;
                        preCross = c;          // set directly — no trigger detection needed
                        src      = _homeNode;
                        dst      = _homeNode;
                        var cap = c.GetComponent<CapsuleCollider>();
                        if (cap != null) radius = cap.radius;
                        Debug.Log($"[Pilot_CSoS {id}] Teleported+preCross set → home node {_homeNode} " +
                                  $"at {transform.position}");
                        break;
                    }
                }
                if (preCross == null)
                    Debug.LogWarning($"[Pilot_CSoS {id}] Could not find Cross with number={_homeNode} for teleport");
            }

            if (!taskArbitrationEnabled) return;
            // Subscribe to arbitrator broadcasts (async → dispatched via FlushCallbacks)
            nats.Subscribe(subDeliveryRequest, OnDeliveryReceived);
            nats.Subscribe(subGoalRequest,     OnGoalAssigned);
        }

        private void Update()
        {
            // Always flush async subscription callbacks onto main thread
            nats?.FlushCallbacks();

            if (!enabled || retired || nats == null || commFailed) return;

            // ── Periodic resource fetch (matches thesis ResourceGetType.Periodic, 1s) ──
            // Keeps local routeSearch flags up-to-date between intersections so that
            // the next Dijkstra call uses accurate occupancy information.
            if (taskArbitrationEnabled && subResource != null)
            {
                resourcePeriodicTimer += Time.deltaTime;
                if (resourcePeriodicTimer >= ResourcePeriodicInterval)
                {
                    resourcePeriodicTimer = 0f;
                    FetchResourceFlags();
                }
            }

            // ── Claim-delay timer (FCFS mode) ──
            // Tick the timer every frame; fire the claim when it expires.
            // We do NOT stop moving: blocking the graph while counting down
            // would congest paths and prevent other robots (especially the
            // high-motivation winner) from reaching their delivery goals.
            if (claimTimer >= 0f)
            {
                claimTimer -= Time.deltaTime;
                if (claimTimer <= 0f)
                {
                    claimTimer = -1f;
                    SendClaim();
                }
                // Fall through — robot continues navigating normally
            }

            // ── Collision recovery (original paper behaviour) ──
            // After backup completes, just resume Go — preCross is NOT reset.
            // Matches the original Shimoyama implementation: the robot re-enters Go and
            // naturally re-approaches the next intersection; OnTriggerEnter fires when it
            // physically reaches a *new* cross node.
            if (handler.CollisionBackCompleted)
            {
                handler.CollisionBackCompleted = false;
                Debug.Log($"[Pilot_CSoS {id}] Collision backup done — resuming Go (original behaviour)");
            }

            // ── Deadlock detection & recovery (task-arbitration mode) ──
            // If the robot hasn't arrived at a new node for deadlockTimeoutSec,
            // it is likely stuck in a head-to-head collision loop.
            // Recovery: pick a new RandomGoal() (wandering) or re-send route (delivery).
            // Controlled by CADL taskArbitration.deadlockRecoveryEnabled.
            if (deadlockRecoveryEnabled && taskArbitrationEnabled && preCross != null &&
                lastProgressNodeNum >= 0 &&
                Time.time - lastNodeProgressTime > deadlockTimeoutSec)
            {
                if (!hasActiveDelivery)
                {
                    // Wandering — change goal to break the deadlock asymmetrically.
                    // Use RandomGoal() even in select mode: we need non-determinism to break
                    // the symmetric deadlock; SelectGoal() would repeat the same stuck goal.
                    int newGoal = RandomGoal();
                    Debug.LogWarning($"[Pilot_CSoS {id}] Deadlock at node {preCross.number} " +
                                     $"({deadlockTimeoutSec}s) — new wander goal {newGoal}");
                    goal = newGoal;
                }
                else
                {
                    // Active delivery — keep goal, but force a fresh route request
                    Debug.LogWarning($"[Pilot_CSoS {id}] Deadlock at node {preCross.number} " +
                                     $"({deadlockTimeoutSec}s) — forcing re-route to delivery goal {goal}");
                }
                re = 0;
                reTime = 0f;
                lastNodeProgressTime = Time.time; // reset to avoid repeated rapid triggers
                int nxt = RouteWithResourceFetch(preCross.number, goal);
                nats.Send_CSoS(subNext,
                    new Demand_CSoS { Id = id, Src = preCross.number, Dst = preCross.number,
                                      Next = nxt, Goal = goal, Re = false });
            }

            // ── Edge-conflict navigation (shared by both modes) ──
            if (re != 0)
            {
                mouse.Stop();
                reTime += Time.deltaTime;
                if (reTime > 1f)
                {
                    re--;
                    reTime = 0f;
                    // v7 fix: use preCross.number as the route source so that
                    // SetNext(next) calls GetDir(next) on the cross the robot
                    // is actually standing on (not stale dst).
                    int retryNode = (preCross != null) ? preCross.number : dst;
                    next = RouteWithResourceFetch(retryNode, goal);
                    int isPermit = nats.Send_CSoS(subNext,
                        new Demand_CSoS { Id = id, Src = retryNode, Dst = retryNode, Next = next, Goal = goal, Re = re == 0 });

                    if (isPermit == Nats.ErrorResult)
                    {
                        commErrorCount++;
                        if (commErrorCount >= MaxCommErrors)
                        {
                            Debug.LogError($"[Pilot_CSoS {id}] NATS halted after {MaxCommErrors} errors.");
                            commFailed = true;
                            return;
                        }
                        re = Mathf.Min(1 << commErrorCount, 1024);
                    }
                    else
                    {
                        commErrorCount = 0;
                        if (isPermit == 1) { SetNext(next); re = 0; }
                        else if (isPermit == -2)
                        {
                            // v8 fix: Arbitrator returned -2 (claimed reverse path for us).
                            // Send RET to clear CrossFlags[retryNode] set by the -2 response,
                            // then reset retry counter so we keep trying (no action=Go wander).
                            nats.Send_CSoS(subRet, new Demand_CSoS { Id = id, Src = retryNode, Dst = retryNode });
                            reted = true;
                            re = MaxRetryCount;
                        }
                        else if (re == 0)
                        {
                            // v8 fix: All retries exhausted but still blocked (-1 or other).
                            // Reset retry counter to prevent falling into Go handler without
                            // a navigation direction (which causes uncontrolled wandering).
                            re = MaxRetryCount;
                        }
                    }
                }
            }
            else
            {
                handler.Handle();
                if (!reted && preCross &&
                    (preCross.transform.position - transform.position).magnitude >
                    CADLConfig.SimulatorConfigurator.GetIntersectionThreshold())
                {
                    reted = true;
                    nats.Send_CSoS(subRet, new Demand_CSoS { Id = id, Src = src, Dst = dst });
                }
            }
        }

        /// <summary>
        /// Fires every physics frame while inside a cross trigger.
        /// Used to initialise preCross when a robot spawns inside a trigger zone
        /// (OnTriggerEnter does not fire for objects that start inside a collider).
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            if (preCross != null) return;          // already initialised — become no-op
            var cross = other.GetComponent<Cross>();
            if (cross == null) return;
            preCross = cross;
            var cap = other.GetComponent<CapsuleCollider>();
            if (cap != null) radius = cap.radius;
            Debug.Log($"[Pilot_CSoS {id}] preCross initialised via OnTriggerStay → node {preCross.number}");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!enabled || retired || nats == null) return;
            var cross = other.GetComponent<Cross>();
            if (!cross || preCross == cross) return;
            preCross = cross;
            radius = other.GetComponent<CapsuleCollider>().radius;

            // Record physical progress (new node reached → reset deadlock timer)
            if (cross.number != lastProgressNodeNum)
            {
                lastProgressNodeNum  = cross.number;
                lastNodeProgressTime = Time.time;
            }

            if (!reted)
            {
                nats.Send_CSoS(subRet, new Demand_CSoS { Id = id, Src = src, Dst = dst });
                reted = true;
            }

            if (cross.number != dst)
            {
                print($"failed : {src} -> {dst} != {cross.number}");
                dst = cross.number;
            }

            if (cross.number == goal)
            {
                CompleteGoal();
                if (retired) return;
            }

            next = RouteWithResourceFetch(dst, goal);
            int permit = nats.Send_CSoS(subNext,
                new Demand_CSoS { Id = id, Src = src, Dst = dst, Next = next, Goal = goal, Re = false });

            if (permit == Nats.ErrorResult)
            {
                commErrorCount++;
                if (commErrorCount >= MaxCommErrors)
                {
                    Debug.LogError($"[Pilot_CSoS {id}] NATS halted after {MaxCommErrors} errors.");
                    commFailed = true;
                    return;
                }
                re = Mathf.Min(1 << commErrorCount, 1024);
            }
            else
            {
                commErrorCount = 0;
                if (permit == 1) { SetNext(next); re = 0; }
                else re = MaxRetryCount;
            }
        }

        // ── Task-arbitration callbacks ────────────────────────────────────────

        /// <summary>
        /// Called when the arbitrator broadcasts a new delivery request.
        /// If this robot is free, start the claim-delay countdown.
        /// </summary>
        private void OnDeliveryReceived(string msg)
        {
            if (retired || hasActiveDelivery || pendingClaim || claimTimer >= 0f) return;
            var req = JsonUtility.FromJson<DeliveryRequest>(msg);
            if (req == null) return;
            pendingDeliveryID = req.DeliveryID;
            pendingClaim = true;

            // Higher motivation → shorter delay → higher chance of winning FCFS
            // Matches thesis formula: Lerp(maxClaimDelaySec, 1.0, motivation)
            // e.g. maxClaimDelaySec=5.0, m=[0,0.025,0.05,0.075,0.1] → 5.0,4.9,4.8,4.7,4.6s
            float delay = Mathf.Lerp(maxClaimDelaySec, 1.0f, Mathf.Clamp01(agentMotivation));
            claimTimer = delay;
            pendingClaim = false; // timer drives the claim now
            Debug.Log($"[Pilot_CSoS {id}] Delivery {req.DeliveryID} received, claiming in {delay:F2}s");
        }

        /// <summary>
        /// Called when the arbitrator announces the winner of a delivery task.
        /// </summary>
        private void OnGoalAssigned(string msg)
        {
            var assignment = JsonUtility.FromJson<GoalAssignment>(msg);
            if (assignment == null) return;
            if (assignment.Id != id)
            {
                // Lost this round: reset pending state so the robot is
                // immediately ready for the next delivery broadcast.
                if (pendingDeliveryID >= 0) pendingDeliveryID = -1;
                claimTimer = -1f; // clear any running claim-delay timer
                return;
            }
            // Won: accept delivery task
            returnGoal = goal;
            goal = assignment.Goal_arbitrator;
            hasActiveDelivery = true;
            pendingDeliveryID = -1;
            Debug.Log($"[Pilot_CSoS {id}] WON delivery → goal {goal} (will return to {returnGoal})");

            // v7 fix: if the robot is already physically at the delivery goal,
            // complete it immediately. This avoids Route(X,X) returning a spurious
            // first-hop that cross X doesn't physically support (GetDir crash).
            if (preCross != null && preCross.number == goal)
            {
                Debug.Log($"[Pilot_CSoS {id}] Already at goal {goal} — instant complete.");
                CompleteGoal();
                return;
            }

            // Robot is at a known cross (preCross); kick navigation toward the goal.
            // Kick off the first navigation request immediately so the robot starts
            // moving without waiting for the next OnTriggerEnter.
            // Use preCross.number (current node) not dst (next waypoint) so that
            // GetDir() is called on the cross the robot is actually standing on.
            if (preCross != null)
            {
                next = RouteWithResourceFetch(preCross.number, goal);
                int permit = nats.Send_CSoS(subNext,
                    new Demand_CSoS { Id = id, Src = preCross.number, Dst = preCross.number, Next = next, Goal = goal, Re = false });
                if (permit == Nats.ErrorResult)
                {
                    commErrorCount++;
                    if (commErrorCount >= MaxCommErrors)
                    {
                        Debug.LogError($"[Pilot_CSoS {id}] NATS halted after {MaxCommErrors} errors.");
                        commFailed = true;
                        return;
                    }
                    re = Mathf.Min(1 << commErrorCount, 1024);
                }
                else
                {
                    commErrorCount = 0;
                    if (permit == 1) { SetNext(next); re = 0; }
                    else re = MaxRetryCount;
                }
            }
        }

        /// <summary>
        /// Complete the current goal (delivery or wandering).
        /// Shared by OnTriggerEnter (physical arrival) and OnGoalAssigned (already-at-goal case).
        ///
        /// Level accounting matches thesis (卒論2):
        ///   level     — increments only on FREE-TIME wandering goal completions.
        ///               Used by SelectGoal() to advance each robot's position in wanderingGoalList.
        ///   deliveryCount — increments on delivery completions (thesis "level_goal").
        /// </summary>
        private void CompleteGoal()
        {
            // NOTE: level++ is intentionally NOT here.
            // Thesis: level counts only free-time completions, NOT delivery completions.
            // See thesis Pilot_CSoS.cs: level++ is inside the wandering-goal completion branch only.

            if (taskArbitrationEnabled && hasActiveDelivery)
            {
                // ── Delivery complete ─────────────────────────────────────────────
                // Do NOT increment level (thesis: level_goal++ for deliveries, level stays for wandering).
                deliveryCount++;
                print("[Goal]" + " RM: " + RMColor[id] + " Delivery count: " + deliveryCount + " time[s]: " + Time.time);
                nats.Publish(subGoalcount, $"{id},{deliveryCount}");

                hasActiveDelivery = false;
                nats.Publish(subTaskEnd,
                    UnityEngine.JsonUtility.ToJson(new Demand { Id = id, Src = src, Dst = dst }));
                // Return to pre-delivery wandering position (thesis: goal = goal_robot).
                // If no returnGoal saved, pick a fresh wandering goal.
                goal = (returnGoal >= 0) ? returnGoal : WanderingGoal();
                // If returnGoal happens to equal current position (dst), Route(dst,dst)
                // would return dst itself and crash in Cross.GetDir. Use RandomGoal()
                // as fallback — SelectGoal() would return the same dst again.
                if (goal == dst) goal = RandomGoal();
                returnGoal = -1;

                // C-SoS P1-P4 cap: check only on delivery completion (matches original level_goal)
                if (maxDeliveries > 0 && deliveryCount >= maxDeliveries)
                {
                    print($"[Retired] RM: {RMColor[id]} reached delivery cap {maxDeliveries}. Autonomous idle.");
                    retired = true;
                    // Notify arbitrator so it can decrement ExpectedRobots in all-robot-wait mode.
                    // Without this, the claim round waits forever for a robot that will never claim.
                    if (subTaskRetire != null)
                        nats.Publish(subTaskRetire,
                            UnityEngine.JsonUtility.ToJson(new Demand { Id = id, Src = src, Dst = dst }));
                }
            }
            else
            {
                // ── Free-time wandering goal complete ─────────────────────────────
                // Increment level HERE (thesis: level++ only in wandering completion branch).
                level++;

                if (taskArbitrationEnabled)
                {
                    // Wandering between deliveries (or returning to pre-delivery position).
                    // Thesis: uses SelectGoal() / RandomGoal() depending on GoalType.
                    goal = WanderingGoal();
                    if (goal == dst) goal = RandomGoal();
                }
                else
                {
                    // Non-task-arb (autonomous wandering) mode: report every goal.
                    print("[Goal]" + " RM: " + RMColor[id] + " Goal count: " + level + " time[s]: " + Time.time);
                    nats.Publish(subGoalcount, $"{id},{level}");
                    goal = WanderingGoal();
                    if (goal == dst) goal = RandomGoal();
                }
            }
        }

        /// <summary>Send goal_claim to the arbitrator (called after claim-delay timer expires).</summary>
        private void SendClaim()
        {
            if (retired || hasActiveDelivery || pendingDeliveryID < 0) return;
            int result = nats.SendDelivery(subGoalClaim,
                new Demand_Delivery { Id = id, DeliveryID = pendingDeliveryID });
            Debug.Log($"[Pilot_CSoS {id}] goal_claim delivery={pendingDeliveryID} → {result}");
        }

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetch current edge/cross occupancy from the arbitrator and update local
        /// routeSearch flags.  Called both periodically (1s) and before every Dijkstra
        /// computation, matching the thesis ResourceGetType.Periodic behaviour.
        /// </summary>
        private void FetchResourceFlags()
        {
            if (subResource == null) return;
            var reply = nats.SendResource(subResource, new DemandResource { Id = id });
            if (reply != null && reply.ok && reply.edgeFlags != null && reply.edgeFlagRow > 0)
            {
                int n = reply.edgeFlagRow;
                for (int i = 0; i < n && i < routeSearch.Size; i++)
                    for (int j = 0; j < n && j < routeSearch.Size; j++)
                        routeSearch.EdgeFlags[i][j] = reply.edgeFlags[i * n + j];
                if (reply.crossFlags != null)
                    Array.Copy(reply.crossFlags, routeSearch.CrossFlags,
                               Math.Min(reply.crossFlags.Length, routeSearch.Size));
            }
        }

        /// <summary>
        /// Fetch current edge/cross occupancy from the arbitrator, update the local
        /// routeSearch, then compute the next hop via Dijkstra.
        /// This mirrors the original Shimoyama code's "resource request before Dijkstra"
        /// mechanism, which ensures the robot avoids edges occupied in either direction.
        /// Falls back to plain Route() if the resource request fails.
        /// </summary>
        private int RouteWithResourceFetch(int from, int goal)
        {
            FetchResourceFlags();
            return RouteSearchConfigurator.Route(routeSearch, from, goal);
        }

        private void SetNext(int next)
        {
            if (src == next)
            {
                handler.SetBack();
                nats.Send_CSoS(subRet, new Demand_CSoS { Id = id, Src = src, Dst = dst });
            }
            else
            {
                reted = false;
                handler.SetCross(preCross.transform.position, preCross.GetDir(next));
            }
            src = preCross.number;
            dst = next;
            if (src == dst) print($"{src} == {dst}");
        }

        /// <summary>
        /// Deterministic wandering goal selection matching thesis (卒論2) Pilot_CSoS.SelectGoal().
        /// Formula: wanderingGoalList[(level + id + 8) % wanderingGoalList.Length]
        ///
        /// level  — counts only free-time completions (NOT delivery completions).
        /// id     — robot index (0-4), offsets each robot's position in the list.
        /// +8     — additional per-list-wrap offset present in the thesis (硬コード).
        ///
        /// This makes each robot's wandering sequence fully deterministic and
        /// independent of Unity's Random state, eliminating seed-to-seed variance.
        /// </summary>
        private int SelectGoal()
        {
            return wanderingGoalList[(level + id + 8) % wanderingGoalList.Length];
        }

        /// <summary>
        /// Returns the next wandering goal: SelectGoal() when selectGoalMode is true,
        /// RandomGoal() otherwise.  Use this everywhere a free-time goal is needed.
        /// </summary>
        private int WanderingGoal() => selectGoalMode ? SelectGoal() : RandomGoal();

        private int RandomGoal() => CADLConfig.GraphDefinition.RandomGoal(dst);
    }
}
