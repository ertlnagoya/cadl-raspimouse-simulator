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
- writes a JSON log of every lifecycle transition and violation,
  for downstream tools (cadl-explorer reads the Sim-IR, not this log).

Reward and sanction handling (e.g. token transfer, blacklisting) is
out of scope for v0.1: the runtime neither records nor executes them.
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
