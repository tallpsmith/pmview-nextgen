# FleetViewController.gd
# Orchestrates the fleet grid view: arranges CompactHosts in an auto-grid,
# manages focus transitions, and coordinates cameras.
extends Node3D

const CompactHostScript := preload("res://scripts/compact_host.gd")
const HolographicBeamScript := preload("res://scripts/holographic_beam.gd")
const FleetHostPipelineScript := preload("res://scripts/fleet_host_pipeline.gd")
const MatrixGridScript := preload("res://addons/pmview-bridge/building_blocks/matrix_progress_grid.gd")

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
var _beam: MeshInstance3D = null
var _fleet_pipeline: Node = null
var _matrix_grid: MeshInstance3D = null
var _preview_zones: Node3D = null
var _preview_ready: bool = false
const DETAIL_VIEW_HEIGHT := 11.0
## Scale factor for the HostView preview — full HostView is too large for fleet context
const PREVIEW_SCALE := 0.4


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

	# Restore focus if returning from HostView dive-in
	var restore_hostname: String = SceneManager.fleet_focused_hostname
	if not restore_hostname.is_empty():
		SceneManager.fleet_focused_hostname = ""
		for i in range(_hosts.size()):
			if _hosts[i].hostname == restore_hostname:
				_enter_focus.call_deferred(i)
				break


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
		8.0,
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
		return
	# Dive-in: Enter or double-click in focus mode with preview ready
	if _view_mode == ViewMode.FOCUS and _preview_ready:
		if event.is_action_pressed("ui_accept"):
			_dive_into_host_view()
			return
		if event is InputEventMouseButton and event.pressed \
				and event.button_index == MOUSE_BUTTON_LEFT and event.double_click:
			_dive_into_host_view()
			return
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
	# Cancel any existing preview/pipeline if re-entering focus
	if _fleet_pipeline or _preview_zones:
		_cleanup_focus_state()
	_focused_host_index = host_index
	_view_mode = ViewMode.TRANSITIONING_TO_FOCUS
	var host: Node3D = _hosts[host_index]

	# Dim all other hosts
	for i in range(_hosts.size()):
		if i != host_index:
			_hosts[i].set_opacity(0.3)

	# Spawn beam immediately with a fade-in — gives instant visual feedback
	# that the host was selected before the camera even starts moving.
	_spawn_beam(host)
	if _beam and _beam.has_method("fade_in"):
		_beam.fade_in(0.4)

	# Compute the orbit destination: a point on the orbit circle around
	# the HostView at the correct height. Use the camera's current XZ
	# direction to the host as the approach angle — the camera arrives
	# from the same side it's currently viewing from, no swooping.
	var detail_centre := host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
	var orbit_radius := 18.0
	var orbit_height_offset := 3.0

	# Direction from host to camera (XZ plane) gives our approach angle
	var cam_pos := patrol_camera.global_position
	var to_cam := Vector2(cam_pos.x - host.position.x, cam_pos.z - host.position.z)
	if to_cam.length() < 0.1:
		to_cam = Vector2(0, 1)  # fallback if camera is directly above
	to_cam = to_cam.normalized()

	var orbit_pos := detail_centre + Vector3(
		to_cam.x * orbit_radius,
		orbit_height_offset,
		to_cam.y * orbit_radius
	)

	# Start the preview pipeline (replaces mock detail view)
	_start_preview_pipeline(host)
	patrol_camera.fly_to_focus(orbit_pos, detail_centre)
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

	_view_mode = ViewMode.FOCUS
	_update_esc_hint()


func _exit_focus() -> void:
	_view_mode = ViewMode.TRANSITIONING_TO_PATROL

	# Hand control back to patrol camera at the focus camera's position
	patrol_camera.global_transform = focus_camera.global_transform
	patrol_camera.make_current()

	# Find the nearest racetrack point to fly back to
	var nearest_pos: Vector3 = patrol_camera.get_nearest_racetrack_point()
	var grid_centre := Vector3(
		_grid_bounds.position.x + _grid_bounds.size.x / 2.0,
		0,
		_grid_bounds.position.y + _grid_bounds.size.y / 2.0)

	# Fly out to the racetrack, looking at the grid centre
	patrol_camera.fly_to_focus(nearest_pos, grid_centre)

	# Fade out beam during fly-out
	if _beam and _beam.has_method("fade_in"):
		var mat: ShaderMaterial = _beam.mesh.surface_get_material(0)
		if mat:
			var tween := _beam.create_tween()
			tween.tween_method(
				func(val: float) -> void: mat.set_shader_parameter("global_alpha", val),
				1.0, 0.0, 1.0
			).set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)

	await patrol_camera.fly_to_focus_completed

	_cleanup_focus_state()

	# Resume patrol from the nearest point
	patrol_camera.return_to_patrol()

	_view_mode = ViewMode.PATROL
	_update_esc_hint()


# --- Preview pipeline ---

func _start_preview_pipeline(host: Node3D) -> void:
	var config: Dictionary = SceneManager.connection_config
	var endpoint: String = config.get("endpoint", "http://localhost:54322")
	var mode: String = config.get("mode", "live")
	var hostname: String = host.hostname

	# Spawn matrix grid on beam top
	_matrix_grid = MeshInstance3D.new()
	_matrix_grid.set_script(MatrixGridScript)
	_matrix_grid.name = "MatrixProgressGrid"
	_matrix_grid.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT + 0.1, 0)
	add_child(_matrix_grid)

	# Start the pipeline
	_fleet_pipeline = Node.new()
	_fleet_pipeline.set_script(FleetHostPipelineScript)
	_fleet_pipeline.name = "FleetHostPipeline"
	add_child(_fleet_pipeline)

	_fleet_pipeline.build_completed.connect(_on_preview_build_completed)
	_fleet_pipeline.build_failed.connect(_on_preview_build_failed)
	_fleet_pipeline.start(endpoint, mode, hostname, _matrix_grid)


func _on_preview_build_completed(zones_root: Node3D) -> void:
	_preview_zones = zones_root
	_preview_ready = true

	# Keep the matrix grid visible at reduced opacity as a contrasting floor
	# beneath the HostView preview — improves visual contrast.
	if _matrix_grid and _matrix_grid.has_method("set_final_opacity"):
		_matrix_grid.set_final_opacity(0.9)

	if _preview_zones and _focused_host_index >= 0:
		var host: Node3D = _hosts[_focused_host_index]
		_preview_zones.position = host.position + Vector3(0, DETAIL_VIEW_HEIGHT, 0)
		# Scale down the full HostView layout to fit the fleet context
		_preview_zones.scale = Vector3.ONE * PREVIEW_SCALE
		add_child(_preview_zones)


func _on_preview_build_failed(error: String) -> void:
	push_warning("[FleetView] Preview build failed: %s" % error)
	if _matrix_grid:
		_matrix_grid.queue_free()
		_matrix_grid = null


func _cleanup_focus_state() -> void:
	if _beam:
		_beam.queue_free()
		_beam = null
	if _fleet_pipeline:
		if _fleet_pipeline.has_method("cancel"):
			_fleet_pipeline.cancel()
		_fleet_pipeline.queue_free()
		_fleet_pipeline = null
	if _matrix_grid:
		_matrix_grid.queue_free()
		_matrix_grid = null
	if _preview_zones:
		_preview_zones.queue_free()
		_preview_zones = null
	_preview_ready = false

	for host: Node3D in _hosts:
		host.set_opacity(1.0)

	_focused_host_index = -1


# --- Dive into full HostView ---

func _dive_into_host_view() -> void:
	if not _preview_zones or _focused_host_index < 0:
		return
	# Prevent double-trigger during fade
	_preview_ready = false

	var host: Node3D = _hosts[_focused_host_index]
	var hostname: String = host.hostname

	# Reset scale to 1.0 before handing off — HostView expects full-size zones
	_preview_zones.scale = Vector3.ONE

	# Detach the zones from our scene tree (don't free — we're handing it off)
	remove_child(_preview_zones)
	var zones: Node3D = _preview_zones
	_preview_zones = null

	# Graft the UI layer and controller script onto the zones root.
	# Must do this before cleanup because we need the pipeline's C# bridge.
	var config: Dictionary = SceneManager.connection_config
	var mode: String = config.get("mode", "live")
	if _fleet_pipeline:
		_fleet_pipeline.graft_host_view_ui(zones, mode)

	# Fade out the fleet scene before transitioning — less jarring than an instant cut
	var fade_rect := ColorRect.new()
	fade_rect.color = Color(0, 0, 0, 0)
	fade_rect.anchors_preset = Control.PRESET_FULL_RECT
	fade_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var canvas := CanvasLayer.new()
	canvas.layer = 100  # on top of everything
	canvas.add_child(fade_rect)
	add_child(canvas)

	var tween := create_tween()
	tween.tween_property(fade_rect, "color:a", 1.0, 0.8)
	await tween.finished

	# Clean up fleet state (beam, pipeline, grid) without freeing the zones
	_cleanup_focus_state()
	canvas.queue_free()

	# Hand off to SceneManager — HostViewController will receive this as built_scene
	SceneManager.go_to_host_view_from_fleet(zones, hostname)


func _spawn_beam(host: Node3D) -> void:
	_beam = MeshInstance3D.new()
	_beam.name = "HolographicBeam"
	_beam.set_script(HolographicBeamScript)
	_beam.position = host.position
	var host_footprint: Vector2 = host.get_footprint()
	var detail_footprint := Vector2(16.0, 12.0)
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
		# Format ISO 8601 to clean "YYYY-MM-DD · HH:MM:SS"
		var clean := timestamp_iso
		if "T" in timestamp_iso:
			var parts := timestamp_iso.split("T")
			var date_part := parts[0]
			var time_part := parts[1].split(".")[0].rstrip("Z")  # strip fractional seconds and Z
			clean = "%s · %s" % [date_part, time_part]
		master_timestamp.text = clean
	if time_control and time_control.has_method("update_playhead"):
		time_control.update_playhead(timestamp_iso, "archive")


func _on_playback_position_changed(position: String, mode: String) -> void:
	if not position.is_empty():
		print("[FleetView] PlaybackPosition: %s (%s)" % [position, mode])
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

	var endpoint: String = config.get("endpoint", "http://localhost:54322")
	print("[FleetView]   endpoint: ", endpoint)
	print("[FleetView]   fleet_poller script: ", fleet_poller.get_script())
	fleet_poller.set("Endpoint", endpoint)

	fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)
	fleet_poller.ScrapeBudgetExceeded.connect(_on_scrape_lagging)
	fleet_poller.HostsDropped.connect(_on_hosts_dropped)
	fleet_poller.PlaybackPositionChanged.connect(_on_playback_position_changed)

	var mode: String = config.get("mode", "live")
	print("[FleetView]   mode: %s" % mode)
	if mode == "archive":
		var start_time: String = config.get("start_time", "")
		if start_time.is_empty():
			# Default: archive end minus 24h, clamped to archive start
			var arch_start_raw = config.get("archive_start_epoch", null)
			var arch_end_raw = config.get("archive_end_epoch", null)
			print("[FleetView]   raw archive_start_epoch=%s (type=%s)" % [arch_start_raw, typeof(arch_start_raw)])
			print("[FleetView]   raw archive_end_epoch=%s (type=%s)" % [arch_end_raw, typeof(arch_end_raw)])
			var arch_start: float = float(arch_start_raw) if arch_start_raw != null else 0.0
			var arch_end: float = float(arch_end_raw) if arch_end_raw != null else 0.0
			print("[FleetView]   arch_start=%f arch_end=%f" % [arch_start, arch_end])
			if arch_end > 0.0:
				var default_start: float = maxf(arch_end - 86400.0, arch_start)
				# Convert epoch to ISO 8601 UTC
				var dt := Time.get_datetime_dict_from_unix_time(int(default_start))
				start_time = "%04d-%02d-%02dT%02d:%02d:%02dZ" % [
					dt.year, dt.month, dt.day, dt.hour, dt.minute, dt.second]
				print("[FleetView]   computed default start_time: %s (end-24h)" % start_time)
			else:
				push_warning("[FleetView] No archive bounds — cannot compute default start time")
		print("[FleetView]   start_time='%s'" % start_time)
		print("[FleetView]   calling StartArchivePlayback...")
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
