extends Node

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Loads scenes with editor-integrated bindings.
## Toggle metric browser with F2, playback controls with F3.

@export var default_scene: String = "res://scenes/test_bars.tscn"

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var metric_browser_bridge: Node = $MetricBrowser
@onready var status_label: Label = $UIOverlay/StatusLabel
@onready var metric_browser_ui: Control = $UIOverlay/MetricBrowser
@onready var playback_controls_ui: Control = $UIOverlay/PlaybackControls

var _connection_state: String = "Disconnected"

# World configuration from ProjectSettings (set by pmview-bridge plugin)
var _launch_endpoint: String = "http://localhost:44322"
var _launch_mode: int = 0  # 0=Archive, 1=Live
var _launch_timestamp: String = ""
var _launch_speed: float = 10.0
var _launch_loop: bool = false

func _ready() -> void:
	_read_launch_settings()
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	metric_poller.connect("PlaybackPositionChanged", _on_playback_position_changed)
	scene_binder.connect("BindingError", _on_binding_error)

	# Wire metric browser: when a metric is chosen, add it to the poller
	if metric_browser_ui:
		metric_browser_ui.connect("metric_chosen", _on_metric_chosen)
		metric_browser_ui.visible = false

	# Hide playback controls by default
	if playback_controls_ui:
		playback_controls_ui.visible = false

	_update_status_display()

	# Load scene with editor-integrated bindings
	if default_scene != "":
		print("[MetricSceneController] Loading scene: %s" % default_scene)
		var metric_names = _load_scene_with_properties(default_scene)
		if metric_names.size() > 0:
			print("[MetricSceneController] Found %d metrics from scene properties" % metric_names.size())
			_start_polling_metrics(metric_names)
		else:
			print("[MetricSceneController] No bindings found in scene: %s" % default_scene)

	_apply_launch_settings()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F2:
			_toggle_metric_browser()
		elif event.keycode == KEY_F3:
			_toggle_playback_controls()

func _load_scene_with_properties(scene_path: String) -> PackedStringArray:
	var packed = load(scene_path) as PackedScene
	if packed == null:
		print("[MetricSceneController] Cannot load scene: %s" % scene_path)
		return PackedStringArray()
	var scene_instance = packed.instantiate()
	scene_binder.add_child(scene_instance)
	var metric_names = scene_binder.call("BindFromSceneProperties", scene_instance)
	return metric_names

func _start_polling_metrics(metric_names: PackedStringArray) -> void:
	if metric_names.size() > 0:
		metric_poller.call("UpdateMetricNames", metric_names)
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

func _read_launch_settings() -> void:
	_launch_endpoint = ProjectSettings.get_setting("pmview/endpoint", "http://localhost:44322")
	_launch_mode = ProjectSettings.get_setting("pmview/mode", 0)
	_launch_timestamp = ProjectSettings.get_setting("pmview/archive_start_timestamp", "")
	_launch_speed = ProjectSettings.get_setting("pmview/archive_speed", 10.0)
	_launch_loop = ProjectSettings.get_setting("pmview/archive_loop", false)
	print("[MetricSceneController] Launch settings: mode=%d endpoint=%s speed=%.1f loop=%s" % [
		_launch_mode, _launch_endpoint, _launch_speed, _launch_loop])

func _apply_launch_settings() -> void:
	# Override endpoint if it differs from the default
	if _launch_endpoint != "http://localhost:44322":
		print("[MetricSceneController] Overriding endpoint from ProjectSettings: %s" % _launch_endpoint)
		metric_poller.set("Endpoint", _launch_endpoint)

	if _launch_mode == 0:  # Archive
		var timestamp = _launch_timestamp
		if timestamp == "":
			# Empty timestamp -> 24 hours before now
			var now = Time.get_unix_time_from_system()
			var day_ago = now - 86400.0
			timestamp = Time.get_datetime_string_from_unix_time(int(day_ago)) + "Z"
			print("[MetricSceneController] Empty timestamp, using 24h ago: %s" % timestamp)

		# Set speed/loop before StartPlayback — StartPlayback is async and discovers
		# EndBound from the server. Loop + EndBound are checked together at AdvanceBy() time.
		metric_poller.call("SetPlaybackSpeed", _launch_speed)
		metric_poller.call("SetLoop", _launch_loop)
		metric_poller.call("StartPlayback", timestamp)
		print("[MetricSceneController] Archive mode: timestamp=%s speed=%.1f loop=%s" % [
			timestamp, _launch_speed, _launch_loop])
	elif _launch_mode == 1:  # Live
		metric_poller.call("ResetToLive")
		print("[MetricSceneController] Live mode: archive settings ignored")

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
