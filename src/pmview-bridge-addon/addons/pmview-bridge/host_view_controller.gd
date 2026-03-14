extends Node3D

## Auto-wires MetricPoller and SceneBinder for generated host-view scenes.
## Discovers all PcpBindable nodes in the scene, collects their metric names,
## and starts polling.

func _ready() -> void:
	var poller := find_child("MetricPoller")
	var binder := find_child("SceneBinder")

	if not poller or not binder:
		push_error("[HostViewController] MetricPoller or SceneBinder not found")
		return

	var metric_names: PackedStringArray = binder.BindFromSceneProperties(self)
	poller.MetricNames = metric_names
	poller.connect("MetricsUpdated", binder.ApplyMetrics)
	poller.StartPolling()
