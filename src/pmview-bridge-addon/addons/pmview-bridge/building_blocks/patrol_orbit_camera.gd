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
	var rect := _grid_bounds.grow(racetrack_margin)
	var r := minf(corner_radius, minf(rect.size.x, rect.size.y) / 2.0)
	var points := PackedVector3Array()
	var segments_per_corner := 8

	# Four corners: top-right, top-left, bottom-left, bottom-right
	var corners := [
		Vector2(rect.end.x - r, rect.end.y - r),
		Vector2(rect.position.x + r, rect.end.y - r),
		Vector2(rect.position.x + r, rect.position.y + r),
		Vector2(rect.end.x - r, rect.position.y + r),
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
	var target_basis := Transform3D().looking_at(
		_fly_target_look - _fly_target_pos, Vector3.UP).basis
	global_transform.basis = _fly_start_basis.slerp(target_basis, smooth_t)

	if t >= 1.0:
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
