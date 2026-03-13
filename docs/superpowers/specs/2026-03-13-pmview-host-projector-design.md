# pmview-host-projector — Design Spec

**Date:** 2026-03-13
**Status:** Approved
**Related Issue:** https://github.com/tallpsmith/pmview-nextgen/issues/4

## Overview

A standalone .NET console application that connects to a pmproxy endpoint, discovers the host's metric topology (OS, CPUs, disks, network interfaces), and generates a ready-to-run Godot `.tscn` scene file with PCP bindings, layout, lighting, and camera — a modern reimagining of the original `pmview` host-level activity view.

The user runs this tool *before* launching Godot. It produces a scene file that Godot loads directly, with all metric bindings pre-configured.

```bash
dotnet run --project src/pmview-host-projector -- \
  --pmproxy http://localhost:44322 \
  -o godot-project/scenes/generated/host-view.tscn
```

Future: wraps into a `pmview host-project` subcommand via a unified CLI runner (out of scope).

## Architecture: Pipeline of Composable Stages

```
MetricDiscovery → HostProfileProvider → LayoutCalculator → SceneEmitter
```

Each stage has clear inputs/outputs, no shared mutable state, independently testable with xUnit.

### Stage 1 — MetricDiscovery

**Input:** pmproxy endpoint URL
**Output:** `HostTopology`

Connects via PcpClient, uses `GetInstanceDomainAsync()` to discover instances:
- `kernel.uname.sysname` → detect OS (Linux / Darwin)
- `kernel.uname.nodename` → hostname
- `disk.dev.read_bytes` → instance domain gives disk device names
- `network.interface.in.bytes` → instance domain gives network interface names
- `kernel.percpu.cpu.user` → instance domain gives CPU instance names

No layout logic, no profile knowledge. If a metric doesn't exist on the endpoint, that instance list returns empty.

**Error handling:** If pmproxy is unreachable, fail with a clear error message. If OS is `Unknown`, fail — we need a known profile to proceed. If all instance lists are empty but OS is detected, emit a minimal scene with just aggregate zones (no background row).

### Stage 2 — HostProfileProvider

**Input:** `OperatingSystem` enum
**Output:** `IReadOnlyList<ZoneDefinition>`

Pure data lookup. No I/O. Returns zone definitions for the given OS. v1 ships with two hardcoded profiles: Linux and macOS. (Note: `HostTopology` flows from Stage 1 directly to Stage 3 — Stage 2 only needs the OS enum.)

#### Linux Profile

**Foreground (system-wide aggregates):**

| Zone | PCP Metrics | Shape |
|------|-------------|-------|
| CPU | `kernel.all.cpu.user`, `.sys`, `.nice` | 3 bars (orange) |
| Load | `kernel.all.load` (instances: 1/5/15 min) | 3 bars (indigo) |
| Memory | `mem.util.used`, `.cached`, `.bufmem` | 3 bars (green) |
| Disk totals | `disk.all.read_bytes`, `.write_bytes` | 2 cylinders (amber) |

**Background (per-instance detail grids):**

| Zone | PCP Metrics | Grid |
|------|-------------|------|
| Per-CPU | `kernel.percpu.cpu.user`, `.sys`, `.nice` | N CPUs × 3 bars (orange) |
| Per-Disk | `disk.dev.read_bytes`, `.write_bytes` | N disks × 2 cylinders (dark green) |
| Network In | `network.interface.in.bytes`, `.packets`, `.errors` | N interfaces × 3 bars (blue, blue, red) |
| Network Out | `network.interface.out.bytes`, `.packets`, `.errors` | N interfaces × 3 bars (rose, rose, red) |

**macOS Profile:** Same structure, adjusted metric names where they differ. Validated against a real macOS pmproxy during implementation.

#### Source Range Guidelines

Counter metrics (`disk.dev.read_bytes`, `network.interface.in.bytes`, etc.) are automatically rate-converted by `MetricRateConverter` at runtime — source ranges are in **per-second rates**, not cumulative counters.

| Metric Category | Source Range | Target Range | Notes |
|---|---|---|---|
| CPU % (`kernel.all.cpu.*`) | 0 – 100 | 0.2 – 5.0 | Percentage of total CPU |
| Load average | 0 – 10 | 0.2 – 5.0 | Reasonable max for most systems |
| Memory (bytes) | 0 – auto | 0.2 – 5.0 | Max discovered from `mem.physmem` at projection time |
| Disk bytes/sec | 0 – 500 MB/s | 0.2 – 5.0 | Typical SSD throughput ceiling |
| Network bytes/sec | 0 – 125 MB/s | 0.2 – 5.0 | ~1 Gbps |
| Network packets/sec | 0 – 100,000 | 0.2 – 5.0 | Reasonable packet rate |
| Errors/sec | 0 – 100 | 0.2 – 5.0 | Any errors are notable |

Target range 0.2–5.0 keeps shapes visible at minimum (not flat) and bounded at maximum. These are starting points — tuned during implementation against real metrics.

#### Foreground Zone Ordering

Canonical left-to-right order: **Disk, Load, Memory, CPU**. This matches the original pmview layout and places the most commonly watched metric (CPU) at the rightmost position nearest the camera's natural focus.

#### Background Zone Ordering

Left-to-right: **Per-CPU, Per-Disk, Network In, Network Out**.

### Stage 3 — LayoutCalculator

**Input:** `List<ZoneDefinition>` + `HostTopology` (for instance counts)
**Output:** `SceneLayout`

Pure geometry. No PCP, no Godot, no I/O.

**Algorithm:**

1. Separate zones into foreground and background.
2. **Foreground row:** Compute zone widths (shape count × spacing). Flow left-to-right with fixed gaps. Center row on X=0.
3. **Background row:** For each per-instance zone, compute grid dimensions (columns = metric count, rows = instance count). Flow zone columns left-to-right with fixed gaps. Center row on X=0. Place at Z offset behind foreground.
4. Per-instance grids grow along **negative Z** (away from camera). Each zone's depth is independent — a 4-disk column next to a 12-interface network column is fine.
5. Fixed X-axis gaps between zone columns. Row depth per-zone is self-determined by instance count.

**Tuneable constants (hardcoded v1):**
- Shape spacing within zone: ~1.5 units
- Gap between zone columns: ~3.0 units
- Background Z offset: ~8.0 units behind foreground
- Grid row spacing: ~2.0 units

### Stage 4 — SceneEmitter (decomposed)

**Input:** `SceneLayout`
**Output:** `.tscn` file content (string)

Orchestrates three sub-components:

- **BindingAssembler** — Maps `PlacedShape` → PcpBindable + PcpBindingResource node definitions. Each generated shape gets a child `PcpBindable` node with a `PcpBindingResource` that specifies the metric name, instance name, target property (`height`), and source/target ranges. These are the existing Godot resource types already in `godot-project/addons/pmview-bridge/`. At runtime, `SceneBinder` discovers these `PcpBindable` nodes and wires them to `MetricPoller` — no TOML involved. Pure data transform. Testable: "given this PlacedShape, assert it produces a binding targeting height with these ranges."
- **WorldSetup** — Computes camera position/angle from scene bounding box, directional light, ambient lighting, environment. Testable: "given scene bounds, assert camera position."
- **TscnWriter** — Serialises node definitions to valid `.tscn` text format. Knows about `[gd_scene]`, `[ext_resource]`, `[node]`, `[sub_resource]` syntax. Testable: "given these nodes, assert valid .tscn output."

### Runtime Binding Flow

The generated `.tscn` file is self-contained — it includes `MetricPoller` and `SceneBinder` nodes at the scene root, with all discovered metric names pre-populated in `MetricPoller`'s metric list. Each shape has a child `PcpBindable` node (the existing type from `addons/pmview-bridge/`) with `PcpBindingResource` entries configured inline. When Godot loads the scene:

1. `MetricPoller` connects to pmproxy and starts polling the metrics listed in its configuration
2. `SceneBinder` discovers all `PcpBindable` nodes in the scene tree
3. On each poll cycle, `SceneBinder` receives metric values via signal and applies them to the bound properties
4. The `GroundedShape` `height` property is targeted as a **custom property** (GDScript `@export`), not via the built-in `PropertyVocabulary` `height → scale:y` mapping. This ensures the ground-anchoring offset logic in the GroundedShape script is used.

No TOML config files are involved — the bindings are embedded directly in the scene file.

## Data Models

### HostTopology (Stage 1 → 3)

```csharp
public record HostTopology(
    OperatingSystem Os,
    string Hostname,
    IReadOnlyList<string> CpuInstances,
    IReadOnlyList<string> DiskDevices,
    IReadOnlyList<string> NetworkInterfaces
);

public enum OperatingSystem { Linux, MacOS, Unknown }
```

### ZoneDefinition (Stage 2 → 3)

```csharp
public record ZoneDefinition(
    string Name,
    ZoneRow Row,
    ZoneType Type,
    IReadOnlyList<MetricShapeMapping> Metrics,
    string? InstanceMetricSource
);

public enum ZoneRow { Foreground, Background }
public enum ZoneType { Aggregate, PerInstance }

// InstanceMetricSource: For PerInstance zones, the PCP metric name used to discover
// instances via GetInstanceDomainAsync(). E.g., "disk.dev.read_bytes" for Per-Disk zone.
// The LayoutCalculator uses this to look up instance names from HostTopology.
// Null for Aggregate zones.

public record MetricShapeMapping(
    string MetricName,
    ShapeType Shape,
    string Label,
    RgbColour DefaultColour,
    float SourceRangeMin,       // Expected metric value range (rate-converted for counters)
    float SourceRangeMax,
    float TargetRangeMin,       // GroundedShape height range (Godot units)
    float TargetRangeMax
);

public enum ShapeType { Bar, Cylinder }

// Simple RGB colour (0-1 floats). Not Godot's Color — this is a pure .NET type.
// TscnWriter converts to Godot Color format when emitting .tscn.
public record RgbColour(float R, float G, float B);
```

### SceneLayout (Stage 3 → 4)

```csharp
public record SceneLayout(
    string Hostname,
    IReadOnlyList<PlacedZone> Zones
);

public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,                         // World position of zone origin
    int? GridColumns,                      // For PerInstance zones: GridLayout3D column count
    float? GridColumnSpacing,
    float? GridRowSpacing,
    IReadOnlyList<PlacedShape> Shapes
);

public record PlacedShape(
    string NodeName,                       // Unique node name in scene tree
    ShapeType Shape,
    Vec3 LocalPosition,                    // Position relative to zone origin
    string MetricName,
    string? InstanceName,                  // null for aggregates, "sda1" for per-instance
    string? DisplayLabel,                  // Shortened instance name for Label3D
    RgbColour Colour,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax
);

// Simple 3D vector. Not Godot's Vector3 — this is a pure .NET type.
// Uses System.Numerics.Vector3 internally or a simple record.
public record Vec3(float X, float Y, float Z);
```

### Instance Name Shortening

Display labels use the last path segment: `/dev/sda1` → `sda1`. Names without `/` are used as-is: `enp0s3` → `enp0s3`.

## Godot Building Blocks

### GroundedShape

Solves the ground-plane anchoring problem. Shapes grow upward from Y=0 instead of expanding from their center.

**Node structure:**
```
GroundedShape (Node3D + grounded_shape.gd)
  └── MeshInstance3D (position.y = 0.5, fixed)
      └── StandardMaterial3D
```

**How it works:** The child mesh (1 unit tall, centered at origin) is offset to `position.y = 0.5` in local space, placing its bottom at the parent's Y=0. When `scale.y` changes on the parent GroundedShape, the local offset scales proportionally — bottom stays at Y=0, shape grows upward. No per-frame repositioning needed.

The `@export var height` property setter maps to `scale.y`. PcpBindable targets this property.

**Template scenes** in `godot-project/scenes/building_blocks/`:
- `grounded_bar.tscn` — BoxMesh-based
- `grounded_cylinder.tscn` — CylinderMesh-based
- `zone_label.tscn` — Label3D, rotated flat on ground plane

### GridLayout3D

Arranges child Node3D nodes in a grid pattern in 3D world space.

```
@export var columns: int = 3
@export var column_spacing: float = 1.5
@export var row_spacing: float = 2.0
```

Positions children left-to-right in columns, wrapping to new rows along negative Z (away from camera). Per-instance zones use this to arrange their shape grids at runtime.

**Division of responsibility:** The `LayoutCalculator` computes zone-level positions (where each zone sits in world space) and sets grid parameters (columns, spacing). The `GridLayout3D` script handles individual shape positioning within a zone at runtime based on child count and the grid parameters. This means the emitted .tscn does not need per-shape position coordinates for grid zones — just the grid configuration on the parent node. For foreground aggregate zones (which have few fixed shapes), positions are pre-computed by `LayoutCalculator` and baked into the .tscn.

**Scripts** in `godot-project/scripts/building_blocks/`:
- `grounded_shape.gd`
- `grid_layout_3d.gd`

## Scene Layout Concept

```
Camera looks from here →

BACKGROUND ROW (per-instance detail):
  Per-CPU         Per-Disk        Net Input       Net Output
  [cpu0  ■ ■ ■]  [sda1  ○ ○]    [eth0  ■ ■ ■]   [eth0  ■ ■ ■]
  [cpu1  ■ ■ ■]  [sdb1  ○ ○]    [eth1  ■ ■ ■]   [eth1  ■ ■ ■]
  [cpu2  ■ ■ ■]                  [lo    ■ ■ ■]   [lo    ■ ■ ■]
  [cpu3  ■ ■ ■]

  ← fixed X gaps between zone columns, Z depth varies per zone →

FOREGROUND ROW (system-wide aggregates):
  [Disk ○○]  [Load ■■■]  [Mem ■■■]  [CPU ■■■]

  ← zones flow left-to-right, row centered on X=0 →

Zone labels: Label3D flat on ground, at front edge of each zone.
All shapes anchored to ground plane via GroundedShape.
```

## Default Colour Scheme

| Zone | Colour | Hex |
|------|--------|-----|
| CPU | Orange | `#f97316` |
| Memory | Green | `#22c55e` |
| Load | Indigo | `#6366f1` |
| Disk totals | Amber | `#f59e0b` |
| Per-disk | Dark green | `#16a34a` |
| Network In | Blue | `#3b82f6` |
| Network Out | Rose | `#f43f5e` |
| Errors (any zone) | Red | `#ef4444` |

## Project Structure

```
src/pmview-host-projector/
  ├── PmviewHostProjector.sln
  ├── src/PmviewHostProjector/
  │   ├── Program.cs
  │   ├── Discovery/
  │   │   └── MetricDiscovery.cs
  │   ├── Profiles/
  │   │   ├── HostProfileProvider.cs
  │   │   ├── LinuxProfile.cs
  │   │   └── MacOsProfile.cs
  │   ├── Layout/
  │   │   └── LayoutCalculator.cs
  │   ├── Emission/
  │   │   ├── SceneEmitter.cs
  │   │   ├── BindingAssembler.cs
  │   │   ├── WorldSetup.cs
  │   │   └── TscnWriter.cs
  │   └── Models/
  │       ├── HostTopology.cs
  │       ├── ZoneDefinition.cs
  │       └── SceneLayout.cs
  └── tests/PmviewHostProjector.Tests/
      ├── Discovery/
      ├── Profiles/
      ├── Layout/
      └── Emission/

godot-project/scenes/building_blocks/
  ├── grounded_bar.tscn
  ├── grounded_cylinder.tscn
  └── zone_label.tscn

godot-project/scripts/building_blocks/
  ├── grounded_shape.gd
  └── grid_layout_3d.gd
```

## What's Not In Scope

These are tracked as separate GitHub issues:

1. **Stacked bar charts** (#15) — Sub-metrics stacking vertically instead of side-by-side
2. **Data-driven profiles** (#16) — Extract hardcoded OS profiles to JSON/TOML config files
3. **Camera fly-around controller** (#17) — Free-fly/orbit camera for scene navigation
4. **`pmview` CLI runner** (#18) — Unified CLI with subcommands (`pmview host-project`, `pmview visualize`)
5. **Packaging & distribution** (#19) — Homebrew formula, Linux packages, dependency bundling
6. **Network interface filtering** (#20) — Exclude `lo`, `docker0`, etc. by pattern/config
