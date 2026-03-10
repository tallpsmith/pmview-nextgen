extends Control

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Intentionally thin.

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var status_label: Label = $StatusOverlay/StatusLabel

var _connection_state: String = "Disconnected"

func _ready() -> void:
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	scene_binder.connect("SceneLoaded", _on_scene_loaded)
	scene_binder.connect("BindingError", _on_binding_error)

	_update_status_display()

func load_config(config_path: String) -> void:
	var metric_names = scene_binder.call("LoadSceneWithBindings", config_path)
	if metric_names.size() > 0:
		metric_poller.set("MetricNames", metric_names)
		metric_poller.call("StartPolling")

func _on_metrics_updated(metrics: Dictionary) -> void:
	scene_binder.call("ApplyMetrics", metrics)

func _on_connection_state_changed(state: String) -> void:
	_connection_state = state
	_update_status_display()

func _on_error_occurred(message: String) -> void:
	push_warning("[MetricSceneController] Error: %s" % message)
	_update_status_display()

func _on_scene_loaded(scene_path: String, config_path: String) -> void:
	print("[MetricSceneController] Scene loaded: %s with %s" % [scene_path, config_path])

func _on_binding_error(message: String) -> void:
	push_warning("[MetricSceneController] Binding error: %s" % message)

func _update_status_display() -> void:
	if status_label:
		status_label.text = "Connection: %s" % _connection_state
		match _connection_state:
			"Connected":
				status_label.add_theme_color_override("font_color", Color.GREEN)
			"Reconnecting", "Connecting":
				status_label.add_theme_color_override("font_color", Color.YELLOW)
			_:
				status_label.add_theme_color_override("font_color", Color.RED)
