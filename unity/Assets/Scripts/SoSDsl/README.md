# Generated Unity C# — SoS-DSL Contract Runtime

Source SoS: **RobotDeliverySoS_DSLDemo**
C# namespace: `CADL.SosDsl`

This directory is the output of:

```bash
cadl codegen --target unity-csharp <input.cadl> --output <this-dir>
```

## Layout

```
Runtime/        # generator-emitted runtime support (regenerated each run)
  ContractEvent.cs
  ContractRuntime.cs
  EventBus.cs
  PredicateEvaluator.cs
  Severity.cs

Generated/      # one set of files per contract that declares lifecycle:
- `DELIVERY_SLA` — lifecycle states: Proposed, Assigned, Accepted, Delivering, Completed, Violated, Terminated
```

## Usage in Unity

1. Copy this entire directory into `Assets/Scripts/SoSDsl/` of your
   Unity project (preserve the subdirectory layout).
2. Add a `MonoBehaviour` host in your scene that owns one
   `ContractRuntime` instance and forwards relevant Unity events
   (port messages, sim ticks, world snapshots) to it.
3. Subscribe to `runtime.OnLifecycle` / `runtime.OnViolation` to
   render visuals or write logs.

The generator's semantics match the Python runtime in
`raspimouse-swarm-simulator/cadl/runtime/engine.py` — a generated
contract that passes the Python tests behaves the same in Unity.

Reward and sanction *execution* is intentionally out of scope (v0.1):
violation events are recorded but not actuated.
