# Multi-Host Fleet View — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fleet-wide monitoring view that arranges multiple hosts in an auto-grid, provides a patrol camera for scanning, and allows focusing on individual hosts via a holographic projection beam — with mock data first, live polling later.

**Architecture:** Scene-first approach. New `fleet_view.tscn` with `FleetViewController.gd` orchestrating the grid layout, two cameras (PatrolCamera + FocusCamera), and a holographic projection beam shader. `SceneManager.gd` gains `go_to_fleet_view()`. `MainMenuController.gd` gains multi-select. Two-tier polling (fleet aggregate + ephemeral detail) wired after the visual experience is proven.

**Tech Stack:** GDScript (scene logic, cameras, shaders), C# .NET 8.0 (MetricPoller, SceneBinder), Godot 4.6

**Testing note:** Scene/camera/shader work is GDScript — visual verification in Godot editor. Grid layout math and polling architecture have C#/xUnit test coverage. Build verification via `dotnet build pmview-nextgen.ci.slnf`.

**Spec:** `docs/superpowers/specs/2026-03-21-multi-host-fleet-view-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/pmview-app/scenes/fleet_view.tscn` | Fleet view scene — two cameras, grid container, master timestamp, HUD |
| `src/pmview-app/scripts/FleetViewController.gd` | Fleet scene orchestrator — grid layout, focus transitions, opacity management |
| `src/pmview-app/scripts/compact_host.gd` | CompactHost node — 4 bars + bezel + licence plate, click/selection support |
| `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/patrol_orbit_camera.gd` | Patrol camera — racetrack path, W/S throttle, arrow-key look |
| `src/pmview-app/shaders/holographic_beam.gdshader` | Projection beam shader — transparent with rising scan-lines |
| `src/pmview-app/scripts/holographic_beam.gd` | Beam geometry builder — truncated cuboid from floor to ceiling bounds |

### Modified Files

| File | Changes |
|------|---------|
| `src/pmview-app/scripts/SceneManager.gd` | Add `go_to_fleet_view()`, extend `connection_config` for multi-host |
| `src/pmview-app/scripts/MainMenuController.gd` | Multi-select on host dropdown, "ALL" button, fleet launch path |

---

## Chunk 1: Static Fleet Grid Scene

Build the fleet view scene with hardcoded mock CompactHosts. No cameras, no transitions — just hosts in a grid. Prove the visual before adding complexity.

### Task 1: Create CompactHost building block

**Files:**
- Create: `src/pmview-app/scripts/compact_host.gd`

The CompactHost is the miniature stand-in for a host: 4 bars in 2×2, a ground bezel, and a licence plate label.

- [ ] **Step 1: Create the CompactHost script**

```gdscript
# compact_host.gd
# Miniature host representation: 4 aggregate bars (CPU, Mem, Disk, Net)
# in a 2x2 grid with a ground bezel and licence plate label.
extends Node3D

const BAR_SCENE := preload("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn")

@export var hostname: String = "unknown":
	set(value):
		hostname = value
		if _label:
			_label.text = value

## Spacing between the 2x2 bar centres
@export var bar_spacing: float = 1.2

## Colours for each aggregate metric bar
const BAR_COLOURS := {
	"cpu": Color(0.2, 0.8, 0.2),       # green
	"memory": Color(0.8, 0.5, 0.2),    # orange
	"disk": Color(0.3, 0.5, 0.9),      # blue
	"network": Color(0.7, 0.4, 0.8),   # purple
}

## Mock heights (replaced by real polling later)
const MOCK_HEIGHTS := {
	"cpu": 0.6,
	"memory": 0.4,
	"disk": 0.3,
	"network": 0.2,
}

var _bars: Dictionary = {}
var _bezel: Node3D
var _label: Label3D
var _selected: bool = false

func _ready() -> void:
	_build_bars()
	_build_bezel()
	_build_label()

func _build_bars() -> void:
	var positions := {
		"cpu":     Vector3(-bar_spacing / 2.0, 0, -bar_spacing / 2.0),
		"memory":  Vector3( bar_spacing / 2.0, 0, -bar_spacing / 2.0),
		"disk":    Vector3(-bar_spacing / 2.0, 0,  bar_spacing / 2.0),
		"network": Vector3( bar_spacing / 2.0, 0,  bar_spacing / 2.0),
	}
	for metric_name: String in positions:
		var bar = BAR_SCENE.instantiate()  # untyped — grounded_shape.gd exposes colour/height
		bar.name = metric_name.capitalize()
		bar.position = positions[metric_name]
		bar.colour = BAR_COLOURS[metric_name]
		bar.height = MOCK_HEIGHTS[metric_name]
		add_child(bar)
		_bars[metric_name] = bar

func _build_bezel() -> void:
	# GroundBezel extends MeshInstance3D — must instantiate correctly
	_bezel = MeshInstance3D.new()
	_bezel.name = "Bezel"
	_bezel.set_script(load("res://addons/pmview-bridge/building_blocks/ground_bezel.gd"))
	add_child(_bezel)
	# Set properties AFTER add_child so _ready() has fired
	_bezel.bezel_colour = Color(0.2, 0.2, 0.25, 1.0)
	var extent := bar_spacing + 1.2  # bar width + padding
	_bezel.resize(extent, extent)

func _build_label() -> void:
	_label = Label3D.new()
	_label.name = "LicencePlate"
	_label.text = hostname
	_label.font_size = 32
	_label.pixel_size = 0.01
	_label.position = Vector3(0, 0.05, (bar_spacing / 2.0) + 0.8)
	_label.rotation_degrees = Vector3(-90, 0, 0)  # flat on ground
	_label.modulate = Color(0.7, 0.7, 0.7)
	var font := load("res://assets/fonts/PressStart2P-Regular.ttf")
	if font:
		_label.font = font
		_label.font_size = 24
	add_child(_label)

## Update a single metric bar height (called by poller later)
func set_metric_value(metric_name: String, value: float) -> void:
	if _bars.has(metric_name):
		_bars[metric_name].height = value

## Set opacity for translucent mode during focus
func set_opacity(alpha: float) -> void:
	for bar: Node3D in _bars.values():
		bar.modulate.a = alpha
	if _bezel:
		_bezel.modulate.a = alpha
	if _label:
		_label.modulate.a = alpha

## Get the footprint extent (width, depth) for grid spacing
func get_footprint() -> Vector2:
	var extent := bar_spacing + 1.2
	return Vector2(extent, extent)
```

- [ ] **Step 2: Verify it loads in a test scene**

Create a throwaway test — open Godot, create a new scene with a Node3D root, attach a script that does:
```gdscript
var ch = load("res://scripts/compact_host.gd").new()
ch.hostname = "test-host-01"
add_child(ch)
```
Confirm 4 coloured bars appear in a 2×2 with a bezel and label. Delete the test scene.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/compact_host.gd
git commit -m "Add CompactHost building block for fleet view

Four aggregate bars (CPU/Mem/Disk/Net) in 2x2 with bezel and licence plate."
```

---

### Task 2: Create fleet_view.tscn and FleetViewController

**Files:**
- Create: `src/pmview-app/scenes/fleet_view.tscn`
- Create: `src/pmview-app/scripts/FleetViewController.gd`

The fleet scene starts minimal — just the environment, a static camera, and the grid of CompactHosts. Patrol camera comes in Task 4.

- [ ] **Step 1: Create FleetViewController.gd**

```gdscript
# FleetViewController.gd
# Orchestrates the fleet grid view: arranges CompactHosts in an auto-grid,
# manages focus transitions, and coordinates cameras.
extends Node3D

const CompactHostScript := preload("res://scripts/compact_host.gd")

@onready var fleet_grid: Node3D = %FleetGrid
@onready var patrol_camera: Camera3D = %PatrolCamera
@onready var master_timestamp: Label3D = %MasterTimestamp

## Spacing between host grid cells (centre to centre)
@export var host_spacing: float = 6.0

var _hosts: Array[Node3D] = []
var _grid_columns: int = 0
var _grid_bounds: Rect2 = Rect2()

func _ready() -> void:
	var config: Dictionary = SceneManager.connection_config
	var hostnames: PackedStringArray = config.get("hostnames", PackedStringArray())
	if hostnames.is_empty():
		# Fallback: mock data for development
		hostnames = _generate_mock_hostnames(12)
	_build_grid(hostnames)
	_position_master_timestamp()

func _generate_mock_hostnames(count: int) -> PackedStringArray:
	var names := PackedStringArray()
	for i in range(count):
		names.append("host-%02d" % (i + 1))
	return names

func _build_grid(hostnames: PackedStringArray) -> void:
	var count := hostnames.size()
	_grid_columns = ceili(sqrt(float(count)))
	var grid_rows := ceili(float(count) / float(_grid_columns))

	# Centre the grid on the origin
	var total_width := (_grid_columns - 1) * host_spacing
	var total_depth := (grid_rows - 1) * host_spacing
	var offset := Vector3(-total_width / 2.0, 0, -total_depth / 2.0)

	for i in range(count):
		var col := i % _grid_columns
		var row := i / _grid_columns
		var host_node := Node3D.new()
		host_node.set_script(CompactHostScript)
		host_node.hostname = hostnames[i]
		host_node.position = offset + Vector3(col * host_spacing, 0, row * host_spacing)
		host_node.name = "CompactHost_%d" % i
		fleet_grid.add_child(host_node)
		_hosts.append(host_node)

	_grid_bounds = Rect2(
		Vector2(offset.x, offset.z),
		Vector2(total_width, total_depth)
	)

func _position_master_timestamp() -> void:
	if not master_timestamp:
		return
	var centre := Vector3(
		_grid_bounds.position.x + _grid_bounds.size.x / 2.0,
		15.0,  # float well above the grid
		_grid_bounds.position.y + _grid_bounds.size.y / 2.0,
	)
	master_timestamp.position = centre
	master_timestamp.text = "2026-03-21 14:32:00"  # mock timestamp

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		_handle_esc()

## Double-ESC to return to main menu (matches host view pattern)
var _esc_pressed_at: float = 0.0
const ESC_DOUBLE_PRESS_WINDOW := 2.0

func _handle_esc() -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now - _esc_pressed_at < ESC_DOUBLE_PRESS_WINDOW:
		SceneManager.go_to_main_menu()
	else:
		_esc_pressed_at = now
		# TODO: Show "Press ESC again to return to menu" hint
```

- [ ] **Step 2: Create fleet_view.tscn**

```ini
[gd_scene load_steps=3 format=3 uid="uid://fleet_view"]

[ext_resource type="Script" path="res://scripts/FleetViewController.gd" id="1"]
[ext_resource type="FontFile" path="res://assets/fonts/PressStart2P-Regular.ttf" id="2"]

[node name="FleetView" type="Node3D"]
script = ExtResource("1")

[node name="WorldEnvironment" type="WorldEnvironment" parent="."]
environment = SubResource("1")

[sub_resource type="Environment" id="1"]
background_mode = 1
background_color = Color(0.05, 0.03, 0.1, 1)
ambient_light_color = Color(0.15, 0.12, 0.2, 1)
ambient_light_energy = 0.3

[node name="DirectionalLight3D" type="DirectionalLight3D" parent="."]
transform = Transform3D(1, 0, 0, 0, -0.7, 0.7, 0, -0.7, -0.7, 0, 20, 0)
light_color = Color(0.6, 0.55, 0.8, 1)
light_energy = 0.6

[node name="PatrolCamera" type="Camera3D" parent="."]
unique_name_in_owner = true
transform = Transform3D(1, 0, 0, 0, 0.7, 0.7, 0, -0.7, 0.7, 0, 25, 30)
current = true

[node name="FocusCamera" type="Camera3D" parent="."]
unique_name_in_owner = true
script = ExtResource("fly_orbit_camera_script")
current = false

[node name="FleetGrid" type="Node3D" parent="."]
unique_name_in_owner = true

[node name="MasterTimestamp" type="Label3D" parent="."]
unique_name_in_owner = true
billboard = 1
font = ExtResource("2")
font_size = 48
pixel_size = 0.02
modulate = Color(1, 0.6, 0, 1)

[node name="HUD" type="CanvasLayer" parent="."]

[node name="EscHint" type="Label" parent="HUD"]
offset_left = 20
offset_top = 20
text = "ESC ESC → Main Menu"
```

Note: The `.tscn` above is a starting template. The `WorldEnvironment` sub_resource and exact light transforms may need tweaking to match `host_view.tscn`'s look. Fine-tune in the Godot editor.

- [ ] **Step 3: Verify grid renders in Godot**

Open the project in Godot. Open `fleet_view.tscn`. Run it (F5 or set as main scene temporarily). You should see 12 CompactHosts arranged in a 4×3 grid with licence plates, an orange timestamp billboard floating above, and a static camera looking down.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scenes/fleet_view.tscn src/pmview-app/scripts/FleetViewController.gd
git commit -m "Add fleet view scene with auto-grid of mock CompactHosts

Static camera, 12 mock hosts in 4x3 grid, master timestamp billboard."
```

---

### Task 3: Wire SceneManager and MainMenu for fleet navigation

**Files:**
- Modify: `src/pmview-app/scripts/SceneManager.gd`
- Modify: `src/pmview-app/scripts/MainMenuController.gd`

- [ ] **Step 1: Add go_to_fleet_view() to SceneManager**

Add after the existing `go_to_host_view()` method (after line 18 in `SceneManager.gd`):

```gdscript
func go_to_fleet_view(config: Dictionary) -> void:
	connection_config = config
	get_tree().change_scene_to_file("res://scenes/fleet_view.tscn")
```

- [ ] **Step 2: Add "ALL" button to MainMenuController**

In `MainMenuController.gd`, add an "ALL" button handler. The exact UI placement depends on the existing layout, but the launch logic is:

After the existing `_launch()` method, add a fleet launch path. Modify `_launch()` to check for multi-host selection:

```gdscript
func _launch_fleet(hostnames: PackedStringArray) -> void:
	var url: String = endpoint_input.text.strip_edges()
	if url.is_empty():
		return
	var config := {
		"endpoint": url,
		"mode": "archive" if archive_button.button_pressed else "live",
		"hostnames": hostnames,
		"verbose_logging": verbose_check.button_pressed,
	}
	if archive_button.button_pressed:
		config["start_time"] = start_time_input.text
		config["archive_start_epoch"] = _archive_start_epoch
		config["archive_end_epoch"] = _archive_end_epoch
	SceneManager.go_to_fleet_view(config)
```

For the "ALL" button, add an `@onready` reference and connect its `pressed` signal to launch fleet mode with all discovered hostnames:

```gdscript
@onready var all_hosts_button: Button = %AllHostsButton

func _on_all_hosts_pressed() -> void:
	if _discovered_hostnames.is_empty():
		return
	_launch_fleet(PackedStringArray(_discovered_hostnames))
```

Store discovered hostnames during `_fetch_hostnames()`:

```gdscript
var _discovered_hostnames: Array[String] = []
```

Populate it in the existing hostname fetch callback (where the OptionButton is populated).

- [ ] **Step 3: Add the AllHostsButton to main_menu.tscn**

Add a Button node named `AllHostsButton` with `unique_name_in_owner = true` in the archive panel area. Label it "ALL HOSTS". Position it near the existing host dropdown.

- [ ] **Step 4: Verify navigation flow**

1. Open Godot, run the project
2. On the main menu, enter a pmproxy endpoint (or leave default)
3. Switch to archive mode — hosts should populate in dropdown
4. Click "ALL HOSTS" button
5. Fleet view should load with the grid of CompactHosts
6. Double-ESC should return to main menu

- [ ] **Step 5: Commit**

```bash
git add src/pmview-app/scripts/SceneManager.gd src/pmview-app/scripts/MainMenuController.gd src/pmview-app/scenes/main_menu.tscn
git commit -m "Wire fleet view navigation from main menu

SceneManager gains go_to_fleet_view(). ALL HOSTS button launches fleet
mode with all discovered hostnames."
```

---

## Chunk 2: Patrol Camera

Build the racetrack patrol camera. This is the guard's legs — the core navigation experience.

### Task 4: Create patrol_orbit_camera.gd

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/patrol_orbit_camera.gd`

- [ ] **Step 1: Create the patrol camera script**

```gdscript
# patrol_orbit_camera.gd
# Racetrack patrol camera for fleet view.
# Travels along a rounded-rectangle path around the grid,
# looking inward at the grid centroid.
extends Camera3D

enum Mode { PATROL, FLYING_TO_FOCUS }

## Speed along the racetrack in units/second
@export var patrol_speed: float = 8.0
## Camera height above the grid
@export var orbit_height: float = 25.0
## Margin around the grid bounds for the racetrack
@export var racetrack_margin: float = 10.0
## Corner radius for the rounded rectangle
@export var corner_radius: float = 5.0
## Look-around sensitivity (arrow keys)
@export var keyboard_look_speed: float = 60.0
## Seconds before look override eases back
@export var look_ease_back_timeout: float = 5.0

var _mode: Mode = Mode.PATROL
var _grid_bounds: Rect2 = Rect2()
var _grid_centroid: Vector3 = Vector3.ZERO
var _racetrack_points: PackedVector3Array = PackedVector3Array()
var _racetrack_length: float = 0.0
var _path_progress: float = 0.0  # 0.0 to 1.0, normalised position on path
var _speed_multiplier: float = 1.0  # W/S throttle
var _look_offset: Vector2 = Vector2.ZERO  # arrow key look override
var _look_override_time: float = 0.0
var _input_enabled: bool = true

# Flying-to-focus state
var _fly_start_pos: Vector3
var _fly_start_basis: Basis
var _fly_target_pos: Vector3
var _fly_target_look: Vector3
var _fly_elapsed: float = 0.0
const FLYTO_DURATION := 1.5

func setup(grid_bounds: Rect2) -> void:
	_grid_bounds = grid_bounds
	_grid_centroid = Vector3(
		grid_bounds.position.x + grid_bounds.size.x / 2.0,
		0,
		grid_bounds.position.y + grid_bounds.size.y / 2.0
	)
	_build_racetrack()
	# Start at the first point
	_path_progress = 0.0
	_apply_patrol_position()

func _build_racetrack() -> void:
	# Rounded rectangle around grid bounds + margin
	var rect := _grid_bounds.grow(racetrack_margin)
	var r := minf(corner_radius, minf(rect.size.x, rect.size.y) / 2.0)
	var points := PackedVector3Array()
	var segments_per_corner := 8

	# Four corners: top-right, top-left, bottom-left, bottom-right
	var corners := [
		Vector2(rect.end.x - r, rect.end.y - r),    # top-right (max x, max z)
		Vector2(rect.position.x + r, rect.end.y - r), # top-left
		Vector2(rect.position.x + r, rect.position.y + r), # bottom-left
		Vector2(rect.end.x - r, rect.position.y + r), # bottom-right
	]

	for ci in range(4):
		var centre := corners[ci]
		var start_angle := ci * PI / 2.0
		for si in range(segments_per_corner + 1):
			var angle := start_angle + (PI / 2.0) * float(si) / float(segments_per_corner)
			var px := centre.x + r * cos(angle)
			var pz := centre.y + r * sin(angle)
			points.append(Vector3(px, orbit_height, pz))

	_racetrack_points = points
	# Calculate total path length
	_racetrack_length = 0.0
	for i in range(points.size()):
		var next_i := (i + 1) % points.size()
		_racetrack_length += points[i].distance_to(points[next_i])

func _process(delta: float) -> void:
	match _mode:
		Mode.PATROL:
			_process_patrol(delta)
		Mode.FLYING_TO_FOCUS:
			_process_flying_to_focus(delta)

func _process_patrol(delta: float) -> void:
	if not _input_enabled:
		return

	# Advance along the racetrack
	var speed := patrol_speed * _speed_multiplier
	var distance := speed * delta
	_path_progress += distance / _racetrack_length
	_path_progress = fmod(_path_progress, 1.0)
	if _path_progress < 0.0:
		_path_progress += 1.0

	_apply_patrol_position()
	_process_look_override(delta)

func _apply_patrol_position() -> void:
	if _racetrack_points.is_empty():
		return
	# Interpolate position along the path
	var total_points := _racetrack_points.size()
	var exact_index := _path_progress * float(total_points)
	var idx := int(exact_index) % total_points
	var next_idx := (idx + 1) % total_points
	var frac := exact_index - float(int(exact_index))

	position = _racetrack_points[idx].lerp(_racetrack_points[next_idx], frac)

	# Look at grid centroid + look offset
	var look_target := _grid_centroid + Vector3(_look_offset.x, 0, _look_offset.y)
	look_at(look_target, Vector3.UP)

func _process_look_override(delta: float) -> void:
	# Ease back to centre if no arrow input for timeout period
	if _look_offset.length() > 0.01:
		_look_override_time += delta
		if _look_override_time > look_ease_back_timeout:
			_look_offset = _look_offset.lerp(Vector2.ZERO, delta * 2.0)
			if _look_offset.length() < 0.01:
				_look_offset = Vector2.ZERO
				_look_override_time = 0.0

func _unhandled_input(event: InputEvent) -> void:
	if not _input_enabled or _mode != Mode.PATROL:
		return

	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_W:
				_speed_multiplier = clampf(_speed_multiplier + 0.5, 0.0, 4.0)
			KEY_S:
				_speed_multiplier = clampf(_speed_multiplier - 0.5, -1.0, 4.0)
			KEY_UP:
				_look_offset.y -= keyboard_look_speed * 0.1
				_look_override_time = 0.0
			KEY_DOWN:
				_look_offset.y += keyboard_look_speed * 0.1
				_look_override_time = 0.0
			KEY_LEFT:
				_look_offset.x -= keyboard_look_speed * 0.1
				_look_override_time = 0.0
			KEY_RIGHT:
				_look_offset.x += keyboard_look_speed * 0.1
				_look_override_time = 0.0

## Initiate cinematic fly to a focus target
func fly_to_focus(target_pos: Vector3, look_at_pos: Vector3) -> void:
	_mode = Mode.FLYING_TO_FOCUS
	_fly_start_pos = position
	_fly_start_basis = global_transform.basis
	_fly_target_pos = target_pos
	_fly_target_look = look_at_pos
	_fly_elapsed = 0.0

func _process_flying_to_focus(delta: float) -> void:
	_fly_elapsed += delta
	var t := clampf(_fly_elapsed / FLYTO_DURATION, 0.0, 1.0)
	# Smooth ease-in-out
	var smooth_t := t * t * (3.0 - 2.0 * t)

	position = _fly_start_pos.lerp(_fly_target_pos, smooth_t)
	# Slerp the look direction
	var target_basis := Transform3D().looking_at(_fly_target_look - _fly_target_pos, Vector3.UP).basis
	global_transform.basis = _fly_start_basis.slerp(target_basis, smooth_t)

	if t >= 1.0:
		# Transition complete — signal the controller
		fly_to_focus_completed.emit()

## Return to patrol from nearest racetrack point
func return_to_patrol() -> void:
	_mode = Mode.PATROL
	_speed_multiplier = 1.0
	_look_offset = Vector2.ZERO
	_look_override_time = 0.0
	# Find nearest point on racetrack to resume from
	var min_dist := INF
	var nearest_idx := 0
	for i in range(_racetrack_points.size()):
		var dist := position.distance_to(_racetrack_points[i])
		if dist < min_dist:
			min_dist = dist
			nearest_idx = i
	_path_progress = float(nearest_idx) / float(_racetrack_points.size())

signal fly_to_focus_completed
```

- [ ] **Step 2: Wire the patrol camera in FleetViewController**

Attach the `patrol_orbit_camera.gd` script directly to the `PatrolCamera` node in `fleet_view.tscn`:

```ini
[node name="PatrolCamera" type="Camera3D" parent="."]
unique_name_in_owner = true
script = ExtResource("patrol_camera_script")
current = true
```

Then in `FleetViewController._ready()`, after `_build_grid()`, call setup:

```gdscript
	patrol_camera.setup(_grid_bounds)
```

Note: The script is attached in the `.tscn` so `_ready()` fires normally. The `setup()` call configures the racetrack geometry after the grid bounds are computed. The `setup()` method handles all initialisation that depends on grid bounds.

- [ ] **Step 3: Verify patrol camera in Godot**

Run the fleet view scene. The camera should:
- Orbit around the grid on a racetrack path
- Look inward at the grid centroid
- W accelerates, S slows/reverses
- Arrow keys shift the look target, ease back after 5 seconds

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/patrol_orbit_camera.gd src/pmview-app/scripts/FleetViewController.gd
git commit -m "Add patrol racetrack camera for fleet view

Rounded-rectangle path around grid, W/S throttle, arrow-key look
with ease-back timeout."
```

---

## Chunk 3: Focus Transition and Holographic Projection Beam

The centrepiece — selecting a host, flying up, spawning the detail view, and projecting the holographic beam.

### Task 5: Create holographic beam shader

**Files:**
- Create: `src/pmview-app/shaders/holographic_beam.gdshader`

- [ ] **Step 1: Write the beam shader**

```glsl
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add;

uniform vec4 beam_colour : source_color = vec4(0.3, 0.75, 0.97, 0.15);
uniform float scan_line_count : hint_range(5.0, 50.0) = 20.0;
uniform float scan_line_speed : hint_range(0.0, 5.0) = 1.5;
uniform float scan_line_intensity : hint_range(0.0, 1.0) = 0.3;
uniform float edge_fade : hint_range(0.0, 0.5) = 0.15;

varying float v_height_ratio;

void vertex() {
	// Pass normalised height (0 = floor, 1 = ceiling) to fragment
	v_height_ratio = clamp(VERTEX.y / max(abs(VERTEX.y), 0.001), 0.0, 1.0);
}

void fragment() {
	// Base beam colour with vertical fade (brighter near source, dimmer at top)
	float vertical_fade = mix(1.0, 0.3, v_height_ratio);

	// Rising scan lines
	float scan = sin((UV.y - TIME * scan_line_speed) * scan_line_count * TAU);
	scan = scan * 0.5 + 0.5;  // normalise to 0-1
	float scan_contribution = scan * scan_line_intensity;

	// Edge fade — soften the beam edges horizontally
	float edge = smoothstep(0.0, edge_fade, UV.x) * smoothstep(0.0, edge_fade, 1.0 - UV.x);

	ALBEDO = beam_colour.rgb;
	ALPHA = beam_colour.a * vertical_fade * edge * (1.0 + scan_contribution);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/shaders/holographic_beam.gdshader
git commit -m "Add holographic projection beam shader

Rising scan-lines, vertical fade, edge softening. R2-D2 style."
```

---

### Task 6: Create holographic beam geometry builder

**Files:**
- Create: `src/pmview-app/scripts/holographic_beam.gd`

- [ ] **Step 1: Write the beam builder script**

```gdscript
# holographic_beam.gd
# Builds a truncated cuboid mesh connecting a compact host (floor)
# to a floating detail view (ceiling). The pyramid flare is emergent
# from the size difference between the two.
extends MeshInstance3D

const BEAM_SHADER := preload("res://shaders/holographic_beam.gdshader")

## Floor dimensions (compact host footprint)
var floor_size: Vector2 = Vector2(3.0, 3.0)
## Ceiling dimensions (detail view footprint)
var ceiling_size: Vector2 = Vector2(20.0, 15.0)
## Height of the beam (floor Y to ceiling Y)
var beam_height: float = 15.0

func _ready() -> void:
	rebuild()

func rebuild() -> void:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)

	var verts := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()

	# Floor corners (Y = 0)
	var fw := floor_size.x / 2.0
	var fd := floor_size.y / 2.0
	var floor_corners := [
		Vector3(-fw, 0, -fd),  # 0: front-left
		Vector3( fw, 0, -fd),  # 1: front-right
		Vector3( fw, 0,  fd),  # 2: back-right
		Vector3(-fw, 0,  fd),  # 3: back-left
	]

	# Ceiling corners (Y = beam_height)
	var cw := ceiling_size.x / 2.0
	var cd := ceiling_size.y / 2.0
	var ceiling_corners := [
		Vector3(-cw, beam_height, -cd),  # 4: front-left
		Vector3( cw, beam_height, -cd),  # 5: front-right
		Vector3( cw, beam_height,  cd),  # 6: back-right
		Vector3(-cw, beam_height,  cd),  # 7: back-left
	]

	# Build 4 quad faces (front, right, back, left)
	var faces := [
		[0, 1, 5, 4],  # front
		[1, 2, 6, 5],  # right
		[2, 3, 7, 6],  # back
		[3, 0, 4, 7],  # left
	]

	for face_idx in range(4):
		var f: Array = faces[face_idx]
		var all_corners: Array[Vector3] = []
		all_corners.append_array(floor_corners)
		all_corners.append_array(ceiling_corners)
		var base := verts.size()
		# Two triangles per quad
		for vi in f:
			verts.append(all_corners[vi])
		# UVs: bottom-left, bottom-right, top-right, top-left
		uvs.append(Vector2(0, 1))
		uvs.append(Vector2(1, 1))
		uvs.append(Vector2(1, 0))
		uvs.append(Vector2(0, 0))
		# Triangle 1
		indices.append(base + 0)
		indices.append(base + 1)
		indices.append(base + 2)
		# Triangle 2
		indices.append(base + 0)
		indices.append(base + 2)
		indices.append(base + 3)

	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices

	var arr_mesh := ArrayMesh.new()
	arr_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)

	var mat := ShaderMaterial.new()
	mat.shader = BEAM_SHADER
	arr_mesh.surface_set_material(0, mat)

	mesh = arr_mesh

## Convenience: set sizes and rebuild in one call
func configure(p_floor_size: Vector2, p_ceiling_size: Vector2, p_height: float) -> void:
	floor_size = p_floor_size
	ceiling_size = p_ceiling_size
	beam_height = p_height
	rebuild()
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-app/scripts/holographic_beam.gd
git commit -m "Add holographic beam geometry builder

Truncated cuboid connecting compact host to floating detail view.
Pyramid flare is emergent from size difference."
```

---

### Task 7: Wire focus transition in FleetViewController

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`

This is the big one — selecting a host, flying the camera, spawning the detail view, and managing the beam.

- [ ] **Step 1: Add focus state and references to FleetViewController**

Add these member variables and the `@onready` for FocusCamera:

```gdscript
@onready var focus_camera: Camera3D = %FocusCamera

enum ViewMode { PATROL, TRANSITIONING_TO_FOCUS, FOCUS, TRANSITIONING_TO_PATROL }
var _view_mode: ViewMode = ViewMode.PATROL
var _focused_host_index: int = -1
var _detail_view: Node3D = null
var _beam: MeshInstance3D = null
const DETAIL_VIEW_HEIGHT := 15.0  # Y offset above grid for floating detail view
```

- [ ] **Step 2: Add host selection via Enter key and mouse click**

Add to `_unhandled_input()`:

```gdscript
	if event.is_action_pressed("ui_accept") and _view_mode == ViewMode.PATROL:
		# Select the nearest host to camera look direction
		_select_nearest_host()
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if _view_mode == ViewMode.PATROL:
			_select_host_by_click(event.position)
```

```gdscript
func _select_nearest_host() -> void:
	# Raycast from camera centre to find nearest CompactHost
	var camera := get_viewport().get_camera_3d()
	var centre := get_viewport().get_visible_rect().size / 2.0
	var from := camera.project_ray_origin(centre)
	var dir := camera.project_ray_normal(centre)
	_raycast_select(from, dir)

func _select_host_by_click(screen_pos: Vector2) -> void:
	var camera := get_viewport().get_camera_3d()
	var from := camera.project_ray_origin(screen_pos)
	var dir := camera.project_ray_normal(screen_pos)
	_raycast_select(from, dir)

func _raycast_select(from: Vector3, direction: Vector3) -> void:
	var space_state := get_world_3d().direct_space_state
	var query := PhysicsRayQueryParameters3D.create(from, from + direction * 200.0)
	var result := space_state.intersect_ray(query)
	if result.is_empty():
		return
	# Walk up from the collider to find the CompactHost
	var node: Node = result.collider
	while node and not (node.has_method("set_metric_value")):
		node = node.get_parent()
	if node:
		var idx := _hosts.find(node)
		if idx >= 0:
			_enter_focus(idx)
```

- [ ] **Step 3: Add focus enter/exit methods**

```gdscript
func _enter_focus(host_index: int) -> void:
	_focused_host_index = host_index
	_view_mode = ViewMode.TRANSITIONING_TO_FOCUS
	var host: Node3D = _hosts[host_index]

	# Dim all other hosts
	for i in range(_hosts.size()):
		if i != host_index:
			_hosts[i].set_opacity(0.3)

	# Calculate focus camera target position (above the host)
	var target_pos := host.position + Vector3(0, DETAIL_VIEW_HEIGHT + 5.0, 15.0)
	var look_pos := host.position + Vector3(0, DETAIL_VIEW_HEIGHT / 2.0, 0)

	# Fly the patrol camera toward the focus position
	patrol_camera.fly_to_focus(target_pos, look_pos)
	await patrol_camera.fly_to_focus_completed

	# Switch to focus camera at the destination
	focus_camera.global_transform = patrol_camera.global_transform
	focus_camera.make_current()
	# Wait a frame for fly_orbit_camera.gd's _ready() to fire (script attached in .tscn)
	await get_tree().process_frame
	focus_camera.orbit_center = host.position + Vector3(0, DETAIL_VIEW_HEIGHT / 2.0, 0)

	# Spawn mock detail view (placeholder — real HostView spawning comes later)
	_spawn_mock_detail_view(host)

	# Spawn holographic beam
	_spawn_beam(host)

	_view_mode = ViewMode.FOCUS

func _exit_focus() -> void:
	_view_mode = ViewMode.TRANSITIONING_TO_PATROL

	# Remove detail view and beam
	if _detail_view:
		_detail_view.queue_free()
		_detail_view = null
	if _beam:
		_beam.queue_free()
		_beam = null

	# Restore patrol camera
	patrol_camera.global_transform = focus_camera.global_transform
	patrol_camera.make_current()
	patrol_camera.return_to_patrol()

	# Restore all host opacities
	for host: Node3D in _hosts:
		host.set_opacity(1.0)

	_focused_host_index = -1
	_view_mode = ViewMode.PATROL

func _spawn_mock_detail_view(host: Node3D) -> void:
	# Placeholder: a simple arrangement of larger bars representing the full host
	# This will be replaced with real HostView scene projection later
	_detail_view = Node3D.new()
	_detail_view.name = "DetailView"
	_detail_view.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
	# Add some placeholder bars to visualise the space
	for i in range(8):
		var bar: Node3D = load("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn").instantiate()
		bar.position = Vector3((i - 3.5) * 2.0, 0, 0)
		bar.height = randf_range(0.3, 1.0)
		bar.colour = Color(randf(), randf(), randf())
		_detail_view.add_child(bar)
	add_child(_detail_view)

func _spawn_beam(host: Node3D) -> void:
	_beam = MeshInstance3D.new()
	_beam.name = "HolographicBeam"
	_beam.set_script(load("res://scripts/holographic_beam.gd"))
	_beam.position = host.position
	var host_footprint: Vector2 = host.get_footprint()
	# Detail view footprint — mock for now, will be calculated from real HostView bounds
	var detail_footprint := Vector2(18.0, 10.0)
	_beam.configure(host_footprint, detail_footprint, DETAIL_VIEW_HEIGHT)
	add_child(_beam)
```

- [ ] **Step 4: Update ESC handling for focus mode**

Modify `_handle_esc()` to handle both focus and patrol exits:

```gdscript
func _handle_esc() -> void:
	if _view_mode == ViewMode.FOCUS:
		_exit_focus()
		return
	# Double-ESC to return to main menu from patrol
	var now := Time.get_ticks_msec() / 1000.0
	if now - _esc_pressed_at < ESC_DOUBLE_PRESS_WINDOW:
		SceneManager.go_to_main_menu()
	else:
		_esc_pressed_at = now
```

- [ ] **Step 5: Verify the full focus flow in Godot**

1. Run fleet view
2. Patrol camera orbits the grid
3. Click on a CompactHost
4. Camera flies up, non-selected hosts dim to 30%
5. Mock detail view appears floating above with holographic beam
6. Free orbit/fly around the detail view works
7. ESC returns to patrol, beam disappears, hosts restore to full opacity
8. Double-ESC from patrol returns to main menu

- [ ] **Step 6: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Wire focus transition with holographic projection beam

Click/Enter selects host, camera flies up, detail view floats above
with R2-D2 pyramid beam. ESC returns to patrol. Non-selected hosts
dim to 30% opacity."
```

---

## Chunk 4: Timeline and Navigation Polish

### Task 8: Wire master timestamp and time controls

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`
- Modify: `src/pmview-app/scenes/fleet_view.tscn`

- [ ] **Step 1: Add TimeControl scene to fleet_view.tscn**

Add the TimeControl packed scene as a child of the HUD CanvasLayer, similar to how `RuntimeSceneBuilder.AddTimeControl()` adds it in the single-host pipeline (line 427-448 of `RuntimeSceneBuilder.cs`).

In the `.tscn` file, add:
```ini
[node name="TimeControl" parent="HUD" instance=ExtResource("time_control_scene")]
unique_name_in_owner = true
```

Add the corresponding ext_resource for the time control scene.

- [ ] **Step 2: Wire master timestamp updates in FleetViewController**

Add a method that receives timestamp updates and propagates to the billboard:

```gdscript
@onready var time_control: Control = %TimeControl

func _update_master_timestamp(timestamp: String) -> void:
	if master_timestamp:
		master_timestamp.text = timestamp
	if time_control and time_control.has_method("update_playhead"):
		time_control.update_playhead(timestamp)
```

This will be connected to the fleet MetricPoller's `PlaybackPositionChanged` signal once polling is wired. For now, the mock timestamp set in `_ready()` is sufficient.

- [ ] **Step 3: Add ESC hint label that updates with context**

```gdscript
@onready var esc_hint: Label = %EscHint if has_node("%EscHint") else null

func _update_esc_hint() -> void:
	if not esc_hint:
		return
	match _view_mode:
		ViewMode.PATROL:
			esc_hint.text = "ESC ESC → Main Menu"
		ViewMode.FOCUS:
			esc_hint.text = "ESC → Return to Patrol"
		_:
			esc_hint.text = ""
```

Call `_update_esc_hint()` at the end of `_enter_focus()` and `_exit_focus()`.

- [ ] **Step 4: Verify**

Run the scene. Confirm the timestamp billboard floats above the grid, the ESC hint updates when entering/exiting focus mode, and the TimeControl panel appears on the right edge (if in archive mode).

- [ ] **Step 5: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd src/pmview-app/scenes/fleet_view.tscn
git commit -m "Wire master timestamp billboard and context-sensitive ESC hint

One Clock To Rule Them All — floating billboard above grid centroid.
ESC hint updates between patrol and focus modes."
```

---

### Task 9: Polish and visual tuning pass

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`
- Modify: `src/pmview-app/scripts/compact_host.gd`
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/patrol_orbit_camera.gd`

This is an open-ended tuning task. Work with the user in the Godot editor to adjust:

- [ ] **Step 1: Tune patrol camera height and speed**

Adjust `orbit_height`, `patrol_speed`, `racetrack_margin` until the patrol feels like walking through a server room. Start at height=25, speed=8, margin=10 and iterate.

- [ ] **Step 2: Tune CompactHost bar sizing and spacing**

Adjust `bar_spacing`, bar scale, bezel padding, and label position/size until the compact hosts are readable from patrol height but not oversized.

- [ ] **Step 3: Tune focus transition timing and camera position**

Adjust `FLYTO_DURATION` (currently 1.5s), `DETAIL_VIEW_HEIGHT` (currently 15.0), and the focus camera's orbit distance until the focus transition feels cinematic but not sluggish.

- [ ] **Step 4: Tune holographic beam appearance**

Adjust shader uniforms: `beam_colour`, `scan_line_count`, `scan_line_speed`, `scan_line_intensity`, `edge_fade`. The beam should be visible but not dominate — ghostly R2-D2 projection, not a rave.

- [ ] **Step 5: Tune host dimming**

Confirm 30% opacity feels right. Adjust if needed. Ensure dimmed hosts still show bar colour (the guard can still glance at health while focused).

- [ ] **Step 6: Commit tuning changes**

```bash
git add -A
git commit -m "Visual tuning pass for fleet view

Adjusted camera height, patrol speed, bar sizing, beam intensity,
and focus transition timing based on visual testing."
```

---

## Chunk 5: Live Polling (Future — Deferred)

This chunk is documented for planning purposes but is NOT part of the initial scene-first implementation. It will be a separate implementation plan once Chunks 1-4 are proven.

### Task 10 (DEFERRED): Fleet MetricPoller

- Create fleet-wide polling that fetches 4 aggregate metrics per host
- Serial requests with throttling and backoff
- Connect to CompactHost bars via SceneBinder or direct calls

### Task 11 (DEFERRED): Detail MetricPoller lifecycle

- Spawn ephemeral MetricPoller on focus enter
- Wire to real HostView scene (replacing mock detail view)
- Dispose on ESC/exit focus

### Task 12 (DEFERRED): Source chooser multi-select

- Convert host dropdown to support multi-select (or list with checkboxes)
- Live mode multi-host support
- Connection config plumbing for fleet mode

---

## Summary

| Chunk | Tasks | Description |
|-------|-------|-------------|
| 1 | 1-3 | Static fleet grid scene with mock data, navigation wiring |
| 2 | 4 | Patrol racetrack camera |
| 3 | 5-7 | Focus transition, holographic beam, detail view |
| 4 | 8-9 | Timeline, ESC hints, visual tuning |
| 5 | 10-12 | Live polling (DEFERRED — separate plan) |

Chunks 1-4 deliver a fully navigable fleet view with mock data. Chunk 5 is a separate effort once the visual experience is validated.
