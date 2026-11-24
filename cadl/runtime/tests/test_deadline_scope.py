"""Deadlines are scoped to the from states of their transition (L-4) and
restart on every entry into one of them, a forced move included (E.5).
See ertlnagoya/cadl#91 and the issue on re-arming after a forced move."""

from pathlib import Path

from cadl.runtime import ContractRuntime


HERE = Path(__file__).parent


def _run(fixture, contract, events, horizon):
    rt = ContractRuntime(HERE / fixture)
    inst = rt.open_instance(contract, "i1", t_ms=0)
    rt.update_world({"A": {"x": 0}})
    for t in range(0, horizon + 1):
        for te, name in events:
            if te == t:
                rt.post_event(name, t_ms=t)
        rt.tick(t)
    records = [(r["kind"], r.get("from_state"), r.get("to_state")) for r in rt.log
               if r.get("transition_id") != "<open>"]
    return inst, records


def test_leaving_the_from_state_cancels_the_deadline():
    inst, records = _run("fixture_deadline_scope.ir.json", "C2",
                         [(1000, "A -> B : req"), (2000, "A -> B : cancel")], 10000)
    assert inst.state == "Idle"
    assert records == [("lifecycle", "Idle", "Waiting"), ("lifecycle", "Waiting", "Idle")]


def test_forced_move_rearms_the_deadline():
    inst, records = _run("fixture_rearm.ir.json", "C3", [], 150000)
    assert inst.state == "Overstay"
    assert records == [("violation", None, None), ("lifecycle", "Holding", "Overstay"),
                       ("violation", None, None), ("lifecycle", "Overstay", "Overstay")]
