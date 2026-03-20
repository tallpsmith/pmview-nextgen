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
@export var keyboard_look_speed: float = 90.0  ## degrees/second for arrow-key look

## When false, all movement and mouse-look input is ignored.
## Used by panels (Help, Range Tuning) to suppress camera while open.
var input_enabled: bool = true

var _mode: Mode = Mode.ORBIT
var _radius: float
var _orbit_height: float
var _orbit_angle: float

# Fly mode state
var _fly_yaw: float = 0.0
var _fly_pitch: float = 0.0
var _is_right_clicking: bool = false

# Orbit look override state (arrow-key temporary look)
var _orbit_look_yaw_offset: float = 0.0
var _orbit_look_pitch_offset: float = 0.0
var _orbit_look_timer: float = 0.0
var _orbit_look_easing_back: bool = false
var _orbit_look_ease_progress: float = 0.0
var _orbit_look_ease_start_yaw: float = 0.0
var _orbit_look_ease_start_pitch: float = 0.0
const ORBIT_LOOK_TIMEOUT: float = 10.0
const ORBIT_LOOK_EASE_DURATION: float = 0.5

# Transition state
var _transition_start_pos: Vector3
var _transition_start_basis: Basis
var _transition_progress: float = 0.0

# Focus state (smooth look-at for auto-focus)
var _focus_target: Vector3 = Vector3.ZERO
var _focus_active: bool = false
var _focus_start_yaw: float = 0.0
var _focus_start_pitch: float = 0.0
var _focus_target_yaw: float = 0.0
var _focus_target_pitch: float = 0.0
var _focus_progress: float = 0.0
const FOCUS_DURATION: float = 0.5

func _ready() -> void:
	_orbit_height = position.y
	_radius = Vector2(position.x - orbit_center.x, position.z - orbit_center.z).length()
	_orbit_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)

func _unhandled_input(event: InputEvent) -> void:
	if not input_enabled:
		return
	if event is InputEventKey and event.pressed and event.physical_keycode == KEY_TAB:
		_toggle_mode()
		get_viewport().set_input_as_handled()
	# Cancel focus animation on any manual camera input
	if _focus_active:
		if event is InputEventMouseMotion and _is_right_clicking:
			_focus_active = false
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
			# Reset any orbit look override
			_orbit_look_yaw_offset = 0.0
			_orbit_look_pitch_offset = 0.0
			_orbit_look_easing_back = false
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

	# Arrow-key look override
	if input_enabled:
		var arrow_input := false
		if Input.is_physical_key_pressed(KEY_LEFT):
			_orbit_look_yaw_offset += deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_RIGHT):
			_orbit_look_yaw_offset -= deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_UP):
			_orbit_look_pitch_offset += deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		if Input.is_physical_key_pressed(KEY_DOWN):
			_orbit_look_pitch_offset -= deg_to_rad(keyboard_look_speed) * delta
			arrow_input = true
		_orbit_look_pitch_offset = clampf(
			_orbit_look_pitch_offset, -PI / 2.0 + 0.1, PI / 2.0 - 0.1)

		if arrow_input:
			_orbit_look_timer = 0.0
			_orbit_look_easing_back = false
		elif _orbit_look_yaw_offset != 0.0 or _orbit_look_pitch_offset != 0.0:
			_orbit_look_timer += delta
			if _orbit_look_timer >= ORBIT_LOOK_TIMEOUT and not _orbit_look_easing_back:
				_orbit_look_easing_back = true
				_orbit_look_ease_progress = 0.0
				_orbit_look_ease_start_yaw = _orbit_look_yaw_offset
				_orbit_look_ease_start_pitch = _orbit_look_pitch_offset

	# Ease back to centre
	if _orbit_look_easing_back:
		_orbit_look_ease_progress += delta / ORBIT_LOOK_EASE_DURATION
		var t := _ease_in_out(_orbit_look_ease_progress)
		_orbit_look_yaw_offset = lerpf(_orbit_look_ease_start_yaw, 0.0, t)
		_orbit_look_pitch_offset = lerpf(_orbit_look_ease_start_pitch, 0.0, t)
		if _orbit_look_ease_progress >= 1.0:
			_orbit_look_yaw_offset = 0.0
			_orbit_look_pitch_offset = 0.0
			_orbit_look_easing_back = false

	# Apply look direction
	if _orbit_look_yaw_offset == 0.0 and _orbit_look_pitch_offset == 0.0:
		look_at(orbit_center, Vector3.UP)
	else:
		look_at(orbit_center, Vector3.UP)
		var base_basis := global_transform.basis
		var offset_basis := Basis.from_euler(Vector3(
			_orbit_look_pitch_offset, _orbit_look_yaw_offset, 0.0))
		global_transform.basis = base_basis * offset_basis

func _process_fly(delta: float) -> void:
	if not input_enabled:
		return
	# Handle focus animation
	if _focus_active:
		_focus_progress += delta / FOCUS_DURATION
		var t := _ease_in_out(_focus_progress)
		_fly_yaw = lerpf(_focus_start_yaw, _focus_target_yaw, t)
		_fly_pitch = lerpf(_focus_start_pitch, _focus_target_pitch, t)
		if _focus_progress >= 1.0:
			_focus_active = false

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

	# Arrow-key look (yaw/pitch)
	var look_input := false
	if Input.is_physical_key_pressed(KEY_LEFT):
		_fly_yaw += deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_RIGHT):
		_fly_yaw -= deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_UP):
		_fly_pitch += deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	if Input.is_physical_key_pressed(KEY_DOWN):
		_fly_pitch -= deg_to_rad(keyboard_look_speed) * delta
		look_input = true
	_fly_pitch = clampf(_fly_pitch, -PI / 2.0 + 0.1, PI / 2.0 - 0.1)

	# Cancel focus if user takes manual control
	if input_dir.length_squared() > 0.0 or look_input:
		_focus_active = false

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

## Smoothly pans the camera to look at the given world position.
## In orbit mode: changes orbit_center so the camera orbits the target.
## In fly mode: smoothly rotates to look at the target without moving.
func focus_on_position(target: Vector3) -> void:
	_focus_target = target
	match _mode:
		Mode.ORBIT:
			orbit_center = target
		Mode.FLY:
			var dir := (target - global_position).normalized()
			_focus_target_yaw = atan2(-dir.x, -dir.z)
			_focus_target_pitch = asin(dir.y)
			_focus_start_yaw = _fly_yaw
			_focus_start_pitch = _fly_pitch
			_focus_progress = 0.0
			_focus_active = true
		Mode.TRANSITIONING:
			_mode = Mode.ORBIT
			orbit_center = target
