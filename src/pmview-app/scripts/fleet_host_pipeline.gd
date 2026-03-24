# fleet_host_pipeline.gd
# Coordinates the LoadingPipeline for a single host within the fleet view.
# Drives a matrix progress grid and holds the built zones scene on completion.
extends Node

signal build_completed(zones_root: Node3D)
signal build_failed(error: String)

var _pipeline: Node = null
var _matrix_grid: MeshInstance3D = null
var _zones_root: Node3D = null
var _is_complete: bool = false
var _start_time_ms: int = 0

## Minimum seconds for the matrix animation to play before revealing the preview.
## Ensures the "computing" visual is always visible even when the pipeline is fast.
const MIN_ANIMATION_SECONDS := 3.0

## Cell allocation per phase (must sum to 100)
const PHASE_CELLS := {0: 10, 1: 20, 2: 0, 3: 10, 4: 20, 5: 40}
var _cells_so_far: int = 0


func start(endpoint: String, mode: String, hostname: String,
		matrix_grid: MeshInstance3D) -> void:
	_matrix_grid = matrix_grid

	# Instantiate the C# LoadingPipeline node
	var pipeline_script := load("res://scripts/LoadingPipeline.cs")
	if pipeline_script == null:
		push_error("[FleetHostPipeline] Failed to load LoadingPipeline script")
		build_failed.emit("Failed to load pipeline")
		return

	_pipeline = Node.new()
	_pipeline.name = "FleetLoadingPipeline"
	_pipeline.set_script(pipeline_script)
	# Re-fetch managed wrapper after SetScript
	_pipeline = instance_from_id(_pipeline.get_instance_id())

	# No artificial delay — let it rip, the matrix animation is the visual feedback
	_pipeline.set("MinPhaseDelayMs", 0)
	# Build zones only — no UI panels or controller script
	_pipeline.set("ZonesOnly", true)

	add_child(_pipeline)

	_pipeline.PhaseCompleted.connect(_on_phase_completed)
	_pipeline.PipelineCompleted.connect(_on_pipeline_completed)
	_pipeline.PipelineError.connect(_on_pipeline_error)

	_start_time_ms = Time.get_ticks_msec()
	_pipeline.StartPipeline(endpoint, mode, hostname, "", false)


func _on_phase_completed(phase_index: int, _phase_name: String) -> void:
	var cells: int = PHASE_CELLS.get(phase_index, 0)
	_cells_so_far += cells
	if _matrix_grid and _matrix_grid.has_method("set_progress"):
		_matrix_grid.set_progress(float(_cells_so_far) / 100.0)


func _on_pipeline_completed() -> void:
	_is_complete = true
	_zones_root = _pipeline.get("BuiltScene") as Node3D
	# Pipeline was started with ZonesOnly=true, so _zones_root has no
	# controller script or UI layer — safe to add to the fleet scene tree.

	# Ensure the matrix animation plays for at least MIN_ANIMATION_SECONDS
	# so the user sees the "computing" effect even when the pipeline is fast.
	var elapsed_s := (Time.get_ticks_msec() - _start_time_ms) / 1000.0
	var remaining_s := MIN_ANIMATION_SECONDS - elapsed_s
	if remaining_s > 0.0:
		# Animate the remaining cells over the remaining time
		_animate_remaining_cells(remaining_s)
		await get_tree().create_timer(remaining_s).timeout

	if _matrix_grid and _matrix_grid.has_method("set_progress"):
		_matrix_grid.set_progress(1.0)

	build_completed.emit(_zones_root)


func _on_pipeline_error(_phase_index: int, error: String) -> void:
	push_error("[FleetHostPipeline] Pipeline failed: %s" % error)
	build_failed.emit(error)


## Gradually fills remaining matrix cells over the given duration.
func _animate_remaining_cells(duration: float) -> void:
	if not _matrix_grid or not _matrix_grid.has_method("set_progress"):
		return
	var start_progress := float(_cells_so_far) / 100.0
	var tween := create_tween()
	tween.tween_method(
		func(val: float) -> void:
			if _matrix_grid and _matrix_grid.has_method("set_progress"):
				_matrix_grid.set_progress(val),
		start_progress, 0.95, duration  # stop at 95% — the final 100% snap happens after
	).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)


## Returns the built zones root, or null if not yet complete.
func get_zones_root() -> Node3D:
	return _zones_root


## Returns true if the pipeline has completed successfully.
func is_complete() -> bool:
	return _is_complete


## Graft the HostView UI (panels, help, controller script) onto a zones root.
## Bridges GDScript → C# since HostSceneBuilder is a static class.
func graft_host_view_ui(zones: Node3D, mode: String) -> void:
	if _pipeline:
		_pipeline.GraftHostViewUi(zones, mode)


## Clean up the pipeline node.
func cancel() -> void:
	if _pipeline:
		_pipeline.queue_free()
		_pipeline = null
