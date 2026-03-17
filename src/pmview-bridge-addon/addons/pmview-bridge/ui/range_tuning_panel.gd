extends PanelContainer

## Floating panel for tuning disk/network source range maximums.
## Calls SceneBinder.UpdateSourceRangeMax() on Apply.

# -- Log-scale range boundaries (bytes/sec) --
# Cannot be const — log() is not a compile-time constant in GDScript.
var LOG_MIN: float = log(100_000.0)          # 100 KB/s
var LOG_MAX: float = log(50_000_000_000.0)   # 50 GB/s

## Preset snap threshold in normalised slider space (0-1)
const SNAP_THRESHOLD: float = 0.02

# -- Preset definitions: {label: bytes_per_sec} --
const DISK_PRESETS: Dictionary = {
	"HDD": 150_000_000.0,
	"SATA SSD": 550_000_000.0,
	"NVMe Gen3": 3_500_000_000.0,
	"NVMe Gen4": 7_000_000_000.0,
	"NVMe Gen5": 14_000_000_000.0,
}

const NETWORK_PRESETS: Dictionary = {
	"1 Gbit": 125_000_000.0,
	"10 Gbit": 1_250_000_000.0,
	"25 Gbit": 3_125_000_000.0,
	"40 Gbit": 5_000_000_000.0,
	"100 Gbit": 12_500_000_000.0,
}

# -- Node references (unique-name-in-owner lookups) --
@onready var disk_total_slider: HSlider = %DiskTotalSlider
@onready var disk_total_readout: Label = %DiskTotalReadout
@onready var disk_total_row: Control = %DiskTotalRow

@onready var per_disk_slider: HSlider = %PerDiskSlider
@onready var per_disk_readout: Label = %PerDiskReadout
@onready var per_disk_row: Control = %PerDiskRow

@onready var network_slider: HSlider = %NetworkSlider
@onready var network_readout: Label = %NetworkReadout
@onready var network_row: Control = %NetworkRow

@onready var apply_button: Button = %ApplyButton

var _scene_binder: Node = null
var _initial_values: Dictionary = {}  # {zone_name: bytes_per_sec}


func _ready() -> void:
	apply_button.pressed.connect(_on_apply_pressed)
	disk_total_slider.value_changed.connect(
		_on_slider_changed.bind(disk_total_slider, disk_total_readout, DISK_PRESETS))
	per_disk_slider.value_changed.connect(
		_on_slider_changed.bind(per_disk_slider, per_disk_readout, DISK_PRESETS))
	network_slider.value_changed.connect(
		_on_slider_changed.bind(network_slider, network_readout, NETWORK_PRESETS))
	apply_button.disabled = true


## Wire up the panel to a SceneBinder node. Populates sliders once bindings are ready.
func initialise(scene_binder: Node) -> void:
	_scene_binder = scene_binder
	if scene_binder.IsBound:
		_populate_from_binder()
	else:
		scene_binder.connect("BindingsReady", _populate_from_binder)


func _populate_from_binder() -> void:
	var ranges: Dictionary = _scene_binder.GetSourceRanges()
	_initial_values = ranges.duplicate()

	_set_slider_if_present(disk_total_slider, disk_total_readout, disk_total_row,
		ranges, "Disk", DISK_PRESETS)
	_set_slider_if_present(per_disk_slider, per_disk_readout, per_disk_row,
		ranges, "Per-Disk", DISK_PRESETS)

	# Network uses whichever is present — prefer "Network In"
	var net_key := "Network In" if ranges.has("Network In") else "Network Out"
	_set_slider_if_present(network_slider, network_readout, network_row,
		ranges, net_key, NETWORK_PRESETS)


func _set_slider_if_present(slider: HSlider, readout: Label, row: Control,
		ranges: Dictionary, zone: String, _presets: Dictionary) -> void:
	if not ranges.has(zone):
		row.visible = false
		return
	row.visible = true
	var bytes_val: float = ranges[zone]
	slider.value = _bytes_to_slider(bytes_val)
	readout.text = _format_bytes(bytes_val)


# -- Log scale transforms --

func _bytes_to_slider(bytes_per_sec: float) -> float:
	if bytes_per_sec <= 0.0:
		return 0.0
	var log_val := log(bytes_per_sec)
	return clampf((log_val - LOG_MIN) / (LOG_MAX - LOG_MIN), 0.0, 1.0)


func _slider_to_bytes(slider_val: float) -> float:
	var log_val := LOG_MIN + slider_val * (LOG_MAX - LOG_MIN)
	return exp(log_val)


# -- Slider change handler with snap-to-preset --

var _snapping: bool = false  # Guard against re-entrant signal from set_value_no_signal


func _on_slider_changed(value: float, slider: HSlider, readout: Label,
		presets: Dictionary) -> void:
	if _snapping:
		return

	# Snap to nearest preset if close enough
	for preset_bytes: float in presets.values():
		var preset_pos := _bytes_to_slider(preset_bytes)
		if absf(value - preset_pos) < SNAP_THRESHOLD:
			_snapping = true
			slider.set_value_no_signal(preset_pos)
			_snapping = false
			value = preset_pos
			break

	var bytes_val := _slider_to_bytes(value)
	readout.text = _format_bytes(bytes_val)
	apply_button.disabled = false


# -- Apply --

func _on_apply_pressed() -> void:
	if not _scene_binder:
		return

	var disk_bytes := _slider_to_bytes(disk_total_slider.value)
	var per_disk_bytes := _slider_to_bytes(per_disk_slider.value)
	var net_bytes := _slider_to_bytes(network_slider.value)

	if disk_total_row.visible:
		_scene_binder.UpdateSourceRangeMax("Disk", disk_bytes)
	if per_disk_row.visible:
		_scene_binder.UpdateSourceRangeMax("Per-Disk", per_disk_bytes)
	if network_row.visible:
		_scene_binder.UpdateSourceRangeMax("Network In", net_bytes)
		_scene_binder.UpdateSourceRangeMax("Network Out", net_bytes)

	apply_button.disabled = true


# -- Human-readable formatting --

static func _format_bytes(bytes_per_sec: float) -> String:
	if bytes_per_sec >= 1_000_000_000.0:
		return "%.1f GB/s" % (bytes_per_sec / 1_000_000_000.0)
	elif bytes_per_sec >= 1_000_000.0:
		return "%.0f MB/s" % (bytes_per_sec / 1_000_000.0)
	elif bytes_per_sec >= 1_000.0:
		return "%.0f KB/s" % (bytes_per_sec / 1_000.0)
	else:
		return "%.0f B/s" % bytes_per_sec
