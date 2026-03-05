extends MeshInstance3D

# Test cube animation script
# This demonstrates basic programmatic animation capabilities

var time_passed: float = 0.0
var base_scale: Vector3 = Vector3(1.0, 1.0, 1.0)
var base_position: Vector3 = Vector3(0.0, 0.0, 0.0)

# Simulated "metric" value that changes over time
var simulated_metric: float = 0.5

func _ready():
	print("Test cube initialized!")
	print("This cube will:")
	print("  - Rotate continuously")
	print("  - Scale height based on simulated metric")
	print("  - Change color based on metric value")

func _process(delta):
	time_passed += delta

	# Simulate a metric value that oscillates (like CPU usage might)
	# This will go from 0.0 to 1.0 and back in a sine wave
	simulated_metric = (sin(time_passed * 0.5) + 1.0) / 2.0

	# ROTATION: Continuous slow rotation
	rotate_y(delta * 0.5)

	# HEIGHT SCALING: Scale Y axis based on metric (like CPU usage)
	# When metric is 0.0, height is 0.5
	# When metric is 1.0, height is 3.0
	var target_height = 0.5 + (simulated_metric * 2.5)
	scale.y = lerp(scale.y, target_height, delta * 2.0)  # Smooth interpolation

	# COLOR CHANGE: Blue (low) -> Red (high)
	var material = get_surface_override_material(0) as StandardMaterial3D
	if material:
		# Low metric = blue, high metric = red
		var color = Color(
			simulated_metric,           # Red increases with metric
			0.2,                        # Green stays low
			1.0 - simulated_metric      # Blue decreases with metric
		)
		material.albedo_color = color

	# Print current state occasionally
	if int(time_passed * 10) % 30 == 0:
		print("Metric: %.2f | Height: %.2f | Color: %s" % [simulated_metric, scale.y, material.albedo_color if material else "N/A"])

# This function could be called externally to set a real metric value
func set_metric_value(value: float):
	"""Set the metric value (0.0 to 1.0) to drive animations"""
	simulated_metric = clamp(value, 0.0, 1.0)
	print("Metric updated to: %.2f" % simulated_metric)
