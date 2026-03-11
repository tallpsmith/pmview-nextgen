extends Control

## Playback controls for time cursor: start time picker, play/pause/resume,
## speed slider, current position display, and reset-to-live button.
## Connects to MetricPoller C# bridge for cursor control.

signal playback_started(start_time: String)
signal playback_paused()
signal playback_resumed()
signal reset_to_live()
signal speed_changed(speed: float)

@onready var mode_label: Label = $VBoxContainer/ModeLabel
@onready var position_label: Label = $VBoxContainer/PositionLabel
@onready var start_time_input: LineEdit = $VBoxContainer/TimeControls/StartTimeInput
@onready var play_button: Button = $VBoxContainer/TimeControls/PlayButton
@onready var pause_button: Button = $VBoxContainer/TimeControls/PauseButton
@onready var live_button: Button = $VBoxContainer/TimeControls/LiveButton
@onready var speed_slider: HSlider = $VBoxContainer/SpeedControls/SpeedSlider
@onready var speed_label: Label = $VBoxContainer/SpeedControls/SpeedLabel

var _metric_poller: Node
var _current_mode: String = "Live"


func _ready() -> void:
	_metric_poller = get_node_or_null("/root/Main/MetricPoller")

	play_button.pressed.connect(_on_play_pressed)
	pause_button.pressed.connect(_on_pause_pressed)
	live_button.pressed.connect(_on_live_pressed)
	speed_slider.value_changed.connect(_on_speed_changed)

	# Speed slider: 0.1x to 100x, default 1x, logarithmic feel
	speed_slider.min_value = 0.1
	speed_slider.max_value = 100.0
	speed_slider.value = 1.0
	speed_slider.step = 0.1

	# Default time input to 1 hour ago (ISO 8601)
	var now = Time.get_datetime_dict_from_system(true)
	now["hour"] = now["hour"] - 1
	start_time_input.placeholder_text = "YYYY-MM-DD HH:MM:SS (UTC)"

	_update_ui()


func _on_play_pressed() -> void:
	var time_str = start_time_input.text.strip_edges()
	if time_str == "":
		push_warning("[PlaybackControls] No start time specified")
		return

	if _current_mode == "Paused":
		_current_mode = "Playback"
		playback_resumed.emit()
		if _metric_poller:
			_metric_poller.call("ResumePlayback")
	else:
		_current_mode = "Playback"
		playback_started.emit(time_str)
		if _metric_poller:
			_metric_poller.call("StartPlayback", time_str)

	_update_ui()


func _on_pause_pressed() -> void:
	_current_mode = "Paused"
	playback_paused.emit()
	if _metric_poller:
		_metric_poller.call("PausePlayback")
	_update_ui()


func _on_live_pressed() -> void:
	_current_mode = "Live"
	reset_to_live.emit()
	if _metric_poller:
		_metric_poller.call("ResetToLive")
	_update_ui()


func _on_speed_changed(value: float) -> void:
	speed_label.text = "%.1fx" % value
	speed_changed.emit(value)
	if _metric_poller:
		_metric_poller.call("SetPlaybackSpeed", value)


func update_position(position_text: String) -> void:
	position_label.text = "Position: %s" % position_text


func update_mode(mode: String) -> void:
	_current_mode = mode
	_update_ui()


func _update_ui() -> void:
	mode_label.text = "Mode: %s" % _current_mode

	match _current_mode:
		"Live":
			play_button.text = "Play"
			play_button.disabled = false
			pause_button.disabled = true
			live_button.disabled = true
			start_time_input.editable = true
			position_label.text = "Position: (live)"
		"Playback":
			play_button.text = "Play"
			play_button.disabled = true
			pause_button.disabled = false
			live_button.disabled = false
			start_time_input.editable = false
		"Paused":
			play_button.text = "Resume"
			play_button.disabled = false
			pause_button.disabled = true
			live_button.disabled = false
			start_time_input.editable = false
