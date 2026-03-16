@tool
class_name GroundedShape
extends Node3D

## Height of the shape in world units. Maps to scale.y.
## Child mesh at y=0.5 means scaling Y grows upward from Y=0.
@export var height: float = 1.0:
	set(value):
		height = maxf(value, 0.01)
		scale.y = height

## Colour applied to the mesh material.
@export var colour: Color = Color.WHITE:
	set(value):
		colour = value
		_apply_colour()

## Ghost mode: desaturates to grey and applies transparency.
## Used for placeholder shapes where the metric is unavailable.
@export var ghost: bool = false:
	set(value):
		ghost = value
		_apply_colour()

func _ready() -> void:
	scale.y = height
	_apply_colour()

func _apply_colour() -> void:
	var mesh_instance := _find_mesh_instance()
	if mesh_instance == null:
		return
	var mat := mesh_instance.get_surface_override_material(0)
	if mat == null:
		mat = StandardMaterial3D.new()
		mesh_instance.set_surface_override_material(0, mat)
	if mat is StandardMaterial3D:
		if ghost:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.albedo_color = Color(0.5, 0.5, 0.5, 0.25)
		else:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_DISABLED
			mat.albedo_color = colour

func _find_mesh_instance() -> MeshInstance3D:
	for child in get_children():
		if child is MeshInstance3D:
			return child
	return null
