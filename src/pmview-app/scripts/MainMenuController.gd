extends Node3D

## Main menu controller — rotates 3D title letters, handles connection
## form submission, drives KITT scanner hover, and manages archive mode UI.

const ROTATION_SPEED := 0.3
const PHASE_OFFSET := 0.4

@onready var title_group: Node3D = $TitleGroup
@onready var endpoint_input: LineEdit = %EndpointInput
@onready var launch_panel: Panel = %LaunchPanel
@onready var kitt_rect: ColorRect = %KittRect
@onready var live_button: Button = %LiveButton
@onready var archive_button: Button = %ArchiveButton
@onready var archive_panel: VBoxContainer = %ArchivePanel
@onready var host_dropdown: OptionButton = %HostDropdown
@onready var range_label: Label = %RangeLabel
@onready var start_time_input: LineEdit = %StartTimeInput

var _sweep_tween: Tween = null
var _archive_start: String = ""
var _archive_end: String = ""


func _ready() -> void:
	launch_panel.mouse_entered.connect(_on_launch_hover)
	launch_panel.mouse_exited.connect(_on_launch_unhover)
	launch_panel.gui_input.connect(_on_launch_gui_input)

	live_button.pressed.connect(_on_live_pressed)
	archive_button.pressed.connect(_on_archive_pressed)
	host_dropdown.item_selected.connect(_on_host_selected)

	archive_panel.visible = false


func _process(delta: float) -> void:
	_rotate_title_letters(delta)


func _rotate_title_letters(delta: float) -> void:
	title_group.rotate_y((ROTATION_SPEED + sin(Time.get_ticks_msec() * 0.001) * 0.15) * delta)


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
		url = "http://localhost:44322"
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
	for hostname in hostnames:
		host_dropdown.add_item(hostname)

	_on_host_selected(0)


func _on_host_selected(index: int) -> void:
	var hostname := host_dropdown.get_item_text(index)
	range_label.text = "Probing..."
	_probe_time_bounds(hostname)


func _probe_time_bounds(hostname: String) -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:44322"

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
	range_label.text = "RANGE: %s → %s" % [start_dt, end_dt]

	# Default start time: end - 24h, clamped to archive start
	var default_start_epoch := max_ts / 1000.0 - 86400.0
	if default_start_epoch < min_ts / 1000.0:
		default_start_epoch = min_ts / 1000.0
	start_time_input.text = Time.get_datetime_string_from_unix_time(
		int(default_start_epoch)) + "Z"


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
		url = "http://localhost:44322"

	if archive_button.button_pressed:
		var hostname := host_dropdown.get_item_text(host_dropdown.selected)
		var start_time := start_time_input.text.strip_edges()
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "archive",
			"hostname": hostname,
			"start_time": start_time,
		})
	else:
		SceneManager.go_to_loading({"endpoint": url, "mode": "live"})
