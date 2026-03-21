# FleetViewController.gd
# Orchestrates the fleet grid view: arranges CompactHosts in an auto-grid,
# manages focus transitions, and coordinates cameras.
extends Node3D

const CompactHostScript := preload("res://scripts/compact_host.gd")
const HolographicBeamScript := preload("res://scripts/holographic_beam.gd")

@onready var fleet_grid: Node3D = %FleetGrid
@onready var patrol_camera: Camera3D = %PatrolCamera
@onready var focus_camera: Camera3D = %FocusCamera
@onready var master_timestamp: Label3D = %MasterTimestamp
@onready var esc_hint: Label = %EscHint

## Spacing between host grid cells (centre to centre)
@export var host_spacing: float = 6.0

enum ViewMode { PATROL, TRANSITIONING_TO_FOCUS, FOCUS, TRANSITIONING_TO_PATROL }

var _hosts: Array[Node3D] = []
var _grid_columns: int = 0
var _grid_bounds: Rect2 = Rect2()
var _view_mode: ViewMode = ViewMode.PATROL
var _focused_host_index: int = -1
var _detail_view: Node3D = null
var _beam: MeshInstance3D = null
const DETAIL_VIEW_HEIGHT := 15.0


func _ready() -> void:
	var config: Dictionary = SceneManager.connection_config
	var hostnames: PackedStringArray = config.get("hostnames", PackedStringArray())
	if hostnames.is_empty():
		hostnames = _generate_mock_hostnames(12)
	_build_grid(hostnames)
	_position_master_timestamp()
	patrol_camera.setup(_grid_bounds)
	_update_esc_hint()


func _generate_mock_hostnames(count: int) -> PackedStringArray:
	var names := PackedStringArray()
	for i in range(count):
		names.append("host-%02d" % (i + 1))
	return names


func _build_grid(hostnames: PackedStringArray) -> void:
	var count := hostnames.size()
	_grid_columns = ceili(sqrt(float(count)))
	var grid_rows := ceili(float(count) / float(_grid_columns))

	var total_width := (_grid_columns - 1) * host_spacing
	var total_depth := (grid_rows - 1) * host_spacing
	var offset := Vector3(-total_width / 2.0, 0, -total_depth / 2.0)

	for i in range(count):
		var col := i % _grid_columns
		var row := i / _grid_columns
		var host_node := Node3D.new()
		host_node.set_script(CompactHostScript)
		host_node.hostname = hostnames[i]
		host_node.position = offset + Vector3(col * host_spacing, 0, row * host_spacing)
		host_node.name = "CompactHost_%d" % i
		fleet_grid.add_child(host_node)
		_hosts.append(host_node)

	_grid_bounds = Rect2(
		Vector2(offset.x, offset.z),
		Vector2(total_width, total_depth)
	)


func _position_master_timestamp() -> void:
	if not master_timestamp:
		return
	var centre := Vector3(
		_grid_bounds.position.x + _grid_bounds.size.x / 2.0,
		15.0,
		_grid_bounds.position.y + _grid_bounds.size.y / 2.0,
	)
	master_timestamp.position = centre
	master_timestamp.text = "2026-03-21 14:32:00"


func get_grid_bounds() -> Rect2:
	return _grid_bounds


# --- Input handling ---

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		_handle_esc()
	if event.is_action_pressed("ui_accept") and _view_mode == ViewMode.PATROL:
		_select_nearest_host()
	elif event is InputEventMouseButton and event.pressed \
			and event.button_index == MOUSE_BUTTON_LEFT:
		if _view_mode == ViewMode.PATROL:
			_select_host_by_click(event.position)


var _esc_pressed_at: float = 0.0
const ESC_DOUBLE_PRESS_WINDOW := 2.0

func _handle_esc() -> void:
	if _view_mode == ViewMode.FOCUS:
		_exit_focus()
		return
	# Double-ESC to return to main menu from patrol
	var now := Time.get_ticks_msec() / 1000.0
	if now - _esc_pressed_at < ESC_DOUBLE_PRESS_WINDOW:
		SceneManager.go_to_main_menu()
	else:
		_esc_pressed_at = now


# --- Host selection ---

func _select_nearest_host() -> void:
	var camera: Camera3D = get_viewport().get_camera_3d()
	var centre := get_viewport().get_visible_rect().size / 2.0
	var from := camera.project_ray_origin(centre)
	var dir := camera.project_ray_normal(centre)
	_raycast_select(from, dir)


func _select_host_by_click(screen_pos: Vector2) -> void:
	var camera: Camera3D = get_viewport().get_camera_3d()
	var from := camera.project_ray_origin(screen_pos)
	var dir := camera.project_ray_normal(screen_pos)
	_raycast_select(from, dir)


func _raycast_select(from: Vector3, direction: Vector3) -> void:
	var space_state := get_world_3d().direct_space_state
	var query := PhysicsRayQueryParameters3D.create(from, from + direction * 200.0)
	# grounded_bar.tscn has StaticBody3D on collision_layer 2
	query.collision_mask = 2
	var result := space_state.intersect_ray(query)
	if result.is_empty():
		return
	# Walk up from the collider to find the CompactHost
	var node: Node = result.collider
	while node and not (node.has_method("set_metric_value")):
		node = node.get_parent()
	if node:
		var idx := _hosts.find(node)
		if idx >= 0:
			_enter_focus(idx)


# --- Focus mode ---

func _enter_focus(host_index: int) -> void:
	_focused_host_index = host_index
	_view_mode = ViewMode.TRANSITIONING_TO_FOCUS
	var host: Node3D = _hosts[host_index]

	# Dim all other hosts
	for i in range(_hosts.size()):
		if i != host_index:
			_hosts[i].set_opacity(0.3)

	# Calculate focus camera target position (above and in front of the host)
	var target_pos := host.position + Vector3(0, DETAIL_VIEW_HEIGHT + 5.0, 15.0)
	var look_pos := host.position + Vector3(0, DETAIL_VIEW_HEIGHT / 2.0, 0)

	# Fly the patrol camera toward the focus position
	patrol_camera.fly_to_focus(target_pos, look_pos)
	await patrol_camera.fly_to_focus_completed

	# Switch to focus camera at the destination
	focus_camera.global_transform = patrol_camera.global_transform
	focus_camera.make_current()
	# Wait a frame for fly_orbit_camera.gd's _ready() state
	await get_tree().process_frame
	focus_camera.orbit_center = host.position + Vector3(0, DETAIL_VIEW_HEIGHT / 2.0, 0)

	# Spawn mock detail view (placeholder — real HostView spawning comes later)
	_spawn_mock_detail_view(host)
	_spawn_beam(host)

	_view_mode = ViewMode.FOCUS
	_update_esc_hint()


func _exit_focus() -> void:
	_view_mode = ViewMode.TRANSITIONING_TO_PATROL

	if _detail_view:
		_detail_view.queue_free()
		_detail_view = null
	if _beam:
		_beam.queue_free()
		_beam = null

	# Restore patrol camera
	patrol_camera.global_transform = focus_camera.global_transform
	patrol_camera.make_current()
	patrol_camera.return_to_patrol()

	# Restore all host opacities
	for host: Node3D in _hosts:
		host.set_opacity(1.0)

	_focused_host_index = -1
	_view_mode = ViewMode.PATROL
	_update_esc_hint()


func _spawn_mock_detail_view(host: Node3D) -> void:
	_detail_view = Node3D.new()
	_detail_view.name = "DetailView"
	_detail_view.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
	# Placeholder bars to visualise the detail view space
	var bar_scene := load("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn")
	for i in range(8):
		var bar: Node3D = bar_scene.instantiate()
		bar.position = Vector3((i - 3.5) * 2.0, 0, 0)
		bar.height = randf_range(0.3, 1.0)
		bar.colour = Color(randf(), randf(), randf())
		_detail_view.add_child(bar)
	add_child(_detail_view)


func _spawn_beam(host: Node3D) -> void:
	_beam = MeshInstance3D.new()
	_beam.name = "HolographicBeam"
	_beam.set_script(HolographicBeamScript)
	_beam.position = host.position
	var host_footprint: Vector2 = host.get_footprint()
	var detail_footprint := Vector2(18.0, 10.0)
	_beam.configure(host_footprint, detail_footprint, DETAIL_VIEW_HEIGHT)
	add_child(_beam)


# --- HUD ---

func _update_esc_hint() -> void:
	if not esc_hint:
		return
	match _view_mode:
		ViewMode.PATROL:
			esc_hint.text = "ESC ESC \u2192 Main Menu"
		ViewMode.FOCUS:
			esc_hint.text = "ESC \u2192 Return to Patrol"
		_:
			esc_hint.text = ""
