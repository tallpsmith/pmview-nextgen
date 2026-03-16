extends Node3D

## Loads all host-view .tscn scenes from res://scenes/ into the SceneRoot
## node at startup. Skips main.tscn (that's us).

@onready var _scene_root: Node3D = $SceneRoot

func _ready() -> void:
	var scenes_dir := "res://scenes/"
	var dir := DirAccess.open(scenes_dir)
	if dir == null:
		push_warning("[MainSceneController] Cannot open %s" % scenes_dir)
		return

	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if _is_loadable_scene(file_name):
			var scene_path := scenes_dir + file_name
			var packed := load(scene_path) as PackedScene
			if packed:
				var instance := packed.instantiate()
				_scene_root.add_child(instance)
				print("[MainSceneController] Loaded %s" % scene_path)
		file_name = dir.get_next()
	dir.list_dir_end()

func _is_loadable_scene(file_name: String) -> bool:
	if file_name == "main.tscn":
		return false
	# Godot imports .tscn to .tscn.remap or keeps as .tscn
	return file_name.ends_with(".tscn") or file_name.ends_with(".tscn.remap")
