extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay.

@onready var esc_label: Label = %EscLabel

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null


func _ready() -> void:
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	# Reparent the built scene into this scene tree
	SceneManager.built_scene = null  # Take ownership
	add_child(scene)

	# Wire up MetricPoller → SceneBinder
	var poller = scene.get_node("MetricPoller")
	var binder = scene.get_node("SceneBinder")
	if poller and binder:
		var metric_names = binder.Call("BindFromSceneProperties", scene)
		poller.Set("MetricNames", metric_names)
		poller.Call("StartPolling")
		poller.Connect("MetricsUpdated", Callable(binder, "ApplyMetrics"))


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if _esc_pending:
			SceneManager.go_to_main_menu()
		else:
			_esc_pending = true
			esc_label.visible = true
			_esc_timer = get_tree().create_timer(2.0)
			_esc_timer.timeout.connect(_dismiss_esc)
		get_viewport().set_input_as_handled()


func _dismiss_esc() -> void:
	_esc_pending = false
	esc_label.visible = false


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		SceneManager.quit_app()
