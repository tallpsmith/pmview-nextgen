extends Control

## Detail panel — shows metric bindings and live values for a selected shape.
## Appears top-right when a shape is selected. Non-input-blocking.

signal panel_opened
signal panel_closed

var _header_label: Label = null
var _content_container: VBoxContainer = null
var _value_labels: Dictionary = {}  # metric_name -> Label

func _ready() -> void:
	visible = false
	_build_layout()

func _build_layout() -> void:
	# Panel background
	var panel := PanelContainer.new()
	panel.anchor_left = 1.0
	panel.anchor_right = 1.0
	panel.anchor_top = 0.0
	panel.anchor_bottom = 0.0
	panel.offset_left = -420
	panel.offset_right = -10
	panel.offset_top = 10
	panel.grow_horizontal = Control.GROW_DIRECTION_BEGIN

	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.08, 0.08, 0.16, 0.92)
	style.border_color = Color(1, 1, 1, 0.15)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.set_content_margin_all(16)
	panel.add_theme_stylebox_override("panel", style)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)

	_header_label = Label.new()
	_header_label.add_theme_font_size_override("font_size", 20)
	_header_label.add_theme_color_override("font_color", Color.WHITE)
	vbox.add_child(_header_label)

	var sep := HSeparator.new()
	sep.add_theme_color_override("separator", Color(1, 1, 1, 0.1))
	vbox.add_child(sep)

	_content_container = VBoxContainer.new()
	_content_container.add_theme_constant_override("separation", 2)
	vbox.add_child(_content_container)

	panel.add_child(vbox)
	add_child(panel)

func show_for_shape(bindings_dict: Dictionary) -> void:
	_value_labels.clear()
	for child in _content_container.get_children():
		child.queue_free()

	var zone: String = bindings_dict.get("zone", "")
	var instance: String = bindings_dict.get("instance", "")
	_header_label.text = "%s • %s" % [zone, instance] if instance else zone

	var properties: Dictionary = bindings_dict.get("properties", {})
	for prop_name: String in properties:
		# Property group header
		var prop_label := Label.new()
		prop_label.text = prop_name
		prop_label.add_theme_font_size_override("font_size", 15)
		prop_label.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
		_content_container.add_child(prop_label)

		var entries: Array = properties[prop_name]
		for entry: Dictionary in entries:
			var metric_name: String = entry.get("metric", "")
			var row := _create_metric_row(metric_name, entry.get("value"))
			_content_container.add_child(row)

	visible = true
	panel_opened.emit()

func update_values(bindings_dict: Dictionary) -> void:
	var properties: Dictionary = bindings_dict.get("properties", {})
	for prop_name: String in properties:
		var entries: Array = properties[prop_name]
		for entry: Dictionary in entries:
			var metric_name: String = entry.get("metric", "")
			if _value_labels.has(metric_name):
				var value = entry.get("value")
				_value_labels[metric_name].text = _format_value(value)

func close_panel() -> void:
	visible = false
	_value_labels.clear()
	panel_closed.emit()

func _create_metric_row(metric_name: String, value) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)

	# Short metric name (strip prefix, e.g., "kernel.cpu.user" -> "user")
	var short_name := metric_name.rsplit(".", true, 1)[-1] if "." in metric_name else metric_name
	var name_label := Label.new()
	name_label.text = "  " + short_name
	name_label.add_theme_font_size_override("font_size", 16)
	name_label.add_theme_color_override("font_color", Color(0.65, 0.65, 0.65))
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(name_label)

	var value_label := Label.new()
	value_label.text = _format_value(value)
	value_label.add_theme_font_size_override("font_size", 16)
	value_label.add_theme_color_override("font_color", Color.WHITE)
	value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(value_label)

	_value_labels[metric_name] = value_label
	return row

func _format_value(value) -> String:
	if value == null:
		return "N/A"
	if value is float:
		return "%.1f" % value
	return str(value)
