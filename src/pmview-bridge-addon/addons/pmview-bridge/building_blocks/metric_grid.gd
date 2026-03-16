@tool
class_name MetricGrid
extends Node3D

## Spacing between columns along +X axis (projector overrides to 2.0 for background grids).
@export var column_spacing: float = 1.2:
	set(value):
		column_spacing = value
		_arrange()

## Spacing between rows along -Z axis.
@export var row_spacing: float = 2.5:
	set(value):
		row_spacing = value
		_arrange()

## Gap between back/right bezel edge and metric/instance labels.
@export var label_gap: float = 2.0:
	set(value):
		label_gap = value
		_arrange()

## Glob filter: only show metrics matching this pattern (empty = show all).
@export var metric_include_filter: String = ""
## Glob filter: hide metrics matching this pattern (takes precedence over include).
@export var metric_exclude_filter: String = ""
## Glob filter: only show instances matching this pattern (empty = show all).
@export var instance_include_filter: String = ""
## Glob filter: hide instances matching this pattern (takes precedence over include).
@export var instance_exclude_filter: String = ""

## Metric labels for column headers (set by projector, read-only in practice).
@export var metric_labels: PackedStringArray = PackedStringArray()
## Instance labels for row headers (set by projector, read-only in practice).
@export var instance_labels: PackedStringArray = PackedStringArray()

var _column_header_nodes: Array[Label3D] = []
var _row_header_nodes: Array[Label3D] = []

func _ready() -> void:
	_arrange()
	child_entered_tree.connect(_on_child_changed)
	child_exiting_tree.connect(_on_child_changed)

func _on_child_changed(node: Node) -> void:
	# Ignore header labels that _arrange itself creates/removes to avoid infinite loop.
	if node is Label3D:
		return
	_arrange.call_deferred()

func get_column_count() -> int:
	return maxi(metric_labels.size(), 1)

func get_row_count() -> int:
	var cols := get_column_count()
	var shape_count := _get_shape_children().size()
	if shape_count == 0:
		return maxi(instance_labels.size(), 1)
	@warning_ignore("integer_division")
	return ceili(float(shape_count) / float(cols))

func get_extent() -> Vector2:
	var cols := get_column_count()
	var rows := get_row_count()
	var w := (cols - 1) * column_spacing + 0.8  # 0.8 = shape width
	var d := (rows - 1) * row_spacing + 0.8
	return Vector2(w, d)

func _arrange() -> void:
	var shapes := _get_shape_children()
	var cols := get_column_count()
	_apply_filters(shapes)
	var visible_idx := 0
	for shape in shapes:
		if not shape.visible:
			continue
		@warning_ignore("integer_division")
		var col := visible_idx % cols
		@warning_ignore("integer_division")
		var row := visible_idx / cols
		shape.position = Vector3(
			col * column_spacing,
			0,
			-row * row_spacing
		)
		visible_idx += 1
	_rebuild_column_headers()
	_rebuild_row_headers()

func _get_shape_children() -> Array:
	var result: Array = []
	for child in get_children():
		if child is Node3D and not child is Label3D and not child is MeshInstance3D:
			result.append(child)
	return result

func _apply_filters(shapes: Array) -> void:
	for shape in shapes:
		var metric_name := _extract_metric_label(shape)
		var instance_name := _extract_instance_label(shape)
		var show := true
		if metric_include_filter != "" and not metric_name.matchn(metric_include_filter):
			show = false
		if metric_exclude_filter != "" and metric_name.matchn(metric_exclude_filter):
			show = false
		if instance_include_filter != "" and instance_name != "" and not instance_name.matchn(instance_include_filter):
			show = false
		if instance_exclude_filter != "" and instance_name != "" and instance_name.matchn(instance_exclude_filter):
			show = false
		shape.visible = show

func _extract_metric_label(shape: Node3D) -> String:
	# Convention: node name ends with _MetricLabel (e.g., CPU_User -> "User")
	var parts := shape.name.split("_")
	if parts.size() > 1:
		return parts[-1]
	return shape.name

func _extract_instance_label(shape: Node3D) -> String:
	# Convention: for per-instance shapes, name is Zone_Instance_Metric
	var parts := shape.name.split("_")
	if parts.size() > 2:
		return parts[-2]
	return ""

func _rebuild_column_headers() -> void:
	for label in _column_header_nodes:
		if is_instance_valid(label):
			label.queue_free()
	_column_header_nodes.clear()

	if metric_labels.is_empty():
		return

	var rows := get_row_count()
	var z := -(rows - 1) * row_spacing - label_gap

	for i in metric_labels.size():
		var label := Label3D.new()
		label.name = "ColHeader_%d" % i
		label.text = metric_labels[i]
		label.font_size = 40
		label.pixel_size = 0.01
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		var x := float(i) * column_spacing
		# Flat-on-floor rotated 90° around Y — text reads in -Z ("branches" from back edge).
		label.transform = Transform3D(
			Vector3(0, 0, -1),
			Vector3(-1, 0, 0),
			Vector3(0, 1, 0),
			Vector3(x, 0.01, z)
		)
		add_child(label)
		_column_header_nodes.append(label)

func _rebuild_row_headers() -> void:
	for label in _row_header_nodes:
		if is_instance_valid(label):
			label.queue_free()
	_row_header_nodes.clear()

	if instance_labels.is_empty():
		return

	var cols := get_column_count()
	var x := (cols - 1) * column_spacing + 0.8 + label_gap

	for i in instance_labels.size():
		var label := Label3D.new()
		label.name = "RowHeader_%d" % i
		label.text = instance_labels[i]
		label.font_size = 40
		label.pixel_size = 0.01
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		var z := -float(i) * row_spacing
		label.transform = Transform3D(
			Vector3(1, 0, 0),
			Vector3(0, 0, -1),
			Vector3(0, 1, 0),
			Vector3(x, 0.01, z)
		)
		add_child(label)
		_row_header_nodes.append(label)
