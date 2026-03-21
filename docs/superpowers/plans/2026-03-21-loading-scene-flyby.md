# Loading Scene Fly-By Camera Transition — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the loading scene's scale-up exit animation with a Battlezone-style ground grid and cinematic banking fly-by camera that accelerates into a hyperspace transition.

**Architecture:** Three independent changes layered together: (1) ground shader added to loading scene, (2) phase delay halved via scene property, (3) fly-by camera animation replaces the zoom-exit in LoadingController.gd. The existing `retro_dot_grid.gdshader` gains `speed` and `streak_angle` uniforms for the hyperspace stretching effect.

**Tech Stack:** GDScript, Godot Shader Language (GLSL-like), Godot 4.6 scene format (.tscn)

**Testing note:** These are GDScript scene/shader changes — no xUnit tests apply. Each task includes a visual verification step the user performs in the Godot editor. Build verification via `dotnet build pmview-nextgen.ci.slnf` confirms no C# breakage.

**Spec:** `docs/superpowers/specs/2026-03-21-loading-scene-flyby-design.md`

---

## Chunk 1: Ground Shader and Phase Delay

### Task 1: Add speed/streak uniforms to retro_dot_grid.gdshader

**Files:**
- Modify: `src/pmview-app/shaders/retro_dot_grid.gdshader`

This is backwards-compatible — `speed = 0.0` preserves current behaviour for the main menu scene.

- [ ] **Step 1: Add the two new uniforms**

Add after the existing `fade_distance` uniform (line 7):

```glsl
uniform float speed : hint_range(0.0, 1.0) = 0.0;
uniform float streak_angle : hint_range(0.0, 6.28) = 0.0;
```

- [ ] **Step 2: Modify the fragment function to support directional stretching**

Replace the cell calculation and Chebyshev distance block (lines 18-22) with:

```glsl
    // Tile into repeating grid cells in world space
    vec2 cell = fract(world_xz / dot_spacing) - 0.5;

    // Rotate cell by streak_angle, then compress along rotated Y to stretch dots
    float sa = sin(streak_angle);
    float ca = cos(streak_angle);
    vec2 rotated = vec2(ca * cell.x + sa * cell.y, -sa * cell.x + ca * cell.y);
    rotated.y /= max(1.0 - speed * 0.95, 0.05);

    // Chebyshev distance from cell centre — square/pixel SDF (stretches into streaks)
    float dist = max(abs(rotated.x), abs(rotated.y));
    float dot = 1.0 - smoothstep(dot_size - 0.003, dot_size + 0.003, dist);
```

- [ ] **Step 3: Verify main menu is unaffected**

Run the Godot project. Navigate to the main menu. The retro dot grid should look identical — square dots, neon green, no stretching. The new uniforms default to `speed = 0.0` which produces no visible change.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/shaders/retro_dot_grid.gdshader
git commit -m "Add speed/streak_angle uniforms to retro dot grid shader

Backwards-compatible stretching for hyperspace effect in loading scene.
Default speed=0.0 preserves existing square-dot appearance."
```

---

### Task 2: Add ground plane and phase delay to loading.tscn

**Files:**
- Modify: `src/pmview-app/scenes/loading.tscn`

- [ ] **Step 1a: Add the ext_resource for the retro dot grid shader**

Add at the top of `loading.tscn`, after the existing `ext_resource` lines (after line 6 — the materialise shader ext_resource). This MUST go with the other `ext_resource` declarations, before any `sub_resource` blocks:

```ini
[ext_resource type="Shader" path="res://shaders/retro_dot_grid.gdshader" id="5"]
```

(Uses `id="5"` — next sequential after the existing IDs 1-4. Godot will assign a `uid=` on first editor open.)

- [ ] **Step 1b: Add the ground shader sub-resources**

Add after the existing `mat_W` sub-resource block (after line 111) in `loading.tscn`:

```ini
[sub_resource type="ShaderMaterial" id="loading_ground_mat"]
render_priority = -1
shader = ExtResource("5")
shader_parameter/dot_spacing = 2.0
shader_parameter/dot_size = 0.08
shader_parameter/dot_color = Color(0, 0.8, 0.3, 1)
shader_parameter/fade_distance = 40.0
shader_parameter/speed = 0.0
shader_parameter/streak_angle = 0.0

[sub_resource type="PlaneMesh" id="loading_ground_mesh"]
size = Vector2(120, 120)
```

- [ ] **Step 2: Add the GroundPlane node**

Add after the `Camera3D` node (after line 121) in the node tree:

```ini
[node name="GroundPlane" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -1.2, 0)
mesh = SubResource("loading_ground_mesh")
surface_material_override/0 = SubResource("loading_ground_mat")
cast_shadow = 0
```

- [ ] **Step 3: Add the WhiteOut ColorRect**

Add inside the existing `CanvasLayer/Control` node (after the `StatusLabel` node, after line 181):

```ini
[node name="WhiteOut" type="ColorRect" parent="CanvasLayer/Control"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
color = Color(1, 1, 1, 1)
modulate = Color(1, 1, 1, 0)
mouse_filter = 2
```

- [ ] **Step 4: Add MinPhaseDelayMs override to the LoadingPipeline node**

Find the `LoadingPipeline` node (line 122-124) and add the property override:

```ini
[node name="LoadingPipeline" type="Node" parent="." unique_id=1377308617]
unique_name_in_owner = true
script = ExtResource("2")
MinPhaseDelayMs = 250
```

- [ ] **Step 5: Verify in Godot**

Run the loading scene. You should see:
- Neon green dot grid beneath the letters
- Letters materialise faster (250ms minimum between phases instead of 500ms)
- White-out rect is invisible (modulate alpha = 0)
- The old zoom-exit still works (we haven't replaced it yet)

- [ ] **Step 6: Commit**

```bash
git add src/pmview-app/scenes/loading.tscn
git commit -m "Add ground plane and halve phase delay in loading scene

Battlezone-style retro dot grid at Y=-1.2 beneath letters.
MinPhaseDelayMs reduced from 500 to 250 for snappier reveals."
```

---

## Chunk 2: Fly-By Camera Animation

### Task 3: Replace zoom-exit with fly-by camera animation

**Files:**
- Modify: `src/pmview-app/scripts/LoadingController.gd`

This is the big one. We replace `_on_pipeline_completed()` with the three-phase fly-by sequence.

**Dependency:** Task 2 must be complete before testing this — the `@onready` refs for `$GroundPlane` and `%WhiteOut` require those nodes to exist in the scene.

- [ ] **Step 1: Add new @onready references and constants**

Add after the existing `@onready var letters` line (line 9):

```gdscript
@onready var camera: Camera3D = $Camera3D
@onready var ground_mat: ShaderMaterial = $GroundPlane.get_surface_override_material(0)
@onready var white_out: ColorRect = %WhiteOut
```

Add after the `PHASE_NAMES` constant (after line 18, noting that line numbers below refer to the original file before any edits in this task):

```gdscript
# Fly-by camera path constants
const FLYBY_PAUSE := 0.3
const FLYBY_APPROACH_DURATION := 0.8
const FLYBY_SWEEP_DURATION := 1.2
const FLYBY_HYPERSPACE_DURATION := 0.5

# Camera positions for the fly-by phases
const CAM_START := Vector3(0.0, 0.18, 6.11)
const CAM_APPROACH_END := Vector3(-5.625, -0.12, 1.5)   # Alongside "P", dipped, forward of letters
const CAM_SWEEP_END := Vector3(12.0, -0.12, 1.5)        # Well past "W"
const CAM_HYPERSPACE_END := Vector3(20.0, -0.12, -5.0)  # Forward and right, into the distance

# Camera rotations (Euler angles in radians)
const ROT_START := Vector3(0.0, 0.0, 0.0)                        # Looking straight ahead
const ROT_APPROACH_PEAK := Vector3(-0.05, -0.6, -0.26)           # Banked left, slight pitch down, rolled ~15°
const ROT_APPROACH_END := Vector3(-0.05, -1.2, 0.0)              # Facing left along letter line, level
const ROT_SWEEP := Vector3(-0.05, -1.4, 0.0)                     # Slightly more left-facing during sweep
const ROT_HYPERSPACE := Vector3(-0.05, -1.0, 0.0)                # Straightening out forward
```

- [ ] **Step 2: Replace _on_pipeline_completed with the fly-by sequence**

Replace the entire `_on_pipeline_completed()` function (find by name — original lines 59-74, but shifted down after Step 1's insertions) with:

```gdscript
func _on_pipeline_completed() -> void:
	status_label.text = "READY"

	# Brief pause to admire the completed word
	await get_tree().create_timer(FLYBY_PAUSE).timeout
	status_label.visible = false

	# Phase 1: Banking approach — arc and dip toward "P"
	await _flyby_banking_approach()

	# Phase 2: Accelerating sweep — lateral fly-by past all letters
	await _flyby_accelerating_sweep()

	# Phase 3: Hyperspace punch — streaks + white-out
	await _flyby_hyperspace_punch()

	# Transition to host view
	SceneManager.go_to_host_view(pipeline.BuiltScene)
```

- [ ] **Step 3: Implement Phase 1 — Banking Approach**

Add after `_on_pipeline_completed`:

```gdscript
func _flyby_banking_approach() -> void:
	var t := FLYBY_APPROACH_DURATION

	# Position: arc from start to alongside "P" with a dip
	var pos_tween := create_tween()
	pos_tween.tween_property(camera, "position", CAM_APPROACH_END, t) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

	# Rotation: bank into the turn (roll peaks mid-way), then level out
	var rot_tween := create_tween()
	rot_tween.tween_property(camera, "rotation", ROT_APPROACH_PEAK, t * 0.5) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	rot_tween.tween_property(camera, "rotation", ROT_APPROACH_END, t * 0.5) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)

	# Wait for both tweens to complete before next phase
	await pos_tween.finished
	if rot_tween.is_running():
		await rot_tween.finished
```

- [ ] **Step 4: Implement Phase 2 — Accelerating Sweep**

Add after `_flyby_banking_approach`:

```gdscript
func _flyby_accelerating_sweep() -> void:
	var t := FLYBY_SWEEP_DURATION

	# Position: lateral sweep with acceleration (ease-in = slow start, fast finish)
	var pos_tween := create_tween()
	pos_tween.tween_property(camera, "position", CAM_SWEEP_END, t) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Rotation: slight adjustment during sweep
	var rot_tween := create_tween()
	rot_tween.tween_property(camera, "rotation", ROT_SWEEP, t) \
		.set_trans(Tween.TRANS_LINEAR)

	# Ground shader: ramp speed for lateral movement (streak_angle stays at 0.0 = X-axis)
	var shader_tween := create_tween()
	shader_tween.tween_property(ground_mat, "shader_parameter/speed", 0.6, t) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Wait for the longest tween
	await pos_tween.finished
	if rot_tween.is_running():
		await rot_tween.finished
```

- [ ] **Step 5: Implement Phase 3 — Hyperspace Punch**

Add after `_flyby_accelerating_sweep`:

```gdscript
func _flyby_hyperspace_punch() -> void:
	var t := FLYBY_HYPERSPACE_DURATION

	# All effects run in parallel — position, shader, white-out
	var tween := create_tween().set_parallel(true)

	# Position: continue forward acceleration
	tween.tween_property(camera, "position", CAM_HYPERSPACE_END, t) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Rotation: straighten out (shorter duration, still parallel)
	tween.tween_property(camera, "rotation", ROT_HYPERSPACE, t * 0.3) \
		.set_trans(Tween.TRANS_LINEAR)

	# Ground shader: full streak, rotate angle toward forward (Z axis = PI/2)
	tween.tween_property(ground_mat, "shader_parameter/speed", 1.0, t) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	tween.tween_property(ground_mat, "shader_parameter/streak_angle", PI / 2.0, t)

	# White-out: fade to white
	tween.tween_property(white_out, "modulate:a", 1.0, t) \
		.set_trans(Tween.TRANS_EXPO).set_ease(Tween.EASE_IN)

	await tween.finished
```

- [ ] **Step 6: Verify the full sequence in Godot**

Run the loading scene end-to-end:
1. Letters materialise with snappier timing (250ms gaps)
2. Green dot grid visible beneath letters throughout
3. After "READY", brief pause
4. Camera banks and dips toward "P" — horizon tilts during the turn
5. Camera sweeps laterally past all letters with acceleration — letters whip past left side
6. Ground dots stretch into streaks, angle rotates, screen whites out
7. Scene transitions to host view

- [ ] **Step 7: Commit**

```bash
git add src/pmview-app/scripts/LoadingController.gd
git commit -m "Replace zoom-exit with banking fly-by camera sequence

Three-phase cinematic exit: banking approach with dip, accelerating
lateral sweep past letters, hyperspace punch with dot-streak white-out."
```

---

## Chunk 3: Polish and Tuning

### Task 4: Visual tuning pass

**Files:**
- Possibly adjust: `src/pmview-app/scripts/LoadingController.gd` (constants)
- Possibly adjust: `src/pmview-app/scenes/loading.tscn` (ground plane position, fade_distance)

This task is manual tuning in Godot. The constants defined in Task 3 are starting points — the user tests in-editor and adjusts values.

- [ ] **Step 1: Tune camera positions and rotations**

Play the loading scene repeatedly. Adjust the `CAM_*` and `ROT_*` constants in `LoadingController.gd` until the fly-by feels natural:

- **Banking approach**: Does the roll feel like an aircraft? Is 15° (`-0.26 rad`) enough or too much? Does the dip (Y: 0.18 → -0.12) feel like dropping into a run?
- **Sweep speed**: Does `TRANS_QUAD` / `EASE_IN` give enough acceleration contrast? Try `TRANS_CUBIC` for more dramatic acceleration.
- **Letter proximity**: Is Z = 1.5 close enough to the letter faces? Closer = more dramatic, but risk clipping through geometry.

- [ ] **Step 2: Tune ground shader streaking**

Check the dot stretching during the sweep and hyperspace phases:
- Do the dots stretch convincingly into speed lines?
- Does the `streak_angle` rotation from 0 → PI/2 look natural?
- Is `fade_distance = 40.0` enough, or does the grid visibly cut off during the fly-by? Increase if needed.

- [ ] **Step 3: Tune timing**

The total fly-by is ~2.8s. Adjust if needed:
- `FLYBY_PAUSE`: 0.3s — enough to register "READY"?
- `FLYBY_APPROACH_DURATION`: 0.8s — does the bank feel rushed or sluggish?
- `FLYBY_SWEEP_DURATION`: 1.2s — enough time for acceleration to build?
- `FLYBY_HYPERSPACE_DURATION`: 0.5s — does the white-out feel punchy or abrupt?

- [ ] **Step 4: Verify main menu is unaffected**

Navigate to the main menu and confirm the retro dot grid looks identical to before (no stretching, same neon green squares).

- [ ] **Step 5: Commit tuning adjustments**

```bash
git add src/pmview-app/scripts/LoadingController.gd src/pmview-app/scenes/loading.tscn
git commit -m "Tune fly-by camera timing and positions after visual testing"
```

---

### Task 5: Build verification

**Files:** None modified — verification only.

- [ ] **Step 1: Run CI build**

```bash
dotnet build pmview-nextgen.ci.slnf
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```

Expected: All green. The changes are GDScript/shader only — no C# source was modified, just a .tscn property override. This confirms no accidental breakage.

- [ ] **Step 2: Verify no untracked files**

```bash
git status
```

Ensure no unexpected files were created. The only changes should be:
- `src/pmview-app/shaders/retro_dot_grid.gdshader` (modified)
- `src/pmview-app/scenes/loading.tscn` (modified)
- `src/pmview-app/scripts/LoadingController.gd` (modified)
