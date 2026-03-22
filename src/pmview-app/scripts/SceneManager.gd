extends Node

## Autoload singleton for scene transitions and data passing.
## Registered in project.godot as SceneManager.

## Data passed from main menu to loading scene.
var connection_config: Dictionary = {}

## Built scene graph passed from loading to host view.
var built_scene: Node3D = null

func go_to_loading(config: Dictionary) -> void:
	connection_config = config
	get_tree().change_scene_to_file("res://scenes/loading.tscn")

func go_to_host_view(scene: Node3D) -> void:
	built_scene = scene
	get_tree().change_scene_to_file("res://scenes/host_view.tscn")

func go_to_fleet_view(config: Dictionary) -> void:
	connection_config = config
	get_tree().change_scene_to_file("res://scenes/fleet_view.tscn")

func go_to_main_menu() -> void:
	connection_config = {}
	if built_scene:
		built_scene.queue_free()
		built_scene = null
	get_tree().change_scene_to_file("res://scenes/main_menu.tscn")

func quit_app() -> void:
	get_tree().quit()
