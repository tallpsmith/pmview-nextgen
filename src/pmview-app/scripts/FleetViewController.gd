# FleetViewController.gd
# Orchestrates the fleet grid view: arranges CompactHosts in an auto-grid,
# manages focus transitions, and coordinates cameras.
extends Node3D

const CompactHostScript := preload("res://scripts/compact_host.gd")

@onready var fleet_grid: Node3D = %FleetGrid
@onready var patrol_camera: Camera3D = %PatrolCamera
@onready var master_timestamp: Label3D = %MasterTimestamp

## Spacing between host grid cells (centre to centre)
@export var host_spacing: float = 6.0

var _hosts: Array[Node3D] = []
var _grid_columns: int = 0
var _grid_bounds: Rect2 = Rect2()


func _ready() -> void:
	var config: Dictionary = SceneManager.connection_config
	var hostnames: PackedStringArray = config.get("hostnames", PackedStringArray())
	if hostnames.is_empty():
		# Fallback: mock data for development
		hostnames = _generate_mock_hostnames(12)
	_build_grid(hostnames)
	_position_master_timestamp()
	patrol_camera.setup(_grid_bounds)


func _generate_mock_hostnames(count: int) -> PackedStringArray:
	var names := PackedStringArray()
	for i in range(count):
		names.append("host-%02d" % (i + 1))
	return names


func _build_grid(hostnames: PackedStringArray) -> void:
	var count := hostnames.size()
	_grid_columns = ceili(sqrt(float(count)))
	var grid_rows := ceili(float(count) / float(_grid_columns))

	# Centre the grid on the origin
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
		15.0,  # float well above the grid
		_grid_bounds.position.y + _grid_bounds.size.y / 2.0,
	)
	master_timestamp.position = centre
	master_timestamp.text = "2026-03-21 14:32:00"  # mock timestamp


func get_grid_bounds() -> Rect2:
	return _grid_bounds


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		_handle_esc()


## Double-ESC to return to main menu (matches host view pattern)
var _esc_pressed_at: float = 0.0
const ESC_DOUBLE_PRESS_WINDOW := 2.0

func _handle_esc() -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now - _esc_pressed_at < ESC_DOUBLE_PRESS_WINDOW:
		SceneManager.go_to_main_menu()
	else:
		_esc_pressed_at = now
