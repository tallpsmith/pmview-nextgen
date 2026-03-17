extends Node3D

## Main menu controller — rotates 3D title letters, handles connection
## form submission, and drives the KITT scanner hover effect on LAUNCH.

# --- Title letter rotation ---
const ROTATION_SPEED := 0.3         # radians/sec base Y rotation
const PHASE_OFFSET := 0.4           # radians offset per letter for wave effect

@onready var title_group: Node3D = $TitleGroup
@onready var endpoint_input: LineEdit = %EndpointInput
@onready var launch_panel: Panel = %LaunchPanel
@onready var kitt_rect: ColorRect = %KittRect

var _letter_nodes: Array[Node3D] = []
var _sweep_tween: Tween = null


func _ready() -> void:
	# Gather all letter MeshInstance3D children for rotation
	for child in title_group.get_children():
		if child is MeshInstance3D:
			_letter_nodes.append(child)

	# Wire up LAUNCH panel hover signals
	launch_panel.mouse_entered.connect(_on_launch_hover)
	launch_panel.mouse_exited.connect(_on_launch_unhover)

	# Wire up LAUNCH panel click
	launch_panel.gui_input.connect(_on_launch_gui_input)


func _process(delta: float) -> void:
	_rotate_title_letters(delta)


func _rotate_title_letters(delta: float) -> void:
	for i in _letter_nodes.size():
		var letter := _letter_nodes[i]
		var phase := i * PHASE_OFFSET
		letter.rotate_y((ROTATION_SPEED + sin(Time.get_ticks_msec() * 0.001 + phase) * 0.15) * delta)


# --- LAUNCH button hover: KITT scanner effect ---

func _on_launch_hover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return

	# Fade intensity in
	mat.set_shader_parameter("intensity", 1.0)

	# Start ping-pong sweep
	_kill_sweep_tween()
	_sweep_tween = create_tween().set_loops()
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", 1.4, 0.9)
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", -0.4, 0.9)


func _on_launch_unhover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return

	_kill_sweep_tween()

	# Fade intensity out
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
