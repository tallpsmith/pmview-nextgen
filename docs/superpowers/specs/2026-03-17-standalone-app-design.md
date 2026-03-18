# Standalone PMVIEW Application — Design Spec

**Date:** 2026-03-17
**Status:** Approved

## Overview

Package pmview-nextgen as a standalone, distributable Godot application with a
retro arcade-inspired main menu. Users launch the app, point it at a pmproxy
endpoint, and get a live 3D host visualisation — no Godot editor, no CLI, no
manual scene generation.

**Target audience:** PCP sysadmins who already have pmproxy deployed. No
onboarding or hand-holding for PCP setup.

## Architecture

### Project Structure

Extract the projection pipeline into a shared core library. The Host Projector
CLI becomes a thin wrapper. The new app consumes the same core.

```
pmview-nextgen/
  src/
    pcp-client-dotnet/           # Pure .NET, pmproxy HTTP client (unchanged)
    pcp-godot-bridge/            # Pure .NET, binding model + TOML (unchanged)
    pmview-projection-core/      # NEW: extracted from host-projector (net8.0)
    │  ├── Discovery/            #   MetricDiscovery, HostTopology, HostOs
    │  ├── Layout/               #   LayoutCalculator
    │  ├── Models/               #   SceneLayout, PlacedZone, PlacedShape, PlacedStack,
    │  │                         #   ZoneDefinition, MetricShapeMapping, Vec3, RgbColour
    │  └── Profiles/             #   HostProfileProvider, LinuxProfile, MacOsProfile, SharedZones
    pmview-host-projector/       # Thin CLI (net10.0): Emission + Scaffolding + Program.cs
    │  ├── Emission/             #   TscnWriter, SceneEmitter (stays here)
    │  └── Scaffolding/          #   ProjectScaffolder, AddonInstaller,
    │                            #   LibraryBuilder, CsprojPatcher (stays here)
    pmview-bridge-addon/         # Addon source of truth (unchanged)
    pmview-app/                  # NEW: standalone Godot application (net8.0, Godot.NET.Sdk 4.6.1)
      ├── project.godot
      ├── scenes/
      │  ├── main_menu.tscn      #   Title screen + connection form
      │  ├── loading.tscn        #   Letter materialisation transition
      │  └── host_view.tscn      #   Template/empty, populated at runtime
      ├── scripts/
      │  ├── MainMenuController.gd
      │  ├── LoadingController.gd
      │  ├── HostViewController.gd
      │  └── RuntimeSceneBuilder.cs  # Instantiates nodes from SceneLayout
      ├── assets/
      │  └── fonts/
      │     └── PressStart2P-Regular.ttf
      └── addons/pmview-bridge/  #   Symlinked from addon source (dev), copied for export
```

### Dependency Graph

Everything flows downward — no circular dependencies:

```
pmview-app ──→ pmview-projection-core ──→ PcpClient
    │                                        ↑
    └──→ pmview-bridge-addon ──→ PcpGodotBridge ─┘
                                     │
                                     └──→ Tomlyn

pmview-host-projector ──→ pmview-projection-core ──→ PcpClient
```

**Key rules:**
- `pmview-projection-core` targets `net8.0`, pure .NET, zero Godot
  dependencies, fully xUnit testable
- `pmview-host-projector` stays at `net10.0`, references the core via
  `ProjectReference`
- `pmview-app` targets `net8.0` (Godot.NET.Sdk 4.6.1 requires net8.0)
- `RuntimeSceneBuilder` lives in `pmview-app` (not the addon — see rationale
  below)
- Host Projector CLI keeps `TscnWriter`, scaffolding, `LibraryBuilder`, and
  `CsprojPatcher` — delegates discovery/layout/profiles to the core

### New Library: pmview-projection-core

Extracted from the current Host Projector. Contains everything needed to go from
"pmproxy endpoint" to "laid-out scene model":

- `Discovery/` — `MetricDiscovery`, `HostTopology`, `HostOs`
- `Layout/` — `LayoutCalculator`
- `Models/` — `SceneLayout`, `PlacedZone`, `PlacedShape`, `PlacedStack`,
  `ZoneDefinition`, `MetricShapeMapping`, `Vec3`, `RgbColour`, etc.
- `Profiles/` — `IHostProfileProvider`, `HostProfileProvider`, `LinuxProfile`,
  `MacOsProfile`, `SharedZones`

Target framework: `net8.0`. Depends on `PcpClient` only. No Godot dependencies.
All existing xUnit tests for these types move with the library.

Note: `ZoneDefinition` and `MetricShapeMapping` are profile/build-time types
that define *what* to lay out. `SceneLayout`, `PlacedZone`, `PlacedShape`, etc.
are the *output* of layout calculation. Both live in the core because
`LayoutCalculator` consumes the former and produces the latter.

### RuntimeSceneBuilder

Lives in `pmview-app/scripts/RuntimeSceneBuilder.cs` — **not** in the addon.

**Rationale:** The addon is a lightweight bridge plugin (MetricPoller,
SceneBinder, building blocks). Adding a dependency on `pmview-projection-core`
would mean every project that installs the addon drags in the entire projection
pipeline. `RuntimeSceneBuilder` is an app concern — it's the app's job to
orchestrate discovery → layout → scene instantiation. The addon provides the
building block scenes and binding infrastructure that the builder uses.

**Input:** `SceneLayout` + pmproxy endpoint string
**Output:** `Node3D` tree ready to add to the scene tree

Builds the same structure as `TscnWriter` emits:
- Root `Node3D` ("HostView")
  - `MetricPoller` (configured with endpoint)
  - `SceneBinder`
  - Per-zone: `MetricGroupNode` → `GroundBezel` + `MetricGrid` → shapes
  - Ghost shapes: `ghost = true`, no binding, no `PcpBindable`
  - Stacks: `StackGroupNode` with ordered members

Takes `IProgress<float>` for progress reporting to the loading screen.

`pmview-app`'s `.csproj` references both the addon's bundled DLLs (PcpClient,
PcpGodotBridge, Tomlyn) and `pmview-projection-core` (via `ProjectReference`
during development, bundled DLL for export).

### Addon Installation for pmview-app

During development, `pmview-app/addons/pmview-bridge/` is a **symlink** to
`src/pmview-bridge-addon/addons/pmview-bridge/`. This avoids copy drift and
lets changes to the addon source appear immediately in the app.

For CI export builds, the symlink is resolved (files copied) as part of the
export preparation step — Godot export doesn't follow symlinks reliably.

## Visual Design

### Style: Neon Minimal Retro

Dark background (#0a0a0f), neon gradient accents (pink #ff006e → purple #8338ec
→ blue #3a86ff). Modern-retro hybrid — nods to the 80s without cosplaying it.

**Font:** Press Start 2P (Google Fonts, SIL Open Font License 1.1) — used for
all UI text: title, labels, input fields, buttons, status messages. The
quintessential arcade high-score-table font.

### Main Menu Scene

Vertical layout, centred on screen:

1. **3D PMVIEW title** — large, dominant, slowly rotating. See "3D Title
   Implementation" below for approach.
2. **Subtitle** — "PERFORMANCE CO-PILOT VISUALISER" in small Press Start 2P
3. **Connection form** (2D Control overlay):
   - PMPROXY ENDPOINT — text input, defaults to `http://localhost:44322`
   - MODE — toggle: LIVE only (Archive mode toggle deferred — see Future Work)
4. **LAUNCH button** — KITT scanner hover effect. A neon gradient sweep
   (pink → purple → blue) slides back and forth across the button on hover,
   inspired by Knight Rider's KITT scanner bar. Implemented as an animated
   `ShaderMaterial` uniform.
5. **Version** — small, bottom of screen

### 3D Title Implementation

Godot 4.6 does not have a built-in node that produces individually-animatable
extruded 3D letter meshes. This requires a **spike** to evaluate approaches:

- **Option A:** Pre-modelled `.glb` meshes — create P, M, V, I, E, W as
  individual 3D models in Blender, export as `.glb`, load as `MeshInstance3D`.
  Most control, best visual quality, but requires Blender workflow.
- **Option B:** Godot `TextMesh` with per-letter nodes — use `TextMesh`
  (available in Godot 4.x) for each letter. Gives extruded 3D text natively
  but with less control over geometry.
- **Option C:** `Label3D` with shader trickery — flat text with a shader that
  fakes depth via outline/shadow. Simpler but not truly 3D.

The spike should evaluate visual quality, shader compatibility (wireframe →
solid materialisation), and rotation appearance for each approach. Recommend
starting with Option B (`TextMesh`) as it's native Godot and may be sufficient.

### Loading Transition

The loading screen doubles as a progress indicator using the 3D title itself:

1. Main menu UI fades out. 3D PMVIEW title moves forward (camera animation).
2. Each of the 6 letters (P-M-V-I-E-W) starts as a **wireframe mesh** —
   transparent fill, edge-only shader.
3. Six loading phases, each mapped to a letter:
   - **P** — Connecting to pmproxy
   - **M** — Fetching host topology
   - **V** — Discovering instances
   - **I** — Selecting profile
   - **E** — Computing layout
   - **W** — Building scene graph
4. As each phase completes, that letter gets a brief **glow pulse**, then
   transitions to the full **solid neon gradient material**.
5. Status text below shows current phase name in Press Start 2P.
6. On error: letter flashes red, status shows error message, "Press ESC to
   return" appears.
7. At 100%: brief pause, then the title **zooms toward camera** and dissolves
   (scale up + fade opacity). Cut to host view scene.

**Implementation:** Each letter is a separate `MeshInstance3D` with a
`ShaderMaterial` that has a `materialise` uniform (0.0 = wireframe, 1.0 = solid).
Transition driven by `Tween` or `AnimationPlayer`. Zoom exit is a camera or
title `AnimationPlayer` sequence.

### Host View Scene

The runtime-built scene showing live metrics:

- Zones with shapes, stacks, grids — built by `RuntimeSceneBuilder`
- Fly/orbit camera (existing building block)
- MetricPoller fetching live data
- SceneBinder animating shapes

**ESC overlay:** First ESC press shows a small Press Start 2P text bar:
"PRESS ESC AGAIN TO RETURN TO MENU". Auto-dismisses after ~2 seconds if no
second press. Second ESC within the window destroys the scene and returns to
MainMenu.

**Quit:** Alt+Q (Linux) / Cmd+Q (macOS) quits the application via standard
Godot `NOTIFICATION_WM_CLOSE_REQUEST` handling.

**Session state:** Clean slate on return. Scene is destroyed, all state
discarded. Next LAUNCH does full rediscovery. Simple, no session management.

### Runtime Error Handling

**During loading (Loading scene):**
- Connection failure, timeout, or unexpected pmproxy response → current letter
  flashes red, status text shows the error, "PRESS ESC TO RETURN" appears.
  User returns to main menu to fix the endpoint and try again.

**During live session (HostView scene):**
- MetricPoller already handles transient failures gracefully (HTTP errors logged,
  polling continues on next interval). Shapes retain their last-known values —
  they don't reset to zero or disappear.
- If pmproxy becomes permanently unreachable, shapes freeze at their last values.
  No automatic return to menu — the user may want to inspect the frozen state or
  wait for pmproxy to recover.
- Future enhancement (out of scope): a subtle "CONNECTION LOST" indicator in
  the HUD after N consecutive poll failures.

## Scene Flow

```
MainMenu ──[LAUNCH]──→ Loading ──[100%]──→ HostView
    ↑                                          │
    └──────────────[ESC ESC]───────────────────┘
```

**Data passed between scenes:**
- MainMenu → Loading: connection config (endpoint URL, mode)
- Loading → HostView: built Node3D scene graph (root node ready to add)

## Distribution

### Packaging

Godot export to platform-specific self-contained binaries:

- **Linux x86_64** — binary + `.pck` (or single file with embedded pck)
- **macOS** — `.app` bundle (universal arm64 + x86_64)
- Windows deferred — add when there's demand

Exports bundle: Godot runtime, .NET 8.0 runtime, compiled assemblies, all
project resources (scenes, fonts, shaders, building blocks, addon DLLs).

### GitHub Releases

Tag-triggered releases. Tag `v0.1.0` → CI exports for each platform → uploads
archives to GitHub Release:

- `pmview-0.1.0-linux-x86_64.tar.gz`
- `pmview-0.1.0-macos.zip`

**User experience:**
- Linux: `tar xf pmview-0.1.0-linux-x86_64.tar.gz && ./pmview`
- macOS: unzip, double-click `.app` (or drag to Applications)

No installer. No runtime dependencies. No pmproxy bundled — user must have PCP
deployed (target audience already does).

### CI Pipeline

- Existing jobs unchanged (dotnet test, gdUnit4 tests)
- New job: on tagged release, runs Godot headless export for each platform
- Uses `chickensoft-games/setup-godot` or similar GitHub Action
- Uploads archives as release assets

### Versioning

Semver. Tags drive releases. No separate version file — tag is the source of
truth.

## Documentation Updates

`docs/ARCHITECTURE.md` must be updated as part of implementation to reflect:
- The new `pmview-projection-core` library and its role
- The `pmview-app` project and runtime scene building
- The three-mode model: CLI generation, addon-in-editor, standalone app

## Third-Party Attribution

| Asset | License | Restrictions |
|-------|---------|-------------|
| Press Start 2P font | SIL Open Font License 1.1 | Cannot sell font standalone |
| Tomlyn | MIT | None |
| PcpClient | Internal | N/A |
| PcpGodotBridge | Internal | N/A |

Project license not yet chosen. Attribution file to be maintained as
dependencies are added.

## Future Work (out of scope)

- Archive mode toggle + time range UI (deferred until archive playback is
  implemented in the bridge layer)
- Homebrew tap for macOS distribution (file GitHub issue)
- Windows export support
- Multiple host views / multi-host dashboard
- Connection history / bookmarks
- "CONNECTION LOST" HUD indicator for persistent poll failures
