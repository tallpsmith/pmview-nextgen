# pmview-nextgen

**Bringing Performance Monitoring to Life**

## Overview

pmview-nextgen is a next-generation performance monitoring visualization tool that represents system performance metrics as living, breathing 3D environments. Inspired by the original PCP pmview, this project aims to restore humanity to system monitoring by bridging how systems naturally behave (alive, flowing, rhythmic) with how human brains naturally comprehend (spatial, environmental, emotional).

## The Vision

> **"Make people see their system alive and say 'oh wow'"**

Current performance monitoring systems force humans to process alive, complex systems through dead, fragmented data representations (grids, charts, numbers), breaking the link between system behavior and human comprehension. pmview-nextgen transforms passive monitoring into active curiosity by encoding system performance data into 4D environments (3D + time) that humans can process naturally.

## Core Philosophy

- **Systems are already alive** - they react, flow, have rhythms, respond to stimulus
- **Humans are optimized for spatial/environmental pattern recognition** - not data grids
- **Transform passive monitoring into active curiosity** - make people curious about their data
- **Bring people together** - technical and non-technical united through shared wonder
- **Augment, don't replace** - complement existing monitoring with team culture fun

## Architecture

```
GDScript Scenes  →  C# Bridge Nodes  →  PcpGodotBridge  →  PcpClient  →  pmproxy
 (visual)           (MetricPoller,       (binding config     (HTTP/JSON     (PCP
                     SceneBinder)         model + TOML)       client)        daemon)
```

**Four layers, each with clear responsibility:**

| Layer | Language | Tests | Purpose |
|-------|----------|-------|---------|
| **PcpClient** | C# (.NET 8.0) | xUnit | HTTP client for pmproxy REST API |
| **PcpGodotBridge** | C# (.NET 8.0) | xUnit | Binding config model, TOML parsing, property vocabulary |
| **Bridge Nodes** | C# (Godot.NET.Sdk) | Godot runtime | MetricPoller (polling + signals), SceneBinder (scene loading + property application) |
| **Scenes** | GDScript + .tscn | Godot runtime | Visual scenes with @export properties driven by metrics |

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Godot 4.4+](https://godotengine.org/download) with .NET support (the Mono/C# flavour)
- [Performance Co-Pilot (PCP)](https://pcp.io/) with pmproxy running (for live data)

## Quick Start

```bash
# Clone the repository
git clone https://github.com/tallpsmith/pmview-nextgen.git
cd pmview-nextgen

# Build and test the pure .NET libraries
dotnet test src/pcp-client-dotnet/PcpClient.sln
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln

# Open the Godot project in Godot Editor
# (File → Open Project → navigate to godot-project/)

# Build the full solution (from godot-project/)
dotnet build godot-project/pmview-nextgen.sln
```

## Project Structure

```
pmview-nextgen/
├── src/
│   ├── pcp-client-dotnet/          # PcpClient: pmproxy HTTP/JSON client
│   │   ├── src/PcpClient/          # Library source
│   │   └── tests/PcpClient.Tests/  # xUnit tests
│   └── pcp-godot-bridge/           # PcpGodotBridge: binding config model
│       ├── src/PcpGodotBridge/     # Library source (TOML parsing, validation)
│       └── tests/PcpGodotBridge.Tests/
├── godot-project/                  # Godot 4.4 project
│   ├── scripts/
│   │   ├── bridge/                 # C# bridge nodes (MetricPoller, SceneBinder)
│   │   └── scenes/                 # GDScript controllers
│   ├── scenes/                     # .tscn scene files
│   ├── bindings/                   # .toml binding configs
│   ├── pmview-nextgen.csproj       # Godot C# project
│   └── pmview-nextgen.sln          # Full solution (all projects)
├── prototypes/                     # Spike prototypes (validated, archived)
├── specs/                          # Feature specifications
└── docs/                           # Design documents and plans
```

## How Binding Configs Work

Binding configs are TOML files that map PCP metrics to scene node properties:

```toml
[meta]
scene = "res://scenes/test_bars.tscn"
poll_interval_ms = 1000
description = "CPU load averages as vertical bars"

[[bindings]]
scene_node = "LoadBar1Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_id = 0
```

**Built-in property vocabulary** maps friendly names to Godot properties:

| Property | Godot Mapping | Requires |
|----------|--------------|----------|
| `height` | `Scale.Y` | Node3D |
| `width` | `Scale.X` | Node3D |
| `depth` | `Scale.Z` | Node3D |
| `scale` | uniform Scale | Node3D |
| `rotation_speed` | Y-axis rotation | Node3D |
| `position_y` | `Position.Y` | Node3D |
| `color_temperature` | HSV hue (blue→red) | MeshInstance3D + StandardMaterial3D |
| `opacity` | alpha channel | MeshInstance3D + StandardMaterial3D |

**Custom properties** pass through directly to `@export` vars on scene scripts — create a River node with `@export var river_flow_speed: float` and use `property = "river_flow_speed"` in the config. Properties are validated at scene load time.

## Dev Environment

For live metric data, run the PCP stack with docker compose:

```bash
# Start PCP + pmproxy + synthetic data generation
docker compose up -d
```

This provides a pmproxy endpoint at `http://localhost:44322` that serves synthetic metric data for development.

## Example Visualizations

- **CPU Load Bars**: 3 vertical bars showing 1/5/15 minute load averages
- **Disk I/O Panel**: Spinning cylinder for reads, growing bar for writes
- **Mountain Landscape** (planned): Heavy CPU = rain → rivers fill, low CPU = rivers dry up
- **Wind Farm Cluster** (planned): Each CPU = wind turbine, speed shows utilization

## Heritage

This project modernizes the original [PCP pmview](https://pcp.io/) tool, which visualized system metrics as 3D shapes (colored cylinders for disks, cubes for CPU/Memory/Load). pmview-nextgen takes this concept to the next level with game-like, living, breathing worlds.

## License

TBD - Exploring open source and potential dual licensing options

## Contact

Paul Smith - Project Creator
