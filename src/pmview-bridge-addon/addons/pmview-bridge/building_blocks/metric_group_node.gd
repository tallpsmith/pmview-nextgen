@tool
class_name MetricGroupNode
extends Node3D

## Title text displayed on the foreground edge of the bezel.
@export var title_text: String = "":
	set(value):
		title_text = value
		if _title_label:
			_title_label.text = title_text

## Gap between the bezel front edge and the title label.
@export var title_gap: float = 1.5

## Gap passed to MetricGrid for metric/instance label offset from bezel edge.
@export var label_gap: float = 1.0:
	set(value):
		label_gap = value
		var grid := _find_grid()
		if grid:
			grid.label_gap = label_gap

var _title_label: Label3D = null

func _ready() -> void:
	_create_title_label()
	var grid := _find_grid()
	if grid:
		grid.label_gap = label_gap

func _process(_delta: float) -> void:
	var grid := _find_grid()
	if grid == null:
		return
	var extent := grid.get_extent()
	_update_title_position(extent, grid)
	_update_bezel(extent, grid)

func _create_title_label() -> void:
	_title_label = Label3D.new()
	_title_label.name = "TitleLabel"
	_title_label.text = title_text
	_title_label.font_size = 56
	_title_label.pixel_size = 0.01
	_title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title_label)

func _update_title_position(extent: Vector2, grid: MetricGrid) -> void:
	if _title_label == null:
		return
	var centre_x := extent.x / 2.0
	var front_z := title_gap
	_title_label.transform = Transform3D(
		Vector3(1, 0, 0),
		Vector3(0, 0, 1),
		Vector3(0, -1, 0),
		Vector3(centre_x, 0.01, front_z)
	)

func _update_bezel(extent: Vector2, grid: MetricGrid) -> void:
	var bezel := _find_bezel()
	if bezel == null:
		return
	bezel.resize(extent.x, extent.y)
	bezel.position = Vector3(extent.x / 2.0, -0.01, -extent.y / 2.0 + 0.4)

func _find_grid() -> MetricGrid:
	for child in get_children():
		if child is MetricGrid:
			return child
	return null

func _find_bezel() -> GroundBezel:
	for child in get_children():
		if child is GroundBezel:
			return child
	return null
