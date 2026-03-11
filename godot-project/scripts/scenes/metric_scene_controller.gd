extends Node

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Auto-loads default config on start.
## Toggle metric browser with F2, playback controls with F3.

@export var default_config: String = "res://bindings/test_bars.toml"

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var metric_browser_bridge: Node = $MetricBrowser
@onready var status_label: Label = $UIOverlay/StatusLabel
@onready var metric_browser_ui: Control = $UIOverlay/MetricBrowser
@onready var playback_controls_ui: Control = $UIOverlay/PlaybackControls

var _configs: Array[String] = []
var _current_config_index: int = -1
var _connection_state: String = "Disconnected"

func _ready() -> void:
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	metric_poller.connect("PlaybackPositionChanged", _on_playback_position_changed)
	scene_binder.connect("SceneLoaded", _on_scene_loaded)
	scene_binder.connect("BindingError", _on_binding_error)

	# Wire metric browser: when a metric is chosen, add it to the poller
	if metric_browser_ui:
		metric_browser_ui.connect("metric_chosen", _on_metric_chosen)
		metric_browser_ui.visible = false

	# Hide playback controls by default
	if playback_controls_ui:
		playback_controls_ui.visible = false

	_update_status_display()
	_scan_configs()

	# Auto-load the default binding config
	if default_config != "":
		print("[MetricSceneController] Auto-loading config: %s" % default_config)
		_current_config_index = _configs.find(default_config)
		load_config(default_config)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_TAB:
			_cycle_config()
		elif event.keycode == KEY_F2:
			_toggle_metric_browser()
		elif event.keycode == KEY_F3:
			_toggle_playback_controls()

func _scan_configs() -> void:
	_configs.clear()
	var dir = DirAccess.open("res://bindings")
	if dir == null:
		return
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if file_name.ends_with(".toml"):
			_configs.append("res://bindings/%s" % file_name)
		file_name = dir.get_next()
	dir.list_dir_end()
	_configs.sort()
	print("[MetricSceneController] Found %d configs: %s" % [_configs.size(), _configs])

func _cycle_config() -> void:
	if _configs.is_empty():
		return
	_current_config_index = (_current_config_index + 1) % _configs.size()
	var path = _configs[_current_config_index]
	print("[MetricSceneController] Switching to config: %s" % path)
	load_config(path)

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

	# Tell the C# MetricBrowser to grab the client from MetricPoller
	if state == "Connected" and metric_browser_bridge:
		metric_browser_bridge.call("ConnectToPoller", metric_poller)

func _on_error_occurred(message: String) -> void:
	print("[MetricSceneController] Error: %s" % message)
	_update_status_display()

func _on_scene_loaded(scene_path: String, config_path: String) -> void:
	print("[MetricSceneController] Scene loaded: %s with config %s" % [scene_path, config_path])
	# Replay cached values so new bindings get data immediately (no stale defaults)
	metric_poller.call("ReplayLastMetrics")

func _on_binding_error(message: String) -> void:
	print("[MetricSceneController] Binding error: %s" % message)

func _on_metric_chosen(metric_name: String) -> void:
	print("[MetricSceneController] Metric chosen from browser: %s" % metric_name)
	# Add the chosen metric to the current polling set
	var current_names: Array = Array(metric_poller.get("MetricNames"))
	if metric_name not in current_names:
		current_names.append(metric_name)
		metric_poller.call("UpdateMetricNames", PackedStringArray(current_names))
		print("[MetricSceneController] Now polling: %s" % [current_names])

func _on_playback_position_changed(position: String, mode: String) -> void:
	if playback_controls_ui:
		playback_controls_ui.call("update_position", position)
		playback_controls_ui.call("update_mode", mode)

func _toggle_metric_browser() -> void:
	if metric_browser_ui:
		metric_browser_ui.visible = not metric_browser_ui.visible
		print("[MetricSceneController] Metric browser: %s" % (
			"shown" if metric_browser_ui.visible else "hidden"))

func _toggle_playback_controls() -> void:
	if playback_controls_ui:
		playback_controls_ui.visible = not playback_controls_ui.visible
		print("[MetricSceneController] Playback controls: %s" % (
			"shown" if playback_controls_ui.visible else "hidden"))

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
