extends Control

## Config selector UI: scans bindings/ for available configs,
## displays them in a list, triggers scene swapping on selection.

signal config_selected(config_path: String)

@onready var config_list: ItemList = $ConfigList
@onready var description_label: Label = $DescriptionLabel

var _config_paths: Array[String] = []

func _ready() -> void:
	config_list.item_selected.connect(_on_config_selected)
	scan_configs()

func scan_configs() -> void:
	config_list.clear()
	_config_paths.clear()

	var dir = DirAccess.open("res://bindings")
	if dir == null:
		push_warning("[ConfigSelector] Cannot open res://bindings/")
		return

	dir.list_dir_begin()
	var file_name = dir.get_next()

	while file_name != "":
		if file_name.ends_with(".toml"):
			var full_path = "res://bindings/%s" % file_name
			_config_paths.append(full_path)

			# Read description from TOML meta section (basic parse)
			var display_name = _read_config_description(full_path, file_name)
			config_list.add_item(display_name)
		file_name = dir.get_next()

	dir.list_dir_end()

func _read_config_description(path: String, fallback: String) -> String:
	var file = FileAccess.open(path, FileAccess.READ)
	if file == null:
		return fallback

	# Simple line-by-line scan for description field in [meta]
	var in_meta = false
	while not file.eof_reached():
		var line = file.get_line().strip_edges()
		if line == "[meta]":
			in_meta = true
		elif line.begins_with("[") and line != "[meta]":
			in_meta = false
		elif in_meta and line.begins_with("description"):
			var parts = line.split("=", true, 1)
			if parts.size() == 2:
				return parts[1].strip_edges().trim_prefix('"').trim_suffix('"')

	return fallback

func _on_config_selected(index: int) -> void:
	if index >= 0 and index < _config_paths.size():
		var path = _config_paths[index]
		description_label.text = "Loading: %s" % path
		config_selected.emit(path)
