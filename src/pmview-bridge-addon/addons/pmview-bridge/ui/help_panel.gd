extends PanelContainer

## Floating translucent help panel listing controls in grouped sections.
## Toggle with show_panel() / hide_panel() / toggle().
## Content set via set_groups() — the addon provides the mechanism,
## the consuming app provides the content.

signal panel_opened
signal panel_closed

const HEADER_FONT_SIZE := 11
const KEY_FONT_SIZE := 13
const ACTION_FONT_SIZE := 13
const KEY_COLUMN_WIDTH := 110
const DISABLED_OPACITY := 0.3

var _groups: Array = []  # Array of HelpGroup
var _group_containers: Dictionary = {}  # group_name -> VBoxContainer


func _ready() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_STOP


func set_groups(groups: Array) -> void:
	_groups = groups
	_rebuild_ui()


func set_group_enabled(group_name: String, enabled: bool) -> void:
	for group in _groups:
		if group.group_name == group_name:
			group.enabled = enabled
	if _group_containers.has(group_name):
		var container: VBoxContainer = _group_containers[group_name]
		container.modulate.a = 1.0 if enabled else DISABLED_OPACITY


func set_entry_enabled(group_name: String, key_text: String, enabled: bool) -> void:
	for group in _groups:
		if group.group_name == group_name:
			for entry in group.entries:
				if entry.key_text == key_text:
					entry.enabled = enabled
	# Rebuild is simplest for per-entry changes
	_rebuild_ui()


func toggle() -> void:
	if visible:
		hide_panel()
	else:
		show_panel()


func show_panel() -> void:
	if visible:
		return
	visible = true
	panel_opened.emit()


func hide_panel() -> void:
	if not visible:
		return
	visible = false
	panel_closed.emit()


func _rebuild_ui() -> void:
	var content: VBoxContainer = %Content
	# Clear existing children (immediate free — these are our own UI nodes)
	for child in content.get_children():
		content.remove_child(child)
		child.free()
	_group_containers.clear()

	for group in _groups:
		var group_box := VBoxContainer.new()
		group_box.add_theme_constant_override("separation", 4)
		if not group.enabled:
			group_box.modulate.a = DISABLED_OPACITY
		_group_containers[group.group_name] = group_box

		# Group header
		var header := Label.new()
		header.text = group.group_name.to_upper()
		if not group.enabled:
			header.text += "  (archive mode only)"
		header.add_theme_font_size_override("font_size", HEADER_FONT_SIZE)
		header.add_theme_color_override("font_color", group.header_color)
		header.uppercase = true
		group_box.add_child(header)

		# Entries grid
		var grid := GridContainer.new()
		grid.columns = 2
		grid.add_theme_constant_override("h_separation", 12)
		grid.add_theme_constant_override("v_separation", 4)

		for entry in group.entries:
			var key_label := Label.new()
			key_label.text = entry.key_text
			key_label.custom_minimum_size.x = KEY_COLUMN_WIDTH
			key_label.add_theme_font_size_override("font_size", KEY_FONT_SIZE)
			key_label.add_theme_color_override("font_color", Color(0.878, 0.816, 1.0))
			if not entry.enabled:
				key_label.modulate.a = DISABLED_OPACITY

			var action_label := Label.new()
			action_label.text = entry.action_text
			action_label.add_theme_font_size_override("font_size", ACTION_FONT_SIZE)
			action_label.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 0.7))
			if not entry.enabled:
				action_label.modulate.a = DISABLED_OPACITY
				var suffix := ""
				# Check if this is a per-entry disable (not whole group)
				if group.enabled:
					suffix = "  (archive)"
					action_label.text += suffix

			grid.add_child(key_label)
			grid.add_child(action_label)

		group_box.add_child(grid)
		content.add_child(group_box)

		# Spacer between groups (except last)
		if group != _groups.back():
			var spacer := Control.new()
			spacer.custom_minimum_size.y = 12
			content.add_child(spacer)
