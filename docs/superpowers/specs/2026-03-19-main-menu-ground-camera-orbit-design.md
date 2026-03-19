# Main Menu: Retro Dot-Grid Ground & Camera Orbit

**Date:** 2026-03-19
**Status:** Approved

## Summary

Replace the rotating title with a stationary title and orbiting camera, add a retro 80s-arcade
dot-grid ground plane beneath the text, and give the UI panel a translucent background so the
3D scene shows through.

## Current State

- `TitleGroup` (Node3D) rotates at 0.3 rad/s with sine wobble — contains 6 emissive TextMesh letters
- Camera3D is static at `(0, 3, 7.5)`, looking slightly downward
- No ground or environment geometry — letters float in dark space
- UI panel (`CanvasLayer`) has an opaque background

## Design

### 1. Camera Orbit (CameraRig)

Introduce a `CameraRig` (Node3D) at the origin. Reparent Camera3D as its child.

- **Orbit:** CameraRig rotates continuously around Y at `orbit_speed` (default 0.3 rad/s) — full 360°
- **Elevation bob:** Camera3D's Y position oscillates on a sine wave at a different frequency
  from the orbit (~0.2 rad/s, ~31s cycle vs ~21s orbit). This creates a Lissajous-like pattern
  so the viewing angle is always novel.
- **Look-at:** Camera always looks at origin (or slightly above, toward text centre)
- **TitleGroup stops rotating** — it becomes stationary. The camera provides all the motion.

**Exported parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `orbit_speed` | 0.3 | Camera orbit angular velocity (rad/s) |
| `orbit_radius` | ~7.5 | Distance from camera to origin |
| `elevation_min` | 1.5 | Lowest camera Y during bob |
| `elevation_max` | 4.5 | Highest camera Y during bob |
| `elevation_speed` | ~0.2 | Elevation oscillation speed (rad/s) |

### 2. Retro Dot-Grid Ground Plane

A `MeshInstance3D` with a `PlaneMesh` positioned below the title, using a procedural
`retro_dot_grid.gdshader`.

**Shader approach:**
- `spatial` shader, `unshaded` render mode
- Uses `fract()` on world-space coordinates to create a repeating grid
- Each grid cell draws a circle via distance field (`step()` or `smoothstep()`)
- Dots fade toward the edges of the plane for a natural falloff
- Green dots on transparent/black background — subtle, wide spacing (Battlezone/Tron aesthetic)

**Shader uniforms:**

| Uniform | Default | Description |
|---------|---------|-------------|
| `dot_spacing` | ~2.0 | Grid cell size (larger = sparser) |
| `dot_radius` | ~0.08 | Dot size relative to cell |
| `dot_color` | green `(0, 0.8, 0.3, 1)` | Dot colour |
| `fade_start` | 0.6 | Edge fade threshold |

**Plane sizing:** Large enough to fill the camera's view during orbit — likely 40x40 or 60x60 units.
Positioned at Y=0 (below title at Y=2.5).

### 3. Translucent UI Panel

The existing `CanvasLayer` UI panel gets a semi-transparent/frosted background so the 3D scene
is visible behind it. Enough opacity to maintain text readability, enough transparency to feel
like a floating HUD over the scene.

### 4. Scene Tree Changes

```
MainMenu (Node3D)
  CameraRig (Node3D)          <- NEW: orbits on Y
    Camera3D                   <- MOVED: child of rig, bobs up/down
  TitleGroup (Node3D)          <- CHANGED: stationary (no rotation)
    LetterP..W (MeshInstance3D)
    SubtitleLabel (Label3D)
  GroundPlane (MeshInstance3D)  <- NEW: PlaneMesh + dot grid shader
  DirectionalLight3D
  WorldEnvironment
  UILayer (CanvasLayer)        <- CHANGED: translucent panel background
```

### 5. Script Changes

**MainMenuController.gd:**
- Remove `TitleGroup` rotation logic from `_process()`
- Add camera orbit logic: rotate `CameraRig` around Y
- Add elevation bob: modulate `Camera3D.position.y` on a different sine frequency
- Camera `look_at()` the origin each frame
- Expose all parameters as `@export` vars

### 6. New Files

| File | Purpose |
|------|---------|
| `shaders/retro_dot_grid.gdshader` | Procedural dot-grid shader |

### 7. Non-Goals

- No terrain undulation (Option B from brainstorming — potential future enhancement)
- No particle-based dots
- No changes to the title letter materials or glow
- No changes to scene transition / LAUNCH button behaviour
