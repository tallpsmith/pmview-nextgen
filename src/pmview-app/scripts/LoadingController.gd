extends Node3D

## Loading screen controller. Maps six pipeline phases to P-M-V-I-E-W letter
## materialisation, then zoom-exits into the host view scene.

@onready var pipeline: Node = %LoadingPipeline
@onready var status_label: Label = %StatusLabel

@onready var letters := [$LetterP, $LetterM, $LetterV, $LetterI, $LetterE, $LetterW]

const PHASE_NAMES := [
	"CONNECTING",
	"FETCHING TOPOLOGY",
	"DISCOVERING INSTANCES",
	"SELECTING PROFILE",
	"COMPUTING LAYOUT",
	"BUILDING SCENE",
]

var _has_error := false


func _ready() -> void:
	var config := SceneManager.connection_config
	var endpoint: String = config.get("endpoint", "http://localhost:44322")

	pipeline.PhaseCompleted.connect(_on_phase_completed)
	pipeline.PipelineCompleted.connect(_on_pipeline_completed)
	pipeline.PipelineError.connect(_on_pipeline_error)

	pipeline.StartPipeline(endpoint)


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

	# Brief pause before zoom-exit
	await get_tree().create_timer(0.5).timeout

	# Zoom-exit: scale up and fade out all letters
	var tween := create_tween().set_parallel(true)
	for letter: MeshInstance3D in letters:
		tween.tween_property(letter, "scale", Vector3(5, 5, 5), 0.8)
		tween.tween_property(letter, "modulate:a" if letter.has_method("set_modulate") else "transparency", 1.0, 0.8)

	await tween.finished

	# Transition to host view with the built scene
	SceneManager.go_to_host_view(pipeline.BuiltScene)


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
