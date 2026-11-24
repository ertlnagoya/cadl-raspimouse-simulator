"""A monitor that moves the instance to a terminal state closes it; the other
monitors of the same tick are not evaluated any more (ertlnagoya/cadl#90)."""

from pathlib import Path

from cadl.runtime import ContractRuntime


FIXTURE = Path(__file__).parent / "fixture_two_monitors.ir.json"


def test_second_monitor_not_evaluated_after_close():
    rt = ContractRuntime(FIXTURE)
    inst = rt.open_instance("C1", "c1", t_ms=0)
    rt.update_world({"A": {"x": 0}})
    for t in range(0, 3001):
        if t == 1500:
            rt.update_world({"A": {"x": 10}})
        rt.tick(t)
    assert inst.state == "Failed"
    violations = [r for r in rt.log if r["kind"] == "violation"]
    assert [v["detected_by"] for v in violations] == ["monitor:m_first"]
    moves = [(r["from_state"], r["to_state"]) for r in rt.log
             if r["kind"] == "lifecycle" and r["transition_id"] != "<open>"]
    assert moves == [("Open", "Failed")]
