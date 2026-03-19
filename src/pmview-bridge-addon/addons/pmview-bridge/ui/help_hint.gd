extends PanelContainer

## Subtle cycling hint in the bottom-right corner.
## Rotates through tips (15s visible, then 60s silent).
## Hidden when the HelpPanel is open or TimeControl is revealed.

signal hint_visibility_changed(is_visible: bool)

const DISPLAY_DURATION := 10.0
const SILENT_DURATION := 60.0
const FADE_DURATION := 0.5

var _hints: Array = []  # Array of HelpHintEntry
var _current_index: int = 0
var _is_cycling: bool = false
var _suppressed: bool = false  # external suppression (panel open, time control overlap)

@onready var _key_label: Label = %KeyLabel
@onready var _action_label: Label = %ActionLabel
@onready var _display_timer: Timer = %DisplayTimer
@onready var _silent_timer: Timer = %SilentTimer
var _fade_tween: Tween = null


func _ready() -> void:
	modulate.a = 0.0
	visible = true  # always in tree, use alpha for visibility
	_display_timer.timeout.connect(_on_display_timeout)
	_silent_timer.timeout.connect(_on_silent_timeout)


func set_hints(hints: Array) -> void:
	_hints = hints
	_current_index = 0


func start_cycling() -> void:
	if _hints.is_empty():
		return
	_is_cycling = true
	_current_index = 0
	_show_current_hint()


func stop_cycling() -> void:
	_is_cycling = false
	_display_timer.stop()
	_silent_timer.stop()
	_fade_out()


func hide_hint() -> void:
	_suppressed = true
	_fade_out()
	visibility_changed.emit(false)


func resume_hint() -> void:
	_suppressed = false
	# Don't immediately show — let the current cycle state drive it.
	# If we were in the middle of displaying, the timer will handle it.


func _show_current_hint() -> void:
	if _hints.is_empty() or _suppressed:
		return
	var hint = _hints[_current_index]
	_key_label.text = hint.key_text
	_action_label.text = hint.action_text
	_fade_in()
	_display_timer.start(DISPLAY_DURATION)
	visibility_changed.emit(true)


func _on_display_timeout() -> void:
	_fade_out()
	_current_index += 1
	if _current_index < _hints.size():
		# Show next hint after fade completes
		get_tree().create_timer(FADE_DURATION).timeout.connect(_show_current_hint)
	else:
		# All hints shown — enter silent period
		_current_index = 0
		_silent_timer.start(SILENT_DURATION)
	visibility_changed.emit(false)


func _on_silent_timeout() -> void:
	if _is_cycling and not _suppressed:
		_show_current_hint()


func _fade_in() -> void:
	if _fade_tween:
		_fade_tween.kill()
	_fade_tween = create_tween()
	_fade_tween.tween_property(self, "modulate:a", 1.0, FADE_DURATION)


func _fade_out() -> void:
	if _fade_tween:
		_fade_tween.kill()
	_fade_tween = create_tween()
	_fade_tween.tween_property(self, "modulate:a", 0.0, FADE_DURATION)
