# Keyboard Fly Controls — Design Spec

**Date:** 2026-03-20
**Status:** Draft

## Problem

Camera look is mouse-only (right-click + drag). Users who prefer keyboard-only navigation have no way to look around. Arrow keys are currently bound to archive scrub, blocking their use for camera look.

## Solution

Two changes:

1. **Arrow keys control camera look** in both Fly and Orbit modes (yaw/pitch via Left/Right and Up/Down).
2. **Archive scrub remapped to modifier+arrow combos** (Shift minimum), freeing bare arrows for camera look.

## Keyboard Look (FlyOrbitCamera)

### New Export

```gdscript
@export var keyboard_look_speed: float = 90.0  # degrees/second
```

Separate from `mouse_sensitivity` — tuned independently via inspector.

### Fly Mode

Arrow keys polled in `_process_fly()` alongside existing WASD:

- `LEFT` / `RIGHT` — adjust `_fly_yaw` by `±keyboard_look_speed * delta`
- `UP` / `DOWN` — adjust `_fly_pitch` by `±keyboard_look_speed * delta`
- Same pitch clamp as mouse: `±(PI/2 - 0.1)`
- Additive with mouse look — both work simultaneously
- Arrow input cancels focus animation (same as WASD movement)

### Orbit Mode — Temporary Look Override

Arrow keys temporarily override the `look_at(orbit_center)` direction. The camera keeps orbiting (position still follows the circular path), but the look direction is offset.

**New state variables:**

```gdscript
var _orbit_look_yaw_offset: float = 0.0
var _orbit_look_pitch_offset: float = 0.0
var _orbit_look_timer: float = 0.0      # seconds since last arrow input
var _orbit_look_easing_back: bool = false
```

**Behaviour:**

- Arrow keys accumulate `_orbit_look_yaw_offset` and `_orbit_look_pitch_offset` (same `keyboard_look_speed` rate)
- While offsets are non-zero, `_process_orbit()` applies the offset after computing the base look-at direction
- `_orbit_look_timer` resets on any arrow input; counts up each frame
- When timer reaches **10.0 seconds**: start easing offsets back to zero over **~0.5 seconds** using existing `_ease_in_out()`
- Once offsets reach zero, resume normal `look_at(orbit_center)`

### Transitioning Mode

Arrow keys ignored during Fly→Orbit transition (brief, not worth the complexity).

## Scrub Remapping (HostViewController)

All scrub controls now require `Shift` as minimum modifier. Bare arrows are freed for camera look.

| Keys | Action | Previous |
|------|--------|----------|
| `Shift + ←/→` | Scrub ±15 seconds | Was ±5s with Shift |
| `Ctrl+Shift + ←/→` | Scrub ±1 minute | Unchanged |
| `Option+Ctrl+Shift + ←/→` | Scrub ±5 minutes | New tier |

**Removed:** Bare arrow scrub (was ±poll interval). This functionality is covered by the modifier combos.

**Note:** `Option` key is `alt_pressed` in Godot's `InputEventKey`.

## Help Panel Update

The Archive Mode Playback group in `_setup_help_content()` must reflect the new bindings:

| Old | New |
|-----|-----|
| `← →` Scrub (poll interval) | Remove |
| `⇧ ← →` Scrub ±5 seconds | `⇧ ← →` Scrub ±15 seconds |
| `⌃⇧ ← →` Scrub ±1 minute | `⌃⇧ ← →` Scrub ±1 minute |
| — | `⌥⌃⇧ ← →` Scrub ±5 minutes |

The Camera group gets a new entry:

| Key | Action |
|-----|--------|
| `← → ↑ ↓` | Look around (arrow keys) |

## Files Changed

1. **`fly_orbit_camera.gd`** — new `keyboard_look_speed` export, arrow key polling in `_process_fly()`, orbit look override with ease-back in `_process_orbit()`
2. **`HostViewController.gd`** — scrub key remapping (remove bare arrows, add Option+Ctrl+Shift tier, change Shift from 5s to 15s), update help panel content strings
