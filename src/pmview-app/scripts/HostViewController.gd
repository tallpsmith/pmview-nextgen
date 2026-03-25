extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay and archive controls.

@onready var esc_label: Label = %EscLabel

## Viewpoint presets: [key, name, zone_names, offset_from_centroid]
## Empty zone_names means "scene centre" (system overview).
const VIEWPOINTS := [
	[KEY_1, "System", [], Vector3(0, 15, 20)],
	[KEY_2, "CPU", ["CPU"], Vector3(0, 3, 5)],
	[KEY_3, "Disk", ["Disk"], Vector3(0, 3, 5)],
	[KEY_4, "Network", ["Net-In", "Net-Out"], Vector3(0, 3, 5)],
]

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null
var _poller: Node = null
var _time_control: Control = null
var _help_panel: Node = null
var _help_hint: Node = null
var _camera: Node = null
var _scene_binder: Node = null
var _timestamp_label_3d: Node = null
var _is_archive_mode := false
var _poll_interval_seconds := 60.0
var _active_viewpoint_key: int = -1  ## Currently active viewpoint key, or -1 for none
var _selected_shape: Node = null  ## Currently selected GroundedShape or StackGroupNode
var _detail_panel: Control = null


func _ready() -> void:
	print("[HostView] _ready called")
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("[HostView] No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	SceneManager.built_scene = null
	print("[HostView] Built scene name: %s, script: %s, child count: %d" % [
		scene.name, scene.get_script(), scene.get_child_count()])
	for child in scene.get_children():
		print("[HostView]   child: %s (type: %s, script: %s)" % [
			child.name, child.get_class(), child.get_script()])

	# Reset position — fleet preview places the scene above the CompactHost,
	# but HostView expects zones centred at the origin.
	scene.position = Vector3.ZERO

	print("[HostView] Adding built scene to tree...")
	add_child(scene)
	print("[HostView] Built scene added to tree")

	# Wire Help panel and hint
	_help_panel = scene.find_child("HelpPanel", true, false)
	_help_hint = scene.find_child("HelpHint", true, false)
	_camera = get_viewport().get_camera_3d()
	_scene_binder = scene.find_child("SceneBinder", true, false)
	print("[HostView] HelpPanel found: %s, HelpHint found: %s, Camera found: %s, SceneBinder found: %s" % [
		_help_panel != null, _help_hint != null, _camera != null, _scene_binder != null])

	# Wire detail panel for shape selection
	_detail_panel = scene.find_child("DetailPanel", true, false)
	if _detail_panel:
		_detail_panel.panel_closed.connect(_on_detail_panel_closed)

	# Subscribe to metric updates for detail panel refresh
	var metric_poller = scene.find_child("MetricPoller", true, false)
	if metric_poller and metric_poller.has_signal("MetricsUpdated"):
		metric_poller.MetricsUpdated.connect(_on_metrics_updated_for_detail)

	# Connect panel signals for camera suppression
	if _help_panel:
		_help_panel.panel_opened.connect(_on_help_opened)
		_help_panel.panel_closed.connect(_on_help_closed)

	# Wire RangeTuningPanel camera suppression
	var tuning_panel = scene.find_child("RangeTuningPanel", true, false)
	if tuning_panel:
		if tuning_panel.has_signal("panel_opened"):
			tuning_panel.panel_opened.connect(_on_tuner_opened)
			tuning_panel.panel_closed.connect(_on_tuner_closed)

	var config := SceneManager.connection_config
	_is_archive_mode = config.get("mode", "live") == "archive"

	if _is_archive_mode:
		_poller = scene.find_child("MetricPoller", true, false)
		_time_control = scene.find_child("TimeControl", true, false)

		if _poller:
			# When coming from fleet, use the fleet's current playback position
			# so the HostView continues seamlessly from where the fleet was.
			var start_time: String = ""
			if SceneManager.origin_scene == "fleet" and not SceneManager.fleet_playback_position.is_empty():
				start_time = SceneManager.fleet_playback_position
				print("[HostView] Continuing fleet playback at: %s" % start_time)
			else:
				start_time = config.get("start_time", "")
				if start_time.is_empty():
					var default_epoch := Time.get_unix_time_from_system() - 86400.0
					start_time = Time.get_datetime_string_from_unix_time(
						int(default_epoch)) + "Z"
					print("[HostView] No start time in config, defaulting to: %s" % start_time)
				else:
					print("[HostView] Starting archive playback at: %s" % start_time)

			# If coming from fleet, the poller is already connected and polling.
			# Start playback immediately instead of waiting for ConnectionStateChanged
			# (which already fired during the fleet preview and won't fire again).
			if SceneManager.origin_scene == "fleet":
				print("[HostView] Poller from fleet — starting playback immediately")
				# Defer so the scene tree is fully set up first
				_poller.call_deferred("StartPlayback", start_time)
			else:
				# Fresh load from loading screen — wait for connection
				_poller.ConnectionStateChanged.connect(
					_on_poller_connected.bind(start_time), CONNECT_ONE_SHOT)

			if _time_control:
				_poller.PlaybackPositionChanged.connect(
					_time_control.update_playhead)
				_time_control.playhead_jumped.connect(_on_playhead_jumped)
				_time_control.range_set.connect(_on_range_set)
				_time_control.range_cleared.connect(_on_range_cleared)
				_time_control.panel_opened.connect(_on_panel_opened)
				_time_control.panel_closed.connect(_on_panel_closed)

				# Pass archive bounds so TimeControl knows the timeline range
				var arc_start: float = config.get("archive_start_epoch", 0.0)
				var arc_end: float = config.get("archive_end_epoch", 0.0)
				if arc_start > 0 and arc_end > 0:
					_time_control.set_archive_bounds(arc_start, arc_end)
					print("[HostView] TimeControl bounds set: %s → %s" % [
						Time.get_datetime_string_from_unix_time(int(arc_start)),
						Time.get_datetime_string_from_unix_time(int(arc_end))])

		# Find 3D timestamp label for pause ghost effect
		_timestamp_label_3d = scene.find_child("TimestampLabel", true, false)

		# Wire PlaybackPositionChanged for mode-aware UI updates
		if _poller:
			_poller.PlaybackPositionChanged.connect(_on_playback_position_changed)

	else:
			push_error("[HostView] MetricPoller not found in built scene")

	# Setup help content (needs _is_archive_mode to be set)
	if _help_panel:
		print("[HostView] Setting up help content (archive=%s)" % _is_archive_mode)
		_setup_help_content()
	else:
		push_warning("[HostView] HelpPanel not found — H key will not work")
	if _help_hint:
		print("[HostView] Starting help hint cycling")
		_setup_help_hints()
	else:
		push_warning("[HostView] HelpHint not found — no cycling hints")


func _on_poller_connected(state: String, start_time: String) -> void:
	if state == "Connected":
		print("[HostView] Poller connected, starting archive playback")
		_poller.StartPlayback(start_time)
	else:
		push_error("[HostView] Poller connection state: %s" % state)


func _on_playback_position_changed(_position: String, mode: String) -> void:
	# Track current position for fleet return continuity
	if not _position.is_empty():
		SceneManager.fleet_playback_position = _position
	# Ghost the 3D timestamp when paused
	if _timestamp_label_3d:
		if mode == "Paused":
			_timestamp_label_3d.set("modulate", Color(0.976, 0.451, 0.086, 0.3))
		else:
			_timestamp_label_3d.set("modulate", Color(0.976, 0.451, 0.086, 1.0))


func _on_playhead_jumped(timestamp: String) -> void:
	if _poller:
		_poller.JumpToTimestamp(timestamp)


func _on_range_set(in_time: String, out_time: String) -> void:
	if _poller:
		_poller.SetInOutRange(in_time, out_time)


func _on_range_cleared() -> void:
	if _poller:
		_poller.ClearRange()


func _on_panel_opened() -> void:
	if _poller:
		_poller.PausePlayback()


func _on_panel_closed() -> void:
	if _poller:
		_poller.ResumePlayback()


func _unhandled_input(event: InputEvent) -> void:
	# Tab — deselect when switching camera mode (don't consume, camera handles toggle)
	if event is InputEventKey and event.pressed and event.physical_keycode == KEY_TAB:
		_deselect_shape()

	# Left-click — shape selection via raycast
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_handle_click_selection(event)
		return

	# H or ? — toggle help panel
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode == KEY_H:
			if _help_panel:
				_help_panel.toggle()
				get_viewport().set_input_as_handled()
			return
		elif event.physical_keycode == KEY_SLASH and event.shift_pressed:
			if _help_panel:
				_help_panel.toggle()
				get_viewport().set_input_as_handled()
			return

	# Viewpoint keys 1-4 — fly to zone preset
	if event is InputEventKey and event.pressed and not event.echo:
		for vp in VIEWPOINTS:
			if event.physical_keycode == vp[0]:
				_activate_viewpoint(vp)
				get_viewport().set_input_as_handled()
				return

	# ESC — close help panel first if open
	if event.is_action_pressed("ui_cancel") and _help_panel and _help_panel.visible:
		_help_panel.hide_panel()
		get_viewport().set_input_as_handled()
		return

	# ESC — deselect shape if selected (RangeTuningPanel handles its own ESC first)
	if event.is_action_pressed("ui_cancel") and _selected_shape != null:
		_deselect_shape()
		get_viewport().set_input_as_handled()
		return

	# ESC — return to orbit from a viewpoint
	if event.is_action_pressed("ui_cancel") and _active_viewpoint_key >= 0:
		if _camera and _camera.has_method("return_to_orbit"):
			_camera.return_to_orbit()
		_active_viewpoint_key = -1
		get_viewport().set_input_as_handled()
		return

	# ESC — return to fleet if launched from fleet (single press)
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if SceneManager.origin_scene == "fleet":
			SceneManager.return_to_fleet()
			return
		# Original double-ESC for main menu
		if _esc_pending:
			SceneManager.go_to_main_menu()
		else:
			_esc_pending = true
			esc_label.visible = true
			_esc_timer = get_tree().create_timer(2.0)
			_esc_timer.timeout.connect(_dismiss_esc)
		return

	if not _is_archive_mode:
		return

	# Archive mode keyboard shortcuts
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_SPACE:
				get_viewport().set_input_as_handled()
				_toggle_playback()
			KEY_LEFT:
				if event.shift_pressed:
					get_viewport().set_input_as_handled()
					if _poller:
						var step := 15.0
						if event.alt_pressed and event.ctrl_pressed:
							step = 300.0  # 5 minutes
						elif event.ctrl_pressed:
							step = 60.0
						_poller.StepPlayback(step, -1)
					if _time_control:
						_time_control.notify_scrub()
			KEY_RIGHT:
				if event.shift_pressed:
					get_viewport().set_input_as_handled()
					if _poller:
						var step := 15.0
						if event.alt_pressed and event.ctrl_pressed:
							step = 300.0  # 5 minutes
						elif event.ctrl_pressed:
							step = 60.0
						_poller.StepPlayback(step, 1)
					if _time_control:
						_time_control.notify_scrub()
			KEY_R:
				if not event.shift_pressed:
					get_viewport().set_input_as_handled()
					if _time_control:
						_time_control.reset_range()
					if _poller:
						_poller.ClearRange()
			KEY_F2:
				get_viewport().set_input_as_handled()
				if _time_control:
					_time_control.toggle_panel()


func _activate_viewpoint(vp: Array) -> void:
	_deselect_shape()
	var vp_key: int = vp[0]
	if vp_key == _active_viewpoint_key:
		return  # Already at this viewpoint

	if not _camera or not _camera.has_method("fly_to_viewpoint"):
		return

	var zone_names: Array = vp[2]
	var offset: Vector3 = vp[3]
	var centroid := _compute_viewpoint_centroid(zone_names)
	var camera_pos := centroid + offset

	_camera.fly_to_viewpoint(camera_pos, centroid)
	_active_viewpoint_key = vp_key


func _compute_viewpoint_centroid(zone_names: Array) -> Vector3:
	if zone_names.is_empty() or not _scene_binder:
		return Vector3.ZERO  # Scene centre for System overview

	var sum := Vector3.ZERO
	var count := 0
	for zone_name: String in zone_names:
		var centroid: Vector3 = _scene_binder.GetZoneCentroid(zone_name)
		if centroid != Vector3.ZERO:
			sum += centroid
			count += 1

	if count == 0:
		return Vector3.ZERO
	return sum / float(count)


func _setup_help_content() -> void:
	var orange := Color(0.94, 0.56, 0.13)
	var purple := Color(0.322, 0.137, 0.925)
	var teal := Color(0.13, 0.84, 0.78)

	var camera_group := HelpGroup.create("Camera", orange, [
		HelpGroup.HelpEntry.create("TAB", "Toggle Orbit / Free Look"),
		HelpGroup.HelpEntry.create("W A S D", "Move (free look mode)"),
		HelpGroup.HelpEntry.create("Q / E", "Descend / Ascend"),
		HelpGroup.HelpEntry.create("SHIFT", "Sprint (hold with movement)"),
		HelpGroup.HelpEntry.create("Right-click", "Look around (hold + drag)"),
		HelpGroup.HelpEntry.create("← → ↑ ↓", "Look around (arrow keys)"),
		HelpGroup.HelpEntry.create("Click", "Select shape (inspect metrics)"),
	])

	var viewpoints_group := HelpGroup.create("Viewpoints", teal, [
		HelpGroup.HelpEntry.create("1", "System overview"),
		HelpGroup.HelpEntry.create("2", "CPU close-up"),
		HelpGroup.HelpEntry.create("3", "Disk close-up"),
		HelpGroup.HelpEntry.create("4", "Network close-up"),
		HelpGroup.HelpEntry.create("ESC", "Return to orbit"),
	])

	var archive_group := HelpGroup.create("Archive Mode Playback", purple, [
		HelpGroup.HelpEntry.create("SPACE", "Play / Pause"),
		HelpGroup.HelpEntry.create("⇧ ← →", "Scrub ±15 seconds"),
		HelpGroup.HelpEntry.create("⌃⇧ ← →", "Scrub ±1 minute"),
		HelpGroup.HelpEntry.create("⌥⌃⇧ ← →", "Scrub ±5 minutes"),
		HelpGroup.HelpEntry.create("R", "Reset time range"),
		HelpGroup.HelpEntry.create("Mouse → edge", "Show timeline panel"),
		HelpGroup.HelpEntry.create("Click", "Jump to time (on timeline)"),
		HelpGroup.HelpEntry.create("⇧ Click", "Set IN / OUT range markers"),
	], _is_archive_mode)

	var panels_group := HelpGroup.create("Panels", orange, [
		HelpGroup.HelpEntry.create("F1", "Range Tuning"),
		HelpGroup.HelpEntry.create("F2", "Timeline Navigator", _is_archive_mode),
		HelpGroup.HelpEntry.create("H / ?", "This help panel"),
	])

	var esc_text: String
	if SceneManager.origin_scene == "fleet":
		esc_text = "Return to Fleet"
	else:
		esc_text = "Return to main menu"
	var general_group := HelpGroup.create("General", orange, [
		HelpGroup.HelpEntry.create("ESC", esc_text) if SceneManager.origin_scene == "fleet" \
			else HelpGroup.HelpEntry.create("ESC × 2", esc_text),
	])

	_help_panel.set_groups([camera_group, viewpoints_group, archive_group, panels_group, general_group])


func _setup_help_hints() -> void:
	_help_hint.set_hints([
		HelpHintEntry.create("H", "for Help"),
		HelpHintEntry.create("TAB", "Orbit / Free Look"),
		HelpHintEntry.create("1-4", "Viewpoints"),
	])
	_help_hint.start_cycling()


func _on_help_opened() -> void:
	_deselect_shape()
	if _camera:
		_camera.input_enabled = false
	if _help_hint:
		_help_hint.hide_hint()
	# Panel exclusivity — close tuner if open
	var scene_root := get_child(0) if get_child_count() > 0 else null
	if scene_root:
		var tuning_panel := scene_root.find_child("RangeTuningPanel", true, false)
		if tuning_panel and tuning_panel.visible:
			tuning_panel.close_panel()


func _on_help_closed() -> void:
	if _camera:
		_camera.input_enabled = true
	# Don't immediately resume hint — let the cycle timer handle it
	if _help_hint:
		_help_hint.resume_hint()


func _on_tuner_opened() -> void:
	_deselect_shape()
	if _camera:
		_camera.input_enabled = false
	# Panel exclusivity — close help if open
	if _help_panel and _help_panel.visible:
		_help_panel.hide_panel()


func _on_tuner_closed() -> void:
	if _camera:
		_camera.input_enabled = true


func _toggle_playback() -> void:
	if _poller:
		_poller.TogglePlayback()


func _dismiss_esc() -> void:
	_esc_pending = false
	esc_label.visible = false


func _handle_click_selection(event: InputEventMouseButton) -> void:
	if not _camera:
		return
	var cam: Camera3D = _camera as Camera3D
	if cam == null:
		return
	var from: Vector3 = cam.project_ray_origin(event.position)
	var dir: Vector3 = cam.project_ray_normal(event.position)
	var to := from + dir * 1000.0

	var space_state := get_world_3d().direct_space_state
	var query := PhysicsRayQueryParameters3D.create(from, to)
	query.collision_mask = 2  # Layer 2 only
	var result := space_state.intersect_ray(query)

	if result.is_empty():
		if _selected_shape != null:
			_deselect_shape()
			get_viewport().set_input_as_handled()
		return
	var hit_node: Node = result["collider"]
	var shape := _find_selectable_ancestor(hit_node)
	if shape and shape != _selected_shape:
		_select_shape(shape)
		get_viewport().set_input_as_handled()
	elif shape == _selected_shape:
		get_viewport().set_input_as_handled()
	elif not shape:
		if _selected_shape != null:
			_deselect_shape()
			get_viewport().set_input_as_handled()


func _find_selectable_ancestor(node: Node) -> Node:
	var current := node
	while current != null:
		if current is StackGroupNode:
			return current
		if current is GroundedShape:
			if current.get_parent() is StackGroupNode:
				return current.get_parent()
			return current
		current = current.get_parent()
	return null


func _select_shape(shape: Node) -> void:
	if _selected_shape and _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)

	_selected_shape = shape
	shape.highlight(true)

	var target_pos: Vector3 = (shape as Node3D).global_position
	if shape is StackGroupNode:
		var sum := Vector3.ZERO
		var count := 0
		for child in shape.get_children():
			if child is Node3D:
				sum += child.global_position
				count += 1
		if count > 0:
			target_pos = sum / float(count)

	var cam3d: Camera3D = _camera as Camera3D if _camera else null
	if cam3d:
		var orbit_height: float = cam3d._orbit_height
		var cam_dir: Vector3 = (cam3d.global_position - target_pos).normalized()
		cam_dir.y = 0.0
		if cam_dir.length_squared() < 0.01:
			cam_dir = Vector3(0, 0, 1)
		cam_dir = cam_dir.normalized()
		var camera_pos: Vector3 = target_pos + cam_dir * 8.0
		camera_pos.y = orbit_height
		cam3d.fly_to_viewpoint(camera_pos, target_pos)
	_active_viewpoint_key = -1

	if _detail_panel and _scene_binder:
		var bindings: Dictionary
		if shape is StackGroupNode:
			bindings = _get_stack_bindings(shape)
		else:
			bindings = _scene_binder.GetBindingsForNode(shape)
		_detail_panel.show_for_shape(bindings)


func _deselect_shape() -> void:
	if _selected_shape == null:
		return
	if is_instance_valid(_selected_shape) and _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)
	_selected_shape = null
	if _detail_panel and _detail_panel.visible:
		_detail_panel.close_panel()


func _on_detail_panel_closed() -> void:
	if _selected_shape and is_instance_valid(_selected_shape) and _selected_shape.has_method("highlight"):
		_selected_shape.highlight(false)
	_selected_shape = null


func _get_stack_bindings(stack: Node) -> Dictionary:
	var zone := ""
	var instance := ""
	var all_properties := {}

	for child in stack.get_children():
		if not child is Node3D:
			continue
		var child_bindings: Dictionary = _scene_binder.GetBindingsForNode(child)
		if child_bindings.is_empty():
			continue
		if zone.is_empty():
			zone = child_bindings.get("zone", "")
			instance = child_bindings.get("instance", "")
		var props: Dictionary = child_bindings.get("properties", {})
		for prop_name: String in props:
			if not all_properties.has(prop_name):
				all_properties[prop_name] = []
			all_properties[prop_name].append_array(props[prop_name])

	return {"zone": zone, "instance": instance, "properties": all_properties}


func _on_metrics_updated_for_detail(_hostname: String, _metrics: Dictionary) -> void:
	if _selected_shape == null or _detail_panel == null or not _detail_panel.visible:
		return
	var bindings: Dictionary
	if _selected_shape is StackGroupNode:
		bindings = _get_stack_bindings(_selected_shape)
	else:
		bindings = _scene_binder.GetBindingsForNode(_selected_shape)
	_detail_panel.update_values(bindings)


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		SceneManager.quit_app()
