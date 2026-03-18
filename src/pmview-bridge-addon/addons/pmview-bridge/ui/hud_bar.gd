extends PanelContainer

## Persistent keybinding HUD bar at the bottom of the viewport.
## Shows available controls. Highlights F1 when the tuner is active.

@onready var f1_label: Label = %F1Label

var _tuner_active: bool = false


func set_tuner_active(active: bool) -> void:
	_tuner_active = active
	if f1_label:
		f1_label.add_theme_color_override(
			"font_color", Color(1.0, 0.95, 0.8) if active else Color(0.97, 0.57, 0.09))
