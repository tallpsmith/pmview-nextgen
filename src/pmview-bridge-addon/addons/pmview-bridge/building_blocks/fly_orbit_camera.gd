extends Camera3D

## Dual-mode camera: orbit (showcase) and fly (WASD exploration).
## Tab toggles between modes. Orbit mode auto-rotates around orbit_center.
## Fly mode: WASD move, Q/E elevation, right-click+drag mouse look, Shift sprint.

enum Mode { ORBIT, FLY, TRANSITIONING }

@export var orbit_speed: float = 20.0
@export var orbit_center: Vector3 = Vector3.ZERO
@export var fly_speed: float = 10.0
@export var sprint_multiplier: float = 2.0
@export var mouse_sensitivity: float = 0.002
@export var transition_speed: float = 0.3

var _mode: Mode = Mode.ORBIT
var _radius: float
var _orbit_height: float
var _orbit_angle: float

# Fly mode state
var _fly_yaw: float = 0.0
var _fly_pitch: float = 0.0
var _is_right_clicking: bool = false

# Transition state
var _transition_start_pos: Vector3
var _transition_start_basis: Basis
var _transition_progress: float = 0.0

func _ready() -> void:
	_orbit_height = position.y
	_radius = Vector2(position.x - orbit_center.x, position.z - orbit_center.z).length()
	_orbit_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.physical_keycode == KEY_TAB:
		_toggle_mode()
		get_viewport().set_input_as_handled()
	if _mode == Mode.FLY:
		if event is InputEventMouseButton:
			_is_right_clicking = event.pressed and event.button_index == MOUSE_BUTTON_RIGHT
		if event is InputEventMouseMotion and _is_right_clicking:
			_fly_yaw -= event.relative.x * mouse_sensitivity
			_fly_pitch -= event.relative.y * mouse_sensitivity
			_fly_pitch = clampf(_fly_pitch, -PI / 2.0 + 0.1, PI / 2.0 - 0.1)

func _process(delta: float) -> void:
	match _mode:
		Mode.ORBIT:
			_process_orbit(delta)
		Mode.FLY:
			_process_fly(delta)
		Mode.TRANSITIONING:
			_process_transition(delta)

func _toggle_mode() -> void:
	match _mode:
		Mode.ORBIT:
			# Orbit -> Fly: instant, capture current orientation
			_mode = Mode.FLY
			var euler := global_transform.basis.get_euler()
			_fly_yaw = euler.y
			_fly_pitch = euler.x
		Mode.FLY:
			# Fly -> Orbit: smooth transition back
			_mode = Mode.TRANSITIONING
			_transition_start_pos = global_position
			_transition_start_basis = global_transform.basis
			_transition_progress = 0.0
		Mode.TRANSITIONING:
			# During transition, Tab snaps to fly at current interpolated position
			_mode = Mode.FLY
			var euler := global_transform.basis.get_euler()
			_fly_yaw = euler.y
			_fly_pitch = euler.x

func _process_orbit(delta: float) -> void:
	_orbit_angle += deg_to_rad(orbit_speed) * delta
	position = Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)
	look_at(orbit_center, Vector3.UP)

func _process_fly(delta: float) -> void:
	var speed := fly_speed
	if Input.is_physical_key_pressed(KEY_SHIFT):
		speed *= sprint_multiplier

	var input_dir := Vector3.ZERO
	if Input.is_physical_key_pressed(KEY_W):
		input_dir.z -= 1.0
	if Input.is_physical_key_pressed(KEY_S):
		input_dir.z += 1.0
	if Input.is_physical_key_pressed(KEY_A):
		input_dir.x -= 1.0
	if Input.is_physical_key_pressed(KEY_D):
		input_dir.x += 1.0
	if Input.is_physical_key_pressed(KEY_Q):
		input_dir.y -= 1.0
	if Input.is_physical_key_pressed(KEY_E):
		input_dir.y += 1.0

	# Build orientation from yaw/pitch
	var fly_basis := Basis.from_euler(Vector3(_fly_pitch, _fly_yaw, 0.0))
	global_transform.basis = fly_basis

	if input_dir.length_squared() > 0.0:
		input_dir = input_dir.normalized()
		global_position += fly_basis * input_dir * speed * delta

func _process_transition(delta: float) -> void:
	_transition_progress += delta * transition_speed
	var t := _ease_in_out(_transition_progress)

	if t >= 1.0:
		_mode = Mode.ORBIT
		# Sync orbit angle to where we ended up
		_orbit_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)
		return

	# Compute current orbit target position
	var target_pos := Vector3(
		orbit_center.x + _radius * cos(_orbit_angle),
		_orbit_height,
		orbit_center.z + _radius * sin(_orbit_angle)
	)

	# Interpolate position
	global_position = _transition_start_pos.lerp(target_pos, t)

	# Interpolate orientation toward looking at orbit_center
	var target_transform := global_transform.looking_at(orbit_center, Vector3.UP)
	global_transform.basis = _transition_start_basis.slerp(target_transform.basis, t)

## Smooth ease-in/ease-out curve (smoothstep).
func _ease_in_out(t: float) -> float:
	var clamped := clampf(t, 0.0, 1.0)
	return clamped * clamped * (3.0 - 2.0 * clamped)
