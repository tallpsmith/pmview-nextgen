# Fleet HostView Preview & Dive-In

**Date:** 2026-03-23
**Status:** Approved

## Summary

Decouple the HostView scene builder so the 3D metric visualisation (zones, shapes, poller, binder) can be rendered independently of the full HostView scene infrastructure (camera, environment, UI panels). This enables a **live read-only HostView preview** floating above a focused host in the fleet grid, with a **dive-in transition** to the full interactive HostView scene.

## Goals

- Replace the placeholder bars in fleet focus mode with a real HostView zone visualisation
- Pre-fetch the HostView scene graph in the background during fleet focus, so dive-in is instant
- Maintain a clean ESC navigation hierarchy: HostView → Fleet Focus → Fleet Patrol → Main Menu
- Provide visual feedback during the pipeline pre-fetch via a matrix loading animation

## Non-Goals

- Interactive shape selection within the fleet preview (read-only only)
- Multiple simultaneous HostView previews (one focused host at a time)
- Changing the standalone HostView loading flow from main menu

## Architecture

### HostSceneBuilder (rename from RuntimeSceneBuilder)

Split the existing monolithic `Build()` method into two:

**`BuildZones(layout, endpoint, mode, hostname)`** returns a plain Node3D containing:
- MetricPoller (C#) — fetches metric data from pmproxy
- SceneBinder (C#) — binds metric values to shape properties
- All metric zones (CPU, Disk, Network, etc.) with bezels, grids, and shapes
- TimestampLabel and HostnameLabel (Label3D)

**Important:** `BuildZones()` does NOT attach the `host_view_controller.gd` script to the root node. The current `Build()` method sets this script on the root, but the controller's `_ready()` expects `SceneManager.built_scene` and would immediately bail to main menu if it's null. The root returned by `BuildZones()` is a plain Node3D — the controller script is only relevant in the full HostView scene context.

**`AddHostViewUi(sceneRoot, isArchiveMode)`** grafts UI onto an existing zones scene:
- UILayer (CanvasLayer) with RangeTuningPanel, HelpPanel, HelpHint, DetailPanel
- TimeControl (archive mode only)
- Re-sets node ownership recursively so `find_child(owned: true)` works after reparenting

**Callers:**
- Fleet preview: `BuildZones()` only
- Full HostView via loading screen: `BuildZones()` + `AddHostViewUi()`
- Dive-in from fleet: detach zones Node3D → pass to HostView → `AddHostViewUi()`

### Fleet Focus Lifecycle

When a user clicks a host in the fleet grid:

1. **T+0s** — Camera flies to focus position. Holographic beam fades in. Other hosts dim. Matrix progress grid appears on beam top (all cells dark). Pipeline kicks off.
2. **Phases 0–4** — Pipeline runs (connect, topology, instances, profile, layout). Matrix cells light up in random scatter pattern, each with a brief glow pulse on activation. Each phase emits `PhaseCompleted` which drives `set_progress()`.
3. **Phase 5: `BuildZones()`** — This is a synchronous call on the main thread (builds the full node graph). The matrix animation will pause during this phase. To handle this gracefully: phases 0–4 fill cells 0–60, then the matrix jumps to 100% when `BuildZones()` returns. The visual effect is: steady scatter fill during discovery phases, brief pause, then snap to complete.
4. **Done** — Matrix grid dissolves. HostView zones fade in above the beam. MetricPoller starts feeding live data. Double-click or Enter now available to dive in.

### Matrix Progress Grid

A 10×10 grid of cells sitting on the top surface of the holographic beam.

- **Mesh:** Plane with 100 cell quads
- **Shader:** Uniform array of cell states, rendered as holographic cyan with glow
- **Cell activation:** Random scatter order (pre-shuffled index array)
- **Glow pulse:** Brief brightness spike when a cell activates, then settles to steady state
- **Opacity variation:** 0.7–1.0 per cell for visual texture
- **API:** `set_progress(0.0–1.0)` — activates proportional number of cells
- **Signal:** `progress_complete` — triggers dissolve animation
- **Script:** `matrix_progress_grid.gd`
- **Shader file:** `matrix_progress_grid.gdshader`

Pipeline phase mapping to cells:
| Phase | Description | Cells | Cumulative |
|-------|-------------|-------|------------|
| 0 | Connect | 10 | 10 |
| 1 | Topology | 20 | 30 |
| 2 | Instances | 0 | 30 |
| 3 | Profile | 10 | 40 |
| 4 | Layout | 20 | 60 |
| 5 | Build | 40 | 100 |

Phase 2 (Instances) is a no-op in the current pipeline — instances are resolved during topology discovery. The `PhaseCompleted` signal still fires for phase 2; the matrix grid absorbs it silently (0 new cells).

### Dive-In Transition

**Trigger:** Double-click on the floating preview (Godot's `InputEventMouseButton.double_click`), or press Enter while in fleet focus. Single-click on the preview is a no-op (avoids ambiguity with existing click-to-focus in patrol mode). The preview's zone shapes already have collision bodies from `BuildZones()`, so raycasting works for double-click detection.

**If pipeline complete:**
1. Detach zones Node3D from fleet scene tree (`remove_child()`, NOT `queue_free()`)
2. Pass to `SceneManager.go_to_host_view_from_fleet(scene, config)`
3. HostView receives pre-built scene, calls `AddHostViewUi()`, re-sets ownership
4. Instant transition — no loading screen

**If pipeline still running:**
1. Transition to loading scene
2. Pipeline continues where it left off
3. Standard loading → HostView flow

### ESC Navigation Hierarchy

| Current Scene | ESC | ESC ESC |
|---|---|---|
| HostView (from fleet) | → Fleet Focus (same host) | → Fleet Focus (same) |
| Fleet Focus | → Fleet Patrol | → Fleet Patrol (same) |
| Fleet Patrol | starts 2s timer | → Main Menu |
| HostView (from main menu) | starts 2s timer | → Main Menu |

Every single ESC goes up one level. Double-ESC is only special at the Patrol level (confirmation gate for Main Menu). When returning from HostView to Fleet, the user lands in Fleet Focus on the same host they dived into.

### SceneManager Changes

New state:
- `origin_scene: String = ""` — tracks where HostView was launched from ("fleet" or "")
- `fleet_focused_hostname: String = ""` — the hostname that was focused when dive-in occurred

New methods:
- `go_to_host_view_from_fleet(scene, config, focused_hostname)` — sets origin_scene = "fleet", stores focused_hostname, preserves connection_config, sets built_scene, transitions to host_view.tscn
- `return_to_fleet()` — clears origin_scene, transitions back to fleet_view.tscn (connection_config and fleet_focused_hostname are preserved)

**Fleet view restoration:** When `FleetViewController._ready()` runs after a return-to-fleet transition, it checks `SceneManager.fleet_focused_hostname`. If set, it skips straight to focus mode on that host: dims other hosts, spawns beam, positions camera at the focus orbit, and clears the stored hostname. The fleet poller resumes from the existing connection_config (same endpoint, same hostnames, same mode). In archive mode, the playback position is carried in connection_config.

HostViewController ESC handler checks `SceneManager.origin_scene`:
- If "fleet": single ESC calls `SceneManager.return_to_fleet()`
- If "": existing double-ESC → main menu behaviour

## Files Impacted

### Renamed
- `RuntimeSceneBuilder.cs` → `HostSceneBuilder.cs` — split `Build()` into `BuildZones()` + `AddHostViewUi()`

### Modified
- **SceneManager.gd** — `origin_scene` state, `go_to_host_view_from_fleet()`, `return_to_fleet()`
- **HostViewController.gd** — single-ESC returns to fleet when `origin_scene == "fleet"`
- **FleetViewController.gd** — replace `_spawn_mock_detail_view()` with pipeline pre-fetch + real preview
- **LoadingPipeline.cs** — call `HostSceneBuilder.BuildZones()` + `AddHostViewUi()` instead of `Build()`
- **LoadingController.gd** — update reference to renamed builder
- **holographic_beam.gd** — host matrix grid as child node

### New
- **matrix_progress_grid.gd** — 10×10 cell grid with random scatter fill
- **matrix_progress_grid.gdshader** — cell state rendering with glow pulse
- **FleetHostPipeline.gd** — GDScript coordinator that instantiates a `LoadingPipeline` C# node, adds it to the fleet scene tree, connects its `PhaseCompleted` signal to drive the matrix grid's `set_progress()`, and holds a reference to the built zones Node3D on completion. The pipeline runs on the main thread (C# async in Godot uses the synchronization context), same as the loading screen — the matrix animation provides visual continuity during the async phases, with a brief freeze during the synchronous `BuildZones()` call.
