# Data Model: PCP-to-Godot 3D Metrics Bridge

**Date:** 2026-03-05
**Source:** [spec.md](spec.md), [research.md](research.md)

## Entities

### PcpConnection

Represents a live connection to a pmproxy endpoint.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| BaseUrl | `Uri` | Required, valid HTTP URL | e.g., `http://localhost:44322` |
| ContextId | `int?` | Server-assigned, nullable before creation | pmproxy context number |
| PollTimeoutSeconds | `int` | Default: 60, min: 5 | Keeps server context alive |
| State | `ConnectionState` | Enum | See state transitions below |
| LastActivity | `DateTime` | UTC | Tracks context freshness |

**State transitions:**

```
Disconnected → Connecting → Connected → Disconnected
                    ↓                        ↑
                  Failed ────────────────────┘
Connected → Reconnecting → Connected
                 ↓
               Failed → Disconnected
```

### MetricDescriptor

Metadata about a single PCP metric. Immutable once fetched.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Name | `string` | Required, dotted notation | e.g., `kernel.all.load` |
| Pmid | `string` | PCP metric identifier | e.g., `60.2.0` |
| Type | `MetricType` | Enum: Float, Double, U32, U64, I32, I64, String | PCP data type |
| Semantics | `MetricSemantics` | Enum: Instant, Counter, Discrete | How to interpret values |
| Units | `string` | Optional | e.g., `Kbyte`, `count/s` |
| IndomId | `string?` | Nullable — null means singular metric | Instance domain reference |
| OneLineHelp | `string` | Short description | From pmproxy `text-oneline` |
| LongHelp | `string?` | Optional detailed description | From pmproxy `text-help` |

### Instance

A single instance within an instance domain (e.g., one CPU, one disk).

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | `int` | Server-assigned | Numeric instance identifier |
| Name | `string` | Required | Human-readable: `cpu0`, `sda`, `eth0` |

### InstanceDomain

A set of instances for a metric. Some metrics have no instance domain.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| IndomId | `string` | Required | e.g., `60.2` |
| Instances | `IReadOnlyList<Instance>` | Can be empty | Ordered list of instances |

### MetricValue

A fetched value for a specific metric at a point in time.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Name | `string` | Required | Metric name |
| Pmid | `string` | PCP metric identifier | Matches MetricDescriptor |
| Timestamp | `double` | Epoch seconds, microsecond precision | From pmproxy response |
| InstanceValues | `IReadOnlyList<InstanceValue>` | At least one entry | Singular metrics have one entry with `InstanceId = null` |

### InstanceValue

A single value for a specific instance of a metric.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| InstanceId | `int?` | Null for singular metrics | Maps to Instance.Id |
| Value | `object` | Numeric or string | Boxed; type determined by MetricDescriptor.Type |

### TimeCursor

Defines what "now" means for metric queries. Controls live vs. historical mode.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Mode | `CursorMode` | Enum: Live, Playback, Paused | Default: Live |
| Position | `DateTime` | UTC | Current position in time |
| PlaybackSpeed | `double` | Default: 1.0, range: 0.1–100.0 | Multiplier for real-time |
| StartTime | `DateTime?` | Null in Live mode | Playback start point |

**State transitions:**

```
Live → Playback (user sets start time)
Playback → Paused (user pauses)
Paused → Playback (user resumes)
Playback → Live (user resets to now)
Paused → Live (user resets to now)
```

### BindingConfig

Top-level binding configuration loaded from a TOML file.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| ScenePath | `string` | Required, valid Godot resource path | e.g., `res://scenes/pmview_classic.tscn` |
| Endpoint | `string?` | Optional, valid URL | Overrides default endpoint |
| PollIntervalMs | `int` | Default: 1000, min: 100 | Polling frequency |
| Description | `string?` | Optional | Human-readable description of this binding set |
| Bindings | `IReadOnlyList<MetricBinding>` | At least one | Scene-to-metric mappings |

### MetricBinding

Maps a single scene node property to a PCP metric value.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| SceneNode | `string` | Required, valid Godot node path | e.g., `CpuLoadBar`, `Gauges/DiskIO` |
| Metric | `string` | Required, valid PCP metric name | e.g., `kernel.all.load` |
| Property | `string` | Required, valid visual property name | See binding vocabulary below |
| SourceRange | `(double Min, double Max)` | Min < Max | Expected metric value range |
| TargetRange | `(double Min, double Max)` | Min < Max | Visual output range |
| InstanceFilter | `string?` | Optional glob pattern | Filter to specific instance(s). Mutually exclusive with InstanceId |
| InstanceId | `int?` | Optional | Specific instance ID. Mutually exclusive with InstanceFilter |

### Binding Vocabulary (Visual Properties)

Documented property names that the SceneBinder understands. New properties require documentation per Constitution Principle IV.

| Property | Godot Mapping | Notes |
|----------|---------------|-------|
| `height` | `Node3D.Scale.Y` | Vertical scaling |
| `width` | `Node3D.Scale.X` | Horizontal scaling |
| `depth` | `Node3D.Scale.Z` | Depth scaling |
| `scale` | `Node3D.Scale` (uniform) | Uniform scaling all axes |
| `rotation_speed` | `Node3D.Rotation.Y` per frame | Angular velocity |
| `position_y` | `Node3D.Position.Y` | Vertical position |
| `color_temperature` | `MeshInstance3D.MaterialOverride.AlbedoColor` | Blue(cold)→Red(hot) gradient |
| `opacity` | `MeshInstance3D.MaterialOverride.AlbedoColor.A` | Transparency 0–1 |

## Enumerations

### ConnectionState
`Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `Failed`

### MetricType
`Float`, `Double`, `U32`, `U64`, `I32`, `I64`, `String`

### MetricSemantics
`Instant` (point-in-time gauge), `Counter` (monotonically increasing), `Discrete` (event-driven)

### CursorMode
`Live` (wall-clock now), `Playback` (advancing through history), `Paused` (frozen at position)

## Validation Rules

1. **BindingConfig.ScenePath** must reference an existing `.tscn` file at load time.
2. **MetricBinding.SceneNode** must resolve to an existing node in the loaded scene. Invalid bindings are reported as warnings; valid bindings continue operating (FR-007, acceptance scenario 3).
3. **MetricBinding.SourceRange** and **TargetRange** must have `Min < Max`. Equal values are a configuration error.
4. **PcpConnection.PollTimeoutSeconds** must be greater than `BindingConfig.PollIntervalMs / 1000` to prevent context expiry between polls.
5. **TimeCursor.PlaybackSpeed** clamped to 0.1–100.0. Values outside this range are clamped with a warning.
6. **MetricBinding.InstanceFilter** applies only to metrics with instance domains. Specifying a filter for a singular metric is a warning, not an error.

## Relationships

```
PcpConnection 1 ──── * MetricDescriptor     (discovered from endpoint)
MetricDescriptor 1 ──── 0..1 InstanceDomain  (some metrics are singular)
InstanceDomain 1 ──── * Instance
MetricDescriptor 1 ──── * MetricValue        (fetched over time)
MetricValue 1 ──── * InstanceValue

BindingConfig 1 ──── * MetricBinding
MetricBinding * ──── 1 MetricDescriptor      (by metric name)
MetricBinding * ──── 1 SceneNode             (by node path, resolved at runtime)

TimeCursor 1 ──── 1 PcpConnection            (controls query timing)
```
