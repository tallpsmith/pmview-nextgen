# Contract: Binding Configuration Schema

**Date:** 2026-03-05
**Format:** TOML
**Parser:** Tomlyn (.NET)
**Location:** `godot-project/bindings/*.toml`

## Purpose

A binding configuration file maps Godot scene nodes to PCP metrics and visual properties. It is the glue between a 3D scene and the metrics layer. The C# bridge layer parses these files at runtime.

## Schema

### `[meta]` — Required

| Key | Type | Required | Default | Description |
|-----|------|----------|---------|-------------|
| `scene` | string | Yes | — | Godot resource path to the `.tscn` file |
| `endpoint` | string | No | From app settings | pmproxy base URL override |
| `poll_interval_ms` | integer | No | `1000` | Milliseconds between metric fetches |
| `description` | string | No | — | Human-readable description of this binding set |

### `[[bindings]]` — Required, one or more

Each `[[bindings]]` entry maps one scene node property to one PCP metric.

| Key | Type | Required | Default | Description |
|-----|------|----------|---------|-------------|
| `scene_node` | string | Yes | — | Godot node path relative to scene root |
| `metric` | string | Yes | — | PCP metric name (dotted notation) |
| `property` | string | Yes | — | Visual property name (see vocabulary) |
| `source_range` | array of 2 floats | Yes | — | Expected metric value range `[min, max]` |
| `target_range` | array of 2 floats | Yes | — | Visual output range `[min, max]` |
| `instance_filter` | string | No | all instances | Glob pattern to match instance names |
| `instance_id` | integer | No | — | Specific instance ID (mutually exclusive with `instance_filter`) |

## Visual Property Vocabulary

Properties the SceneBinder understands. Extending this list requires documentation per Constitution Principle IV.

| Property Name | Godot Mapping | Value Type | Notes |
|---------------|---------------|------------|-------|
| `height` | `Node3D.Scale.Y` | float | Vertical scale factor |
| `width` | `Node3D.Scale.X` | float | Horizontal scale factor |
| `depth` | `Node3D.Scale.Z` | float | Depth scale factor |
| `scale` | `Node3D.Scale` (all axes) | float | Uniform scale |
| `rotation_speed` | `Node3D.Rotation.Y` delta/frame | float | Degrees per second |
| `position_y` | `Node3D.Position.Y` | float | World units |
| `color_temperature` | `AlbedoColor` HSV hue | float | 0=blue(cold), 1=red(hot) |
| `opacity` | `AlbedoColor.A` | float | 0=transparent, 1=opaque |

## Full Example

```toml
# Binding config for the classic pmview scene
# Maps CPU and disk metrics to 3D bar visualisations

[meta]
scene = "res://scenes/pmview_classic.tscn"
endpoint = "http://monitoring-host:44322"
poll_interval_ms = 1000
description = "Classic pmview-style CPU and disk overview"

[[bindings]]
scene_node = "CpuBars/LoadBar"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.0, 5.0]
instance_id = 1  # 1-minute load average

[[bindings]]
scene_node = "CpuBars/UserBar"
metric = "kernel.all.cpu.user"
property = "height"
source_range = [0.0, 1000.0]
target_range = [0.0, 5.0]

[[bindings]]
scene_node = "CpuBars/UserBar"
metric = "kernel.all.cpu.user"
property = "color_temperature"
source_range = [0.0, 1000.0]
target_range = [0.0, 1.0]

[[bindings]]
scene_node = "DiskPanel/ReadSpinner"
metric = "disk.dev.read"
property = "rotation_speed"
source_range = [0.0, 5000.0]
target_range = [0.0, 360.0]
instance_filter = "sd*"  # all SCSI disks

[[bindings]]
scene_node = "DiskPanel/WriteBar"
metric = "disk.dev.write"
property = "height"
source_range = [0.0, 5000.0]
target_range = [0.0, 3.0]
instance_filter = "sda"  # primary disk only
```

## Validation Rules

1. **`[meta].scene`** must be a valid Godot resource path starting with `res://` and ending with `.tscn`.
2. **`[[bindings]].scene_node`** must resolve to an existing node in the loaded scene. Invalid nodes produce a warning; valid bindings continue operating.
3. **`source_range` and `target_range`** must each have exactly 2 elements with `[0] < [1]`.
4. **`instance_filter` and `instance_id`** are mutually exclusive. Specifying both is a validation error.
5. **`instance_filter`/`instance_id`** on a singular metric (no instance domain) produces a warning.
6. **`property`** must be either a recognised vocabulary term (built-in) or a valid custom property name. Built-in properties are validated at config load time. Custom properties are classified as pass-through and validated at scene load time against the node's actual properties via `GetPropertyList()`. An info message is logged for custom properties.
7. **Multiple bindings to the same node** are allowed (e.g., one metric drives height, another drives colour). Multiple bindings to the same node+property is a validation error (last-write-wins is unpredictable).
8. **`poll_interval_ms`** must be >= 100. Values below 100ms are an error; the loader logs the error and substitutes the default (1000ms) to avoid aborting the entire config.

## Error Handling

| Condition | Severity | Behaviour |
|-----------|----------|-----------|
| TOML parse failure | Error | Abort load, report file and line number |
| Missing `[meta].scene` | Error | Abort load |
| Missing required binding field | Error | Skip binding, report field name |
| `scene_node` not found in scene | Warning | Skip binding, continue others |
| `metric` not found on endpoint | Warning | Skip binding, continue others |
| Unknown `property` value (not built-in) | Info | Classify as custom pass-through, validate at scene load |
| Duplicate node+property binding | Error | Skip duplicate, keep first |
| `instance_filter` + `instance_id` both set | Error | Skip binding |
| `source_range[0] >= source_range[1]` | Error | Skip binding |
