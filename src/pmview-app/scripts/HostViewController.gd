extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay and archive controls.

@onready var esc_label: Label = %EscLabel

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null
var _poller: Node = null
var _time_control: Control = null
var _help_panel: Node = null
var _help_hint: Node = null
var _camera: Node = null
var _timestamp_label_3d: Node = null
var _is_archive_mode := false
var _poll_interval_seconds := 60.0


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

	print("[HostView] Adding built scene to tree...")
	add_child(scene)
	print("[HostView] Built scene added to tree")

	# Wire Help panel and hint
	_help_panel = scene.find_child("HelpPanel", true, false)
	_help_hint = scene.find_child("HelpHint", true, false)
	_camera = get_viewport().get_camera_3d()
	print("[HostView] HelpPanel found: %s, HelpHint found: %s, Camera found: %s" % [
		_help_panel != null, _help_hint != null, _camera != null])

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
			var start_time: String = config.get("start_time", "")
			if start_time.is_empty():
				# No start time provided — default to 24h ago
				var default_epoch := Time.get_unix_time_from_system() - 86400.0
				start_time = Time.get_datetime_string_from_unix_time(
					int(default_epoch)) + "Z"
				print("[HostView] No start time in config, defaulting to: %s" % start_time)
			else:
				print("[HostView] Starting archive playback at: %s" % start_time)
			# Defer StartPlayback so it runs after MetricPoller._Ready() has
			# connected to pmproxy and started the poll timer via CallDeferred.
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

	# ESC — close help panel first if open
	if event.is_action_pressed("ui_cancel") and _help_panel and _help_panel.visible:
		_help_panel.hide_panel()
		get_viewport().set_input_as_handled()
		return

	# ESC — double-press to return to menu
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
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
				get_viewport().set_input_as_handled()
				if _poller:
					var step := _poll_interval_seconds
					if event.ctrl_pressed and event.shift_pressed:
						step = 60.0
					elif event.shift_pressed:
						step = 5.0
					_poller.StepPlayback(step, -1)
				if _time_control:
					_time_control.notify_scrub()
			KEY_RIGHT:
				get_viewport().set_input_as_handled()
				if _poller:
					var step := _poll_interval_seconds
					if event.ctrl_pressed and event.shift_pressed:
						step = 60.0
					elif event.shift_pressed:
						step = 5.0
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


func _setup_help_content() -> void:
	var orange := Color(0.94, 0.56, 0.13)
	var purple := Color(0.322, 0.137, 0.925)

	var camera_group := HelpGroup.create("Camera", orange, [
		HelpGroup.HelpEntry.create("TAB", "Toggle Orbit / Free Look"),
		HelpGroup.HelpEntry.create("W A S D", "Move (free look mode)"),
		HelpGroup.HelpEntry.create("Q / E", "Descend / Ascend"),
		HelpGroup.HelpEntry.create("SHIFT", "Sprint (hold with movement)"),
		HelpGroup.HelpEntry.create("Right-click", "Look around (hold + drag)"),
	])

	var archive_group := HelpGroup.create("Archive Mode Playback", purple, [
		HelpGroup.HelpEntry.create("SPACE", "Play / Pause"),
		HelpGroup.HelpEntry.create("← →", "Scrub (poll interval)"),
		HelpGroup.HelpEntry.create("⇧ ← →", "Scrub ±5 seconds"),
		HelpGroup.HelpEntry.create("⌃⇧ ← →", "Scrub ±1 minute"),
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

	var general_group := HelpGroup.create("General", orange, [
		HelpGroup.HelpEntry.create("ESC × 2", "Return to main menu"),
	])

	_help_panel.set_groups([camera_group, archive_group, panels_group, general_group])


func _setup_help_hints() -> void:
	_help_hint.set_hints([
		HelpHintEntry.create("H", "for Help"),
		HelpHintEntry.create("TAB", "Orbit / Free Look"),
	])
	_help_hint.start_cycling()


func _on_help_opened() -> void:
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


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		SceneManager.quit_app()
