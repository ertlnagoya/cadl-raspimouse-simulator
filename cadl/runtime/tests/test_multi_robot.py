"""Tests for the multi-robot scenario and the runtime additions
(targeted post_event, per-instance world overrides) that it relies on.
"""

from pathlib import Path

import pytest

from cadl.runtime import ContractRuntime
from cadl.runtime.multi_robot_demo import run_scenario


FIXTURE = Path(__file__).parent / "fixture_delivery.ir.json"


# ---------------------------------------------------------------------------
# Targeted post_event
# ---------------------------------------------------------------------------

class TestTargetedPostEvent:
    def test_targeted_event_only_advances_named_instance(self):
        rt = ContractRuntime(FIXTURE)
        a = rt.open_instance("DELIVERY_SLA", "A", t_ms=0)
        b = rt.open_instance("DELIVERY_SLA", "B", t_ms=0)
        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=10, instance_id="A",
        )
        assert a.state == "Assigned"
        assert b.state == "Proposed"

    def test_unscoped_event_still_broadcasts(self):
        """instance_id=None is the legacy broadcast path."""
        rt = ContractRuntime(FIXTURE)
        a = rt.open_instance("DELIVERY_SLA", "A", t_ms=0)
        b = rt.open_instance("DELIVERY_SLA", "B", t_ms=0)
        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment", t_ms=10,
        )
        assert a.state == "Assigned"
        assert b.state == "Assigned"


# ---------------------------------------------------------------------------
# Per-instance world overrides
# ---------------------------------------------------------------------------

class TestPerInstanceWorld:
    def test_instance_world_overrides_global(self):
        rt = ContractRuntime(FIXTURE)
        rt.update_world({"ROBOT": {"battery": 90.0}})
        a = rt.open_instance("DELIVERY_SLA", "A", t_ms=0)
        b = rt.open_instance("DELIVERY_SLA", "B", t_ms=0)

        rt.update_instance_world("B", {"ROBOT": {"battery": 5.0}})

        snap_a = rt.world_snapshot(a)
        snap_b = rt.world_snapshot(b)
        assert snap_a["ROBOT"]["battery"] == 90.0
        assert snap_b["ROBOT"]["battery"] == 5.0

    def test_instance_world_two_level_merge(self):
        rt = ContractRuntime(FIXTURE)
        rt.update_world({"ROBOT": {"battery": 90.0, "speed": 1.0}})
        inst = rt.open_instance("DELIVERY_SLA", "A", t_ms=0)
        rt.update_instance_world("A", {"ROBOT": {"battery": 10.0}})
        snap = rt.world_snapshot(inst)
        # battery overridden, speed inherited from global.
        assert snap["ROBOT"]["battery"] == 10.0
        assert snap["ROBOT"]["speed"] == 1.0


# ---------------------------------------------------------------------------
# multi_robot_demo end-to-end
# ---------------------------------------------------------------------------

class TestMultiRobotScenario:
    @pytest.fixture
    def rt(self):
        rt = ContractRuntime(FIXTURE)
        run_scenario(rt)
        return rt

    def _terminal_state(self, rt, instance_id):
        last = None
        for r in rt.log:
            if (r["kind"] == "lifecycle"
                    and r["instance_id"] == instance_id):
                last = r["to_state"]
        return last

    def _violations_for(self, rt, instance_id):
        return [
            r["detected_by"] for r in rt.log
            if r["kind"] == "violation"
            and r["instance_id"] == instance_id
        ]

    def test_robot_0_completes(self, rt):
        assert self._terminal_state(rt, "robot-0-1") == "Completed"
        assert self._violations_for(rt, "robot-0-1") == []

    def test_robot_1_violates_via_deadline_accept(self, rt):
        assert self._terminal_state(rt, "robot-1-1") == "Violated"
        viols = self._violations_for(rt, "robot-1-1")
        assert any("deadline:accept" in v for v in viols)

    def test_robot_2_violates_via_battery_guard(self, rt):
        assert self._terminal_state(rt, "robot-2-1") == "Violated"
        viols = self._violations_for(rt, "robot-2-1")
        assert any("monitor:battery_guard" in v for v in viols)

    def test_robot_3_violates_via_deadline_watch_monitor(self, rt):
        assert self._terminal_state(rt, "robot-3-1") == "Violated"
        viols = self._violations_for(rt, "robot-3-1")
        assert any("monitor:deadline_watch" in v for v in viols)

    def test_robot_4_stays_proposed(self, rt):
        # Only the <open> lifecycle event for this robot.
        events = [
            r for r in rt.log if r["instance_id"] == "robot-4-1"
        ]
        assert len(events) == 1
        assert events[0]["kind"] == "lifecycle"
        assert events[0]["transition_id"] == "<open>"
        assert events[0]["to_state"] == "Proposed"

    def test_total_event_count(self, rt):
        # 5 opens + 4 assigns + 2 acks + 2 starts + 1 complete
        #   + 3 violations + 3 jumps = 20
        # The exact count is sensitive to scenario tweaks; just
        # require it to be in a sane range.
        assert 18 <= len(rt.log) <= 30

    def test_log_timestamps_never_go_back(self, rt):
        # The scenario runs on a single monotonically increasing clock,
        # so the trace (and the NDJSON log) can be plotted as a timeline.
        times = [r["t_ms"] for r in rt.log]
        assert times == sorted(times)

    def test_violation_times(self, rt):
        at = {
            (r["instance_id"], r["detected_by"]): r["t_ms"]
            for r in rt.log if r["kind"] == "violation"
        }
        # battery drops at 400ms; battery_guard (500ms period) fires at
        # its next evaluation, after the drop.
        assert 400 < at[("robot-2-1", "monitor:battery_guard")] <= 1000
        # accept deadline: assigned at 100ms + 5s.
        assert at[("robot-1-1", "deadline:accept")] == 5100
        # deadline_watch: request.deadline = 4000ms, 1s sampling.
        assert 4000 < at[("robot-3-1", "monitor:deadline_watch")] <= 5000
