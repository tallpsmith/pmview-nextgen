extends Node

## Autoload singleton for scene transitions and data passing.
## Registered in project.godot as SceneManager.

## Data passed from main menu to loading scene.
var connection_config: Dictionary = {}

## Built scene graph passed from loading to host view.
var built_scene: Node3D = null

## Tracks where HostView was launched from ("fleet" or "")
var origin_scene: String = ""

## Hostname that was focused when dive-in occurred (for restoring fleet focus)
var fleet_focused_hostname: String = ""

func go_to_loading(config: Dictionary) -> void:
	connection_config = config
	get_tree().change_scene_to_file("res://scenes/loading.tscn")

func go_to_host_view(scene: Node3D) -> void:
	built_scene = scene
	get_tree().change_scene_to_file("res://scenes/host_view.tscn")

func go_to_fleet_view(config: Dictionary) -> void:
	connection_config = config
	get_tree().change_scene_to_file("res://scenes/fleet_view.tscn")

func go_to_host_view_from_fleet(scene: Node3D, focused_hostname: String) -> void:
	origin_scene = "fleet"
	fleet_focused_hostname = focused_hostname
	built_scene = scene
	get_tree().change_scene_to_file("res://scenes/host_view.tscn")


func return_to_fleet() -> void:
	origin_scene = ""
	# connection_config and fleet_focused_hostname are preserved
	# so fleet view can restore focus on the same host
	if built_scene:
		built_scene.queue_free()
		built_scene = null
	get_tree().change_scene_to_file("res://scenes/fleet_view.tscn")


func go_to_main_menu() -> void:
	connection_config = {}
	origin_scene = ""
	fleet_focused_hostname = ""
	if built_scene:
		built_scene.queue_free()
		built_scene = null
	get_tree().change_scene_to_file("res://scenes/main_menu.tscn")

func quit_app() -> void:
	get_tree().quit()
