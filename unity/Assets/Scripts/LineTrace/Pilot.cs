using LineTrace.Handlers;
using RasPiMouse;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LineTrace
{
    public class Pilot : MonoBehaviour
    {
        public int id;
        public int src;
        public int dst;
        public int re;

        public int goal;
        public int level;

        private Mouse mouse;
        private Handler handler;
        private Nats nats;

        private Cross preCross = null;
        private float radius = 0f;
        private bool reted = true;

        private float reTime;

        // NATS communication error tracking
        private int commErrorCount = 0;
        private const int MaxCommErrors = 10;
        private bool commFailed = false;

        // NATS subject names (loaded from CADL config)
        private string subInit;
        private string subNext;
        private string subRet;
        private string subGoalcount;

        private string[] RMColor => CADLConfig.GraphDefinition.RMColor;

        private void Awake()
        {
            if (!enabled) return; // Skip if component is disabled in Inspector
            mouse = GetComponent<Mouse>();
            handler = new Handler(mouse, RMColor[id]);
            nats = new Nats();

            // Cache NATS subjects from CADL config
            subInit      = CADLConfig.SimulatorConfigurator.GetNatsSubject("init");
            subNext      = CADLConfig.SimulatorConfigurator.GetNatsSubject("next");
            subRet       = CADLConfig.SimulatorConfigurator.GetNatsSubject("ret");
            subGoalcount = CADLConfig.SimulatorConfigurator.GetNatsSubject("goalcount");

            goal = RandomGoal();
            nats.Send(subInit, new Demand {Id = id, Src = src, Dst = dst});
        }

        private void Update()
        {
            if (!enabled || nats == null || commFailed) return;
            if (re != 0)
            {
                mouse.Stop();

                reTime += Time.deltaTime;
                if (reTime > 1f)
                {
                    re--;
                    reTime = 0f;
                    var next = nats.Send(subNext, new Demand {Id = id, Src = src, Dst = dst, Goal = goal, Re = re == 0});

                    if (next == Nats.ErrorResult)
                    {
                        commErrorCount++;
                        if (commErrorCount >= MaxCommErrors)
                        {
                            Debug.LogError($"[Pilot {id}] NATS communication failed {MaxCommErrors} times consecutively. Halting.");
                            commFailed = true;
                            return;
                        }
                        // Exponential backoff: wait 2^errorCount seconds (capped at ~17min by MaxCommErrors=10)
                        re = Mathf.Min(1 << commErrorCount, 1024);
                        Debug.LogWarning($"[Pilot {id}] NATS error #{commErrorCount}, backoff {re}s.");
                    }
                    else
                    {
                        commErrorCount = 0; // Any non-error response = NATS is working
                        if (next >= 0)
                        {
                            SetNext(next);
                            re = 0;
                        }
                    }
                }
            }
            else
            {
                handler.Handle();
                if (!reted && preCross && (preCross.transform.position - transform.position).magnitude > CADLConfig.SimulatorConfigurator.GetIntersectionThreshold())
                {
                    reted = true;
                    nats.Send(subRet, new Demand {Id = id, Src = src, Dst = dst});
                }
            }
        }


        private void OnTriggerEnter(Collider other)
        {
            if (!enabled || nats == null) return;
            var cross = other.GetComponent<Cross>();
            if (!cross || preCross == cross) return;
            preCross = cross;
            radius = other.GetComponent<CapsuleCollider>().radius;

            if (!reted)
            {
                nats.Send(subRet, new Demand {Id = id, Src = src, Dst = dst});
                reted = true;
            }

            if (cross.number != dst)
            {
                print($"failed : {src} -> {dst} != {cross.number}");
                dst = cross.number;
            }

            if (cross.number == goal)
            {
                level++;
                goal = RandomGoal();
                print("[Goal]" + " RM: " + RMColor[id] + " Goal count: " + level + " time[s]: " + Time.time);
                // Notify arbitrator of cumulative goal count so motivation throttling works
                nats.Publish(subGoalcount, $"{id},{level}");
            }

            var next = nats.Send(subNext, new Demand {Id = id, Src = src, Dst = dst, Goal = goal, Re = false});

            if (next == Nats.ErrorResult)
            {
                commErrorCount++;
                if (commErrorCount >= MaxCommErrors)
                {
                    Debug.LogError($"[Pilot {id}] NATS communication failed {MaxCommErrors} times consecutively. Halting.");
                    commFailed = true;
                    return;
                }
                re = Mathf.Min(1 << commErrorCount, 1024);
                Debug.LogWarning($"[Pilot {id}] NATS error #{commErrorCount}, backoff {re}s.");
            }
            else if (next >= 0)
            {
                commErrorCount = 0;
                SetNext(next);
                re = 0;
            }
            else
            {
                commErrorCount = 0; // Normal rejection = NATS is working
                re = -next;
            }
        }

        private void SetNext(int next)
        {
            if (src == next)
            {
                handler.SetBack();
                nats.Send(subRet, new Demand {Id = id, Src = src, Dst = dst});
            }
            else
            {
                reted = false;
                handler.SetCross(preCross.transform.position, preCross.GetDir(next));
            }

            src = preCross.number;
            dst = next;
            if (src == dst)
            {
                print($"{src} == {dst}");
            }
        }

//        private void OnTriggerExit(Collider other)
//        {
//            var cross = other.GetComponent<Cross>();
//            if (!cross || preCross != cross || reted) return;
//
//            nats.Send(subRet, new Demand {Id = id, Src = src, Dst = dst});
//            reted = true;
//        }

        private int RandomGoal() => CADLConfig.GraphDefinition.RandomGoal(dst);
    }
}