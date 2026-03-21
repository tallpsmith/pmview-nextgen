# Multi-Host Fleet View Design

**Date:** 2026-03-21
**Status:** Approved
**Approach:** Scene-first with mock data, then wire polling

## Overview

A fleet-wide monitoring view that arranges multiple hosts in a grid, provides a patrol camera for scanning the fleet, and allows focusing on individual hosts via a holographic projection effect. Designed for 50+ hosts from the start.

The navigation metaphor: a security guard patrolling a server room — scanning for trouble at a glance, then stepping in close to inspect when something catches the eye.

## Fleet Grid Scene

New top-level scene `fleet_view.tscn`, separate from the existing `host_view.tscn`.

### Scene Tree

```
FleetView (Node3D)
├── WorldEnvironment
├── DirectionalLight3D
├── PatrolCamera (Camera3D)          — patrol_orbit_camera.gd (current in patrol)
├── FocusCamera (Camera3D)           — fly_orbit_camera.gd (current in focus)
├── FleetGrid (Node3D)              — arranges CompactHosts in auto-grid
│   ├── CompactHost_0 (Node3D)
│   ├── CompactHost_1 (Node3D)
│   └── ...
├── MasterTimestamp (Label3D)        — floating billboard above grid centroid
├── TimeControls (CanvasLayer)       — same right-side panel as existing, wired to fleet poller
└── CanvasLayer                      — ESC hint, mode indicator
```

Camera handoff uses Godot's `make_current()`: PatrolCamera is current during patrol mode, FocusCamera becomes current during the fly-to-focus transition. Both cameras coexist in the scene tree; only one is active at a time.

### Auto-Grid Layout

Hosts arranged in a roughly square grid:

- Columns: `ceil(sqrt(N))`
- Fill order: left-to-right, top-to-bottom
- Configurable `host_spacing` between grid cells

Example: 50 hosts → 8 columns × 7 rows (last row partially filled).

### CompactHost Node

Each host rendered as a miniature stand-in:

- **4 grounded bars** in a tight 2×2 arrangement: CPU, Memory, Disk, Network
- **Ground bezel** sized to the compact footprint
- **Licence plate label** — flat `Label3D` on the ground plane beneath, like a nameplate on a desk
- **Approximate footprint:** ~4×4 units per host (with spacing)

The compact view is not a scaled-down HostView — it is a purpose-built miniature that shows aggregate health at a glance.

## Patrol Camera

New script `patrol_orbit_camera.gd`, separate from the existing `fly_orbit_camera.gd`.

### PATROL State

- **Racetrack path:** rounded rectangle computed from grid bounds + margin
- **Camera position:** elevated, angled down ~30-45° at the grid centroid
- **Movement:** automatic travel along the racetrack at configurable speed
- **W/S keys:** throttle along the rail — speed up to catch an area of interest, slow down to linger
- **Arrow keys:** look around while patrolling (same ease-back pattern as existing orbit camera)
- **No free-flight mode.** The guard stays on the path.

### FLYING_TO_FOCUS State

- Triggered by Enter/click on a CompactHost
- Cinematic fly-to (1.2–1.5s) — camera swoops up to orbit height above the selected host
- On arrival: hands off to `fly_orbit_camera.gd` for the detail view

## Focus Mode & Holographic Projection

When a CompactHost is selected:

1. Camera flies up to orbit height
2. Full `HostView` spawns **floating above** the selected host's grid position
3. Holographic projection beam connects the two
4. All non-selected CompactHosts drop to ~30% opacity (still animating)
5. `fly_orbit_camera.gd` takes over (ORBIT + FLY modes available)

### Holographic Projection Beam

Inspired by R2-D2 projecting Princess Leia — a truncated cuboid (rectangular pyramid) connecting the compact host to its floating detail view.

- **Floor face:** matches the CompactHost's grid cell bounds (width × depth)
- **Ceiling face:** matches the floating HostView's actual bounds (width × depth)
- **Shape:** four transparent quad faces connecting floor corners to ceiling corners
- **Material:** transparent shader with rising scan-line animation, subtle blue/cyan holographic tint
- **Emergent scaling:** a host with many disks/instances produces a larger detail view and therefore a more dramatic pyramid flare. The beam shape tells you something about the host's complexity.

### Detail View Sizing

The detail view floats above the grid and is **unconstrained** — it expands to whatever size the host's full 10-zone layout requires. This avoids cramming busy hosts into grid cell bounds. The grid below remains stable and undisturbed.

**Occlusion note:** a busy host's detail view may span multiple grid cells when viewed from above. This is mitigated by the 30% opacity on non-selected hosts and the elevated float height. If occlusion proves disorienting in practice, a maximum detail view width can be introduced as a tuning parameter — but start unconstrained and test with real layouts before adding constraints.

### Returning to Patrol

- ESC triggers reverse transition
- Detail view fades out, projection beam fades
- Camera flies back down to the racetrack (nearest point)
- All CompactHosts restore to full opacity
- Patrol resumes

## Master Timeline — One Clock To Rule Them All

A single authoritative timestamp controls all hosts simultaneously.

### Visual Readouts

- **Floating billboard:** large PressStart2P timestamp hovering above the grid centroid. Visible from any point on the patrol racetrack. Acts as the visual anchor for the entire fleet.
- **Ground-level timestamp:** existing behaviour preserved — shows between foreground and background rows in focus/detail mode, where the floating billboard may be occluded by the detail view above.

### Controls

- Same right-side time management panel as existing single-host view
- Spacebar: play/pause
- Scrubber: drag to move through time
- All hosts update simultaneously — no per-host overrides

## Data Polling — Two-Tier Architecture

### Fleet MetricPoller (Long-Lived)

- Runs for the entire fleet view session
- Polls **4 aggregate metrics** per host: CPU, Memory, Disk, Network
- One poller per pmproxy source, requests serialised with throttling and backoff
- Keeps running during focus mode (translucent hosts still animate)
- Rate limiting baked in from day one — pmproxy is fragile under load

### Detail MetricPoller (Ephemeral)

- Spun up when entering focus mode on a specific host
- Polls the **full metric set** — all zones, all instances, per-instance breakdowns
- No double-polling: the fleet poller covers aggregates, the detail poller covers the additional instance-level data
- **Disposed** when ESC returns to patrol mode

### Polling Constraints

- Serial requests per tick (not parallel) to protect pmproxy
- Backoff on slow responses or errors
- Staggering host fetches across ticks if needed at scale
- No camera-visibility-based polling — steady state, predictable load

## Source Selection

### Immediate Changes

- Existing source chooser gains **multi-select** and **"ALL"** options
- Single host selected → existing `HostView` (unchanged)
- Multiple hosts or "ALL" → `FleetView`

### Deferred

- Config-file-based host/source discovery
- Hybrid model: config defines pmproxy sources, app discovers hosts from each

## Navigation State Machine

```
MAIN MENU
  │
  ├─ Single host selected ──→ HOST VIEW (existing, unchanged)
  │
  └─ Multiple / "ALL" ──→ FLEET VIEW
                              │
                              ├─ PATROL MODE
                              │   • Racetrack orbit around grid
                              │   • W/S = throttle along rail
                              │   • Arrow keys = look around (ease-back)
                              │   • Enter/click CompactHost = focus
                              │   • ESC (double-press) = back to Main Menu
                              │
                              └─ FOCUS MODE
                                  • Holographic projection beam active
                                  • Detail HostView floats above compact host
                                  • Non-selected hosts at 30% opacity
                                  • fly_orbit_camera (ORBIT + FLY modes)
                                  • ESC = back to Patrol Mode
```

## Scene Navigation Plumbing

### SceneManager Changes

`SceneManager.gd` gains a new `go_to_fleet_view()` method alongside the existing `go_to_host_view()`. The fleet view receives a list of hostnames rather than a single hostname.

### MainMenuController Changes

- Source chooser gains multi-select capability on the host dropdown (or a separate "ALL" button)
- Single host selection → `SceneManager.go_to_host_view()` (unchanged)
- Multiple hosts / "ALL" → `SceneManager.go_to_fleet_view(hostnames)`
- `connection_config` dictionary extended with a `hostnames: Array[String]` key for fleet mode

### Loading Pipeline

The fleet view **bypasses the existing loading scene pipeline** initially. The loading scene is designed for single-host scene projection (HostProjector → `.tscn`). Fleet mode starts with mock/static CompactHosts — no projection step needed for the compact 2×2 bars. When live polling is wired up, the fleet view builds CompactHosts dynamically from the hostname list without the full scene projection pipeline.

## Explicitly Deferred

These are out of scope for this design and parked for future work:

- Alert thresholds / beacon indicators (the human is the judge for now)
- Proximity-based LOD transitions
- Config-file host/source discovery
- Host grouping / clustering in grid layout
- Archive vs live mode differences for fleet
- Frustum-culling-based polling optimisation

## Implementation Approach

**Scene-first with mock data:**

1. Build the fleet grid scene with static CompactHosts
2. Build the patrol camera and racetrack path
3. Build the focus transition and holographic projection beam
4. Build the master timeline HUD
5. Wire up the two-tier MetricPoller architecture
6. Update source chooser with multi-select / "ALL"

Mock data allows iterating on the visual experience — camera feel, transition timing, beam aesthetics — before introducing the complexity of live polling and pmproxy throttling.
