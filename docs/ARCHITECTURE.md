# Architecture

## Overview

`pmview-nextgen` has two distinct phases: **build time** (topology discovery + scene generation) and **runtime** (Godot running the generated scene with live metric updates).

```mermaid
graph TD
    pmproxy["pmproxy (PCP daemon)"]

    subgraph "Build Time"
        Projector["Host Projector (CLI)"]
        Projector -- "discovers topology" --> pmproxy
        Projector -- "generates" --> Scenes
    end

    subgraph "Runtime (Godot)"
        Scenes[".tscn Scenes + GDScript"]
        Bridge["Bridge Plugin<br/><small>PcpBindable · PcpBindingResource<br/>MetricPoller · SceneBinder</small>"]
        Scenes -- "bindings discovered by" --> Bridge
    end

    subgraph ".NET Libraries"
        ProjectionCore["PmviewProjectionCore<br/><small>discovery · layout · profiles</small>"]
        GodotBridge["PcpGodotBridge<br/><small>binding model · validation</small>"]
        Client["PcpClient<br/><small>HTTP/JSON client</small>"]
        Bridge --> GodotBridge
        GodotBridge --> Client
        Projector --> ProjectionCore
        ProjectionCore --> Client
    end

    Client -- "REST API" --> pmproxy
```

## Layers

From scene surface down to the wire:

| Layer | Language | Tests | Purpose |
|-------|----------|-------|---------|
| **Host Projector** | C# (.NET 10.0) | xUnit | CLI tool: orchestrates scene emission and project scaffolding |
| **PmviewProjectionCore** | C# (.NET 8.0) | xUnit | Discovery, layout calculation, OS profiles — reusable by any consumer |
| **Scenes** | GDScript + .tscn | Godot runtime | Visual scenes with metric-driven properties |
| **Bridge Plugin** | C# (Godot.NET.Sdk) | gdUnit4 | MetricPoller, SceneBinder, PcpBindable, PcpBindingResource, editor inspector |
| **PcpGodotBridge** | C# (.NET 8.0) | xUnit | Binding model, validation, converter |
| **PcpClient** | C# (.NET 8.0) | xUnit | HTTP client for pmproxy REST API |

**Key design decisions:**

- PcpClient has zero Godot dependencies — pure .NET, fully xUnit testable
- PcpGodotBridge is also Godot-free: binding model and validation live here, maximising test surface
- PmviewProjectionCore is Godot-free: topology discovery, layout, and OS profiles live here so any consumer (CLI, future standalone app) can reuse them
- The Bridge Plugin is the only Godot-dependent layer — kept thin by design
- Scenes are GDScript: lightweight controllers, no business logic

### PmviewProjectionCore

Extracted from Host Projector to allow multiple consumers (the CLI today, a standalone Godot app in future) to share topology discovery, layout calculation, and OS-specific profile logic without duplicating code.

**Contents:**

| Namespace | Responsibility |
|-----------|---------------|
| `Discovery` | `MetricDiscovery` — queries pmproxy for available metrics and builds a `HostTopology` |
| `Models` | `HostTopology`, `HostOs`, `SceneLayout`, `PlacedZone`, `PlacedShape`, `PlacedStack`, `ZoneDefinition`, `MetricShapeMapping`, `Vec3`, `RgbColour`, and supporting enums |
| `Layout` | `LayoutCalculator` — turns a topology + zone definitions into a positioned `SceneLayout` |
| `Profiles` | `IHostProfileProvider`, `HostProfileProvider`, `LinuxProfile`, `MacOsProfile`, `SharedZones` — OS-aware zone and metric-shape mappings |

**Dependencies:** PcpClient only (pure .NET 8.0, no Godot).

Host Projector now depends on PmviewProjectionCore and retains only `Emission/` (scene file writing) and `Scaffolding/` (Godot project bootstrap).

## Runtime Data Flow

```mermaid
graph LR
    pmproxy["pmproxy"] -->|REST API| Client["PcpClient"]
    Client -->|raw value| Bridge["PcpGodotBridge<br/><small>range mapping</small>"]
    Bridge -->|mapped value| Poller["MetricPoller<br/><small>polls on interval</small>"]
    Poller -->|signal| Binder["SceneBinder"]
    Binder -->|updates| Node["Node3D properties<br/>/ @export vars"]
```

## Project Structure

```
pmview-nextgen/
├── pmview-nextgen.sln                  # Root solution (all .NET projects)
├── src/
│   ├── pcp-client-dotnet/              # PcpClient library
│   │   ├── src/PcpClient/
│   │   └── tests/PcpClient.Tests/
│   ├── pcp-godot-bridge/               # PcpGodotBridge library
│   │   ├── src/PcpGodotBridge/
│   │   └── tests/PcpGodotBridge.Tests/
│   ├── pmview-projection-core/         # PmviewProjectionCore library
│   │   ├── src/PmviewProjectionCore/
│   │   └── tests/PmviewProjectionCore.Tests/
│   ├── pmview-host-projector/          # Host Projector CLI (emission + scaffolding)
│   │   ├── src/PmviewHostProjector/
│   │   └── tests/PmviewHostProjector.Tests/
│   └── pmview-bridge-addon/            # Addon development workspace (Godot project)
│       ├── addons/pmview-bridge/       # Self-contained addon (copied to target projects)
│       │   ├── *.cs                    # Bridge plugin source
│       │   └── building_blocks/        # GroundedBar/Cylinder, GridLayout3D, ZoneLabel
│       ├── test/                       # gdUnit4 tests
│       ├── pmview-nextgen.csproj
│       └── pmview-nextgen.sln
├── dev-environment/                    # Docker compose: PCP + pmproxy + synthetic data
├── specs/                              # Feature specifications
└── docs/                              # Design documents and plans
```

## Further Reading

- [docs/BINDINGS.md](BINDINGS.md) — binding system deep-dive
- [docs/HOST-PROJECTOR.md](HOST-PROJECTOR.md) — scene generator reference
- [docs/plans/](plans/) — design documents and architecture decisions
