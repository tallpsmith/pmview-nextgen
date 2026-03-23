# Architecture

## Overview

`pmview-nextgen` supports three modes of operation: **CLI generation** (Host Projector discovers topology and writes `.tscn` files), **addon-in-editor** (bridge plugin runs inside any Godot project), and **standalone app** (pmview-app discovers topology at launch and builds live nodes). All three share the same PmviewProjectionCore library and bridge plugin.

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

    subgraph "Standalone App (Godot)"
        App["pmview-app"]
        App -- "discovers topology at launch" --> pmproxy
        App -- "builds live nodes via" --> RuntimeBuilder["HostSceneBuilder"]
        App -- "uses" --> Bridge
    end

    subgraph ".NET Libraries"
        ProjectionCore["PmviewProjectionCore<br/><small>discovery · layout · profiles</small>"]
        GodotBridge["PcpGodotBridge<br/><small>binding model · validation</small>"]
        Client["PcpClient<br/><small>HTTP/JSON client</small>"]
        Bridge --> GodotBridge
        GodotBridge --> Client
        Projector --> ProjectionCore
        App --> ProjectionCore
        ProjectionCore --> Client
    end

    Client -- "REST API" --> pmproxy
```

## Layers

From scene surface down to the wire:

| Layer | Language | Tests | Purpose |
|-------|----------|-------|---------|
| **pmview-app** | C# (Godot.NET.Sdk) + GDScript | — | Standalone Godot app: runtime discovery, scene building, scene flow |
| **Host Projector** | C# (.NET 10.0) | xUnit | CLI tool: orchestrates scene emission and project scaffolding |
| **PmviewProjectionCore** | C# (.NET 8.0) | xUnit | Discovery, layout calculation, OS profiles — reusable by any consumer |
| **Scenes** | GDScript + .tscn | Godot runtime | Visual scenes with metric-driven properties |
| **Bridge Plugin** | C# (Godot.NET.Sdk) | gdUnit4 | MetricPoller, SceneBinder, PcpBindable, PcpBindingResource, editor inspector |
| **PcpGodotBridge** | C# (.NET 8.0) | xUnit | Binding model, validation, converter |
| **PcpClient** | C# (.NET 8.0) | xUnit | HTTP client for pmproxy REST API |

**Key design decisions:**

- PcpClient has zero Godot dependencies — pure .NET, fully xUnit testable
- PcpGodotBridge is also Godot-free: binding model and validation live here, maximising test surface
- PmviewProjectionCore is Godot-free: topology discovery, layout, and OS profiles live here so any consumer (CLI, standalone app) can reuse them
- The Bridge Plugin is the only Godot-dependent layer — kept thin by design
- Scenes are GDScript: lightweight controllers, no business logic
- **Three-mode model:** the same `SceneLayout` drives TscnWriter (CLI → `.tscn` text), the bridge addon (editor workflow), and HostSceneBuilder (standalone app → live nodes)

### PmviewProjectionCore

Extracted from Host Projector to allow multiple consumers (the CLI and the standalone app) to share topology discovery, layout calculation, and OS-specific profile logic without duplicating code.

**Contents:**

| Namespace | Responsibility |
|-----------|---------------|
| `Discovery` | `MetricDiscovery` — queries pmproxy for available metrics and builds a `HostTopology` |
| `Models` | `HostTopology`, `HostOs`, `SceneLayout`, `PlacedZone`, `PlacedShape`, `PlacedStack`, `ZoneDefinition`, `MetricShapeMapping`, `Vec3`, `RgbColour`, and supporting enums |
| `Layout` | `LayoutCalculator` — turns a topology + zone definitions into a positioned `SceneLayout` |
| `Profiles` | `IHostProfileProvider`, `HostProfileProvider`, `LinuxProfile`, `MacOsProfile`, `SharedZones` — OS-aware zone and metric-shape mappings |

**Dependencies:** PcpClient only (pure .NET 8.0, no Godot).

Host Projector now depends on PmviewProjectionCore and retains only `Emission/` (scene file writing) and `Scaffolding/` (Godot project bootstrap).

### pmview-app (Standalone Application)

A self-contained Godot application that performs topology discovery and scene building at runtime — no CLI step, no pre-generated `.tscn` files. The user launches the app, enters a pmproxy endpoint, and gets a live 3D host view.

**Scene flow:** main menu → loading → host view

| Scene | Controller | Role |
|-------|-----------|------|
| `main_menu.tscn` | `MainMenuController.gd` | Connection form (endpoint URL), animated 3D title, KITT scanner launch button |
| `loading.tscn` | `LoadingController.gd` + `LoadingPipeline.cs` | Six-phase pipeline: connect → topology → instances → profile → layout → build. Each phase materialises a letter of P-M-V-I-E-W |
| `host_view.tscn` | `HostViewController.gd` | Receives the built `Node3D` tree, reparents it, wires MetricPoller → SceneBinder, handles ESC-to-menu |

A `SceneManager` autoload singleton carries data between scenes (connection config, built scene graph).

**HostSceneBuilder vs TscnWriter:**

Both consume the same `SceneLayout` from PmviewProjectionCore. They produce the exact same node hierarchy, but through different mechanisms:

- **TscnWriter** (Host Projector CLI) serialises the layout to `.tscn` text files on disk. Godot loads these at editor or runtime. This is the offline/batch path.
- **HostSceneBuilder** (pmview-app) instantiates live Godot `Node3D`, `MeshInstance3D`, `Label3D` nodes in-process, loading packed scenes for building blocks and attaching scripts/bindings programmatically. This is the online/interactive path.

The structural equivalence means SceneBinder works identically regardless of which builder created the tree.

**Dependencies:** PmviewProjectionCore (discovery, layout, profiles) + bridge addon (building blocks, MetricPoller, SceneBinder, PcpBindable). PcpClient is used directly by LoadingPipeline for the discovery connection and transitively via the addon's MetricPoller for live polling.

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
│   ├── pmview-app/                     # Standalone Godot application
│   │   ├── addons/pmview-bridge/       # Bridge addon (copied from pmview-bridge-addon)
│   │   ├── scenes/                     # main_menu, loading, host_view
│   │   ├── scripts/                    # GDScript controllers + HostSceneBuilder.cs
│   │   └── pmview-app.csproj
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
