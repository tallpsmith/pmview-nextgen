# Research: Editor Launch Configuration

**Feature Branch**: `002-editor-launch-config` | **Date**: 2026-03-11

## R1: Godot EditorPlugin Mechanics (C#)

**Decision**: Use a C# class extending `EditorPlugin` with the `[Tool]` attribute, registered via `plugin.cfg`.

**Rationale**: Godot 4.4+ fully supports C# EditorPlugins. The `[Tool]` attribute makes the class run in the editor context, which is required to add custom inspector controls and react to editor lifecycle events. This is the standard Godot pattern — no GDScript wrapper needed since the bridge layer is already C#.

**Alternatives considered**:
- GDScript EditorPlugin wrapping C# nodes: Adds an unnecessary language boundary. The bridge is C# — keep the plugin C# too.
- `@tool` GDScript with C# interop: More fragile; Godot's C#/GDScript interop for editor plugins is less tested than pure C#.

## R2: Configuration Persistence Strategy

**Decision**: Use `Godot.ProjectSettings` with a custom `pmview/` prefix for all world configuration values.

**Rationale**: ProjectSettings persists to `project.godot` automatically, survives editor restarts, is project-global (not per-scene), and is accessible from both C# and GDScript. This exactly matches FR-009 (project-global, persists across sessions). EditorSettings was considered but is per-user, not per-project — wrong scope.

**Settings keys**:
- `pmview/endpoint` (String, default `"http://localhost:44322"`)
- `pmview/mode` (Int enum, 0=Archive, 1=Live, default 0)
- `pmview/archive_start_timestamp` (String ISO 8601, default empty → 24h ago)
- `pmview/archive_speed` (Float, default 10.0, range 0.1–100.0)
- `pmview/archive_loop` (Bool, default false)

**Alternatives considered**:
- EditorSettings: Per-user, not per-project. Config would differ between team members — violates FR-009.
- Custom `.cfg` file: Reinvents what ProjectSettings already does. More code, no benefit.
- Extending TOML binding configs: Wrong scope — bindings are per-scene, world config is project-global.

## R3: Inspector UI for World Configuration

**Decision**: Use `EditorPlugin._GetPluginName()` and override `_MakeVisible()` with an `EditorInspectorPlugin` to add a custom property category in the Inspector when the plugin node is selected, OR simpler: use `ProjectSettings` with `AddPropertyInfo()` to add categorised settings with proper hints.

**Refined Decision**: Use `ProjectSettings` with `AddPropertyInfo()` for hint metadata (enum dropdowns, ranges, etc.) registered during `_EnterTree()`. The Godot Inspector automatically displays ProjectSettings — no custom EditorInspectorPlugin needed. Users access settings via Project > Project Settings > General > pmview/.

**Rationale**: Minimal code, standard Godot pattern. ProjectSettings with property info hints gives us enum dropdowns for mode, range sliders for speed, and proper labels — all for free. A custom EditorInspectorPlugin would be over-engineering for 5 settings.

**Alternatives considered**:
- Custom EditorInspectorPlugin: Much more code, custom UI to maintain. Overkill for 5 fields.
- Custom dock panel: Even more code, non-standard UX. Users expect settings in Project Settings.

## R4: Settings Application at Scene Launch

**Decision**: `metric_scene_controller.gd` reads ProjectSettings in `_ready()` and configures MetricPoller before the first poll fires.

**Rationale**: The controller already orchestrates startup (loads binding config, wires signals, starts polling). Adding 5 `ProjectSettings.get_setting()` calls at the top of `_ready()` is trivial and keeps the single-responsibility of the controller as "scene startup orchestrator". The settings are read once at launch — no need for reactive updates.

**Flow**:
1. `_ready()` reads `pmview/*` from ProjectSettings
2. Sets MetricPoller endpoint (overrides TOML default if set)
3. If mode == Archive: calls `MetricPoller.StartPlayback(timestamp)` and `SetPlaybackSpeed(speed)`
4. If mode == Live: calls `MetricPoller.ResetToLive()` (existing method)
5. Polling starts as normal

**Alternatives considered**:
- EditorPlugin injects settings via autoload singleton: Adds a runtime dependency on the editor plugin, which shouldn't exist. Plugin is editor-only; runtime reads ProjectSettings directly.
- Signal-based reactive settings: Over-engineering. Settings don't change during gameplay.

## R5: Addon Directory Structure

**Decision**: Relocate bridge code to `addons/pmview-bridge/` with this layout:

```
addons/pmview-bridge/
├── plugin.cfg              # Godot plugin manifest
├── PmviewBridgePlugin.cs   # EditorPlugin (registers settings, editor-only)
├── MetricPoller.cs          # Moved from scripts/bridge/
├── SceneBinder.cs           # Moved from scripts/bridge/
└── MetricBrowser.cs         # Moved from scripts/bridge/
```

**Rationale**: Godot requires addons under `addons/<name>/` with a `plugin.cfg`. The bridge nodes (MetricPoller, SceneBinder, MetricBrowser) are the runtime component of the addon. The EditorPlugin class is the editor-time component. Keeping them together in one directory makes the addon self-contained and distributable as a single directory copy.

**Alternatives considered**:
- Subdirectories (e.g., `addons/pmview-bridge/runtime/`, `addons/pmview-bridge/editor/`): Premature structure. 4 files don't need subdirectories.
- Keeping bridge code in `scripts/bridge/` and only adding EditorPlugin to `addons/`: Splits the plugin across directories, breaks Godot's addon packaging model, creates cross-directory dependencies.

## R6: Loop Behaviour Implementation

**Decision**: Extend `TimeCursor` with a `Loop` property. When `AdvanceBy()` advances past the end of available data and `Loop` is true, reset `Position` to `StartTime`. Detection of "end of data" uses the existing `ArchiveDiscovery.DetectTimeBounds()` result.

**Rationale**: The loop logic belongs in `TimeCursor` because it's a cursor state concern — when the cursor reaches the end, should it wrap? This keeps MetricPoller's fetch logic unchanged (it just fetches at whatever position the cursor reports).

**Alternatives considered**:
- Loop logic in MetricPoller: Mixes fetch concerns with cursor state. TimeCursor already owns position management.
- Loop logic in GDScript controller: Wrong layer. This is playback engine behaviour, not UI orchestration.
- Separate LoopController class: YAGNI. One boolean property and 3 lines of logic don't warrant a new class.

## R7: Scene Reference Updates After Migration

**Decision**: Update `.tscn` files' `[ext_resource]` paths and `script` references to point to `addons/pmview-bridge/` instead of `scripts/bridge/`. GDScript files using `preload()` or string paths to bridge scripts will also need updating.

**Rationale**: Godot uses `res://` paths in `.tscn` files. Moving the scripts changes their `res://` path. All references must be updated or scenes won't load.

**Files requiring path updates**:
- `godot-project/scenes/main.tscn` (references MetricPoller.cs, SceneBinder.cs, MetricBrowser.cs)
- `godot-project/scripts/scenes/metric_scene_controller.gd` (may reference bridge scripts)
- `godot-project/pmview-nextgen.csproj` (C# file includes if using explicit paths)
