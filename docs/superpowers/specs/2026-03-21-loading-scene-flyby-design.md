# Loading Scene: Ground Shader + Fly-By Camera Transition

**Date:** 2026-03-21

## Summary

Three enhancements to the loading scene:

1. Add the retro dot grid ground shader (same as main menu) beneath the letters
2. Halve the minimum wait time between letter materialisation phases
3. Replace the "scale-up and fade" exit animation with a cinematic fly-by camera sequence ending in a hyperspace transition

## 1. Ground Shader

Add a `GroundPlane` MeshInstance3D to `loading.tscn`, identical in setup to the main menu's but larger:

- **Mesh:** PlaneMesh, 120x120 (vs main menu's 60x60 — extra runway for the fly-by)
- **Material:** ShaderMaterial using `retro_dot_grid.gdshader`
- **Position:** Y = -1.2 (below the letter baselines at Y = 0; TextMesh glyphs extend ~0.6 units below baseline at font_size 120, so -1.2 gives clear visual separation. The main menu puts its ground at Y = 0 but elevates its letters to Y = 2.5 via TitleGroup — same visual result, different approach)
- **Shader params:** Same as main menu — `dot_spacing = 2.0`, `dot_size = 0.08`, `dot_color = (0, 0.8, 0.3, 1)` neon green, `fade_distance = 40.0` (increased from 18.0 to suit the larger plane). Note: the radial fade is centred on world origin, which will look fine during the static loading phase but may produce a visible fade edge during the fly-by when the camera moves off-centre — tune visually
- **Render:** `cast_shadow = off`, `render_priority = -1`

The green Battlezone grid sits as ambient floor during the letter materialisation phase, then becomes the primary visual element during the fly-by.

## 2. Halve Phase Delay

Add a `MinPhaseDelayMs = 250` property override on the `LoadingPipeline` node in `loading.tscn`. The scene currently has no override — it runs at the C# default of `500`. Adding the override in the .tscn halves the delay without modifying the C# source. Snappier letter reveals while still allowing the materialise animation (0.4s) to breathe.

## 3. Fly-By Camera Exit Animation

Replace the current `_on_pipeline_completed()` exit sequence (scale letters to 5x + fade) with a three-phase camera fly-by.

### 3.1 Retro Dot Grid Shader Enhancement

Add a `speed` uniform to `retro_dot_grid.gdshader` that stretches dots into streaks. At `speed = 0.0` dots are square (current behaviour). As speed increases, dots elongate — the ground becomes a starfield.

```
uniform float speed : hint_range(0.0, 1.0) = 0.0;
uniform float streak_angle : hint_range(0.0, 6.28) = 0.0;
```

The shader uses `world_xz` as a `vec2` where index 0 = world X, index 1 = world Z. The `streak_angle` uniform controls the stretching direction (radians), allowing the controller to rotate the streak axis to match the camera's movement direction at any point during the fly-by:

- During Phase 2 (lateral sweep along X): `streak_angle = 0.0` stretches along X
- During Phase 3 (forward punch along Z): `streak_angle = PI/2` stretches along Z
- Transition between phases: tween `streak_angle` smoothly

Implementation: rotate the cell coordinate by `streak_angle` before the SDF, compress the rotated Y component by `(1.0 - speed * 0.95)`, then evaluate the Chebyshev distance. This is backwards-compatible — existing scenes with `speed = 0.0` see no change regardless of angle.

### 3.2 Camera Path (GDScript in LoadingController.gd)

The camera starts at `(0, 0.18, 6.11)` looking at the letters head-on. The letters span X = -5.625 ("P") to X = 5.625 ("W").

**Phase 1 — Banking Approach (~0.8s):**
- Camera arcs from its start position toward the "P" letter at X = -5.625
- Path curves forward and left, using sequential Tweens on `position` and `rotation`
- Camera **dips slightly** (Y drops ~0.3) during the turn — aircraft dropping into an attack run
- Camera **rolls** (`rotation.z` tilts ~15°) to sell the banking turn, then levels out
- **Implementation note:** Do NOT use `look_at()` during the fly-by — it resets the camera's up vector and obliterates any roll. Drive the entire camera path via explicit `position` and `rotation` tweens (or `basis`/`transform` tweens). Pre-compute the target rotations as Euler angles or quaternions. This applies to all three phases.
- The camera rotation is tweened to face toward "P" during the arc, not via `look_at()`

**Phase 2 — Accelerating Sweep (~1.2s):**
- Camera flies laterally along the front face of all letters (negative-X to positive-X)
- Start position: alongside "P", slightly forward (Z offset ~1.5 from letter face)
- End position: well past "W" (X = ~12, past the last letter)
- **Acceleration:** Ease-in curve (e.g. `TRANS_QUAD` / `EASE_IN`) — starts moderate, builds to high speed
- Letters whip past the viewer's left side (camera looking slightly left during sweep)
- Ground shader `speed` uniform ramps from 0.0 to ~0.6 during this phase

**Phase 3 — Hyperspace Punch (~0.5s):**
- Camera continues accelerating forward/right
- Ground shader `speed` ramps from 0.6 to 1.0 — dots fully stretched into streaks
- White-out via a full-screen `ColorRect` (white, initially transparent) in the `CanvasLayer` — tween its `modulate:a` from 0.0 to 1.0. Simpler and more predictable than environment glow manipulation
- Scene transition via `SceneManager.go_to_host_view(pipeline.BuiltScene)` fires when white-out completes

### 3.3 Post-Process Radial Blur (Optional Enhancement)

A full-screen radial blur shader on a ColorRect in the CanvasLayer, with an `intensity` uniform. Disabled (0.0) during loading, ramped to ~0.4 during phase 3 to amplify the hyperspace effect. This layers on top of the ground shader stretching.

If the ground shader streaking alone sells the effect well enough during testing, skip this — YAGNI. The ground dots doing the work is the primary mechanism; the radial blur is gravy.

### 3.4 Timing Summary

| Phase | Duration | Camera Action | Ground Shader |
|-------|----------|---------------|---------------|
| Pause | 0.3s | Hold | speed = 0.0 |
| Banking approach | 0.8s | Arc + dip + roll toward "P" | speed = 0.0 |
| Accelerating sweep | 1.2s | Lateral fly-by, ease-in | speed 0.0 → 0.6 |
| Hyperspace punch | 0.5s | Continue acceleration | speed 0.6 → 1.0, white-out |
| **Total** | **~2.8s** | | |

## Files Modified

| File | Change |
|------|--------|
| `scenes/loading.tscn` | Add GroundPlane node, add WhiteOut ColorRect, add MinPhaseDelayMs = 250 override |
| `scripts/LoadingController.gd` | Replace zoom-exit with fly-by camera animation, add `@onready` refs for ground plane material and white-out rect |
| `shaders/retro_dot_grid.gdshader` | Add `speed` and `streak_angle` uniforms for directional dot stretching |

## Files Created

None expected. If radial blur is needed, a new `shaders/radial_blur.gdshader` would be created, but we start without it.

## Out of Scope

- Particle systems (star streak particles) — ground shader does the work
- Changes to the main menu scene
- Changes to LoadingPipeline.cs (only the scene property changes)
- Sound effects (though a warp-speed whoosh would be *chef's kiss*)
