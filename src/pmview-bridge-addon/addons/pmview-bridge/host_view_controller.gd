extends Node3D

## Auto-wires MetricPoller and SceneBinder for generated host-view scenes.
## Discovers all PcpBindable nodes in the scene, collects their metric names,
## and starts polling.

func _ready() -> void:
	print("[host_view_controller] _ready called on: %s" % name)
	var poller := find_child("MetricPoller")
	var binder := find_child("SceneBinder")
	print("[host_view_controller] poller=%s, binder=%s" % [poller, binder])

	if not poller or not binder:
		push_error("[host_view_controller] MetricPoller or SceneBinder not found")
		return

	print("[host_view_controller] Calling BindFromSceneProperties...")
	var metric_names: PackedStringArray = binder.BindFromSceneProperties(self)
	print("[host_view_controller] Bound %d metrics: %s" % [metric_names.size(), metric_names])

	print("[host_view_controller] Setting MetricNames on poller...")
	poller.MetricNames = metric_names

	print("[host_view_controller] Connecting MetricsUpdated signal...")
	poller.connect("MetricsUpdated", binder.ApplyMetrics)

	print("[host_view_controller] Calling StartPolling...")
	poller.StartPolling()
	print("[host_view_controller] Wiring complete")
