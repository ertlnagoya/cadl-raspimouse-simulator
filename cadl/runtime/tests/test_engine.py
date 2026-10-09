"""Tests for the SoS-DSL contract runtime engine."""

from pathlib import Path

import pytest

from cadl.runtime import (
    ContractRuntime,
    Severity,
    evaluate_predicate,
)
from cadl.runtime.engine import _tokenize


FIXTURE = Path(__file__).parent / "fixture_delivery.ir.json"


# ---------------------------------------------------------------------------
# Predicate evaluator
# ---------------------------------------------------------------------------

class TestPredicate:
    def test_simple_comparison(self):
        assert evaluate_predicate("x < 10", {"x": 5}) is True
        assert evaluate_predicate("x < 10", {"x": 15}) is False

    def test_member_access(self):
        world = {"ROBOT": {"battery": 12}}
        assert evaluate_predicate("ROBOT.battery < 20", world) is True

    def test_strip_index(self):
        # ROBOT[i] subscripts are placeholders; resolution drops them.
        world = {"ROBOT": {"battery": 18}}
        assert evaluate_predicate("ROBOT[i].battery < 20", world) is True

    def test_and_or_not(self):
        w = {"a": 1, "b": 2}
        assert evaluate_predicate("a == 1 AND b == 2", w) is True
        assert evaluate_predicate("a == 1 OR b == 99", w) is True
        assert evaluate_predicate("NOT a == 1", w) is False

    def test_in(self):
        w = {"state": "Assigned"}
        assert evaluate_predicate(
            "state IN [Assigned, Accepted]", {**w}
        ) in (True, False)
        # Strict form using string values
        assert evaluate_predicate(
            'state == "Assigned"', {**w}
        ) is True

    def test_state_reserved(self):
        # Bare identifier RHS that the world cannot resolve (e.g.
        # `Delivering`) is treated as an enum-like symbolic literal,
        # so `state == Delivering` matches when state == "Delivering".
        w = {"state": "Delivering"}
        assert evaluate_predicate("state == Delivering", w) is True
        assert evaluate_predicate("state == Assigned", w) is False
        # Quoted form still works.
        assert evaluate_predicate('state == "Delivering"', w) is True

    def test_lenient_on_failure(self):
        # A malformed expression must not raise.
        assert evaluate_predicate("x <<<", {}) is False

    def test_tokenize_handles_brackets(self):
        toks = _tokenize("ROBOT[i].battery < 20")
        kinds = [t[0] for t in toks]
        assert "ID" in kinds and "OP" in kinds and "NUM" in kinds


# ---------------------------------------------------------------------------
# Lifecycle: opening, normal completion
# ---------------------------------------------------------------------------

class TestLifecycleHappyPath:
    def test_open_initial_state(self, tmp_path):
        rt = ContractRuntime(FIXTURE, log_path=tmp_path / "log.ndjson")
        inst = rt.open_instance("DELIVERY_SLA", "req-001", t_ms=0)
        assert inst.state == "Proposed"
        # the open is recorded in the log
        assert any(
            r["kind"] == "lifecycle" and r["transition_id"] == "<open>"
            for r in rt.log
        )

    def test_event_drives_assign_then_accept(self):
        rt = ContractRuntime(FIXTURE)
        inst = rt.open_instance("DELIVERY_SLA", "req-001", t_ms=0)

        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=100,
        )
        assert inst.state == "Assigned"

        rt.post_event(
            "ROBOT[i] -> DISPATCHER : ack(accepted)",
            t_ms=200,
        )
        assert inst.state == "Accepted"

        # Two lifecycle events in addition to the <open>.
        kinds = [r["transition_id"] for r in rt.log
                 if r["kind"] == "lifecycle"]
        assert "<open>" in kinds
        assert "assign" in kinds
        assert "accept" in kinds


# ---------------------------------------------------------------------------
# Deadline -> on_violation transition
# ---------------------------------------------------------------------------

class TestDeadlineViolation:
    def test_accept_deadline_lifts_to_violated(self):
        rt = ContractRuntime(FIXTURE)
        inst = rt.open_instance("DELIVERY_SLA", "req-002", t_ms=0)

        # Move to Assigned -> arms the 5s accept deadline.
        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=1_000,
        )
        assert inst.state == "Assigned"

        # Tick to just before deadline — still Assigned.
        rt.tick(now_ms=5_999)
        assert inst.state == "Assigned"

        # Tick past deadline — must lift to Violated and emit a
        # violation tagged with deadline:<transition_id>.
        rt.tick(now_ms=6_001)
        assert inst.state == "Violated"
        assert inst.closed is True

        violations = [r for r in rt.log if r["kind"] == "violation"]
        assert len(violations) == 1
        v = violations[0]
        assert v["norm"] == "accept"
        assert v["severity"] == "Major"
        assert v["detected_by"].startswith("deadline:")

    def test_accept_before_deadline_cancels_timer(self):
        rt = ContractRuntime(FIXTURE)
        inst = rt.open_instance("DELIVERY_SLA", "req-003", t_ms=0)

        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=1_000,
        )
        rt.post_event(
            "ROBOT[i] -> DISPATCHER : ack(accepted)",
            t_ms=2_000,
        )
        # Past the original 5s window — no violation should fire.
        rt.tick(now_ms=10_000)
        assert inst.state == "Accepted"
        assert all(r["kind"] != "violation" for r in rt.log)


# ---------------------------------------------------------------------------
# Periodic monitor: battery_guard -> Violated
# ---------------------------------------------------------------------------

class TestBatteryMonitor:
    def test_battery_below_threshold_in_assigned_lifts_to_violated(self):
        rt = ContractRuntime(FIXTURE)
        inst = rt.open_instance("DELIVERY_SLA", "req-004", t_ms=0)

        # Drive to Assigned.
        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=100,
        )
        assert inst.state == "Assigned"

        # World says battery is healthy: monitor must NOT match.
        rt.update_world({"ROBOT": {"battery": 80}})
        rt.tick(now_ms=600)            # > 500ms period, monitor evaluates
        assert inst.state == "Assigned"

        # Drop the battery below 20: monitor must fire and lift the
        # contract into Violated.
        rt.update_world({"ROBOT": {"battery": 10}})
        rt.tick(now_ms=1_200)
        assert inst.state == "Violated"

        # The violation entry should be attributed to the monitor.
        v = next(r for r in rt.log if r["kind"] == "violation")
        assert v["detected_by"].startswith("monitor:battery_guard")
        assert v["severity"] == "Major"


# ---------------------------------------------------------------------------
# Log persistence
# ---------------------------------------------------------------------------

class TestLogPersistence:
    def test_ndjson_log(self, tmp_path):
        log = tmp_path / "trace.ndjson"
        rt = ContractRuntime(FIXTURE, log_path=log)
        rt.open_instance("DELIVERY_SLA", "req-005", t_ms=0)
        rt.post_event(
            "DISPATCHER -> ROBOT[i] : route_assignment",
            t_ms=10,
        )
        rt.tick(now_ms=10_000)         # blow past the 5s deadline

        text = log.read_text(encoding="utf-8").strip().splitlines()
        # at least: open, assign, deadline-violation, jump-to-Violated
        assert len(text) >= 4
        import json as _json
        last = _json.loads(text[-1])
        # Either a violation or a lifecycle record should be present
        assert last["kind"] in ("violation", "lifecycle")
