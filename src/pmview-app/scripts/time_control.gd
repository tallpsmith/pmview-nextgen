extends Control

## Time Control panel — Time Machine-style timeline navigation.
## Translucent right-edge overlay with timeline bars, playhead, and IN/OUT points.
## Archive mode only.

signal playhead_jumped(timestamp: String)
signal range_set(in_time: String, out_time: String)
signal range_cleared()
signal panel_opened()
signal panel_closed()

# Trigger zone — wider so the bars can grow visibly as mouse approaches
const EDGE_TRIGGER_WIDTH := 200
const EDGE_HIDE_MARGIN := 60

# Bar dimensions — bigger, fewer, more readable
const BAR_MAX_LENGTH := 120.0
const BAR_MIN_LENGTH := 6.0
const BAR_HEIGHT := 4.0
const BAR_SPACING := 10.0
const ATTRACTION_RADIUS := 200.0
const PANEL_WIDTH := 140.0
const PANEL_PADDING := 30.0

var archive_start: float = 0.0
var archive_end: float = 0.0
var playhead_position: float = 0.0
var in_point: float = -1.0
var out_point: float = -1.0
var _is_visible := false
var _f2_dismissed := false

enum RangeState { NO_RANGE, IN_SET, RANGE_COMPLETE }
var _range_state: RangeState = RangeState.NO_RANGE

var colour_active := Color(0.514, 0.22, 0.925, 0.7)
var colour_inactive := Color(0.3, 0.3, 0.3, 0.3)
var colour_playhead := Color(0.976, 0.451, 0.086, 0.9)
var colour_in_point := Color(0.298, 0.686, 0.314, 0.9)
var colour_out_point := Color(0.937, 0.325, 0.314, 0.9)
var colour_panel_bg := Color(0.05, 0.04, 0.08, 0.5)

@onready var timestamp_label: Label = $TimestampLabel


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_PASS
	visible = false


func _process(_delta: float) -> void:
	var viewport_size := get_viewport_rect().size
	var mouse_pos := get_global_mouse_position()

	if not _is_visible:
		if mouse_pos.x > viewport_size.x - EDGE_TRIGGER_WIDTH and not _f2_dismissed:
			_show_panel()
	else:
		if mouse_pos.x < viewport_size.x - EDGE_TRIGGER_WIDTH - EDGE_HIDE_MARGIN:
			_hide_panel()
		queue_redraw()
		_update_timestamp_tooltip()


func _show_panel() -> void:
	if _is_visible:
		return
	_is_visible = true
	visible = true
	panel_opened.emit()


func _hide_panel() -> void:
	if not _is_visible:
		return
	_is_visible = false
	visible = false
	panel_closed.emit()


func toggle_panel() -> void:
	if _is_visible:
		_f2_dismissed = true
		_hide_panel()
	else:
		_f2_dismissed = false
		_show_panel()


func update_playhead(position_iso: String, _mode: String) -> void:
	var dict := Time.get_datetime_dict_from_datetime_string(position_iso, false)
	if dict.is_empty():
		return
	playhead_position = Time.get_unix_time_from_datetime_dict(dict)
	if _is_visible:
		queue_redraw()


func set_archive_bounds(start_epoch: float, end_epoch: float) -> void:
	archive_start = start_epoch
	archive_end = end_epoch


## Attempt to smooth the bar attraction using an ease-in-out curve
## instead of linear falloff. This creates a gentler peak that
## rolls off naturally rather than a harsh triangular shape.
func _ease_in_out(t: float) -> float:
	# Hermite smoothstep: 3t^2 - 2t^3
	return t * t * (3.0 - 2.0 * t)


func _draw() -> void:
	if archive_end <= archive_start:
		return

	var rect := get_rect()
	var panel_x := rect.size.x - PANEL_WIDTH
	var mouse_pos := get_local_mouse_position()

	# Translucent background
	draw_rect(Rect2(panel_x, 0, PANEL_WIDTH, rect.size.y), colour_panel_bg)

	# Calculate bars — fewer, bigger bars for readability
	var usable_height := rect.size.y - PANEL_PADDING * 2
	var bar_count := int(usable_height / (BAR_HEIGHT + BAR_SPACING))
	if bar_count <= 0:
		return

	var time_range := archive_end - archive_start
	var bar_right := rect.size.x - 10.0
	var time_per_bar := time_range / float(maxi(bar_count - 1, 1))

	# Distance from mouse to right edge — drives overall bar growth
	# as the mouse approaches (wider trigger zone = visible lerp)
	var edge_distance: float = rect.size.x - mouse_pos.x
	var edge_proximity := clampf(edge_distance / float(EDGE_TRIGGER_WIDTH), 0.0, 1.0)
	# Invert: closer to edge = higher proximity
	edge_proximity = 1.0 - edge_proximity
	var edge_factor: float = _ease_in_out(edge_proximity)

	for i in bar_count:
		var y := PANEL_PADDING + i * (BAR_HEIGHT + BAR_SPACING)
		var t := archive_start + float(i) / float(maxi(bar_count - 1, 1)) * time_range

		# Colour based on IN/OUT range
		var colour: Color
		if _range_state == RangeState.NO_RANGE:
			colour = colour_active
		elif in_point >= 0 and out_point >= 0:
			colour = colour_active if t >= in_point and t <= out_point else colour_inactive
		elif in_point >= 0:
			colour = colour_active if t >= in_point else colour_inactive
		else:
			colour = colour_active

		# Mouse attraction — ease in/out curve for smooth peak rolloff
		var distance: float = absf(mouse_pos.y - y)
		var linear_attraction := clampf(1.0 - distance / ATTRACTION_RADIUS, 0.0, 1.0)
		var attraction: float = _ease_in_out(linear_attraction)

		# Combine edge proximity (overall growth) with vertical attraction (peak)
		var bar_length: float = BAR_MIN_LENGTH + (BAR_MAX_LENGTH - BAR_MIN_LENGTH) * attraction * edge_factor

		# Special markers: playhead, IN, OUT — always prominent
		var is_playhead: bool = absf(t - playhead_position) < time_per_bar * 0.5
		var is_in: bool = in_point >= 0 and absf(t - in_point) < time_per_bar * 0.5
		var is_out: bool = out_point >= 0 and absf(t - out_point) < time_per_bar * 0.5

		if is_playhead:
			colour = colour_playhead
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.7 * edge_factor)
		elif is_in:
			colour = colour_in_point
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.55 * edge_factor)
		elif is_out:
			colour = colour_out_point
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.55 * edge_factor)

		# Ensure minimum visibility even at edge of trigger zone
		bar_length = maxf(bar_length, BAR_MIN_LENGTH * edge_factor)

		if bar_length > 0.5:
			draw_rect(Rect2(bar_right - bar_length, y, bar_length, BAR_HEIGHT), colour)


func _update_timestamp_tooltip() -> void:
	if not timestamp_label or archive_end <= archive_start:
		return

	var rect := get_rect()
	var mouse_pos := get_local_mouse_position()
	var usable_height := rect.size.y - PANEL_PADDING * 2
	var relative_y := clampf((mouse_pos.y - PANEL_PADDING) / usable_height, 0.0, 1.0)
	var hover_time := archive_start + relative_y * (archive_end - archive_start)
	var hover_dt := Time.get_datetime_string_from_unix_time(int(hover_time))

	timestamp_label.text = hover_dt + "Z"
	timestamp_label.position = Vector2(rect.size.x - 320, mouse_pos.y - 12)
	timestamp_label.visible = true


func _gui_input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	if not event.pressed or event.button_index != MOUSE_BUTTON_LEFT:
		return
	if archive_end <= archive_start:
		return

	var rect := get_rect()
	var usable_height := rect.size.y - PANEL_PADDING * 2
	var relative_y := clampf((event.position.y - PANEL_PADDING) / usable_height, 0.0, 1.0)
	var clicked_time := archive_start + relative_y * (archive_end - archive_start)
	var clicked_iso := Time.get_datetime_string_from_unix_time(int(clicked_time)) + "Z"

	if event.shift_pressed:
		_handle_shift_click(clicked_time, clicked_iso)
	else:
		playhead_jumped.emit(clicked_iso)


func _handle_shift_click(time_epoch: float, time_iso: String) -> void:
	match _range_state:
		RangeState.NO_RANGE:
			in_point = time_epoch
			out_point = archive_end
			_range_state = RangeState.IN_SET
			var out_iso := Time.get_datetime_string_from_unix_time(
				int(archive_end)) + "Z"
			range_set.emit(time_iso, out_iso)
		RangeState.IN_SET:
			out_point = time_epoch
			_range_state = RangeState.RANGE_COMPLETE
			var in_iso := Time.get_datetime_string_from_unix_time(
				int(in_point)) + "Z"
			range_set.emit(in_iso, time_iso)
		RangeState.RANGE_COMPLETE:
			in_point = time_epoch
			out_point = archive_end
			_range_state = RangeState.IN_SET
			var out_iso := Time.get_datetime_string_from_unix_time(
				int(archive_end)) + "Z"
			range_set.emit(time_iso, out_iso)


func reset_range() -> void:
	in_point = -1.0
	out_point = -1.0
	_range_state = RangeState.NO_RANGE
	range_cleared.emit()
