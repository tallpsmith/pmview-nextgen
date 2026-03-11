# Contract: ProjectSettings Schema

**Feature Branch**: `002-editor-launch-config` | **Date**: 2026-03-11

## Overview

The pmview-bridge plugin exposes world configuration via Godot's `ProjectSettings` under the `pmview/` prefix. These settings are the public contract between the editor plugin (which registers them) and the scene controller (which reads them at launch).

## Settings

| Key | Type | Default | Hint | Description |
|-----|------|---------|------|-------------|
| `pmview/endpoint` | `String` | `"http://localhost:44322"` | None | pmproxy base URL |
| `pmview/mode` | `int` | `0` | `PROPERTY_HINT_ENUM, "Archive:0,Live:1"` | Playback mode |
| `pmview/archive_start_timestamp` | `String` | `""` | None | ISO 8601 start time; empty = 24h before current time |
| `pmview/archive_speed` | `float` | `10.0` | `PROPERTY_HINT_RANGE, "0.1,100.0,0.1"` | Archive time multiplier |
| `pmview/archive_loop` | `bool` | `false` | None | Restart from start timestamp when archive data ends |

## Behaviour Rules

1. **Mode = Archive (0)**: All `archive_*` settings are active. Scene launches in archive playback mode.
2. **Mode = Live (1)**: All `archive_*` settings are ignored. Scene launches in live polling mode.
3. **Empty `archive_start_timestamp`**: Interpreted as "24 hours before current wall-clock time" at scene launch.
4. **Speed clamping**: Enforced by both the ProjectSettings hint (UI-level) and `TimeCursor.PlaybackSpeed` setter (code-level).

## Reading Settings (GDScript)

```gdscript
# In metric_scene_controller.gd _ready():
var endpoint = ProjectSettings.get_setting("pmview/endpoint", "http://localhost:44322")
var mode = ProjectSettings.get_setting("pmview/mode", 0)  # 0=Archive, 1=Live
var timestamp = ProjectSettings.get_setting("pmview/archive_start_timestamp", "")
var speed = ProjectSettings.get_setting("pmview/archive_speed", 10.0)
var loop = ProjectSettings.get_setting("pmview/archive_loop", false)
```

## Registering Settings (C# EditorPlugin)

```csharp
// In PmviewBridgePlugin._EnterTree():
ProjectSettings.SetSetting("pmview/endpoint", "http://localhost:44322");
ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary {
    { "name", "pmview/endpoint" },
    { "type", (int)Variant.Type.String },
    { "hint", (int)PropertyHint.None },
    { "hint_string", "" }
});
// ... repeat for each setting with appropriate hints
```

## Versioning

Settings are additive — new settings may be added in future features. Existing keys will not be renamed or retyped without a major version bump and migration path.
