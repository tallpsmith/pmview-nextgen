# FleetViewController.gd
# Orchestrates the fleet grid view: arranges CompactHosts in an auto-grid,
# manages focus transitions, and coordinates cameras.
extends Node3D

const CompactHostScript := preload("res://scripts/compact_host.gd")
const HolographicBeamScript := preload("res://scripts/holographic_beam.gd")
const BAR_SCENE := preload("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn")

@onready var fleet_grid: Node3D = %FleetGrid
@onready var patrol_camera: Camera3D = %PatrolCamera
@onready var focus_camera: Camera3D = %FocusCamera
@onready var master_timestamp: Label3D = %MasterTimestamp
@onready var esc_hint: Label = %EscHint
@onready var time_control: Control = %TimeControl
@onready var warning_toast: Control = %WarningToast
@onready var fleet_poller: Node = %FleetMetricPoller

## Spacing between host grid cells (centre to centre)
@export var host_spacing: float = 6.0

enum ViewMode { PATROL, TRANSITIONING_TO_FOCUS, FOCUS, TRANSITIONING_TO_PATROL }

var _hosts: Array[Node3D] = []
var _host_lookup: Dictionary = {}
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
	_setup_time_control(config)
	_setup_fleet_poller(config)


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
		_host_lookup[hostnames[i]] = host_node

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

	# Camera sits at detail view height, offset to the side so we see the
	# detail view and beam from outside, not from inside the beam.
	var detail_centre := host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
	var target_pos := detail_centre + Vector3(0, 3.0, 18.0)
	var look_pos := detail_centre

	# Fly the patrol camera toward the focus position
	patrol_camera.fly_to_focus(target_pos, look_pos)
	await patrol_camera.fly_to_focus_completed

	# Switch to focus camera at the destination.
	# Must set position BEFORE make_current, then re-init orbit params
	# because fly_orbit_camera._ready() only runs once at scene load.
	focus_camera.global_transform = patrol_camera.global_transform
	focus_camera.orbit_center = detail_centre
	focus_camera._orbit_height = focus_camera.position.y
	focus_camera._radius = Vector2(
		focus_camera.position.x - detail_centre.x,
		focus_camera.position.z - detail_centre.z).length()
	focus_camera._orbit_angle = atan2(
		focus_camera.position.z - detail_centre.z,
		focus_camera.position.x - detail_centre.x)
	focus_camera.make_current()

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
	for i in range(8):
		var bar: Node3D = BAR_SCENE.instantiate()
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


func _setup_time_control(config: Dictionary) -> void:
	if not time_control:
		return
	var mode: String = config.get("mode", "live")
	if mode != "archive":
		return
	# Set archive bounds so the TimeControl panel can render its timeline
	var start_epoch: float = config.get("archive_start_epoch", 0.0)
	var end_epoch: float = config.get("archive_end_epoch", 0.0)
	if time_control.has_method("set_archive_bounds"):
		time_control.set_archive_bounds(start_epoch, end_epoch)


## Update the floating master timestamp billboard and TimeControl playhead.
func update_master_timestamp(timestamp_iso: String) -> void:
	if master_timestamp:
		master_timestamp.text = timestamp_iso
	if time_control and time_control.has_method("update_playhead"):
		time_control.update_playhead(timestamp_iso, "archive")


func _on_playback_position_changed(position: String, mode: String) -> void:
	if not position.is_empty():
		update_master_timestamp(position)


# --- Fleet metric poller ---

func _setup_fleet_poller(config: Dictionary) -> void:
	print("[FleetView] _setup_fleet_poller called")
	print("[FleetView]   fleet_poller node: ", fleet_poller)
	print("[FleetView]   config keys: ", config.keys())
	if not fleet_poller:
		print("[FleetView]   BAIL: fleet_poller is null")
		return
	var hostnames: PackedStringArray = config.get("hostnames", PackedStringArray())
	print("[FleetView]   hostnames count: ", hostnames.size())
	if hostnames.is_empty():
		print("[FleetView]   BAIL: no hostnames in config")
		return  # Mock mode — no polling

	var endpoint: String = config.get("endpoint", "http://localhost:44322")
	print("[FleetView]   endpoint: ", endpoint)
	print("[FleetView]   fleet_poller script: ", fleet_poller.get_script())
	fleet_poller.set("Endpoint", endpoint)

	fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)
	fleet_poller.ScrapeBudgetExceeded.connect(_on_scrape_lagging)
	fleet_poller.HostsDropped.connect(_on_hosts_dropped)
	fleet_poller.PlaybackPositionChanged.connect(_on_playback_position_changed)

	var mode: String = config.get("mode", "live")
	if mode == "archive":
		var start_time: String = config.get("start_time", "")
		print("[FleetView]   calling StartArchivePlayback at %s..." % start_time)
		fleet_poller.StartArchivePlayback(hostnames, start_time)
	else:
		print("[FleetView]   calling StartPolling (live)...")
		fleet_poller.StartPolling(hostnames)
	print("[FleetView]   polling started successfully")


var _fleet_update_count: int = 0

func _on_fleet_metrics_updated(hostname: String, metrics: Dictionary) -> void:
	_fleet_update_count += 1
	if _fleet_update_count <= 3 or _fleet_update_count % 10 == 0:
		print("[FleetView] Update #%d — host '%s': %s" % [
			_fleet_update_count, hostname, metrics])
	var host: Node3D = _host_lookup.get(hostname)
	if not host:
		return
	for metric_name: String in metrics:
		host.set_metric_value(metric_name, metrics[metric_name])


func _on_scrape_lagging() -> void:
	if warning_toast and warning_toast.has_method("show_toast"):
		warning_toast.show_toast(
			"METRIC POLLING LAGGING", WarningToast.Severity.WARNING,
			5.0, "scrape_lag")


func _on_hosts_dropped(count: int, hostnames: Array) -> void:
	if warning_toast and warning_toast.has_method("show_toast"):
		warning_toast.show_toast(
			"250 HOST LIMIT - %d HOSTS DROPPED (SEE LOGS)" % count,
			WarningToast.Severity.WARNING, 10.0)
