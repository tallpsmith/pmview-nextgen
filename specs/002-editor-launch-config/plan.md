# Implementation Plan: Editor Launch Configuration

**Branch**: `002-editor-launch-config` | **Date**: 2026-03-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-editor-launch-config/spec.md`

## Summary

Restructure the existing bridge code into a proper Godot addon (`addons/pmview-bridge/`), then add a C# EditorPlugin that registers world configuration settings (endpoint, mode, timestamp, speed, loop) in Godot's ProjectSettings. The scene controller reads these at launch so scenes work immediately on Play without runtime overlay fumbling.

## Technical Context

**Language/Version**: C# (.NET 8.0 LTS) for EditorPlugin + bridge nodes; GDScript for scene controller integration
**Primary Dependencies**: Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries
**Storage**: Godot ProjectSettings (`project.godot` file — automatic persistence)
**Testing**: xUnit for TimeCursor.Loop extension; manual Godot editor testing for plugin UI (no Godot in CI)
**Target Platform**: Linux primary, macOS dev — cross-platform via Godot
**Project Type**: Godot addon (editor plugin + runtime bridge nodes)
**Performance Goals**: 60 FPS sustained; settings read once at launch (negligible overhead)
**Constraints**: EditorPlugin must be editor-only (no runtime dependency on plugin class); bridge nodes must work without plugin enabled
**Scale/Scope**: 5 ProjectSettings entries, 1 new C# class, 3 file relocations, ~15 path reference updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Prototype-First | **PASS** | The addon restructure (US0) is a structural move, not a new capability. The EditorPlugin (US1-3) builds on proven infrastructure (MetricPoller, TimeCursor, ProjectSettings are all well-understood). No prototype needed — this is configuration wiring, not feasibility exploration. |
| II. TDD | **PASS** | TimeCursor.Loop is new production logic → TDD required. EditorPlugin settings registration is Godot editor glue → manual test only (no Godot in CI). File relocation → verified by existing scenes functioning. |
| III. Code Quality | **PASS** | Small, focused changes. EditorPlugin is one class with one job. No speculative abstractions. |
| IV. UX Consistency | **PASS** | Uses standard Godot ProjectSettings UI — consistent with how all Godot plugins expose settings. |
| V. Performance | **PASS** | Settings read once at launch. Zero runtime overhead. |

### Post-Design Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Prototype-First | **PASS** | No new unproven capabilities. All building blocks exist and are tested. |
| II. TDD | **PASS** | TimeCursor.Loop: red-green-refactor. Settings integration: manual verification in Godot editor. |
| III. Code Quality | **PASS** | 1 new class (PmviewBridgePlugin), 1 new property (TimeCursor.Loop), ~20 lines in metric_scene_controller.gd. Minimal surface area. |
| IV. UX Consistency | **PASS** | Standard Godot plugin UX patterns. |
| V. Performance | **PASS** | No runtime changes to hot path. |

## Project Structure

### Documentation (this feature)

```text
specs/002-editor-launch-config/
├── plan.md                              # This file
├── research.md                          # Phase 0: technical decisions
├── data-model.md                        # Phase 1: entity definitions
├── quickstart.md                        # Phase 1: developer guide
├── contracts/
│   └── project-settings-schema.md       # Phase 1: ProjectSettings contract
└── tasks.md                             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
godot-project/
├── addons/pmview-bridge/                # NEW: addon directory (US0 restructure)
│   ├── plugin.cfg                       # Godot plugin manifest
│   ├── PmviewBridgePlugin.cs            # NEW: EditorPlugin (settings registration)
│   ├── MetricPoller.cs                  # MOVED from scripts/bridge/
│   ├── SceneBinder.cs                   # MOVED from scripts/bridge/
│   └── MetricBrowser.cs                 # MOVED from scripts/bridge/
├── scenes/
│   └── main.tscn                        # MODIFIED: updated script paths
├── scripts/scenes/
│   └── metric_scene_controller.gd       # MODIFIED: reads ProjectSettings at launch
└── project.godot                        # MODIFIED: plugin registration + pmview/ settings

src/pcp-client-dotnet/
└── src/PcpClient/
    └── TimeCursor.cs                    # MODIFIED: add Loop property + wrap logic
```

**Structure Decision**: Existing multi-project structure (PcpClient in `src/`, Godot project in `godot-project/`). This feature adds `addons/pmview-bridge/` inside the Godot project and extends TimeCursor in PcpClient. No new projects or solutions.

## Complexity Tracking

No constitution violations. No complexity justifications needed.

## Implementation Phases

### Phase A: Addon Restructure (US0 — P0)

**Goal**: Move bridge code to `addons/pmview-bridge/`, create `plugin.cfg`, verify existing scenes still work.

1. Create `godot-project/addons/pmview-bridge/` directory
2. Create `plugin.cfg` manifest (name: "pmview-bridge", description, script: PmviewBridgePlugin.cs)
3. Move MetricPoller.cs, SceneBinder.cs, MetricBrowser.cs from `scripts/bridge/` to `addons/pmview-bridge/`
4. Update C# namespace declarations if needed
5. Update all `.tscn` file references (`[ext_resource]` paths)
6. Update any GDScript `preload()` or path references
7. Update `pmview-nextgen.csproj` if it has explicit file includes
8. Delete `scripts/bridge/` directory
9. **Verify**: User loads project in Godot, enables plugin, runs existing scenes

### Phase B: TimeCursor Loop Extension (US1 — P1, TDD)

**Goal**: Add Loop property to TimeCursor with wrap-around behaviour.

1. **RED**: Write xUnit tests for Loop behaviour:
   - Default Loop = false
   - Loop = true + position past end → wraps to StartTime
   - Loop = false + position past end → position continues advancing (no wrap)
   - Loop + no EndBound set → no wrap (can't loop without known bounds)
2. **GREEN**: Add `Loop` bool property and wrap logic in `AdvanceBy()`
3. **REFACTOR**: Clean up if needed
4. Add `EndBound` property (set from ArchiveDiscovery results)

### Phase C: EditorPlugin + Settings Registration (US1-3 — P1-P3)

**Goal**: Create PmviewBridgePlugin.cs that registers ProjectSettings with proper hints.

1. Create `PmviewBridgePlugin.cs` extending `EditorPlugin` with `[Tool]` attribute
2. In `_EnterTree()`: register all 5 pmview/* settings with defaults and PropertyHint metadata
3. In `_ExitTree()`: optionally clean up (or leave settings for persistence)
4. **Verify**: User enables plugin, sees settings in Project Settings > General > pmview/

### Phase D: Scene Controller Integration (US1-3 — P1-P3)

**Goal**: metric_scene_controller.gd reads ProjectSettings at launch and configures MetricPoller.

1. Add ProjectSettings reads at top of `_ready()` (5 settings)
2. Apply endpoint to MetricPoller (override TOML default if ProjectSettings value differs from default)
3. Apply mode: Archive → call `StartPlayback()` + `SetPlaybackSpeed()`; Live → `ResetToLive()`
4. Pass loop setting to MetricPoller (which passes to TimeCursor)
5. Handle empty timestamp → compute 24h ago
6. Expose MetricPoller method to set TimeCursor.Loop (or add MetricPoller.SetLoop())
7. **Verify**: User sets archive mode in Project Settings, presses Play, scene immediately replays

### Phase E: MetricPoller Loop Integration

**Goal**: Wire Loop from ProjectSettings through MetricPoller to TimeCursor, handle end-of-data detection.

1. Add `SetLoop(bool)` method to MetricPoller
2. Pass `ArchiveDiscovery.DetectTimeBounds()` result to `TimeCursor.EndBound`
3. **Verify**: Archive playback with Loop=true wraps around; Loop=false freezes

## Key Risks

| Risk | Mitigation |
|------|-----------|
| `.tscn` path references break after move | Systematic grep for old paths; user verifies in Godot editor |
| C# EditorPlugin + Godot 4.4 quirks | Research confirms C# EditorPlugins are well-supported in 4.4+ |
| ProjectSettings not visible without plugin enabled | Settings persist in project.godot regardless; plugin just adds hint metadata |
| No automated Godot editor testing in CI | Manual verification checklist; TDD covers TimeCursor logic |
