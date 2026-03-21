extends Node3D

## Loading screen controller. Maps six pipeline phases to P-M-V-I-E-W letter
## materialisation, then zoom-exits into the host view scene.

@onready var pipeline: Node = %LoadingPipeline
@onready var status_label: Label = %StatusLabel

@onready var letters := [$LetterP, $LetterM, $LetterV, $LetterI, $LetterE, $LetterW]
@onready var camera: Camera3D = $Camera3D
@onready var ground_mat: ShaderMaterial = $GroundPlane.get_surface_override_material(0)
@onready var fade_out: ColorRect = %FadeOut

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
# IMPORTANT: in/out handles must be collinear (opposite directions) at each interior
# point for C1 tangent continuity — otherwise the curve kinks and the camera jerks.
# Path: straight at letter centre → bank right → sweep along faces → climb → fade
const FLYBY_WAYPOINTS := [
	# Start: front-on view — out handle points straight at the letters
	{ "pos": Vector3(0.0, 0.18, 6.11),    "in": Vector3.ZERO,              "out": Vector3(0.0, -0.1, -3.0),  "tilt": 0.0 },
	# Near letters: tangent transitions from forward (-Z) to rightward (+X)
	# in/out collinear along the (-Z → +X) diagonal
	{ "pos": Vector3(0.5, -0.05, 1.8),    "in": Vector3(-0.4, 0.1, 1.5),   "out": Vector3(0.4, -0.1, -1.5),  "tilt": 0.15 },
	# Banking right past "I", close to the face — tangent is pure +X
	# in/out collinear along X axis
	{ "pos": Vector3(3.5, -0.12, 1.5),    "in": Vector3(-2.5, 0.0, 0.0),   "out": Vector3(2.5, 0.0, 0.0),    "tilt": 0.15 },
	# Past "W": staying alongside letter faces, pure +X, starting to climb
	# in/out collinear along (+X, +Y slight)
	{ "pos": Vector3(9.0, -0.05, 1.5),    "in": Vector3(-3.0, 0.0, 0.0),   "out": Vector3(3.0, 0.1, 0.0),    "tilt": 0.05 },
	# Climbing away: still +X, gaining altitude, fading out
	# in/out collinear along (+X, +Y)
	{ "pos": Vector3(15.0, 0.8, 1.5),     "in": Vector3(-3.0, -0.4, 0.0),  "out": Vector3(3.0, 0.4, 0.0),    "tilt": 0.0 },
	# Final: high and far right, same Z — no left turn
	{ "pos": Vector3(22.0, 2.5, 1.5),     "in": Vector3(-3.0, -0.8, 0.0),  "out": Vector3.ZERO,              "tilt": 0.0 },
]

# Progress ratio at which fade-to-black begins
const FADE_OUT_START := 0.70

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
		curve.add_point(wp["pos"], wp["in"], wp["out"])
		curve.set_point_tilt(curve.point_count - 1, wp["tilt"])
	return curve


func _run_flyby_sequence() -> void:
	# Build the spline path at runtime
	var path := Path3D.new()
	path.curve = _build_flyby_curve()
	add_child(path)

	var follow := PathFollow3D.new()
	follow.rotation_mode = PathFollow3D.ROTATION_ORIENTED
	follow.use_model_front = false
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

	# Drive fade-to-black based on progress
	var shader_updater := func() -> void:
		var p: float = follow.progress_ratio

		# Fade to black as camera climbs away
		if p >= FADE_OUT_START:
			var fade_t := clampf((p - FADE_OUT_START) / (1.0 - FADE_OUT_START), 0.0, 1.0)
			fade_out.modulate.a = fade_t * fade_t  # Quadratic for smooth late ramp

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
