"""Multi-robot end-to-end demo for the SoS-DSL contract runtime.

Drives **five** DeliverySlaContract instances concurrently against a
single ContractRuntime, each with a different scripted outcome. This
is the non-Unity counterpart of Assets/Scripts/SoSDsl/Demo/
PilotContractBridge: same IR, same event names, same predicate
evaluator. The trace this script emits should match the trace a
Unity build of the equivalent scenario would emit, modulo timing.

Outcomes
--------
+ ``robot-0-1`` — completes on time              → Completed
+ ``robot-1-1`` — never acks within 5s deadline  → Violated  (deadline:accept)
+ ``robot-2-1`` — battery drops mid-Assigned     → Violated  (monitor:battery_guard)
+ ``robot-3-1`` — accepts but never delivers,
  hits the late_failure deadline_watch monitor   → Violated  (monitor:deadline_watch)
+ ``robot-4-1`` — never receives a delivery      → stays Proposed (no events)

Usage::

    python -m cadl.runtime.multi_robot_demo
    python -m cadl.runtime.multi_robot_demo --log /tmp/trace.ndjson
    python -m cadl.runtime.multi_robot_demo --summary
"""

from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path

from .engine import ContractRuntime


FIXTURE = Path(__file__).parent / "tests" / "fixture_delivery.ir.json"

EV_ASSIGN = "DISPATCHER -> ROBOT[i] : route_assignment"
EV_ACK    = "ROBOT[i] -> DISPATCHER : ack(accepted)"
EV_TRANS  = "ROBOT[i].status == InTransit"
EV_DELIV  = "ROBOT[i].status == Delivered"


def _seed_battery(rt: ContractRuntime, instance_id: str, level: float) -> None:
    rt.update_instance_world(
        instance_id, {"ROBOT": {"battery": level}}
    )


ROBOTS = ("robot-0-1", "robot-1-1", "robot-2-1", "robot-3-1", "robot-4-1")
STEP_MS = 50
END_MS = 7500


def _script(rt: ContractRuntime) -> dict[int, list]:
    """Scripted actions keyed by simulated time (ms).

    Every action is applied at its own time inside a single,
    monotonically increasing clock (see ``run_scenario``), so the
    events in the trace and in the NDJSON log never go back in time.
    """
    def assign(inst):
        return lambda: rt.post_event(EV_ASSIGN, t_ms=100, instance_id=inst)

    def ack(inst):
        return lambda: rt.post_event(EV_ACK, t_ms=200, instance_id=inst)

    def in_transit(inst):
        return lambda: rt.post_event(EV_TRANS, t_ms=300, instance_id=inst)

    return {
        # 100ms: dispatcher offers to robots 0..3. robot-4-1 is left
        # silent and will stay Proposed for the rest of the run.
        100: [assign(i) for i in ("robot-0-1", "robot-1-1", "robot-2-1", "robot-3-1")],
        # 200ms: robots 0 and 3 ack. robot-1-1 (silent) blows its 5s
        # accept deadline (fires at 5100ms). robot-2-1 stays in
        # Assigned (no ack) so its battery_guard can fire.
        200: [ack(i) for i in ("robot-0-1", "robot-3-1")],
        # 300ms: robot-0 and robot-3 transition to Delivering.
        300: [in_transit(i) for i in ("robot-0-1", "robot-3-1")],
        # 400ms: robot-2 battery drops below the 20% threshold while
        # still Assigned; the periodic battery_guard (500ms period)
        # fires at its next evaluation. robot-3-1's request deadline is
        # tightened to 4000ms so deadline_watch lifts it to Violated
        # before the run ends.
        400: [lambda: _seed_battery(rt, "robot-2-1", 10.0),
              lambda: rt.update_instance_world(
                  "robot-3-1", {"request": {"deadline": 4000}})],
        # 600ms: robot-0 reaches its destination and completes on time
        # (the complete: guard `now <= request.deadline` holds).
        600: [lambda: rt.post_event(EV_DELIV, t_ms=600, instance_id="robot-0-1")],
    }


def run_scenario(rt: ContractRuntime) -> None:
    # 0ms: open all five contract instances. Each one starts in
    # Proposed; arming is per-instance.
    for inst_id in ROBOTS:
        rt.open_instance("DELIVERY_SLA", inst_id, t_ms=0)
        _seed_battery(rt, inst_id, 90.0)
        # The complete: transition has `when: now <= request.deadline`.
        # Seed both per-instance fields so the guard is satisfiable.
        rt.update_instance_world(inst_id, {
            "request": {"deadline": 60000},   # 60s from t=0
            "now": 0,
        })

    # One clock, 0..7500ms in 50ms steps. At each step the scripted
    # actions for that time are applied first, then every instance's
    # "now" is advanced and the runtime is ticked, so that deadline
    # timers and periodic monitors are evaluated at the same time:
    #   - robot-1-1 deadline:accept fires at t=5100ms (5s after t=100)
    #   - robot-2-1 battery_guard fires at its first evaluation after
    #     the battery drop while state == Assigned
    #   - robot-3-1 deadline_watch fires once now > 4000 while state
    #     is Assigned/Accepted/Delivering
    script = _script(rt)
    for now in range(0, END_MS + 1, STEP_MS):
        for inst_id in ROBOTS:
            rt.update_instance_world(inst_id, {"now": now})
        for action in script.get(now, []):
            action()
        if now > 0:
            rt.tick(now_ms=now)


# ---------------------------------------------------------------------------
# Entry
# ---------------------------------------------------------------------------

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--log", type=Path, default=None,
                   help="optional NDJSON log path")
    p.add_argument("--ir", type=Path, default=FIXTURE)
    p.add_argument("--summary", action="store_true",
                   help="terminal-state table instead of full trace")
    args = p.parse_args()

    rt = ContractRuntime(args.ir, log_path=args.log)
    run_scenario(rt)

    if args.summary:
        terminal: dict[str, str] = {}
        viol: dict[str, list[str]] = {}
        for r in rt.log:
            inst = r["instance_id"]
            if r["kind"] == "lifecycle":
                terminal[inst] = r["to_state"]
            elif r["kind"] == "violation":
                viol.setdefault(inst, []).append(r["detected_by"])

        print(f"# multi_robot_demo summary ({len(rt.log)} events)")
        for inst in sorted({**terminal, **viol}):
            v = ", ".join(viol.get(inst, [])) or "(none)"
            s = terminal.get(inst, "?")
            print(f"  {inst:12s}  state={s:12s}  violations=[{v}]")
        kinds = Counter(r["kind"] for r in rt.log)
        causes = Counter(
            r["cause"] for r in rt.log if r["kind"] == "lifecycle"
        )
        print(f"\n  kind counts:  {dict(kinds)}")
        print(f"  cause counts: {dict(causes)}")
        return 0

    print(f"# multi_robot_demo: {len(rt.log)} events")
    for r in rt.log:
        print(json.dumps(r, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
