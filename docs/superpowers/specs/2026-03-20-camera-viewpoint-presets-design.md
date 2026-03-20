# Camera Viewpoint Presets — Design Spec

**Issue:** [#39 — Camera focus hotkeys for zone groups](https://github.com/tallpsmith/pmview-nextgen/issues/39)
**Date:** 2026-03-20

## Summary

Number keys `1`–`4` trigger smooth cinematic fly-to animations that move the camera to predetermined viewpoints for inspecting zone groups. ESC returns to the default orbit. The camera lands in fly mode so the user has full manual control to inspect metrics at leisure.

## Viewpoints

| Key | Name | Zones | Camera Feel |
|-----|------|-------|-------------|
| `1` | System | All foreground (CPU, Load, Memory, Disk, Net-In, Net-Out) | Elevated bird's-eye — see the whole host |
| `2` | CPU | CPU, per-CPU | Low, close — read individual CPU bars |
| `3` | Disk | Disk, Per-Disk | Close-up on I/O throughput |
| `4` | Network | Net-In, Net-Out | Close-up on network traffic |
| ESC | Return to orbit | — | Smooth fly-back to default orbit |

## Interaction Model

### Pressing a Number Key

1. If in orbit mode, switch to fly mode (capture current orientation).
2. Begin smooth fly-to animation toward the viewpoint's target position and orientation.
3. Animation uses smoothstep easing over ~1–1.5 seconds.
4. On arrival, camera is in fly mode with full WASD/mouse control.

### Pressing ESC

1. Smooth fly-back to the default orbit position.
2. Resume orbit mode with auto-rotate.
3. Orbit centre resets to the scene centre (Vector3.ZERO or layout centroid).
4. If already in default orbit, ESC does nothing (double-ESC still exits to main menu as before).

### Cancellation

- Any WASD movement or mouse-look input during a fly-to animation cancels it immediately.
- Camera stays wherever it is in fly mode — no rubber-banding.

### Re-press Behaviour

- Pressing the same number key while already at that viewpoint: no-op.
- Pressing a different number key: fly-to the new viewpoint from current position.

## Camera Positioning

### Strategy: Centroid + Offset (B1)

Each viewpoint defines:
- **Zone list**: which zone names to include in the centroid calculation.
- **Offset vector**: a fixed (x, y, z) offset from the computed centroid.

The centroid is calculated dynamically using `SceneBinder.GetZoneCentroid()` (or a multi-zone variant), so viewpoints adapt to the host's actual topology and layout.

### Offset Tuning

| Viewpoint | Y (height) | Z (distance) | Character |
|-----------|-----------|--------------|-----------|
| System | High (~15) | Far (~20) | Elevated overview, slight downward look |
| CPU | Low (~3) | Close (~5) | At bar-reading height |
| Disk | Low (~3) | Close (~5) | Same pattern as CPU |
| Network | Low (~3) | Close (~5) | Same pattern as CPU |

These are starting values — will need tuning once we can see them in-engine.

## Camera State Machine

A new `FLYING_TO_VIEWPOINT` state is added to the existing `Mode` enum:

```
ORBIT ──[number key]──► FLYING_TO_VIEWPOINT ──[arrive]──► FLY
  ▲                         │                                │
  │                         │ [WASD/mouse = cancel]          │
  │                         ▼                                │
  │                        FLY                               │
  │                                                          │
  └──────────────[ESC]──── TRANSITIONING ◄──[ESC]────────────┘
```

`FLYING_TO_VIEWPOINT` interpolates both position and orientation using smoothstep, similar to the existing `TRANSITIONING` state but with a target position+look-at rather than returning to the orbit path.

## Multi-Zone Centroid

`GetZoneCentroid` currently takes a single zone name. For viewpoints that span multiple zones (System, Disk, Network), we need a method that computes the centroid across multiple zones. Options:

- **New method** `GetMultiZoneCentroid(string[] zoneNames)` on `SceneBinder`.
- **Or** average multiple single-zone centroids in GDScript.

The GDScript averaging approach is simpler and avoids touching the C# bridge for a purely camera concern.

## Help Panel

Add a new "Viewpoints" group to the help panel:

| Key | Description |
|-----|-------------|
| 1 | System overview |
| 2 | CPU close-up |
| 3 | Disk close-up |
| 4 | Network close-up |
| ESC | Return to orbit |

Use a distinct colour to differentiate from existing Camera and Panels groups.

## Non-Goals

- Custom/user-defined viewpoints (future consideration).
- Bounding-box framing (B2 approach — can upgrade later if needed).
- Orbit-around-zone mode (user prefers stationary fly mode for inspection).
- Memory-specific viewpoint (visible from System overview).

## Dependencies

- `fly_orbit_camera.gd` — new `FLYING_TO_VIEWPOINT` state + `fly_to_viewpoint()` method.
- `SceneBinder.GetZoneCentroid()` — already exists, used for position calculation.
- `HostViewController.gd` — input handling for number keys 1–4.
- `help_panel.gd` / `HostViewController._setup_help_content()` — new Viewpoints group.
