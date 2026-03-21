extends Node3D

## Loading screen controller. Maps six pipeline phases to P-M-V-I-E-W letter
## materialisation, then zoom-exits into the host view scene.

@onready var pipeline: Node = %LoadingPipeline
@onready var status_label: Label = %StatusLabel

@onready var letters := [$LetterP, $LetterM, $LetterV, $LetterI, $LetterE, $LetterW]
@onready var camera: Camera3D = $Camera3D
@onready var ground_mat: ShaderMaterial = $GroundPlane.get_surface_override_material(0)
@onready var white_out: ColorRect = %WhiteOut

const PHASE_NAMES := [
	"CONNECTING",
	"FETCHING TOPOLOGY",
	"DISCOVERING INSTANCES",
	"SELECTING PROFILE",
	"COMPUTING LAYOUT",
	"BUILDING SCENE",
]

# Fly-by timing
const FLYBY_PAUSE := 0.3
const FLYBY_TOTAL_DURATION := 2.5

# Spline waypoints: position, in-handle (relative), out-handle (relative), tilt (radians)
# Handles control the curve shape — longer handles = wider/smoother curves
const FLYBY_WAYPOINTS := [
	# Start: front-on view of letters
	{ "pos": Vector3(0.0, 0.18, 6.11),    "in": Vector3.ZERO,               "out": Vector3(0.0, 0.0, -2.0),    "tilt": 0.0 },
	# Approach: diving toward the text centre, beginning to turn
	{ "pos": Vector3(-2.0, -0.05, 2.0),   "in": Vector3(0.5, 0.1, 1.5),     "out": Vector3(-1.5, -0.1, -0.5),  "tilt": -0.26 },
	# Alongside "P": banked, low, close to the letter face
	{ "pos": Vector3(-5.625, -0.12, 1.5),  "in": Vector3(1.0, 0.0, 0.5),     "out": Vector3(-2.0, 0.0, 0.0),    "tilt": -0.15 },
	# Mid-sweep: flying past letters, picking up speed
	{ "pos": Vector3(3.0, -0.12, 1.5),     "in": Vector3(-3.0, 0.0, 0.0),    "out": Vector3(3.0, 0.0, 0.0),     "tilt": 0.0 },
	# Past "W": letters behind, beginning hyperspace
	{ "pos": Vector3(12.0, -0.12, 1.0),    "in": Vector3(-3.0, 0.0, 0.2),    "out": Vector3(3.0, 0.0, -1.0),    "tilt": 0.05 },
	# Hyperspace exit: forward and away
	{ "pos": Vector3(20.0, -0.12, -5.0),   "in": Vector3(-2.0, 0.0, 2.0),    "out": Vector3.ZERO,               "tilt": 0.0 },
]

# Progress ratios at which shader effects kick in (0.0 = start, 1.0 = end)
const SHADER_SPEED_START := 0.35   # Start stretching dots during sweep
const SHADER_SPEED_FULL := 0.85    # Full streak by hyperspace
const WHITE_OUT_START := 0.75      # Begin white-out
const STREAK_ANGLE_START := 0.70   # Begin rotating streak angle

var _has_error := false


func _ready() -> void:
	var config := SceneManager.connection_config
	var endpoint: String = config.get("endpoint", "http://localhost:44322")
	var mode: String = config.get("mode", "live")
	var hostname: String = config.get("hostname", "")
	var start_time: String = config.get("start_time", "")
	var verbose: bool = config.get("verbose_logging", false)

	pipeline.PhaseCompleted.connect(_on_phase_completed)
	pipeline.PipelineCompleted.connect(_on_pipeline_completed)
	pipeline.PipelineError.connect(_on_pipeline_error)

	pipeline.StartPipeline(endpoint, mode, hostname, start_time, verbose)


func _on_phase_completed(index: int, _phase_name: String) -> void:
	if index < 0 or index >= letters.size():
		return

	var letter: MeshInstance3D = letters[index]
	var mat: ShaderMaterial = letter.get_surface_override_material(0)

	# Update status text
	if index < PHASE_NAMES.size():
		status_label.text = PHASE_NAMES[index]

	# Materialise: wireframe → solid over 0.4s
	var tween := create_tween()
	tween.tween_property(mat, "shader_parameter/materialise", 1.0, 0.4)

	# Glow pulse: flash to 1.0 then fade to 0 over 0.3s
	var glow_tween := create_tween()
	glow_tween.tween_property(mat, "shader_parameter/glow_pulse", 1.0, 0.05)
	glow_tween.tween_property(mat, "shader_parameter/glow_pulse", 0.0, 0.25)


func _on_pipeline_completed() -> void:
	status_label.text = "READY"

	# Brief pause to admire the completed word
	await get_tree().create_timer(FLYBY_PAUSE).timeout
	status_label.visible = false

	await _run_flyby_sequence()

	# Transition to host view
	SceneManager.go_to_host_view(pipeline.BuiltScene)


func _build_flyby_curve() -> Curve3D:
	var curve := Curve3D.new()
	for wp: Dictionary in FLYBY_WAYPOINTS:
		var idx := curve.add_point(wp["pos"], wp["in"], wp["out"])
		curve.set_point_tilt(idx, wp["tilt"])
	return curve


func _run_flyby_sequence() -> void:
	# Build the spline path at runtime
	var path := Path3D.new()
	path.curve = _build_flyby_curve()
	add_child(path)

	var follow := PathFollow3D.new()
	follow.rotation_mode = PathFollow3D.ROTATION_ORIENTED
	follow.use_model_front = true
	follow.loop = false
	path.add_child(follow)

	# Reparent camera under the path follower so it rides the spline
	var cam_local_xform := Transform3D.IDENTITY
	camera.reparent(follow)
	camera.transform = cam_local_xform

	# Tween progress along the curve — EASE_IN for acceleration buildup
	var progress_tween := create_tween()
	progress_tween.tween_property(follow, "progress_ratio", 1.0, FLYBY_TOTAL_DURATION) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Use _process to drive shader effects based on progress
	var shader_updater := func() -> void:
		var p: float = follow.progress_ratio

		# Ground shader speed: ramp from 0 to 1 across the sweep/hyperspace portion
		if p >= SHADER_SPEED_START:
			var speed_t := clampf((p - SHADER_SPEED_START) / (SHADER_SPEED_FULL - SHADER_SPEED_START), 0.0, 1.0)
			ground_mat.set_shader_parameter("speed", speed_t * speed_t)  # Quadratic ramp

		# Streak angle: rotate toward forward (PI/2) during hyperspace
		if p >= STREAK_ANGLE_START:
			var angle_t := clampf((p - STREAK_ANGLE_START) / (1.0 - STREAK_ANGLE_START), 0.0, 1.0)
			ground_mat.set_shader_parameter("streak_angle", angle_t * PI / 2.0)

		# White-out: exponential ramp at the end
		if p >= WHITE_OUT_START:
			var white_t := clampf((p - WHITE_OUT_START) / (1.0 - WHITE_OUT_START), 0.0, 1.0)
			white_out.modulate.a = white_t * white_t * white_t  # Cubic for late-hitting punch

	# Connect to tree process to update effects each frame
	get_tree().process_frame.connect(shader_updater)

	await progress_tween.finished

	# Cleanup: disconnect updater, free path nodes
	get_tree().process_frame.disconnect(shader_updater)
	camera.reparent(self)
	path.queue_free()


func _on_pipeline_error(index: int, error: String) -> void:
	_has_error = true

	# Flash the current letter red
	if index >= 0 and index < letters.size():
		var letter: MeshInstance3D = letters[index]
		var tween := create_tween()
		tween.tween_property(letter, "modulate", Color.RED, 0.3)

	# Show error in status label
	status_label.text = "ERROR: %s\nPRESS ESC TO RETURN" % error
	status_label.modulate = Color(1.0, 0.3, 0.3, 1.0)


func _unhandled_input(event: InputEvent) -> void:
	if _has_error and event.is_action_pressed("ui_cancel"):
		SceneManager.go_to_main_menu()
		get_viewport().set_input_as_handled()
