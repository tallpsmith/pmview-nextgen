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

	# Lock camera to known starting position before fly-by
	camera.position = CAM_START
	camera.rotation = ROT_START

	await _run_flyby_sequence()

	# Transition to host view
	SceneManager.go_to_host_view(pipeline.BuiltScene)


func _run_flyby_sequence() -> void:
	var t1 := FLYBY_APPROACH_DURATION
	var t2 := FLYBY_SWEEP_DURATION
	var t3 := FLYBY_HYPERSPACE_DURATION

	# Single continuous position tween — no velocity discontinuities between phases
	var pos_tween := create_tween()
	pos_tween.tween_property(camera, "position", CAM_APPROACH_END, t1) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	pos_tween.tween_property(camera, "position", CAM_SWEEP_END, t2) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	pos_tween.tween_property(camera, "position", CAM_HYPERSPACE_END, t3) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Single continuous rotation tween — smooth banking throughout
	var rot_tween := create_tween()
	rot_tween.tween_property(camera, "rotation", ROT_APPROACH_PEAK, t1 * 0.5) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	rot_tween.tween_property(camera, "rotation", ROT_APPROACH_END, t1 * 0.5) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	rot_tween.tween_property(camera, "rotation", ROT_SWEEP, t2) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	rot_tween.tween_property(camera, "rotation", ROT_HYPERSPACE, t3 * 0.3) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

	# Ground shader: continuous speed ramp across sweep + hyperspace
	var shader_tween := create_tween()
	shader_tween.tween_interval(t1)  # Hold at 0.0 during approach
	shader_tween.tween_property(ground_mat, "shader_parameter/speed", 0.6, t2) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	shader_tween.tween_property(ground_mat, "shader_parameter/speed", 1.0, t3) \
		.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	# Streak angle: rotate toward forward during hyperspace
	var angle_tween := create_tween()
	angle_tween.tween_interval(t1 + t2)  # Hold at 0.0 during approach + sweep
	angle_tween.tween_property(ground_mat, "shader_parameter/streak_angle", PI / 2.0, t3) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)

	# White-out: fade to white during hyperspace only
	var white_tween := create_tween()
	white_tween.tween_interval(t1 + t2)  # Hold transparent during approach + sweep
	white_tween.tween_property(white_out, "modulate:a", 1.0, t3) \
		.set_trans(Tween.TRANS_EXPO).set_ease(Tween.EASE_IN)

	await pos_tween.finished


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
