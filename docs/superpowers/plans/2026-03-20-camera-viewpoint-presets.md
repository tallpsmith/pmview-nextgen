# Implementation Plan — Camera Viewpoint Presets

**Spec:** `docs/superpowers/specs/2026-03-20-camera-viewpoint-presets-design.md`
**Issue:** [#39](https://github.com/tallpsmith/pmview-nextgen/issues/39)

## Overview

Four tasks, ordered by dependency. Each task is independently testable (where applicable — GDScript work relies on user testing in Godot).

---

## Task 1: Add `FLYING_TO_VIEWPOINT` state to fly_orbit_camera.gd

**File:** `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/fly_orbit_camera.gd`

**Changes:**

1. Add `FLYING_TO_VIEWPOINT` to the `Mode` enum.
2. Add state variables for the fly-to animation:
   - `_flyto_start_pos: Vector3`
   - `_flyto_start_basis: Basis`
   - `_flyto_target_pos: Vector3`
   - `_flyto_target_look_at: Vector3`
   - `_flyto_progress: float`
   - `const FLYTO_DURATION: float = 1.2`
3. Add `_process_flyto(delta)` method:
   - Increment `_flyto_progress` by `delta / FLYTO_DURATION`.
   - Smoothstep interpolate position from `_flyto_start_pos` to `_flyto_target_pos`.
   - Slerp orientation from `_flyto_start_basis` toward looking at `_flyto_target_look_at`.
   - On completion (`t >= 1.0`): switch to `Mode.FLY`, capture yaw/pitch from final orientation.
4. Add `fly_to_viewpoint(target_pos: Vector3, look_at_pos: Vector3)` public method:
   - Capture current position and basis as start values.
   - Set target position and look-at.
   - If currently in `ORBIT`, reset orbit look overrides first.
   - Set `_flyto_progress = 0.0`, switch to `FLYING_TO_VIEWPOINT`.
5. Wire `FLYING_TO_VIEWPOINT` into `_process()` match statement.
6. Handle cancellation: in `_unhandled_input`, if `_mode == Mode.FLYING_TO_VIEWPOINT` and user provides WASD or mouse-look input, cancel by switching to `Mode.FLY` at current interpolated position (capture yaw/pitch).
7. Add `return_to_orbit()` public method:
   - Same as existing fly→orbit transition logic but callable externally.
   - If already in `ORBIT`, no-op.

**Tests:** User tests in Godot. The camera is pure GDScript with Godot types — no xUnit path.

---

## Task 2: Add viewpoint definitions and input handling to HostViewController.gd

**File:** `src/pmview-app/scripts/HostViewController.gd`

**Changes:**

1. Add viewpoint configuration as a const array at the top of the script:
   ```gdscript
   const VIEWPOINTS := [
       # [key, name, zone_names, offset]
       [KEY_1, "System", [], Vector3(0, 15, 20)],
       [KEY_2, "CPU", ["CPU"], Vector3(0, 3, 5)],
       [KEY_3, "Disk", ["Disk", "Per-Disk"], Vector3(0, 3, 5)],
       [KEY_4, "Network", ["Net-In", "Net-Out"], Vector3(0, 3, 5)],
   ]
   ```
   Note: System uses empty zone list as a sentinel meaning "all zones" / scene centre.

2. Add `_scene_binder: Node = null` variable. Populate in `_ready()` via `scene.find_child("SceneBinder", true, false)`.

3. Add `_active_viewpoint: int = -1` to track which viewpoint is active (-1 = none/orbit).

4. Add `_compute_viewpoint_centroid(zone_names: Array) -> Vector3` helper:
   - If empty, return `Vector3.ZERO` (scene centre for System view).
   - Otherwise, average `_scene_binder.GetZoneCentroid(name)` for each zone in the list.
   - Skip zones that return `Vector3.ZERO` (missing zones).

5. Add number key handling in `_unhandled_input()` — before the ESC handlers:
   ```gdscript
   # Viewpoint keys 1-4
   if event is InputEventKey and event.pressed and not event.echo:
       for vp in VIEWPOINTS:
           if event.physical_keycode == vp[0]:
               _activate_viewpoint(vp)
               get_viewport().set_input_as_handled()
               return
   ```

6. Add `_activate_viewpoint(vp: Array)` method:
   - If `_active_viewpoint == vp key index`, no-op (already there).
   - Compute centroid from zone names.
   - Calculate camera position: `centroid + vp[3]` (offset vector).
   - Call `_camera.fly_to_viewpoint(camera_pos, centroid)`.
   - Set `_active_viewpoint` to the key index.

7. Modify ESC handling — add a new check before the double-ESC logic:
   ```gdscript
   # ESC — return to orbit from viewpoint
   if event.is_action_pressed("ui_cancel") and _active_viewpoint >= 0:
       _camera.return_to_orbit()
       _active_viewpoint = -1
       get_viewport().set_input_as_handled()
       return
   ```
   This goes after the "close help panel" ESC check but before the "double-ESC to menu" check.

8. Reset `_active_viewpoint = -1` when Tab is pressed (mode toggle) — listen for it or let camera handle it, just keep the tracking in sync.

**Tests:** User tests in Godot. Input handling is tightly coupled to Godot scene tree.

---

## Task 3: Add Viewpoints group to Help panel content

**File:** `src/pmview-app/scripts/HostViewController.gd` (in `_setup_help_content()`)

**Changes:**

1. Add a new "Viewpoints" help group with a distinct colour (e.g., cyan/teal `Color(0.13, 0.84, 0.78)`):
   ```gdscript
   var viewpoints_group := HelpGroup.create("Viewpoints", teal, [
       HelpGroup.HelpEntry.create("1", "System overview"),
       HelpGroup.HelpEntry.create("2", "CPU close-up"),
       HelpGroup.HelpEntry.create("3", "Disk close-up"),
       HelpGroup.HelpEntry.create("4", "Network close-up"),
       HelpGroup.HelpEntry.create("ESC", "Return to orbit"),
   ])
   ```

2. Insert it into the `set_groups()` call — after Camera, before Archive Mode Playback.

3. Update the ESC entry in the General group to clarify: `"ESC × 2"` remains for "Return to main menu" (the viewpoint ESC is in the Viewpoints group).

4. Add viewpoint hints to `_setup_help_hints()`:
   ```gdscript
   HelpHintEntry.create("1-4", "Viewpoints"),
   ```

**Tests:** Visual — user verifies in Godot.

---

## Task 4: Tune offsets and polish

**File:** `src/pmview-app/scripts/HostViewController.gd`

This is a user-driven tuning task, not a code task. Once tasks 1–3 are implemented:

1. User tests each viewpoint in Godot with a real host scene.
2. Adjust offset vectors in `VIEWPOINTS` until the camera lands feel right.
3. Tune `FLYTO_DURATION` if the animation feels too fast/slow.
4. Verify ESC return-to-orbit feels smooth.
5. Verify WASD cancellation during fly-to works cleanly.

---

## Build Sequence

```
Task 1 (camera state machine)
  └──► Task 2 (input handling + viewpoint definitions)
         └──► Task 3 (help panel)
                └──► Task 4 (tuning — user-driven)
```

Tasks 1→2→3 are strictly sequential. Task 3 could technically be done in parallel with Task 2 since it only touches `_setup_help_content()`, but it's small enough that sequencing is fine.

## Files Modified

| File | Task | Nature |
|------|------|--------|
| `fly_orbit_camera.gd` | 1 | New state + two public methods |
| `HostViewController.gd` | 2, 3 | Input handling + viewpoint config + help content |

That's it — two files. Clean scope.
