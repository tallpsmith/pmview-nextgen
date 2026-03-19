extends PanelContainer

## Persistent keybinding HUD bar at the bottom of the viewport.
## Shows available controls. Highlights F1 when the tuner is active.

@export var font_size: int = 36

@onready var f1_label: Label = %F1Label

var _tuner_active: bool = false


func _ready() -> void:
	_apply_font_size()


func _apply_font_size() -> void:
	var keys_container = find_child("Keys", false, false)
	if not keys_container:
		return
	for child in keys_container.get_children():
		if child is Label:
			child.add_theme_font_size_override("font_size", font_size)


func set_tuner_active(active: bool) -> void:
	_tuner_active = active
	if f1_label:
		f1_label.add_theme_color_override(
			"font_color", Color(1.0, 0.95, 0.8) if active else Color(0.97, 0.57, 0.09))
