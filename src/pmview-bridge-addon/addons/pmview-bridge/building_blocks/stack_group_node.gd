@tool
class_name StackGroupNode
extends Node3D

## Stacks child grounded_bar nodes vertically every _process frame.
##
## Two modes:
##   PROPORTIONAL — stack height reflects real utilisation; bars grow/shrink as values change.
##   NORMALISED   — stack always fills target_height regardless of total; shows proportions only.
##
## SceneBinder drives each child's scale.y via the height binding.
## This node reads those smoothed values and sets child position.y so bars sit on top of each other.
## All children should have position.y = 0 in the scene file; this node owns Y at runtime.

enum StackMode { PROPORTIONAL, NORMALISED }

@export var stack_mode: StackMode = StackMode.PROPORTIONAL
@export var target_height: float = 4.0

func _process(_delta: float) -> void:
	var children := get_children()
	if children.is_empty():
		return
	match stack_mode:
		StackMode.PROPORTIONAL:
			_layout_proportional(children)
		StackMode.NORMALISED:
			_layout_normalised(children)


## Proportional: stack height reflects real utilisation.
## Each bar's Y offset is the sum of all bars below it.
func _layout_proportional(children: Array) -> void:
	var offset := 0.0
	for child in children:
		if child is Node3D:
			child.position.y = offset
			offset += child.scale.y


## Normalised: stack always fills target_height regardless of total utilisation.
## Preserves relative proportions; rescales all bars so they sum to target_height.
func _layout_normalised(children: Array) -> void:
	var bars: Array = children.filter(func(c): return c is Node3D)
	var total_h: float = bars.reduce(func(acc, c): return acc + c.scale.y, 0.0)
	if total_h < 0.001:
		return  # avoid div/0 when system is idle
	var scale_factor: float = target_height / total_h
	var offset := 0.0
	for bar in bars:
		var h: float = bar.scale.y * scale_factor
		bar.scale.y = h
		bar.position.y = offset
		offset += h


func highlight(enabled: bool) -> void:
	for child in get_children():
		if child.has_method("highlight"):
			child.highlight(enabled)


func is_highlighted() -> bool:
	for child in get_children():
		if child.has_method("is_highlighted"):
			return child.is_highlighted()
	return false
