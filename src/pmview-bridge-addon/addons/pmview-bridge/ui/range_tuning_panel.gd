extends Control

## Range tuning modal — preset buttons for disk and network hardware speeds.
## Click a preset to instantly apply via SceneBinder.UpdateSourceRangeMax().
## F1 toggles open/close. ESC also closes.

signal panel_opened
signal panel_closed

# -- Zone definitions: each maps a display label to API zone name(s) --
# Disk and Per-Disk share the same presets.
const DISK_PRESETS: Dictionary = {
	"HDD": 150_000_000.0,
	"SATA SSD": 550_000_000.0,
	"NVMe Gen3": 3_500_000_000.0,
	"NVMe Gen4": 7_000_000_000.0,
	"NVMe Gen5": 14_000_000_000.0,
}

const NETWORK_PRESETS: Dictionary = {
	"1 Gbit": 125_000_000.0,
	"10 Gbit": 1_250_000_000.0,
	"25 Gbit": 3_125_000_000.0,
	"40 Gbit": 5_000_000_000.0,
	"100 Gbit": 12_500_000_000.0,
}

# Zone column config: [display_label, api_zone_names, presets, colour]
const ZONE_COLUMNS: Array = [
	["Disk Total", ["Disk"], "disk", Color(0.97, 0.57, 0.09)],
	["Per-Disk", ["Per-Disk"], "disk", Color(0.13, 0.77, 0.37)],
	["Network", ["Network In", "Network Out"], "network", Color(0.23, 0.51, 0.96)],
]

var _scene_binder: Node = null
var _camera: Node = null
var _columns: Dictionary = {}  # zone_api_name -> {container, buttons, active_value}

@onready var _overlay: ColorRect = %Overlay


func _ready() -> void:
	visible = false
	_overlay.color = Color(0, 0, 0, 0.35)
	_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE


func initialise(scene_binder: Node) -> void:
	_scene_binder = scene_binder
	# Find the camera for auto-focus
	_camera = get_viewport().get_camera_3d()
	if scene_binder.IsBound:
		_build_ui()
	else:
		scene_binder.connect("BindingsReady", _build_ui)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed:
		if event.physical_keycode == KEY_F1:
			_toggle_panel()
			get_viewport().set_input_as_handled()
		elif event.physical_keycode == KEY_ESCAPE and visible:
			_close_panel()
			get_viewport().set_input_as_handled()


func _toggle_panel() -> void:
	if visible:
		_close_panel()
	else:
		_open_panel()


func _open_panel() -> void:
	visible = true
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	panel_opened.emit()


func _close_panel() -> void:
	visible = false
	if _overlay:
		_overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel_closed.emit()


## Public API for external callers (panel exclusivity).
func close_panel() -> void:
	if visible:
		_close_panel()


func _build_ui() -> void:
	var ranges: Dictionary = _scene_binder.GetSourceRanges()

	# Build the column layout programmatically
	for col_def in ZONE_COLUMNS:
		var display_name: String = col_def[0]
		var api_zones: Array = col_def[1]
		var preset_type: String = col_def[2]
		var colour: Color = col_def[3]

		# Check if any of this column's zones are present
		var active_zone: String = ""
		var current_value: float = 0.0
		for zone_name in api_zones:
			if ranges.has(zone_name):
				active_zone = zone_name
				current_value = ranges[zone_name]
				break

		if active_zone.is_empty():
			continue  # Zone not present — skip column

		var presets: Dictionary = DISK_PRESETS if preset_type == "disk" else NETWORK_PRESETS
		_add_zone_column(display_name, api_zones, presets, colour, current_value)


func _add_zone_column(display_name: String, api_zones: Array,
		presets: Dictionary, colour: Color, current_value: float) -> void:
	var column: VBoxContainer = %ColumnsContainer.get_node_or_null(display_name.replace(" ", ""))
	if column == null:
		column = VBoxContainer.new()
		column.name = display_name.replace(" ", "")
		%ColumnsContainer.add_child(column)

	# Zone header
	var header := Label.new()
	header.text = display_name
	header.add_theme_color_override("font_color", colour)
	header.add_theme_font_size_override("font_size", 13)
	header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(header)

	# Preset buttons
	var buttons: Array[Button] = []
	for preset_name: String in presets:
		var bytes_val: float = presets[preset_name]
		var btn := Button.new()
		btn.text = "%s  %s" % [preset_name, _format_bytes(bytes_val)]
		btn.custom_minimum_size = Vector2(160, 0)
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT

		# Style: muted by default
		btn.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))

		# Highlight if this preset matches current value
		if absf(bytes_val - current_value) < 1.0:
			_highlight_button(btn, colour)

		btn.pressed.connect(_on_preset_pressed.bind(api_zones, bytes_val, colour, buttons, btn))
		column.add_child(btn)
		buttons.append(btn)

	_columns[display_name] = {"buttons": buttons, "colour": colour}


func _on_preset_pressed(api_zones: Array, bytes_val: float,
		colour: Color, all_buttons: Array[Button], pressed_btn: Button) -> void:
	# Apply to all API zones in this column
	for zone_name: String in api_zones:
		_scene_binder.UpdateSourceRangeMax(zone_name, bytes_val)

	# Update highlights — deselect all, highlight pressed
	for btn: Button in all_buttons:
		_unhighlight_button(btn)
	_highlight_button(pressed_btn, colour)

	# Auto-focus camera on the first zone
	if _camera and _camera.has_method("focus_on_position"):
		var centroid: Vector3 = _scene_binder.GetZoneCentroid(api_zones[0])
		if centroid != Vector3.ZERO:
			_camera.focus_on_position(centroid)


func _highlight_button(btn: Button, colour: Color) -> void:
	btn.add_theme_color_override("font_color", colour)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(colour, 0.15)
	style.border_color = colour
	style.set_border_width_all(1)
	style.set_corner_radius_all(4)
	style.set_content_margin_all(4)
	btn.add_theme_stylebox_override("normal", style)


func _unhighlight_button(btn: Button) -> void:
	btn.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	btn.remove_theme_stylebox_override("normal")


static func _format_bytes(bytes_per_sec: float) -> String:
	if bytes_per_sec >= 1_000_000_000.0:
		return "%.1f GB/s" % (bytes_per_sec / 1_000_000_000.0)
	elif bytes_per_sec >= 1_000_000.0:
		return "%.0f MB/s" % (bytes_per_sec / 1_000_000.0)
	elif bytes_per_sec >= 1_000.0:
		return "%.0f KB/s" % (bytes_per_sec / 1_000.0)
	else:
		return "%.0f B/s" % bytes_per_sec
