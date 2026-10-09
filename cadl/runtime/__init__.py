"""SoS-DSL contract runtime (Appendix E reference implementation).

Reads a CADL Sim-IR JSON document (output of `cadl sim-ir`) that
includes the SoS-DSL extension's `lifecycle:` and `monitors:` blocks,
and provides a small in-process runtime that:

- creates a contract instance for each subject (delivery request),
- drives state transitions when matching events fire,
- arms deadline timers (`deadline_ms`) and lifts on_violation
  transitions when they expire,
- runs declarative monitors (event-driven and periodic) and emits
  violations / lifecycle transitions on rule match,
- writes a JSON log of every transition / violation / reward event,
  consumable by cadl-explorer's Lifecycle View and downstream tools.

Reward and sanction *execution* (e.g. token transfer, blacklisting)
is intentionally out of scope for v0.1; the runtime *records* reward
and sanction events without actuating them.
"""

from .engine import (
    ContractInstance,
    ContractRuntime,
    Event,
    EventBus,
    LifecycleEvent,
    MonitorEngine,
    Severity,
    StateMachineEngine,
    TimerService,
    ViolationEvent,
    evaluate_predicate,
    load_ir,
)

__all__ = [
    "ContractInstance",
    "ContractRuntime",
    "Event",
    "EventBus",
    "LifecycleEvent",
    "MonitorEngine",
    "Severity",
    "StateMachineEngine",
    "TimerService",
    "ViolationEvent",
    "evaluate_predicate",
    "load_ir",
]
