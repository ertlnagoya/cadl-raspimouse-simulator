// <auto-edited-on-import="false">
//   Hand-written. Bridges raspimouse Pilot_CSoS state changes to a
//   per-robot DeliverySlaContract instance owned by the SoS-DSL
//   ContractRuntime singleton. Observation-only; does NOT modify
//   Pilot_CSoS at runtime.
// </auto-edited-on-import>
using System.Collections.Generic;
using LineTrace;
using UnityEngine;

namespace CADL.SosDsl.Demo {

    /// <summary>
    /// Attach this component to the same GameObject as a
    /// <see cref="LineTrace.Pilot_CSoS"/>. It opens one
    /// <see cref="DeliverySlaContract"/> instance per robot and
    /// drives it through Proposed → Assigned → Accepted → Delivering
    /// → Completed by *observing* state changes on the pilot, with
    /// no edits to the pilot's logic.
    ///
    /// State-change → contract event mapping
    /// -------------------------------------
    ///   pilot.HasActiveDelivery: false → true
    ///       == "DISPATCHER -> ROBOT[i] : route_assignment"
    ///          followed immediately by
    ///       == "ROBOT[i] -> DISPATCHER : ack(accepted)"
    ///       (the FCFS arbitrator broadcasts then awards in one
    ///       round-trip from the robot's POV).
    ///
    ///   first Update tick after assignment with mouse moving
    ///       == "ROBOT[i].status == InTransit"
    ///
    ///   pilot.DeliveryCount increments
    ///       == "ROBOT[i].status == Delivered"
    ///       (then opens a fresh contract instance for the next
    ///       request).
    ///
    /// Each contract instance ID is "robot-{id}-{deliveryNo}", which
    /// is stable, unique within a run, and easy to correlate with
    /// the pilot's own logs in the Console.
    /// </summary>
    [RequireComponent(typeof(Pilot_CSoS))]
    public sealed class PilotContractBridge : MonoBehaviour {

        [Header("World keys (must match the contract's predicates)")]
        [Tooltip("Battery level fed into the per-instance world. The "
                 + "battery_guard monitor compares ROBOT.battery < 20.")]
        [Range(0f, 100f)] public float robotBattery = 90f;

        [Tooltip("Per-delivery deadline in milliseconds, fed into the "
                 + "per-instance world as `request.deadline`. The "
                 + "deadline_watch monitor compares now > request.deadline. "
                 + "Default: 5 minutes per delivery.")]
        public long requestDeadlineMs = 5 * 60 * 1000;

        [Tooltip("How long (ms) to dwell in the Assigned state before "
                 + "emitting accept. Periodic monitors gated on "
                 + "state==Assigned (e.g. battery_guard samples every "
                 + "500ms) need at least one Tick to land while Assigned, "
                 + "so this must exceed the slowest such monitor's period. "
                 + "Default 700ms > battery_guard's 500ms.")]
        public long assignedDwellMs = 700;

        private Pilot_CSoS _pilot;
        private ContractRuntimeHost _host;
        private DeliverySlaContract _instance;
        private long _instanceOpenMs;
        private bool _prevHasActiveDelivery;
        private int  _prevDeliveryCount;
        private bool _emittedInTransit;
        private bool _emittedAssign;
        private bool _emittedAccept;
        private long _assignedAtMs;

        private void Awake() {
            _pilot = GetComponent<Pilot_CSoS>();
        }

        private void Start() {
            _host = ContractRuntimeHost.Instance;
            if (_host == null) {
                Debug.LogError(
                    $"[PilotContractBridge {_pilot?.id}] "
                    + "ContractRuntimeHost.Instance is null. "
                    + "Add a ContractRuntimeHost GameObject to the scene.");
                enabled = false;
                return;
            }
            OpenFreshInstance();
        }

        private void Update() {
            if (_host == null || _pilot == null || _instance == null) return;

            // 1) Stream a *per-instance* world snapshot so each robot's
            // monitors evaluate against this robot's telemetry, not a
            // global ROBOT.battery shared across the swarm.
            _instance.SetInstanceField(
                "ROBOT",
                new Dictionary<string, object> {
                    { "battery", (double)robotBattery },
                });
            _instance.SetInstanceField("now", _host.NowMs);
            _instance.SetInstanceField(
                "request",
                new Dictionary<string, object> {
                    { "deadline", _instanceOpenMs + requestDeadlineMs },
                });

            // 2) Observe state transitions and translate them into
            // contract events.
            bool hasActive = _pilot.HasActiveDelivery;

            if (!_prevHasActiveDelivery && hasActive) {
                // Won a delivery → enter Assigned now. Do NOT accept in
                // the same frame: the runtime Ticks once per frame, so if
                // assign+accept fire together the contract leaves Assigned
                // within one frame and no Tick ever samples a monitor
                // while state==Assigned (battery_guard would never fire).
                _host.Post("DISPATCHER -> ROBOT[i] : route_assignment");
                _assignedAtMs     = _host.NowMs;
                _emittedAssign    = true;
                _emittedAccept    = false;
                _emittedInTransit = false;
            }

            // Dwell in Assigned for assignedDwellMs so periodic monitors
            // gated on state==Assigned get at least one sample, THEN accept.
            if (_emittedAssign && !_emittedAccept &&
                _host.NowMs - _assignedAtMs >= assignedDwellMs) {
                _host.Post("ROBOT[i] -> DISPATCHER : ack(accepted)");
                _emittedAccept = true;
            }

            if (_emittedAccept && !_emittedInTransit) {
                // First tick after accept while delivering → InTransit.
                _host.Post("ROBOT[i].status == InTransit");
                _emittedInTransit = true;
            }

            int dc = _pilot.DeliveryCount;
            if (dc > _prevDeliveryCount) {
                // Delivery completed (pilot's CompleteGoal was reached).
                _host.Post("ROBOT[i].status == Delivered");
                // The contract has now closed. Prepare for the next.
                OpenFreshInstance();
            }

            _prevHasActiveDelivery = hasActive;
            _prevDeliveryCount = dc;
        }

        private void OpenFreshInstance() {
            int dn = _pilot.DeliveryCount + 1;
            string instanceId = $"robot-{_pilot.id}-{dn}";
            _instanceOpenMs = _host.NowMs;
            _instance = new DeliverySlaContract(
                instanceId: instanceId,
                rt: _host.Runtime,
                nowMs: _instanceOpenMs);
            _host.Runtime.Register(_instance);
            _emittedInTransit = false;
            _emittedAssign = false;
            _emittedAccept = false;
        }

        /// <summary>Current contract instance state, for inspector binding.</summary>
        public string CurrentState =>
            _instance != null ? _instance.State.ToString() : "(none)";
    }
}
