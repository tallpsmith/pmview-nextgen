# warning_toast.gd
# Generic warning toast system — top-left HUD overlay with severity levels,
# auto-fade, and cooldown for recurring warnings. Reusable across scenes.
class_name WarningToast
extends Control

enum Severity { WARNING, ERROR }

const SEVERITY_PREFIXES := {
	Severity.WARNING: "⚠ ",
	Severity.ERROR: "✖ ",
}

const SEVERITY_COLOURS := {
	Severity.WARNING: Color(0.9, 0.5, 0.1, 0.4),
	Severity.ERROR: Color(0.9, 0.15, 0.1, 0.4),
}

var _active_toasts: Array[Control] = []
var _cooldowns: Dictionary = {}  # cooldown_key → expiry timestamp
const COOLDOWN_DURATION := 30.0
const TOAST_SPACING := 4
const TOAST_Y_START := 50.0


func show_toast(message: String, severity: int = Severity.WARNING,
		duration: float = 5.0, cooldown_key: String = "") -> void:
	# Check cooldown
	if not cooldown_key.is_empty():
		var now := Time.get_ticks_msec() / 1000.0
		if _cooldowns.has(cooldown_key) and now < _cooldowns[cooldown_key]:
			return
		_cooldowns[cooldown_key] = now + COOLDOWN_DURATION

	var toast := _create_toast_panel(message, severity)
	add_child(toast)
	_active_toasts.append(toast)
	_reposition_toasts()

	# Fade out after duration
	var tween := create_tween()
	tween.tween_interval(duration - 0.5)
	tween.tween_property(toast, "modulate:a", 0.0, 0.5)
	tween.tween_callback(_remove_toast.bind(toast))


func clear_all() -> void:
	for toast: Control in _active_toasts:
		toast.queue_free()
	_active_toasts.clear()


func _create_toast_panel(message: String, severity: int) -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	var bg_colour: Color = SEVERITY_COLOURS.get(
		severity, SEVERITY_COLOURS[Severity.WARNING])
	style.bg_color = bg_colour
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_right = 4
	style.corner_radius_bottom_left = 4
	style.content_margin_left = 12.0
	style.content_margin_right = 12.0
	style.content_margin_top = 6.0
	style.content_margin_bottom = 6.0
	panel.add_theme_stylebox_override("panel", style)

	var label := Label.new()
	var prefix: String = SEVERITY_PREFIXES.get(severity, "⚠ ")
	label.text = prefix + message
	var font := load("res://assets/fonts/PressStart2P-Regular.ttf")
	if font:
		label.add_theme_font_override("font", font)
	label.add_theme_font_size_override("font_size", 10)
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	panel.add_child(label)

	return panel


func _reposition_toasts() -> void:
	var y_offset := 0.0
	for toast: Control in _active_toasts:
		toast.position = Vector2(20, TOAST_Y_START + y_offset)
		y_offset += toast.size.y + TOAST_SPACING


func _remove_toast(toast: Control) -> void:
	_active_toasts.erase(toast)
	toast.queue_free()
	_reposition_toasts()
