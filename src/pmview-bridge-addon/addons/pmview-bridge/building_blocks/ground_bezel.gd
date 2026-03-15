@tool
class_name GroundBezel
extends MeshInstance3D

## Colour of the ground slab.
@export var bezel_colour: Color = Color(0.3, 0.3, 0.3, 1.0):
	set(value):
		bezel_colour = value
		_apply_colour()

## Padding around the content extent on each side.
@export var padding: float = 0.4:
	set(value):
		padding = value
		_rebuild_mesh()

var _width: float = 0.0
var _depth: float = 0.0

func _ready() -> void:
	_rebuild_mesh()
	_apply_colour()

## Called by MetricGroupNode when the grid extent changes.
func resize(width: float, depth: float) -> void:
	if _width == width and _depth == depth:
		return  # skip rebuild if dimensions unchanged
	_width = width
	_depth = depth
	_rebuild_mesh()

func _rebuild_mesh() -> void:
	if _width <= 0.0 or _depth <= 0.0:
		mesh = null
		return
	var padded_w := _width + padding * 2.0
	var padded_d := _depth + padding * 2.0
	var box := BoxMesh.new()
	box.size = Vector3(padded_w, 0.02, padded_d)
	mesh = box

func _apply_colour() -> void:
	var mat := get_surface_override_material(0)
	if mat == null:
		mat = StandardMaterial3D.new()
		set_surface_override_material(0, mat)
	if mat is StandardMaterial3D:
		mat.albedo_color = bezel_colour
