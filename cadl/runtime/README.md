# SoS-DSL Contract Runtime

Reference implementation of the SoS-DSL extension's per-instance
contract execution model, as specified in
[cadl-spec Appendix E](https://github.com/ertlnagoya/cadl-spec)
(`appendix-e-sos-dsl.md`).

The runtime consumes a CADL Sim-IR JSON document that includes the
extension's `lifecycle:` and `monitors:` blocks and drives one
contract instance per delivery request through its lifecycle, emitting
an NDJSON event log (`--log <path>`). The cadl-explorer Contract Lifecycle
page reads the same Sim-IR JSON; it does not read the log.

## End-to-end pipeline

```
  ┌─────────────────────┐
  │  examples/*.cadl    │  CADL source with lifecycle: + monitors:
  │  (cadl_repo)        │  (Appendix E.6)
  └──────────┬──────────┘
             │  cadl sim-ir … --format json
             ▼
  ┌─────────────────────┐
  │  *.ir.json          │  Sim-IR (Appendix E.7)
  └──────────┬──────────┘
             │
             ├──────────────►  cadl-explorer / Contract Lifecycle page
             │                 (graphviz state machine + monitors panel)
             │
             ▼
  ┌─────────────────────┐
  │  ContractRuntime    │  cadl/runtime/engine.py
  │  (this package)     │  drives state machine, deadline timers,
  └──────────┬──────────┘  periodic + event monitors
             │
             ▼
  ┌─────────────────────┐
  │  trace.ndjson       │  one record per lifecycle event / violation
  └─────────────────────┘
```

## Running the demo

```bash
# Happy path: ack within deadline -> Accepted (terminal not yet)
python -m cadl.runtime.demo_delivery --scenario happy

# Late: no ack within 5s -> deadline violation -> Violated
python -m cadl.runtime.demo_delivery --scenario late

# Battery: world.ROBOT.battery drops below 20 in Assigned ->
# battery_guard monitor fires -> Violated
python -m cadl.runtime.demo_delivery --scenario battery
```

## Tests

```bash
PYTHONPATH=. python -m pytest cadl/runtime/tests/test_engine.py -v
```

14 tests cover the predicate evaluator, lifecycle happy path,
deadline-driven `on_violation` lift, deadline cancellation on early
ack, periodic-monitor lift, and NDJSON log persistence.

## Scope (v0.1)

Implemented:
- Lifecycle state machine (initial / terminal / transitions)
- `on:` event-driven transitions
- `when:` guard predicates
- `deadline_ms` timers with `on_violation` lift
- Periodic and event-driven `monitors:` with `on_match` lift
- NDJSON log of `lifecycle` and `violation` events

Out of scope for v0.1 (see Appendix E.8):
- Reward and sanction *execution* — events are recorded only
- ROS2 topic ↔ port mapping — the host wires `post_event`
- Multi-instance fairness, persistence, replay
