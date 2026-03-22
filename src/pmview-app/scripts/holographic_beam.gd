# holographic_beam.gd
# Builds a truncated cuboid mesh connecting a compact host (floor)
# to a floating detail view (ceiling). The pyramid flare is emergent
# from the size difference between the two.
extends MeshInstance3D

const BEAM_SHADER := preload("res://shaders/holographic_beam.gdshader")

## Floor dimensions (compact host footprint)
var floor_size: Vector2 = Vector2(3.0, 3.0)
## Ceiling dimensions (detail view footprint)
var ceiling_size: Vector2 = Vector2(20.0, 15.0)
## Height of the beam (floor Y to ceiling Y)
var beam_height: float = 15.0


func _ready() -> void:
	rebuild()


func rebuild() -> void:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)

	var verts := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()

	# Floor corners (Y = 0)
	var fw := floor_size.x / 2.0
	var fd := floor_size.y / 2.0
	var floor_corners := [
		Vector3(-fw, 0, -fd),  # 0: front-left
		Vector3( fw, 0, -fd),  # 1: front-right
		Vector3( fw, 0,  fd),  # 2: back-right
		Vector3(-fw, 0,  fd),  # 3: back-left
	]

	# Ceiling corners (Y = beam_height)
	var cw := ceiling_size.x / 2.0
	var cd := ceiling_size.y / 2.0
	var ceiling_corners := [
		Vector3(-cw, beam_height, -cd),  # 4: front-left
		Vector3( cw, beam_height, -cd),  # 5: front-right
		Vector3( cw, beam_height,  cd),  # 6: back-right
		Vector3(-cw, beam_height,  cd),  # 7: back-left
	]

	# Build 4 quad faces (front, right, back, left)
	var faces := [
		[0, 1, 5, 4],  # front
		[1, 2, 6, 5],  # right
		[2, 3, 7, 6],  # back
		[3, 0, 4, 7],  # left
	]

	for face_idx in range(4):
		var f: Array = faces[face_idx]
		var all_corners: Array[Vector3] = []
		all_corners.append_array(floor_corners)
		all_corners.append_array(ceiling_corners)
		var base := verts.size()
		for vi in f:
			verts.append(all_corners[vi])
		# UVs: bottom-left, bottom-right, top-right, top-left
		uvs.append(Vector2(0, 1))
		uvs.append(Vector2(1, 1))
		uvs.append(Vector2(1, 0))
		uvs.append(Vector2(0, 0))
		# Triangle 1
		indices.append(base + 0)
		indices.append(base + 1)
		indices.append(base + 2)
		# Triangle 2
		indices.append(base + 0)
		indices.append(base + 2)
		indices.append(base + 3)

	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices

	var arr_mesh := ArrayMesh.new()
	arr_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)

	var mat := ShaderMaterial.new()
	mat.shader = BEAM_SHADER
	mat.set_shader_parameter("beam_height", beam_height)
	arr_mesh.surface_set_material(0, mat)

	mesh = arr_mesh


## Convenience: set sizes and rebuild in one call
func configure(p_floor_size: Vector2, p_ceiling_size: Vector2, p_height: float) -> void:
	floor_size = p_floor_size
	ceiling_size = p_ceiling_size
	beam_height = p_height
	rebuild()
