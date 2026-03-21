# compact_host.gd
# Miniature host representation for fleet view: 4 aggregate metric bars
# (CPU, Mem, Disk, Net) in a 2x2 grid with a ground bezel and licence plate label.
extends Node3D

const BAR_SCENE := preload("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn")

@export var hostname: String = "unknown":
	set(value):
		hostname = value
		if _label:
			_label.text = value

## Spacing between the 2x2 bar centres
@export var bar_spacing: float = 1.2

## Colours for each aggregate metric bar
const BAR_COLOURS := {
	"cpu": Color(0.2, 0.8, 0.2),       # green
	"memory": Color(0.8, 0.5, 0.2),    # orange
	"disk": Color(0.3, 0.5, 0.9),      # blue
	"network": Color(0.7, 0.4, 0.8),   # purple
}

## Mock heights (replaced by real polling later)
const MOCK_HEIGHTS := {
	"cpu": 0.6,
	"memory": 0.4,
	"disk": 0.3,
	"network": 0.2,
}

var _bars: Dictionary = {}
var _bezel: GroundBezel
var _label: Label3D
var _selected: bool = false
var _base_colours: Dictionary = {}  # original colours for opacity restore


func _ready() -> void:
	_build_bars()
	_build_bezel()
	_build_label()


func _build_bars() -> void:
	var positions := {
		"cpu":     Vector3(-bar_spacing / 2.0, 0, -bar_spacing / 2.0),
		"memory":  Vector3( bar_spacing / 2.0, 0, -bar_spacing / 2.0),
		"disk":    Vector3(-bar_spacing / 2.0, 0,  bar_spacing / 2.0),
		"network": Vector3( bar_spacing / 2.0, 0,  bar_spacing / 2.0),
	}
	for metric_name: String in positions:
		var bar: Node3D = BAR_SCENE.instantiate()
		bar.name = metric_name.capitalize()
		bar.position = positions[metric_name]
		bar.colour = BAR_COLOURS[metric_name]
		bar.height = MOCK_HEIGHTS[metric_name]
		add_child(bar)
		_bars[metric_name] = bar
		_base_colours[metric_name] = BAR_COLOURS[metric_name]


func _build_bezel() -> void:
	_bezel = GroundBezel.new()
	_bezel.name = "Bezel"
	add_child(_bezel)
	_bezel.bezel_colour = Color(0.2, 0.2, 0.25, 1.0)
	var extent := bar_spacing + 1.2  # bar width + padding
	_bezel.resize(extent, extent)


func _build_label() -> void:
	_label = Label3D.new()
	_label.name = "LicencePlate"
	_label.text = hostname
	_label.font_size = 32
	_label.pixel_size = 0.01
	_label.position = Vector3(0, 0.05, (bar_spacing / 2.0) + 0.8)
	_label.rotation_degrees = Vector3(-90, 0, 0)  # flat on ground
	_label.modulate = Color(0.7, 0.7, 0.7)
	var font := load("res://assets/fonts/PressStart2P-Regular.ttf")
	if font:
		_label.font = font
		_label.font_size = 24
	add_child(_label)


## Update a single metric bar height (called by poller later)
func set_metric_value(metric_name: String, value: float) -> void:
	if _bars.has(metric_name):
		_bars[metric_name].height = value


## Set opacity for translucent mode during focus.
## Works through GroundedShape's material system — sets albedo alpha
## and enables transparency mode on the StandardMaterial3D.
func set_opacity(alpha: float) -> void:
	for metric_name: String in _bars:
		var bar: Node3D = _bars[metric_name]
		var base_col: Color = _base_colours[metric_name]
		if alpha < 1.0:
			# Use ghost-like transparency via the colour's alpha channel
			var mesh_instance := _find_mesh_in(bar)
			if mesh_instance:
				var mat := mesh_instance.get_surface_override_material(0)
				if mat is StandardMaterial3D:
					mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
					mat.albedo_color = Color(base_col.r, base_col.g, base_col.b, alpha)
		else:
			# Restore full opacity — let GroundedShape handle it
			bar.colour = base_col
	if _bezel:
		var mat := _bezel.get_surface_override_material(0)
		if mat is StandardMaterial3D:
			if alpha < 1.0:
				mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
				mat.albedo_color = Color(
					_bezel.bezel_colour.r, _bezel.bezel_colour.g,
					_bezel.bezel_colour.b, alpha)
			else:
				mat.transparency = BaseMaterial3D.TRANSPARENCY_DISABLED
				mat.albedo_color = _bezel.bezel_colour
	if _label:
		_label.modulate.a = alpha


## Get the footprint extent (width, depth) for grid spacing calculations
func get_footprint() -> Vector2:
	var extent := bar_spacing + 1.2
	return Vector2(extent, extent)


## Find the MeshInstance3D child within a GroundedShape bar
func _find_mesh_in(node: Node3D) -> MeshInstance3D:
	for child in node.get_children():
		if child is MeshInstance3D:
			return child
	return null
