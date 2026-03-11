# Design: Godot Bridge Layer + Scene Player (Phase 4 + Phase 5)

**Date:** 2026-03-10
**Branch:** `001-pcp-godot-bridge`
**Scope:** Tasks T023-T028 (Phase 4 US01 bridge layer) + T029-T035 (Phase 5 US03 scene player)
**Depends on:** PcpClient library (Phase 3, complete)

## Context

The PcpClient library (pure .NET) handles PCP protocol communication with pmproxy. This design covers the layers above it: a pure .NET binding configuration library, thin Godot-dependent bridge nodes, GDScript scene controllers, and test scenes that prove the system works end-to-end.

## Architecture

```
GDScript scenes/controllers
    ↓ signals
Godot bridge nodes (MetricPoller, SceneBinder)     ← godot-project/scripts/bridge/
    ↓ method calls
PcpGodotBridge (binding config model + validation) ← src/pcp-godot-bridge/ (pure .NET)
    ↓ method calls
PcpClient (PCP protocol)                           ← src/pcp-client-dotnet/ (pure .NET)
    ↓ HTTP
pmproxy
```

## Layer 1: PcpGodotBridge Library

**Location:** `src/pcp-godot-bridge/`
**SDK:** net8.0 (no Godot dependency)
**Testing:** xUnit

### Core Types

```csharp
record BindingConfig(
    string ScenePath,
    string? Endpoint,
    int PollIntervalMs,
    string? Description,
    IReadOnlyList<MetricBinding> Bindings);

record MetricBinding(
    string SceneNode,
    string Metric,
    string Property,
    double SourceRangeMin,
    double SourceRangeMax,
    double TargetRangeMin,
    double TargetRangeMax,
    string? InstanceFilter,
    int? InstanceId);

enum PropertyKind { BuiltIn, Custom }

record ResolvedBinding(
    MetricBinding Binding,
    PropertyKind Kind,
    string GodotPropertyName);

enum ValidationSeverity { Info, Warning, Error }

record ValidationMessage(
    ValidationSeverity Severity,
    string Message,
    string? BindingContext);

record BindingConfigResult(
    BindingConfig? Config,
    IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
    public bool IsValid => Config != null && !HasErrors;
}
```

### BindingConfigLoader

Static class. Parses TOML via Tomlyn, validates structure, classifies properties.

```csharp
static class BindingConfigLoader
{
    static BindingConfigResult Load(string toml);
    static BindingConfigResult LoadFromFile(string filePath);
}
```

### Built-in Property Vocabulary

| Property Name | Godot Mapping | Notes |
|---------------|---------------|-------|
| `height` | `Scale.Y` | Vertical scale factor |
| `width` | `Scale.X` | Horizontal scale factor |
| `depth` | `Scale.Z` | Depth scale factor |
| `scale` | `Scale` (uniform) | All axes |
| `rotation_speed` | `Rotation.Y` delta | Degrees per second |
| `position_y` | `Position.Y` | World units |
| `color_temperature` | `AlbedoColor` HSV hue | 0=blue(cold), 1=red(hot) |
| `opacity` | `AlbedoColor.A` | 0=transparent, 1=opaque |

Properties not in this table are classified as `PropertyKind.Custom` and passed through to `node.Set()` at runtime. Custom properties support `@export` vars on scene-specific GDScript/C# node types (e.g., `river_flow_speed`, `wind_intensity`).

### Validation Rules (Config Load Phase)

| Rule | Severity | Behaviour |
|------|----------|-----------|
| TOML parse failure | Error | Abort, no Config |
| Missing `[meta].scene` | Error | Abort |
| `scene` not `res://...*.tscn` | Error | Abort |
| Missing required binding field | Error | Skip binding |
| `source_range[0] >= source_range[1]` | Error | Skip binding |
| `target_range[0] >= target_range[1]` | Error | Skip binding |
| `source_range`/`target_range` not 2 elements | Error | Skip binding |
| `instance_filter` + `instance_id` both set | Error | Skip binding |
| Duplicate node+property pair | Error | Keep first, skip duplicate |
| `poll_interval_ms` < 100 | Error | Use default 1000 |
| Unknown property name (not built-in) | Info | Classify as Custom pass-through |

### Structured Logging

Every load produces a full audit trail:

- **Info:** "Loaded 5 bindings from test_bars.toml"
- **Info per valid binding:** "bindings[0]: CpuLoadBar.height <- kernel.all.load [0-10] -> [0-5]"
- **Info for custom:** "bindings[3]: property 'river_flow_speed' is custom pass-through"
- **Warning:** "bindings[3]: instance_filter on singular metric, ignored"
- **Error:** "bindings[2]: unknown required field missing: metric"
- **Info summary:** "Config loaded: 4 active bindings, 1 skipped"

## Layer 2: Godot Bridge Nodes

**Location:** `godot-project/scripts/bridge/`
**SDK:** Godot.NET.Sdk
**Testing:** Manual in Godot editor (kept thin by design)

### MetricPoller

Extends `Node`. Timer-driven service that polls PcpClient and emits signals.

```csharp
public partial class MetricPoller : Node
{
    [Signal] public delegate void MetricsUpdatedEventHandler(Godot.Collections.Dictionary metrics);
    [Signal] public delegate void ConnectionStateChangedEventHandler(string state);
    [Signal] public delegate void ErrorOccurredEventHandler(string message);

    [Export] public string Endpoint { get; set; } = "http://localhost:44322";
    [Export] public int PollIntervalMs { get; set; } = 1000;
    [Export] public string[] MetricNames { get; set; } = [];
}
```

**Signal data format** (Godot Dictionary, GDScript-friendly):

```
{
  "kernel.all.load": {
    "timestamp": 1709856000.123,
    "instances": { -1: 2.45 }      // singular: key -1 (int) is the sentinel for InstanceId=null
  },
  "disk.dev.read": {
    "timestamp": 1709856000.123,
    "instances": { 0: 1024.0, 1: 512.0 }  // per-instance: keys are instance IDs (int)
  }
}
```

**Marshalling convention:** `InstanceValue.InstanceId` (nullable `int?` in C#) is marshalled as integer key `-1` for singular metrics (where InstanceId is null). GDScript consumers use `instances[-1]` for singular, `instances[id]` for instanced. This sentinel is chosen because PCP itself uses `-1` for "no instance" in its wire protocol.

**ConnectionStateChanged string format:** Emits PascalCase strings matching the C# `ConnectionState` enum names: `"Disconnected"`, `"Connecting"`, `"Connected"`, `"Reconnecting"`, `"Failed"`. GDScript `match` statements should use these exact strings.

**Lifecycle:** `_Ready()` → connect → timer tick → `FetchAsync()` → marshal → emit signal. Auto-reconnect on failure. `_ExitTree()` → dispose.

### SceneBinder

Extends `Node`. Loads scene+config pairs, validates properties against real nodes, applies metric values.

```csharp
public partial class SceneBinder : Node
{
    [Signal] public delegate void SceneLoadedEventHandler(string scenePath, string configPath);
    [Signal] public delegate void BindingErrorEventHandler(string message);

    public void LoadSceneWithBindings(string configPath);
    public void ApplyMetrics(Godot.Collections.Dictionary metrics);
}
```

**Two-phase validation:**

1. **Config load** (PcpGodotBridge, pure .NET): structural validation per rules above
2. **Scene load** (SceneBinder, Godot): resolve nodes, check properties via `GetPropertyList()`, cache valid bindings

By the time the render loop runs, every binding is proven valid. The hot path is just cached node reference + `Set()`.

**Property application — two-tier:**

1. Built-in vocabulary: SceneBinder maps to specific Godot property (e.g., "height" → `Scale.Y`)
2. Custom pass-through: SceneBinder calls `node.Set(propertyName, value)` for `@export` vars

**Scene swapping (US03):**

1. Free current scene children
2. Clear cached node references
3. Load new config via `BindingConfigLoader.LoadFromFile()`
4. Log full validation report
5. Instantiate new scene via `PackedScene`
6. Resolve nodes + validate properties (log errors for missing)
7. Update MetricPoller with new metric names
8. Emit `SceneLoaded`

**Normalisation:** Linear interpolation from source_range to target_range:

```
normalised = targetMin + (clamp(value, srcMin, srcMax) - srcMin) / (srcMax - srcMin) * (targetMax - targetMin)
```

## Layer 3: GDScript Scenes

### metric_scene_controller.gd

Main scene controller. Wires MetricPoller signals to SceneBinder. Displays connection status overlay. Intentionally thin.

### config_selector.gd (US03)

Runtime config/scene swapping UI:
- Scans `res://bindings/*.toml` for available configs
- Reads `description` from `[meta]` for display
- Simple ItemList selection
- Triggers `SceneBinder.LoadSceneWithBindings()` on selection

## Layer 4: Test Scenes + Configs

### test_bars.tscn + test_bars.toml

CPU bar visualisation. 3 BoxMesh bars named `LoadBar1Min`, `LoadBar5Min`, `LoadBar15Min`. Camera + light. Binding maps `kernel.all.load` instances to bar height.

### disk_io_panel.tscn + disk_io_panel.toml

Disk I/O layout. Visually distinct from test_bars (flat panel style). Nodes: `ReadSpinner`, `WriteBar`. Proves scene swapping works with different layouts.

## Project Scaffolding

### godot-project/

```
godot-project/
├── project.godot
├── pmview-nextgen.csproj       # Godot.NET.Sdk, refs PcpClient + PcpGodotBridge
├── pmview-nextgen.sln          # All projects: Godot + PcpClient + Bridge + Tests
├── scripts/bridge/             # MetricPoller.cs, SceneBinder.cs
├── scripts/scenes/             # GDScript controllers
├── scenes/                     # .tscn files
└── bindings/                   # .toml configs
```

### src/pcp-godot-bridge/

```
src/pcp-godot-bridge/
├── PcpGodotBridge.sln
├── src/PcpGodotBridge/
│   ├── PcpGodotBridge.csproj   # net8.0, Tomlyn, refs PcpClient
│   ├── BindingConfig.cs
│   ├── MetricBinding.cs
│   ├── BindingConfigLoader.cs
│   ├── BindingConfigResult.cs
│   └── PropertyVocabulary.cs
└── tests/PcpGodotBridge.Tests/
    ├── PcpGodotBridge.Tests.csproj
    └── BindingConfigValidationTests.cs
```

## Future Work

- Extract `godot-project/scripts/bridge/` to `addons/pmview-bridge/` Godot plugin (GitHub issue #1)
- PcpClient and PcpGodotBridge as NuGet packages
