# patrol_orbit_camera.gd
# Racetrack patrol camera for fleet view.
# Travels along a rounded-rectangle path around the grid,
# looking inward at the grid centroid.
extends Camera3D

enum Mode { PATROL, FLYING_TO_FOCUS }

## Speed along the racetrack in units/second
@export var patrol_speed: float = 8.0
## Camera height above the grid
@export var orbit_height: float = 15.0
## Margin around the grid bounds for the racetrack
@export var racetrack_margin: float = 10.0
## Look-around sensitivity (arrow keys and mouse)
@export var keyboard_look_speed: float = 8.0
## Mouse look sensitivity (right-click drag)
@export var mouse_sensitivity: float = 0.3
## Acceleration rate for W/S throttle (multiplier per second)
@export var throttle_accel: float = 1.5
## Seconds before look override eases back
@export var look_ease_back_timeout: float = 5.0

var _mode: Mode = Mode.PATROL
var _grid_bounds: Rect2 = Rect2()
var _grid_centroid: Vector3 = Vector3.ZERO
var _racetrack_points: PackedVector3Array = PackedVector3Array()
var _racetrack_cumulative: PackedFloat64Array = PackedFloat64Array()  # cumulative distance at each point
var _racetrack_length: float = 0.0
var _path_progress: float = 0.0  # 0.0 to 1.0, normalised position on path
var _speed_multiplier: float = 1.0  # W/S throttle
var _look_offset: Vector2 = Vector2.ZERO  # look override (arrow keys + mouse)
var _look_override_time: float = 0.0
var _input_enabled: bool = true
var _is_right_clicking: bool = false

# Flying-to-focus state
var _fly_start_pos: Vector3
var _fly_start_basis: Basis
var _fly_target_pos: Vector3
var _fly_target_look: Vector3
var _fly_elapsed: float = 0.0
const FLYTO_DURATION := 1.5

signal fly_to_focus_completed


func setup(grid_bounds: Rect2) -> void:
	_grid_bounds = grid_bounds
	_grid_centroid = Vector3(
		grid_bounds.position.x + grid_bounds.size.x / 2.0,
		0,
		grid_bounds.position.y + grid_bounds.size.y / 2.0
	)
	_build_racetrack()
	_path_progress = 0.0
	_apply_patrol_position()


func _build_racetrack() -> void:
	# Elliptical path around the grid — constant visual distance from centroid,
	# no zoom effect between straights and corners.
	var half_w := _grid_bounds.size.x / 2.0 + racetrack_margin
	var half_d := _grid_bounds.size.y / 2.0 + racetrack_margin
	var cx := _grid_bounds.position.x + _grid_bounds.size.x / 2.0
	var cz := _grid_bounds.position.y + _grid_bounds.size.y / 2.0
	var points := PackedVector3Array()
	var num_points := 64  # smooth ellipse

	for i in range(num_points):
		var angle := TAU * float(i) / float(num_points)
		var px := cx + half_w * cos(angle)
		var pz := cz + half_d * sin(angle)
		points.append(Vector3(px, orbit_height, pz))

	_racetrack_points = points
	# Build cumulative distance table for uniform-speed interpolation
	_racetrack_cumulative = PackedFloat64Array()
	_racetrack_cumulative.append(0.0)
	var cumul := 0.0
	for i in range(points.size()):
		var next_i := (i + 1) % points.size()
		cumul += points[i].distance_to(points[next_i])
		_racetrack_cumulative.append(cumul)
	_racetrack_length = cumul


func _process(delta: float) -> void:
	match _mode:
		Mode.PATROL:
			_process_patrol(delta)
		Mode.FLYING_TO_FOCUS:
			_process_flying_to_focus(delta)


func _process_patrol(delta: float) -> void:
	if not _input_enabled:
		return

	# Smooth W/S throttle — continuous acceleration while held
	if Input.is_physical_key_pressed(KEY_W):
		_speed_multiplier = clampf(_speed_multiplier + throttle_accel * delta, 0.0, 4.0)
	elif Input.is_physical_key_pressed(KEY_S):
		_speed_multiplier = clampf(_speed_multiplier - throttle_accel * delta, -1.0, 4.0)

	# Smooth arrow-key look — continuous while held
	var look_input := false
	if Input.is_physical_key_pressed(KEY_LEFT):
		_look_offset.x -= keyboard_look_speed * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_RIGHT):
		_look_offset.x += keyboard_look_speed * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_UP):
		_look_offset.y -= keyboard_look_speed * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_DOWN):
		_look_offset.y += keyboard_look_speed * delta
		look_input = true
	if look_input:
		_look_override_time = 0.0

	# Advance along the path
	var speed := patrol_speed * _speed_multiplier
	var distance := speed * delta
	_path_progress += distance / _racetrack_length
	_path_progress = fmod(_path_progress, 1.0)
	if _path_progress < 0.0:
		_path_progress += 1.0

	_apply_patrol_position()
	_process_look_override(delta)


func _apply_patrol_position() -> void:
	if _racetrack_points.is_empty() or _racetrack_length <= 0.0:
		return
	# Convert normalised progress to actual distance along the path
	var target_dist := _path_progress * _racetrack_length
	# Binary search for the segment containing target_dist
	var total_points := _racetrack_points.size()
	var idx := 0
	for i in range(total_points):
		if _racetrack_cumulative[i + 1] >= target_dist:
			idx = i
			break
	var seg_start := _racetrack_cumulative[idx]
	var seg_end := _racetrack_cumulative[idx + 1]
	var seg_length := seg_end - seg_start
	var frac := 0.0
	if seg_length > 0.001:
		frac = (target_dist - seg_start) / seg_length
	var next_idx := (idx + 1) % total_points

	position = _racetrack_points[idx].lerp(_racetrack_points[next_idx], frac)

	# Look at grid centroid + look offset
	var look_target := _grid_centroid + Vector3(_look_offset.x, 0, _look_offset.y)
	look_at(look_target, Vector3.UP)


func _process_look_override(delta: float) -> void:
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

	# Right-click mouse look — drag to shift look target while orbiting
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_RIGHT:
		_is_right_clicking = event.pressed
	if event is InputEventMouseMotion and _is_right_clicking:
		_look_offset.x += event.relative.x * mouse_sensitivity * 0.1
		_look_offset.y += event.relative.y * mouse_sensitivity * 0.1
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
	# Look at the target from the current interpolated position each frame,
	# not slerp towards a pre-computed final orientation
	look_at(_fly_target_look, Vector3.UP)

	if t >= 1.0:
		_mode = Mode.PATROL  # stop processing, await return_to_patrol() to resume
		_input_enabled = false
		fly_to_focus_completed.emit()


## Return to patrol from nearest racetrack point
func return_to_patrol() -> void:
	_mode = Mode.PATROL
	_input_enabled = true
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
