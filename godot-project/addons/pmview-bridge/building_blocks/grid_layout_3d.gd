@tool
class_name GridLayout3D
extends Node3D

## Number of columns before wrapping to a new row.
@export var columns: int = 3:
	set(value):
		columns = maxi(value, 1)
		_arrange()

## Spacing between columns along X axis.
@export var column_spacing: float = 1.5:
	set(value):
		column_spacing = value
		_arrange()

## Spacing between rows along negative Z axis.
@export var row_spacing: float = 2.0:
	set(value):
		row_spacing = value
		_arrange()

func _ready() -> void:
	_arrange()
	child_entered_tree.connect(_on_child_changed)
	child_exiting_tree.connect(_on_child_changed)

func _on_child_changed(_node: Node) -> void:
	_arrange.call_deferred()

func _arrange() -> void:
	var idx := 0
	for child in get_children():
		if child is Node3D and not child is Label3D:
			var col := idx % columns
			var row := idx / columns
			child.position = Vector3(
				col * column_spacing,
				0,
				-row * row_spacing
			)
			idx += 1
