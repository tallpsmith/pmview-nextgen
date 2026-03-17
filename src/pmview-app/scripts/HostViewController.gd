extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay.

@onready var esc_label: Label = %EscLabel

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null


func _ready() -> void:
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("[HostView] No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	SceneManager.built_scene = null

	# Add to tree first — MetricPoller._Ready() sees MetricNames
	# (populated by RuntimeSceneBuilder) and auto-starts polling from C#.
	add_child(scene)

	# Wire up signal connections and resolve bindings after everything
	# is in the tree and _Ready() has fired on all children.
	var poller = scene.find_child("MetricPoller")
	var binder = scene.find_child("SceneBinder")
	if not poller or not binder:
		push_error("[HostView] MetricPoller or SceneBinder not found in built scene")
		return

	var metric_names: PackedStringArray = binder.call("BindFromSceneProperties", scene)
	print("[HostView] Bound %d metric names, polling auto-started from _Ready" % metric_names.size())

	poller.connect("MetricsUpdated", Callable(binder, "ApplyMetrics"))
	poller.connect("ErrorOccurred", _on_poller_error)
	poller.connect("ConnectionStateChanged", _on_poller_state)


func _on_poller_error(message: String) -> void:
	push_error("[HostView] MetricPoller error: %s" % message)


func _on_poller_state(state: String) -> void:
	print("[HostView] MetricPoller state: %s" % state)


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
