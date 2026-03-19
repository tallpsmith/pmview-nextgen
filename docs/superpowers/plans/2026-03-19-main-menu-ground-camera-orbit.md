# Main Menu Ground & Camera Orbit Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the self-rotating title with a camera orbit, add a retro dot-grid ground plane, and make the UI panel translucent.

**Architecture:** A CameraRig Node3D at the origin orbits around Y while its child Camera3D bobs vertically on a different frequency. A flat PlaneMesh with a procedural dot-grid shader sits below the stationary title. The existing CanvasLayer UI panel gets a semi-transparent background.

**Tech Stack:** GDScript, Godot Shader Language (GLSL-like), Godot 4.6 scene format

**Testing note:** This is pure visual/scene work — shaders, camera motion, and scene layout have no unit-testable surface. Each task includes "run in Godot editor and verify visually" steps instead.

---

## Chunk 1: Shader + Ground Plane

### Task 1: Create the retro dot-grid shader

**Files:**
- Create: `src/pmview-app/shaders/retro_dot_grid.gdshader`

- [ ] **Step 1: Create the shader file**

The shader uses world-space coordinates derived from `VERTEX` (via `MODEL_MATRIX`) so that dot spacing is mesh-size-independent — no fragile coupling between shader uniforms and PlaneMesh dimensions.

```glsl
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform float dot_spacing : hint_range(0.5, 10.0) = 2.0;
uniform float dot_radius : hint_range(0.005, 0.5) = 0.06;
uniform vec4 dot_color : source_color = vec4(0.0, 0.8, 0.3, 1.0);
uniform float fade_distance : hint_range(1.0, 50.0) = 18.0;

varying vec2 world_xz;

void vertex() {
    vec4 world_pos = MODEL_MATRIX * vec4(VERTEX, 1.0);
    world_xz = world_pos.xz;
}

void fragment() {
    // Tile into repeating grid cells in world space
    vec2 cell = fract(world_xz / dot_spacing) - 0.5;

    // Distance from cell centre — circle SDF
    float dist = length(cell);
    float dot = 1.0 - smoothstep(dot_radius - 0.005, dot_radius + 0.005, dist);

    // Radial fade from world origin
    float dist_from_centre = length(world_xz);
    float fade = 1.0 - smoothstep(fade_distance * 0.5, fade_distance, dist_from_centre);

    ALBEDO = dot_color.rgb;
    ALPHA = dot * fade * dot_color.a;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/shaders/retro_dot_grid.gdshader
git commit -m "Add retro dot-grid shader for main menu ground plane"
```

### Task 2: Add the GroundPlane to the scene

**Files:**
- Modify: `src/pmview-app/scenes/main_menu.tscn`

The ground plane is a `MeshInstance3D` with a `PlaneMesh` at Y=0, using the dot-grid shader. It needs to be large enough to fill the camera's view during orbit.

- [ ] **Step 1: Add shader ext_resource and sub_resources to the .tscn**

Add after the existing `ext_resource` entries (after id="4"). **Do not fabricate a UID** — omit it and let Godot assign one when the scene is next opened in the editor:

```
[ext_resource type="Shader" path="res://shaders/retro_dot_grid.gdshader" id="5"]
```

Add after the existing `sub_resource` entries (before the first `[node]`):

```
[sub_resource type="ShaderMaterial" id="ground_shader_mat"]
render_priority = -1
shader = ExtResource("5")
shader_parameter/dot_spacing = 2.0
shader_parameter/dot_radius = 0.06
shader_parameter/dot_color = Color(0, 0.8, 0.3, 1)
shader_parameter/fade_distance = 18.0

[sub_resource type="PlaneMesh" id="ground_plane_mesh"]
size = Vector2(60, 60)
```

- [ ] **Step 2: Add the GroundPlane node**

Add after the `DirectionalLight3D` node and before `Camera3D`:

```
[node name="GroundPlane" type="MeshInstance3D" parent="."]
mesh = SubResource("ground_plane_mesh")
surface_material_override/0 = SubResource("ground_shader_mat")
cast_shadow = 0
```

Position is `(0, 0, 0)` by default — below the title at `Y=2.5`.

- [ ] **Step 3: Open scene in Godot editor and verify**

Expected: A flat plane of green dots visible below the floating title letters. Dots should be widely spaced and fade toward the edges. Adjust `dot_spacing`, `dot_radius`, and `fade_distance` uniforms in the inspector if needed. Godot will assign UIDs to the ext_resource on save — let it.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scenes/main_menu.tscn
git commit -m "Add dot-grid ground plane to main menu scene"
```

## Chunk 2: Camera Rig + Orbit

### Task 3: Restructure Camera3D under a CameraRig

**Files:**
- Modify: `src/pmview-app/scenes/main_menu.tscn`

Introduce a `CameraRig` (Node3D) at the origin. Move Camera3D to be its child. The rig rotates on Y (orbit), while the camera's local position is set each frame by the script.

- [ ] **Step 1: Replace the Camera3D node with a CameraRig + child Camera3D**

Find the current Camera3D node in the .tscn:

```
[node name="Camera3D" type="Camera3D" parent="." unique_id=1337806946]
transform = Transform3D(1, 0, 0, 0, 0.9847534, 0.17395642, 0, -0.17395642, 0.9847534, 0, 3, 7.535516)
```

Replace with (preserve Camera3D's `unique_id`; the initial transform will be overwritten by the script on the first frame but keeps the editor preview sensible):

```
[node name="CameraRig" type="Node3D" parent="."]

[node name="Camera3D" type="Camera3D" parent="CameraRig" unique_id=1337806946]
transform = Transform3D(1, 0, 0, 0, 0.9847534, 0.17395642, 0, -0.17395642, 0.9847534, 0, 3, 7.535516)
```

- [ ] **Step 2: Move SubtitleLabel under TitleGroup with billboard mode**

The `SubtitleLabel` (Label3D) is currently a child of root at Y=0.8. Since the camera orbits 360°, a single-sided Label3D would vanish from behind. Use `billboard = 1` (Y-billboard) so the text always faces the camera. Move it under `TitleGroup` and adjust the Y offset (TitleGroup is at Y=2.5, so relative Y = 0.8 - 2.5 = -1.7).

**Preserve all existing properties** — `unique_id`, `pixel_size`, `modulate`, `font`, `font_size`.

Find:
```
[node name="SubtitleLabel" type="Label3D" parent="." unique_id=628825361]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.8, 0)
pixel_size = 0.01
modulate = Color(0.5, 0.55, 0.7, 0.7)
text = "PERFORMANCE  CO-PILOT  VISUALISER"
font = ExtResource("2")
font_size = 24
```

Replace with:
```
[node name="SubtitleLabel" type="Label3D" parent="TitleGroup" unique_id=628825361]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -1.7, 0)
pixel_size = 0.01
modulate = Color(0.5, 0.55, 0.7, 0.7)
billboard = 1
text = "PERFORMANCE  CO-PILOT  VISUALISER"
font = ExtResource("2")
font_size = 24
```

- [ ] **Step 3: Open scene in Godot editor and verify**

Expected: Scene looks identical to before from the default camera angle — camera hasn't moved, just reparented. Title and subtitle visible. Ground dots still there.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scenes/main_menu.tscn
git commit -m "Reparent Camera3D under CameraRig, move SubtitleLabel into TitleGroup with billboard"
```

### Task 4: Implement camera orbit and elevation bob in GDScript

**Files:**
- Modify: `src/pmview-app/scripts/MainMenuController.gd`

Replace the title rotation with camera rig orbit + elevation modulation.

- [ ] **Step 1: Update the script**

Replace the entire contents of `MainMenuController.gd` with:

```gdscript
extends Node3D

## Main menu controller — orbits the camera around the 3D title,
## modulates camera elevation on a different frequency, handles
## connection form submission, and drives the KITT scanner hover effect.

# --- Camera orbit ---
@export_group("Camera Orbit")
@export var orbit_speed := 0.3           ## Orbit angular velocity (rad/s)
@export var orbit_radius := 7.5          ## Distance from camera to origin

@export_group("Camera Elevation")
@export var elevation_min := 1.5         ## Lowest camera Y during bob
@export var elevation_max := 4.5         ## Highest camera Y during bob
@export var elevation_speed := 0.2       ## Elevation oscillation speed (rad/s)

@onready var camera_rig: Node3D = $CameraRig
@onready var camera: Camera3D = $CameraRig/Camera3D
@onready var endpoint_input: LineEdit = %EndpointInput
@onready var launch_panel: Panel = %LaunchPanel
@onready var kitt_rect: ColorRect = %KittRect

var _sweep_tween: Tween = null
var _orbit_angle := 0.0
var _elevation_angle := 0.0


func _ready() -> void:
	launch_panel.mouse_entered.connect(_on_launch_hover)
	launch_panel.mouse_exited.connect(_on_launch_unhover)
	launch_panel.gui_input.connect(_on_launch_gui_input)


func _process(delta: float) -> void:
	_update_camera_orbit(delta)


func _update_camera_orbit(delta: float) -> void:
	# Orbit around Y axis
	_orbit_angle += orbit_speed * delta
	camera_rig.rotation.y = _orbit_angle

	# Elevation bob on a different frequency
	_elevation_angle += elevation_speed * delta
	var elevation_t := (sin(_elevation_angle) + 1.0) * 0.5  # 0..1
	var cam_y := lerpf(elevation_min, elevation_max, elevation_t)

	# Position camera at orbit radius, looking at origin
	camera.position = Vector3(0.0, cam_y, orbit_radius)
	camera.look_at(Vector3.ZERO, Vector3.UP)


# --- LAUNCH button hover: KITT scanner effect ---

func _on_launch_hover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return

	mat.set_shader_parameter("intensity", 1.0)

	_kill_sweep_tween()
	_sweep_tween = create_tween().set_loops()
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", 1.4, 0.9)
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", -0.4, 0.9)


func _on_launch_unhover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return

	_kill_sweep_tween()

	var fade := create_tween()
	fade.tween_property(mat, "shader_parameter/intensity", 0.0, 0.3)


func _kill_sweep_tween() -> void:
	if _sweep_tween and _sweep_tween.is_valid():
		_sweep_tween.kill()
		_sweep_tween = null


# --- LAUNCH button press ---

func _on_launch_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_launch()


func _launch() -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:44322"
	SceneManager.go_to_loading({"endpoint": url, "mode": "live"})
```

- [ ] **Step 2: Open in Godot editor, run the scene (F6), and verify**

Expected:
- Camera orbits smoothly around the title at ~21s per revolution
- Camera bobs up and down on a ~31s cycle (different from orbit)
- Title letters stay stationary — all motion is from the camera
- Ground dots provide spatial context as the camera moves
- Subtitle always faces the camera (billboard mode)

- [ ] **Step 3: Tune @export parameters in the inspector**

Play with `orbit_speed`, `elevation_min`, `elevation_max`, `elevation_speed` until the feel is right. The defaults are a starting point.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/MainMenuController.gd
git commit -m "Camera orbits title with elevation modulation, title no longer self-rotates"
```

## Chunk 3: Translucent UI Panel

### Task 5: Make the UI panel translucent

**Files:**
- Modify: `src/pmview-app/scenes/main_menu.tscn`

The existing `launch_panel_style` `StyleBoxFlat` has `bg_color = Color(0.08, 0.06, 0.12, 0.9)`. We need a translucent backdrop behind the form controls so the 3D scene shows through.

Wrap the existing `CenterContainer` inside a `PanelContainer` that draws the translucent background. `CenterContainer` doesn't paint `panel` style overrides (only `PanelContainer` does), so we need a proper wrapper. The `PanelContainer` gets the existing anchors/offsets; `CenterContainer` becomes a fill-parent child that centres the `VBoxContainer` within.

- [ ] **Step 1: Add a translucent StyleBoxFlat sub_resource**

Add a new sub_resource after the existing `launch_panel_style`:

```
[sub_resource type="StyleBoxFlat" id="ui_panel_bg"]
bg_color = Color(0.04, 0.03, 0.08, 0.65)
border_width_left = 1
border_width_top = 1
border_width_right = 1
border_width_bottom = 1
border_color = Color(0.514, 0.22, 0.925, 0.3)
corner_radius_top_left = 8
corner_radius_top_right = 8
corner_radius_bottom_right = 8
corner_radius_bottom_left = 8
content_margin_left = 24.0
content_margin_top = 24.0
content_margin_right = 24.0
content_margin_bottom = 24.0
```

- [ ] **Step 2: Wrap CenterContainer inside a PanelContainer**

The current `CenterContainer` has anchors/offsets that position it at bottom-centre. Move those to a new `PanelContainer` parent and let `CenterContainer` fill it.

Find the `CenterContainer` node:

```
[node name="CenterContainer" type="CenterContainer" parent="CanvasLayer/Control" unique_id=1773607422]
layout_mode = 1
anchors_preset = 7
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -240.0
offset_top = -340.0
offset_right = 240.0
offset_bottom = -40.0
grow_horizontal = 2
grow_vertical = 0
```

Replace with a `PanelContainer` wrapper (gets the anchors/offsets) and a nested `CenterContainer` (fills parent):

```
[node name="UIPanel" type="PanelContainer" parent="CanvasLayer/Control"]
layout_mode = 1
anchors_preset = 7
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -264.0
offset_top = -388.0
offset_right = 264.0
offset_bottom = -16.0
grow_horizontal = 2
grow_vertical = 0
theme_override_styles/panel = SubResource("ui_panel_bg")

[node name="CenterContainer" type="CenterContainer" parent="CanvasLayer/Control/UIPanel" unique_id=1773607422]
layout_mode = 2
```

Also update the `VBoxContainer` parent path from `CanvasLayer/Control/CenterContainer` to `CanvasLayer/Control/UIPanel/CenterContainer`. All children of VBoxContainer keep their existing parent path relative to VBoxContainer — only the VBoxContainer's own `parent` attribute changes:

Find: `parent="CanvasLayer/Control/CenterContainer"`
Replace: `parent="CanvasLayer/Control/UIPanel/CenterContainer"`

And all nodes that reference `CanvasLayer/Control/CenterContainer/VBoxContainer` in their parent path:

Find: `parent="CanvasLayer/Control/CenterContainer/VBoxContainer`
Replace: `parent="CanvasLayer/Control/UIPanel/CenterContainer/VBoxContainer`

(This applies to: EndpointLabel, EndpointInput, HSeparator, ModeLabel, ModeButtons, HSeparator2, LaunchPanel, VersionLabel, and children of ModeButtons and LaunchPanel.)

- [ ] **Step 3: Open in Godot editor and verify**

Expected: The UI panel area has a dark, semi-transparent background with a subtle purple border. The 3D scene (ground dots, title glow) is visible through it. Text and controls remain legible. The form fields are centred within the translucent panel.

Tune `bg_color` alpha (0.65) up or down — higher = more opaque/readable, lower = more see-through.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scenes/main_menu.tscn
git commit -m "Make UI panel translucent so 3D scene shows through"
```

### Task 6: Final polish and verification

**Files:**
- Possibly tweak: `src/pmview-app/scenes/main_menu.tscn`, `src/pmview-app/scripts/MainMenuController.gd`, `src/pmview-app/shaders/retro_dot_grid.gdshader`

- [ ] **Step 1: Run the full scene in Godot and do a visual review**

Check:
- Camera orbit is smooth and continuous
- Elevation bob creates varied viewing angles
- Ground dots look right from all camera positions (not too dense, not too sparse)
- Dots fade naturally at edges (no hard cutoff visible)
- UI panel is readable but translucent
- LAUNCH button KITT scanner effect still works
- Subtitle text always faces camera during orbit (billboard)
- No z-fighting between ground plane and other elements
- Scene transition to loading still works when LAUNCH is clicked

- [ ] **Step 2: Adjust any shader uniforms, export vars, or style values**

This is the tweaking pass — adjust in the inspector until it feels right.

- [ ] **Step 3: Final commit if any tweaks were made**

```bash
git add -u
git commit -m "Polish main menu ground + camera orbit values"
```
