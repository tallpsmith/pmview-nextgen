# Standalone Project Generation + Dual-Mode Camera

**Date:** 2026-03-16
**Status:** Approved
**Related Issues:** #17 (fly camera), #18 (unified CLI), #32 (scene switching — deferred)

## Problem

The host projector generates `.tscn` scene files, but requires users to bring their own Godot project, manually create a C# solution in the editor, and run `--install-addon`. There is no way to go from zero to a runnable 3D visualisation in a single command. The existing camera is a non-interactive helicopter orbit — users cannot explore the scene freely.

## Goals

1. A CLI command that creates a complete, ready-to-open Godot project from scratch
2. A dual-mode camera: orbit (showcase) and fly (exploration) with smooth transitions
3. Generated host-view scenes become lighter — camera, lights, and environment live in the project's main scene, not repeated per-view

## CLI Design

### `pmview init <project-dir>`

Creates a complete Godot .NET project:

```
<project-dir>/
├── project.godot            # Godot 4.6, C# + Forward Plus, main_scene = main.tscn
├── <project-name>.csproj    # net8.0, Godot.NET.Sdk, addon library refs
├── <project-name>.sln       # Solution wrapper
├── addons/
│   └── pmview-bridge/       # Full addon (scripts, building blocks, DLLs)
│       ├── plugin.cfg
│       ├── lib/             # PcpClient.dll, PcpGodotBridge.dll, Tomlyn.dll
│       └── ...
└── scenes/
    └── main.tscn            # Camera3D + fly_orbit_camera.gd, lighting, environment
```

- Project name derived from directory name
- Idempotent: safe to re-run (updates addon, doesn't clobber user files)
- No manual steps required — open in Godot and it works

### `pmview generate --pmproxy <url> -o <path>`

Generates a host-view scene into an existing project:

- Requires `project.godot` to exist somewhere up the output path
- If not found, **errors with a helpful message** suggesting `pmview init` or `--init`
- `--init` flag: creates the project first if missing, then generates the scene
- Generated scenes no longer contain camera, lights, or environment — those live in `main.tscn`

### Composability

`init` is the primitive. `generate --init` composes them. Users who want a custom project layout run `init` first, then `generate` into it.

## Dual-Mode Camera Controller

A single GDScript `fly_orbit_camera.gd` attached to Camera3D in `main.tscn`. Replaces and deletes the existing `camera_orbit.gd`.

### Orbit Mode (default on launch)

- Helicopter orbit: constant rotation around a computed centre point at fixed altitude
- Same behaviour as the current `camera_orbit.gd`
- Hands-off showcase mode

### Fly Mode (Tab to toggle)

| Control | Action |
|---------|--------|
| W/S | Forward / backward relative to camera facing |
| A/D | Strafe left / right |
| Q/E | Descend / ascend elevation |
| Right-click + drag | Mouse look (camera rotation) |
| Shift | Sprint (2x movement speed) |
| Tab | Toggle back to orbit mode |

- Movement speed proportional to scene extent (feels natural at any scale)
- Mouse stays free — right-click-drag to look, no capture/escape needed

### Transitions

**Orbit → Fly (Tab):** Instant. Camera stays at current position, user has immediate control.

**Fly → Orbit (Tab):** Smooth ease-in/ease-out interpolation from current position back to the orbit position/altitude/orientation. Uses exponential-decay approach consistent with SceneBinder's animation pipeline.

**Tab during transition:** Immediately snaps to fly mode at the current interpolated position. User wants control back — they get it instantly.

### Orbit Centre Computation

`WorldSetup.ComputeCamera()` is repurposed to compute the orbit centre and initial camera position for `main.tscn`. The orbit centre is baked into the camera node's exported properties when the project is scaffolded, and can be updated when new scenes are generated.

## Scene Architecture

### What lives in main.tscn (project-level)

- Camera3D with `fly_orbit_camera.gd`
- WorldEnvironment (dark background, ambient light)
- KeyLight + FillLight (directional lights)
- SceneRoot (empty Node3D) — host-view scenes loaded as children

### What lives in host-view scenes (generated per-host)

- MetricGroupNode hierarchies (GroundBezel + MetricGrid + shapes)
- AmbientLabels (timestamp, hostname)
- MetricPoller + SceneBinder
- No camera, no lights, no environment

This is idiomatic Godot scene composition: child scenes inherit the parent's camera, environment, and lights automatically. The active Camera3D in main.tscn governs all loaded content.

## File Changes

| File | Action | Detail |
|------|--------|--------|
| `fly_orbit_camera.gd` | Create | Dual-mode camera controller in addon building_blocks |
| `ProjectScaffolder.cs` | Create | Generates project.godot, .csproj, .sln, main.tscn |
| `camera_orbit.gd` | Delete | Superseded by fly_orbit_camera.gd |
| `Program.cs` | Modify | Add `init` subcommand, `--init` flag on `generate` |
| `TscnWriter.cs` | Modify | Stop emitting camera, lights, environment; remove camera_orbit ext_resource |
| `SceneEmitter.cs` | Modify | Drop WorldSetup.ComputeCamera call for scene generation |
| `WorldSetup.cs` | Modify | Repurpose ComputeCamera for main.tscn orbit centre |
| `TscnWriterTests.cs` | Modify | Update/remove camera/light assertions |

## Testing Strategy

| Component | Approach |
|-----------|----------|
| `ProjectScaffolder` | xUnit: assert generated project.godot has correct keys, .csproj has right SDK/refs, main.tscn has camera node. All string/file generation — no Godot dependency. |
| `TscnWriter` changes | xUnit: existing tests updated to assert camera/lights are *absent* from generated scenes |
| `fly_orbit_camera.gd` | Manual testing in Godot editor. Orbit math and transition interpolation could be extracted into testable pure functions if complexity warrants. |
| CLI integration | xUnit: `init` creates expected file tree; `generate` errors without project.godot; `generate --init` creates project + scene |

## Out of Scope

- Scene switching UI (deferred to Issue #32)
- Archive playback mode
- Custom project templates
