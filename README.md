# CADL Raspimouse Simulator

English | [日本語](README_ja.md)

The simulator used by the hands-on course of
[CADL](https://github.com/ertlnagoya/cadl) (Contract Architecture Description
Language): five Raspberry Pi Mouse robots deliver goods on a graph road
network under a Collaborative System of Systems (C-SoS), and the contracts
written in CADL / SoS-DSL are monitored while they run.

This repository contains only what the course needs. Follow the
[hands-on textbook](https://www.ertl.jp/cadl-spec/docs/handson/main-textbook)
for the full walkthrough; the notes below are a map of the repository.

## Layout

| Path | What it is | Used in |
|---|---|---|
| `cadl/runtime/` | Python reference runtime for SoS-DSL contracts (lifecycle, deadlines, monitors) with scripted demos and tests | Step 5, exercises |
| `unity/` | Unity project: the C-SoS scene, robot and road prefabs, and `Assets/Scripts/SoSDsl/` where generated C# is dropped | Steps 5–6 |
| `arbitrator/C-SoS/` | Go arbitrator that grants road segments and assigns deliveries over NATS | Step 6 |
| `unity-mcp-custom/MCPForUnity/` | Unity package the project references from `unity/Packages/manifest.json` | (opened by Unity) |

## Quick check

The Python runtime has no dependencies beyond Python 3.9+:

```bash
python3 -m cadl.runtime.multi_robot_demo --summary
python3 -m pytest cadl/runtime/tests -q
```

Run these from the repository root. The directory is named `cadl` like the
compiler's Python package; running from the root makes Python pick this one.

The arbitrator needs Go 1.23+ and a running NATS server:

```bash
nats-server &
cd arbitrator/C-SoS/main
go run . -config ../../../unity/Assets/streamingAssets/cadl_config.json
```

Open `unity/` with **Unity 6000.2.9f1** (Unity 6.2), the version the project is
saved with, then open `Assets/Scenes/C-SoS.unity`.

## Generating the contract code

`unity/Assets/Scripts/SoSDsl/Generated/` and `Runtime/` are produced by the
CADL compiler. To regenerate them from a `.cadl` file, run this in a checkout
of [cadl](https://github.com/ertlnagoya/cadl):

```bash
./scripts/sos_dsl_handson_e2e.sh examples/sos_dsl_robot_delivery.cadl \
    --unity ../cadl-raspimouse-simulator/unity
```

## The MCP-SoS scene

The hands-on course uses `C-SoS.unity` only. `Assets/Scenes/MCP-SoS.unity`
(an Acknowledged SoS in which the robots are driven through
[MCP for Unity](unity-mcp-custom/MCPForUnity/README.md) instead of an
arbitrator) needs a different configuration file. With the bundled
`cadl_config.json` (`sosType: collaborative`) the scene disables its robots'
`Pilot_MCP` component and nothing moves.

Generate the configuration in a checkout of
[cadl](https://github.com/ertlnagoya/cadl) and write it over the bundled one:

```bash
cadl sim-gen examples/raspimouse_mcp_sos.cadl --target unity \
    -o ../cadl-raspimouse-simulator/unity/Assets/streamingAssets/cadl_config.json
```

The generated file has `sosType: acknowledged`. Start `nats-server` (no
arbitrator is needed), open `Assets/Scenes/MCP-SoS.unity` and press Play. To
go back to the course setup, restore the file:

```bash
git checkout -- unity/Assets/streamingAssets/cadl_config.json
```

MCP for Unity sends anonymous usage statistics unless the environment
variable `DISABLE_TELEMETRY=true` is set before Unity starts.

`Assets/Scenes/D-SoS.unity` is included for reference; its arbitrator is not
part of this export.

## About this repository

The simulator is developed in a separate research repository; this one is a
periodic export of the parts the course uses (`.export-source` records the
source commits). Issues are welcome here. Changes to `cadl/`, `unity/`,
`arbitrator/` and `unity-mcp-custom/` are overwritten by the next export, so
please describe a proposed change in an issue rather than a pull request.

## License

[Apache License 2.0](LICENSE). Third-party components keep their own
licenses; see [NOTICE](NOTICE).
