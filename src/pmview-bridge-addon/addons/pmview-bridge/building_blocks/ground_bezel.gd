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
var _collision_shape: CollisionShape3D = null

func _ready() -> void:
	_rebuild_mesh()
	_apply_colour()
	_build_collision_body()

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
	_apply_colour()
	_update_collision_shape(padded_w, padded_d)

func _apply_colour() -> void:
	if mesh == null:
		return  # mesh not built yet — _rebuild_mesh() will trigger _apply_colour()
	var mat := get_surface_override_material(0)
	if mat == null:
		mat = StandardMaterial3D.new()
		set_surface_override_material(0, mat)
	if mat is StandardMaterial3D:
		mat.albedo_color = bezel_colour


func _build_collision_body() -> void:
	if Engine.is_editor_hint():
		return  # no collision in @tool mode
	var body := StaticBody3D.new()
	body.name = "BezelBody"
	body.collision_layer = 2  # same layer as grounded_bar/cylinder
	body.collision_mask = 0
	_collision_shape = CollisionShape3D.new()
	_collision_shape.name = "BezelCollision"
	body.add_child(_collision_shape)
	add_child(body)


func _update_collision_shape(padded_w: float, padded_d: float) -> void:
	if _collision_shape == null:
		return
	var box := BoxShape3D.new()
	box.size = Vector3(padded_w, 0.1, padded_d)  # slightly taller than mesh for easier clicking
	_collision_shape.shape = box
