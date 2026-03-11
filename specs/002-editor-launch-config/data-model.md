# Data Model: Editor Launch Configuration

**Feature Branch**: `002-editor-launch-config` | **Date**: 2026-03-11

## Entities

### WorldConfiguration (ProjectSettings-backed)

Project-global settings that control how scenes connect to and replay PCP data. Persisted via Godot's `ProjectSettings` to `project.godot`.

| Field | Type | Default | Constraints | ProjectSettings Key |
|-------|------|---------|-------------|-------------------|
| Endpoint | String | `"http://localhost:44322"` | Valid URL | `pmview/endpoint` |
| Mode | PlaybackMode (enum) | Archive (0) | 0=Archive, 1=Live | `pmview/mode` |
| ArchiveStartTimestamp | String | `""` (empty → 24h ago) | ISO 8601 or empty | `pmview/archive_start_timestamp` |
| ArchiveSpeed | Float | 10.0 | 0.1 ≤ x ≤ 100.0 | `pmview/archive_speed` |
| ArchiveLoop | Bool | false | — | `pmview/archive_loop` |

**Relationships**: Read by `metric_scene_controller.gd` at scene startup. Applied to `MetricPoller` (endpoint, mode, speed, timestamp) and `TimeCursor` (loop behaviour).

**Validation**: Speed clamping already exists in `TimeCursor.PlaybackSpeed` setter (0.1–100x). Endpoint validation deferred to connection time (existing error handling). Timestamp validation at `MetricPoller.StartPlayback()` (existing ISO 8601 parsing).

### PlaybackMode (Enum)

| Value | Int | Behaviour |
|-------|-----|-----------|
| Archive | 0 | Historical replay with configurable speed and timestamp |
| Live | 1 | Real-time polling of current metric values |

**Note**: Archive is the default (most common dev workflow per spec assumptions).

### PmviewBridgePlugin (EditorPlugin)

Editor-only C# class that manages the lifecycle of ProjectSettings entries.

| Responsibility | Method | When |
|---------------|--------|------|
| Register settings with defaults and hints | `_EnterTree()` | Plugin enabled |
| Clean up settings | `_ExitTree()` | Plugin disabled |

**No persistent state** — the plugin itself stores nothing. It merely ensures ProjectSettings entries exist with proper metadata (type hints, range hints, enum labels).

## State Transitions

### Scene Launch Configuration Flow

```
Editor (plugin enabled)
  │
  ├─ ProjectSettings contain pmview/* values
  │
  ▼
Scene Launch (_ready)
  │
  ├─ Read pmview/endpoint → set MetricPoller.Endpoint
  ├─ Read pmview/mode
  │   ├─ Archive → read timestamp, speed, loop
  │   │   ├─ MetricPoller.StartPlayback(timestamp)
  │   │   ├─ MetricPoller.SetPlaybackSpeed(speed)
  │   │   └─ TimeCursor.Loop = loop
  │   └─ Live → MetricPoller.ResetToLive() (no-op if already live)
  │
  ▼
Running (metrics flowing)
  │
  ├─ F3 overlay still available for runtime overrides (FR-010)
  └─ Loop: TimeCursor wraps position when reaching end of archive data
```

### TimeCursor Loop Extension

```
Playback mode, cursor advancing
  │
  AdvanceBy(elapsed)
  │
  ├─ newPosition = Position + (elapsed * Speed)
  ├─ if Loop && newPosition > EndBound:
  │   └─ Position = StartTime (wrap around)
  └─ else:
      └─ Position = newPosition (may exceed bounds → fetch returns empty → freeze)
```

## Existing Entities (unchanged)

These entities already exist and require no schema changes:

- **TimeCursor** (`PcpClient`): Gains a `Loop` bool property. No other changes.
- **BindingConfig** (`PcpGodotBridge`): Unchanged. Per-scene binding configs remain separate from project-global world config.
- **MetricPoller** (`scripts/bridge/` → `addons/pmview-bridge/`): File moves but API unchanged.
- **SceneBinder** (`scripts/bridge/` → `addons/pmview-bridge/`): File moves but API unchanged.
- **MetricBrowser** (`scripts/bridge/` → `addons/pmview-bridge/`): File moves but API unchanged.
