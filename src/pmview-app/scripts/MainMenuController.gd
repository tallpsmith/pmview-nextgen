extends Node3D

## Main menu controller — orbits the camera around the 3D title,
## modulates camera elevation on a different frequency, handles
## connection form submission, drives KITT scanner hover, and manages
## archive mode UI.

# --- Camera orbit ---
@export_group("Camera Orbit")
@export var orbit_speed := 0.3           ## Orbit angular velocity (rad/s)
@export var orbit_radius := 7.5          ## Distance from camera to origin

@export_group("Camera Elevation")
@export var elevation_min := 1.5         ## Lowest camera Y during bob
@export var elevation_max := 4.5         ## Highest camera Y during bob
@export var elevation_speed := 0.2       ## Elevation oscillation speed (rad/s)

@onready var camera_rig: Node3D = $CameraRig
@onready var camera: Camera3D = $CameraRig/Camera3D
@onready var endpoint_input: LineEdit = %EndpointInput
@onready var launch_panel: Panel = %LaunchPanel
@onready var kitt_rect: ColorRect = %KittRect
@onready var live_button: Button = %LiveButton
@onready var archive_button: Button = %ArchiveButton
@onready var archive_panel: VBoxContainer = %ArchivePanel
@onready var host_dropdown: OptionButton = %HostDropdown
@onready var range_label: Label = %RangeLabel
@onready var start_time_input: LineEdit = %StartTimeInput
@onready var verbose_check: CheckButton = %VerboseCheck

@onready var all_hosts_button: Button = %AllHostsButton

var _discovered_hostnames: Array[String] = []
var _sweep_tween: Tween = null
var _orbit_angle := 0.0
var _elevation_angle := 0.0
var _archive_start: String = ""
var _archive_end: String = ""
var _archive_start_epoch: float = 0.0
var _archive_end_epoch: float = 0.0


func _ready() -> void:
	launch_panel.mouse_entered.connect(_on_launch_hover)
	launch_panel.mouse_exited.connect(_on_launch_unhover)
	launch_panel.gui_input.connect(_on_launch_gui_input)

	live_button.pressed.connect(_on_live_pressed)
	archive_button.pressed.connect(_on_archive_pressed)
	host_dropdown.item_selected.connect(_on_host_selected)
	all_hosts_button.pressed.connect(_on_all_hosts_pressed)

	archive_panel.visible = false
	all_hosts_button.visible = false


func _process(delta: float) -> void:
	_update_camera_orbit(delta)


func _update_camera_orbit(delta: float) -> void:
	# Orbit around Y axis
	_orbit_angle = wrapf(_orbit_angle + orbit_speed * delta, 0.0, TAU)
	camera_rig.rotation.y = _orbit_angle

	# Elevation bob on a different frequency
	_elevation_angle = wrapf(_elevation_angle + elevation_speed * delta, 0.0, TAU)
	var elevation_t := (sin(_elevation_angle) + 1.0) * 0.5  # 0..1
	var cam_y := lerpf(elevation_min, elevation_max, elevation_t)

	# Position camera at orbit radius, looking at origin
	camera.position = Vector3(0.0, cam_y, orbit_radius)
	camera.look_at(Vector3.ZERO, Vector3.UP)


# --- Mode switching ---

func _on_live_pressed() -> void:
	archive_panel.visible = false


func _on_archive_pressed() -> void:
	archive_panel.visible = true
	_fetch_hostnames()


func _fetch_hostnames() -> void:
	range_label.text = ""
	start_time_input.text = ""
	host_dropdown.clear()
	host_dropdown.add_item("Loading...")
	host_dropdown.disabled = true

	var http := HTTPRequest.new()
	add_child(http)
	http.request_completed.connect(_on_hostnames_response.bind(http))
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:54322"
	http.request(url + "/series/labels?names=hostname")


func _on_hostnames_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest) -> void:
	http.queue_free()
	host_dropdown.clear()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		host_dropdown.add_item("(connection error)")
		return

	var json = JSON.parse_string(body.get_string_from_utf8())
	if json == null or not json.has("hostname"):
		host_dropdown.add_item("(no hosts found)")
		return

	var hostnames: Array = json["hostname"]
	if hostnames.is_empty():
		host_dropdown.add_item("(no hosts found)")
		return

	host_dropdown.disabled = false
	_discovered_hostnames.clear()
	for hostname in hostnames:
		host_dropdown.add_item(hostname)
		_discovered_hostnames.append(hostname)

	# Show ALL HOSTS button when multiple hosts are available
	all_hosts_button.visible = _discovered_hostnames.size() > 1

	_on_host_selected(0)


func _on_host_selected(index: int) -> void:
	var hostname := host_dropdown.get_item_text(index)
	range_label.text = "Probing..."
	start_time_input.text = ""
	_set_launch_enabled(false)
	_probe_time_bounds(hostname)


func _set_launch_enabled(enabled: bool) -> void:
	start_time_input.editable = enabled
	if enabled:
		launch_panel.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		launch_panel.modulate = Color(1, 1, 1, 1)
	else:
		launch_panel.mouse_default_cursor_shape = Control.CURSOR_FORBIDDEN
		launch_panel.modulate = Color(0.4, 0.4, 0.5, 0.6)
		_kill_sweep_tween()
		var mat := kitt_rect.material as ShaderMaterial
		if mat:
			mat.set_shader_parameter("intensity", 0.0)


func _probe_time_bounds(hostname: String) -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:54322"

	# Query series for this hostname (URL-encoded filter)
	var filter := "kernel.all.load{hostname==\"%s\"}" % hostname
	var encoded_filter := filter.uri_encode()
	var query_url := url + "/series/query?expr=" + encoded_filter

	var http := HTTPRequest.new()
	add_child(http)
	http.request_completed.connect(
		_on_series_query_response.bind(http, url, hostname))
	http.request(query_url)


func _on_series_query_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest, endpoint: String, _hostname: String) -> void:
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		range_label.text = "Query failed"
		return

	var series_ids = JSON.parse_string(body.get_string_from_utf8())
	if series_ids == null or series_ids.is_empty():
		range_label.text = "No data for this host"
		return

	# Fetch values with 30-day window
	var series_id: String = series_ids[0]
	var now := Time.get_unix_time_from_system()
	var start := now - (30 * 86400)
	var values_url := "%s/series/values?series=%s&start=%s&finish=%s" % [
		endpoint, series_id, "%.3f" % start, "%.3f" % now]

	var http2 := HTTPRequest.new()
	add_child(http2)
	http2.request_completed.connect(_on_values_response.bind(http2))
	http2.request(values_url)


func _on_values_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest) -> void:
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		range_label.text = "Probe failed"
		return

	var values = JSON.parse_string(body.get_string_from_utf8())
	if values == null or values.is_empty():
		range_label.text = "No archive data found"
		return

	# Find min/max timestamps
	var min_ts: float = values[0]["timestamp"]
	var max_ts: float = values[0]["timestamp"]
	for v in values:
		var ts: float = v["timestamp"]
		if ts < min_ts:
			min_ts = ts
		if ts > max_ts:
			max_ts = ts

	# Convert epoch ms to ISO 8601
	var start_dt := Time.get_datetime_string_from_unix_time(int(min_ts / 1000.0))
	var end_dt := Time.get_datetime_string_from_unix_time(int(max_ts / 1000.0))
	_archive_start = start_dt
	_archive_end = end_dt
	_archive_start_epoch = min_ts / 1000.0
	_archive_end_epoch = max_ts / 1000.0
	range_label.text = "RANGE: %s → %s" % [start_dt, end_dt]

	# Default start time: end - 24h, clamped to archive start
	var default_start_epoch := max_ts / 1000.0 - 86400.0
	if default_start_epoch < min_ts / 1000.0:
		default_start_epoch = min_ts / 1000.0
	start_time_input.text = Time.get_datetime_string_from_unix_time(
		int(default_start_epoch)) + "Z"
	_set_launch_enabled(true)


# --- LAUNCH button hover: KITT scanner effect ---

func _on_launch_hover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return

	mat.set_shader_parameter("intensity", 1.0)

	_kill_sweep_tween()
	_sweep_tween = create_tween().set_loops()
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", 1.4, 0.9)
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", -0.4, 0.9)


func _on_launch_unhover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return
	_kill_sweep_tween()

	var fade := create_tween()
	fade.tween_property(mat, "shader_parameter/intensity", 0.0, 0.3)


func _kill_sweep_tween() -> void:
	if _sweep_tween and _sweep_tween.is_valid():
		_sweep_tween.kill()
		_sweep_tween = null


# --- LAUNCH button press ---

func _on_launch_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed \
			and event.button_index == MOUSE_BUTTON_LEFT:
		_launch()


func _launch() -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:54322"

	if archive_button.button_pressed:
		if not start_time_input.editable:
			return  # Still probing — don't launch yet
		var hostname := host_dropdown.get_item_text(host_dropdown.selected)
		var start_time := start_time_input.text.strip_edges()
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "archive",
			"hostname": hostname,
			"start_time": start_time,
			"archive_start_epoch": _archive_start_epoch,
			"archive_end_epoch": _archive_end_epoch,
			"verbose_logging": verbose_check.button_pressed,
		})
	else:
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "live",
			"verbose_logging": verbose_check.button_pressed,
		})


# --- Fleet view launch ---

func _on_all_hosts_pressed() -> void:
	if _discovered_hostnames.is_empty():
		return
	_launch_fleet(PackedStringArray(_discovered_hostnames))


func _launch_fleet(hostnames: PackedStringArray) -> void:
	var url: String = endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:54322"
	var config := {
		"endpoint": url,
		"mode": "archive" if archive_button.button_pressed else "live",
		"hostnames": hostnames,
		"verbose_logging": verbose_check.button_pressed,
	}
	if archive_button.button_pressed:
		config["start_time"] = start_time_input.text
		config["archive_start_epoch"] = _archive_start_epoch
		config["archive_end_epoch"] = _archive_end_epoch
	SceneManager.go_to_fleet_view(config)
