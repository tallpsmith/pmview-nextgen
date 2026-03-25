# matrix_progress_grid.gd
# 10x10 cell grid that fills with random scatter to visualise loading progress.
extends MeshInstance3D

signal progress_complete

@export var grid_width: float = 16.0
@export var grid_depth: float = 12.0

const GRID_SIZE := 10
const TOTAL_CELLS := GRID_SIZE * GRID_SIZE
const GLOW_DURATION := 0.3

var _cell_states: Array[float] = []
var _activated: Array[bool] = []
var _scatter_order: Array[int] = []
var _active_count: int = 0
var _glow_timers: Array[float] = []
var _material: ShaderMaterial = null
var _dissolving: bool = false


func _ready() -> void:
	var plane := PlaneMesh.new()
	plane.size = Vector2(grid_width, grid_depth)
	mesh = plane

	_material = ShaderMaterial.new()
	_material.shader = preload("res://addons/pmview-bridge/building_blocks/matrix_progress_grid.gdshader")
	set_surface_override_material(0, _material)

	_cell_states.resize(TOTAL_CELLS)
	_activated.resize(TOTAL_CELLS)
	_glow_timers.resize(TOTAL_CELLS)
	for i in range(TOTAL_CELLS):
		_cell_states[i] = 0.0
		_activated[i] = false
		_glow_timers[i] = 0.0

	_scatter_order = []
	for i in range(TOTAL_CELLS):
		_scatter_order.append(i)
	_scatter_order.shuffle()

	_push_states_to_shader()


func _process(delta: float) -> void:
	var needs_update := false
	for i in range(TOTAL_CELLS):
		if _glow_timers[i] > 0.0:
			_glow_timers[i] = maxf(_glow_timers[i] - delta, 0.0)
			var glow_intensity := _glow_timers[i] / GLOW_DURATION
			_cell_states[i] = 1.0 + glow_intensity
			needs_update = true
		elif _activated[i] and _cell_states[i] != 1.0:
			_cell_states[i] = 1.0
			needs_update = true

	if needs_update:
		_push_states_to_shader()


func set_progress(progress: float) -> void:
	var target_count := clampi(roundi(progress * TOTAL_CELLS), 0, TOTAL_CELLS)
	while _active_count < target_count:
		var cell_idx: int = _scatter_order[_active_count]
		_activated[cell_idx] = true
		_glow_timers[cell_idx] = GLOW_DURATION
		_cell_states[cell_idx] = 1.0 + 1.0
		_active_count += 1

	_push_states_to_shader()

	if _active_count >= TOTAL_CELLS and not _dissolving:
		_dissolving = true
		progress_complete.emit()


## Dissolve the grid (fade out all cells then free)
func dissolve(duration: float = 0.5) -> void:
	var tween := create_tween()
	tween.tween_method(_set_global_alpha, 1.0, 0.0, duration)
	tween.tween_callback(queue_free)


## Keep the grid visible at a target opacity as a contrasting floor.
## Fades from full brightness to the target over duration, then stays.
func set_final_opacity(target: float = 0.9, duration: float = 0.5) -> void:
	var tween := create_tween()
	tween.tween_method(_set_global_alpha, 1.0, target, duration)


func _set_global_alpha(val: float) -> void:
	if _material:
		_material.set_shader_parameter("global_alpha", val)


func _push_states_to_shader() -> void:
	if not _material:
		return
	var packed := PackedFloat32Array()
	packed.resize(TOTAL_CELLS)
	for i in range(TOTAL_CELLS):
		packed[i] = _cell_states[i]
	_material.set_shader_parameter("cell_states", packed)
