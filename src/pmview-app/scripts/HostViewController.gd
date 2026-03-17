extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay.

@onready var esc_label: Label = %EscLabel

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null


func _ready() -> void:
	print("[HostView] _ready called")
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("[HostView] No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	SceneManager.built_scene = null
	print("[HostView] Built scene name: %s, script: %s, child count: %d" % [scene.name, scene.get_script(), scene.get_child_count()])
	for child in scene.get_children():
		print("[HostView]   child: %s (type: %s, script: %s)" % [child.name, child.get_class(), child.get_script()])

	# The built scene's root has host_view_controller.gd (from the addon)
	# which handles MetricPoller ↔ SceneBinder wiring in its own _ready().
	print("[HostView] Adding built scene to tree...")
	add_child(scene)
	print("[HostView] Built scene added to tree")


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if _esc_pending:
			SceneManager.go_to_main_menu()
		else:
			_esc_pending = true
			esc_label.visible = true
			_esc_timer = get_tree().create_timer(2.0)
			_esc_timer.timeout.connect(_dismiss_esc)


func _dismiss_esc() -> void:
	_esc_pending = false
	esc_label.visible = false


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		SceneManager.quit_app()
