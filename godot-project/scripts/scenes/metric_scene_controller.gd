extends Node

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Auto-loads default config on start.

@export var default_config: String = "res://bindings/test_bars.toml"

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var status_label: Label = $UIOverlay/StatusLabel

var _connection_state: String = "Disconnected"

func _ready() -> void:
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	scene_binder.connect("SceneLoaded", _on_scene_loaded)
	scene_binder.connect("BindingError", _on_binding_error)

	_update_status_display()

	# Auto-load the default binding config
	if default_config != "":
		print("[MetricSceneController] Auto-loading config: %s" % default_config)
		load_config(default_config)

func load_config(config_path: String) -> void:
	print("[MetricSceneController] Loading config: %s" % config_path)
	metric_poller.call("StopPolling")
	var metric_names = scene_binder.call("LoadSceneWithBindings", config_path)
	print("[MetricSceneController] Metrics to poll: %s" % [metric_names])
	if metric_names.size() > 0:
		metric_poller.call("UpdateMetricNames", metric_names)
		var config = scene_binder.get("CurrentConfig")
		if config != null:
			var endpoint = config.Endpoint
			var poll_ms = config.PollIntervalMs
			if endpoint != null and endpoint != "":
				print("[MetricSceneController] Using endpoint from config: %s" % endpoint)
				metric_poller.call("UpdateEndpoint", endpoint, poll_ms)
			else:
				metric_poller.call("StartPolling")
		else:
			metric_poller.call("StartPolling")

func _on_metrics_updated(metrics: Dictionary) -> void:
	scene_binder.call("ApplyMetrics", metrics)

func _on_connection_state_changed(state: String) -> void:
	_connection_state = state
	print("[MetricSceneController] Connection state: %s" % state)
	_update_status_display()

func _on_error_occurred(message: String) -> void:
	print("[MetricSceneController] Error: %s" % message)
	_update_status_display()

func _on_scene_loaded(scene_path: String, config_path: String) -> void:
	print("[MetricSceneController] Scene loaded: %s with config %s" % [scene_path, config_path])

func _on_binding_error(message: String) -> void:
	print("[MetricSceneController] Binding error: %s" % message)

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
