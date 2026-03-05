extends Node3D

# pmview Classic Scene - Recreating the original PCP pmview visualization
# This scene demonstrates the classic layout with programmable metrics

# Material colors for different utilization levels
const COLOR_LOW = Color(0.2, 0.8, 0.2)      # Green - low utilization
const COLOR_MEDIUM = Color(0.2, 0.4, 1.0)   # Blue - medium utilization
const COLOR_HIGH = Color(1.0, 0.2, 0.2)     # Red - high utilization
const COLOR_DISK = Color(1.0, 0.9, 0.2)     # Yellow - disk
const COLOR_MEM = Color(0.2, 0.8, 0.8)      # Cyan - memory
const COLOR_PLATFORM = Color(0.15, 0.15, 0.15)  # Dark gray for platforms

# References to metric objects (stored with metadata about their base height)
var disk_cylinder: MeshInstance3D
var disk_base_height: float = 1.0
var cpu_bars: Array[MeshInstance3D] = []
var cpu_base_height: float = 1.0
var memory_block: MeshInstance3D
var memory_base_height: float = 0.8
var load_cubes: Array[MeshInstance3D] = []
var load_base_height: float = 1.0
var network_matrix: Array[Array] = []  # 2D array of cubes
var network_base_height: float = 0.5
var disk_controllers: Array[MeshInstance3D] = []
var disk_controller_base_height: float = 0.5

# Containers
var metrics_container: Node3D
var labels_container: Node3D

# Camera animation
var camera: Camera3D
var camera_orbit_time: float = 0.0
var camera_orbit_enabled: bool = true
var camera_orbit_radius: float = 8.0
var camera_orbit_height: float = 4.5
var camera_orbit_speed: float = 0.15

func _ready():
	print("pmview Classic scene initializing...")
	metrics_container = $MetricsContainer
	labels_container = $Labels
	camera = $Camera3D

	# Build the scene
	create_platforms()  # Create base platforms first
	create_disk()
	create_disk_controllers()
	create_cpu()
	create_memory()
	create_load()
	create_network_matrix()
	create_labels()

	print("pmview Classic scene ready!")
	print("Objects created:")
	print("  - Disk cylinder: 1")
	print("  - Disk controllers: %d" % disk_controllers.size())
	print("  - CPU segments: %d" % cpu_bars.size())
	print("  - Load cubes: %d" % load_cubes.size())
	print("  - Network matrix: %d x %d" % [network_matrix.size(), network_matrix[0].size() if network_matrix.size() > 0 else 0])
	print("Camera orbit: ENABLED (press SPACE to toggle)")
	print("Camera controls: R=reset, +/- =speed, Up/Down=height")

func _process(delta):
	# Animate with simulated metrics
	var time = Time.get_ticks_msec() / 1000.0

	# Simulate disk activity (oscillates)
	var disk_value = (sin(time * 0.8) + 1.0) / 2.0
	animate_disk(disk_value)

	# Simulate CPU per-core (different phases)
	for i in cpu_bars.size():
		var cpu_value = (sin(time * 0.5 + i * 0.5) + 1.0) / 2.0
		animate_cpu_segment(i, cpu_value)

	# Simulate memory usage (slow growth)
	var mem_value = (sin(time * 0.3) + 1.0) / 2.0
	animate_memory(mem_value)

	# Simulate load average (3 values)
	for i in load_cubes.size():
		var load_value = (sin(time * 0.4 + i * 1.0) + 1.0) / 2.0
		animate_load_cube(i, load_value)

	# Simulate network matrix (bytes, packets, errors for each interface)
	for row in network_matrix.size():
		for col in network_matrix[row].size():
			var net_value = (sin(time * 0.6 + row * 0.3 + col * 0.8) + 1.0) / 2.0
			animate_network_cell(row, col, net_value)

	# Animate disk controllers
	for i in disk_controllers.size():
		var controller_value = (sin(time * 0.7 + i * 0.2) + 1.0) / 2.0
		animate_disk_controller(i, controller_value)

	# Camera orbit animation
	if camera_orbit_enabled and camera:
		animate_camera_orbit(delta)

func _input(event):
	"""Handle keyboard input for camera controls"""
	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_SPACE:
				camera_orbit_enabled = not camera_orbit_enabled
				print("Camera orbit: %s" % ("ENABLED" if camera_orbit_enabled else "DISABLED"))
			KEY_R:
				reset_camera()
				print("Camera reset")
			KEY_EQUAL, KEY_PLUS:
				camera_orbit_speed += 0.05
				print("Camera speed: %.2f" % camera_orbit_speed)
			KEY_MINUS:
				camera_orbit_speed = max(0.05, camera_orbit_speed - 0.05)
				print("Camera speed: %.2f" % camera_orbit_speed)
			KEY_UP:
				camera_orbit_height += 0.5
				print("Camera height: %.2f" % camera_orbit_height)
			KEY_DOWN:
				camera_orbit_height = max(2.0, camera_orbit_height - 0.5)
				print("Camera height: %.2f" % camera_orbit_height)

func animate_camera_orbit(delta: float):
	"""Smoothly orbit camera around the scene"""
	camera_orbit_time += delta * camera_orbit_speed

	# Calculate orbit position
	var angle = camera_orbit_time
	var x = cos(angle) * camera_orbit_radius
	var z = sin(angle) * camera_orbit_radius
	var y = camera_orbit_height

	# Set camera position
	camera.position = Vector3(x, y, z)

	# Look at center of scene (slightly above ground)
	camera.look_at(Vector3(0, 1, 0), Vector3.UP)

func reset_camera():
	"""Reset camera to default position"""
	camera_orbit_time = 0.0
	camera_orbit_radius = 8.0
	camera_orbit_height = 4.5
	camera_orbit_speed = 0.15

func create_platforms():
	"""Create platform bases under grouped objects"""
	# Platform under disk controllers
	var disk_controller_platform = create_platform(Vector3(2.5, 0.05, 2.5))
	disk_controller_platform.position = Vector3(-1.8, 0.025, -0.8)
	metrics_container.add_child(disk_controller_platform)

	# Platform under network matrix
	var network_platform = create_platform(Vector3(1.5, 0.05, 3.0))
	network_platform.position = Vector3(0.5, 0.025, -0.8)
	metrics_container.add_child(network_platform)

	# Platform under CPU bars
	var cpu_platform = create_platform(Vector3(1.5, 0.05, 0.6))
	cpu_platform.position = Vector3(3.45, 0.025, 2)
	metrics_container.add_child(cpu_platform)

	# Platform under Load cubes
	var load_platform = create_platform(Vector3(2.0, 0.05, 0.6))
	load_platform.position = Vector3(-1.4, 0.025, 4)
	metrics_container.add_child(load_platform)

	# Platform under Disk cylinder
	var disk_platform = create_platform(Vector3(1.0, 0.05, 1.0))
	disk_platform.position = Vector3(-4, 0.025, 2)
	metrics_container.add_child(disk_platform)

	# Platform under Memory block
	var mem_platform = create_platform(Vector3(1.8, 0.05, 1.2))
	mem_platform.position = Vector3(2, 0.025, 4)
	metrics_container.add_child(mem_platform)

func create_disk():
	"""Create main disk cylinder on the left"""
	var cylinder = create_cylinder_from_ground(0.4, disk_base_height, COLOR_DISK)
	cylinder.position = Vector3(-4, 0, 2)
	metrics_container.add_child(cylinder)
	disk_cylinder = cylinder

func create_disk_controllers():
	"""Create grid of disk controller cylinders"""
	var grid_size = 4
	var spacing = 0.6
	var base_pos = Vector3(-3, 0, -2)

	for x in grid_size:
		for z in grid_size:
			var cyl = create_cylinder_from_ground(0.2, disk_controller_base_height, COLOR_LOW)
			cyl.position = base_pos + Vector3(x * spacing, 0, z * spacing)
			metrics_container.add_child(cyl)
			disk_controllers.append(cyl)

func create_cpu():
	"""Create CPU as segmented vertical bars (simulating multi-core)"""
	var num_cores = 4
	var spacing = 0.3
	var base_pos = Vector3(3, 0, 2)

	for i in num_cores:
		var bar = create_box_from_ground(Vector3(0.25, cpu_base_height, 0.25), COLOR_LOW)
		bar.position = base_pos + Vector3(i * spacing, 0, 0)
		metrics_container.add_child(bar)
		cpu_bars.append(bar)

func create_memory():
	"""Create memory block"""
	var mem = create_box_from_ground(Vector3(1.5, memory_base_height, 0.8), COLOR_MEM)
	mem.position = Vector3(2, 0, 4)
	metrics_container.add_child(mem)
	memory_block = mem

func create_load():
	"""Create load average cubes (1min, 5min, 15min)"""
	var base_pos = Vector3(-2, 0, 4)
	var spacing = 0.6

	for i in 3:
		var cube = create_box_from_ground(Vector3(0.4, load_base_height, 0.4), COLOR_MEDIUM)
		cube.position = base_pos + Vector3(i * spacing, 0, 0)
		metrics_container.add_child(cube)
		load_cubes.append(cube)

func create_network_matrix():
	"""Create network interface matrix (interfaces x metrics)"""
	var interfaces = ["eth0", "eth1", "et11", "et12", "et13", "xpi1", "xpi0"]
	var metrics_cols = 3  # Bytes, Packets, Errors
	var base_pos = Vector3(0, 0, -2)
	var spacing = 0.4

	for row in interfaces.size():
		var row_array: Array[MeshInstance3D] = []
		for col in metrics_cols:
			var cube = create_box_from_ground(Vector3(0.3, network_base_height, 0.3), COLOR_LOW)
			cube.position = base_pos + Vector3(col * spacing, 0, row * spacing)
			metrics_container.add_child(cube)
			row_array.append(cube)
		network_matrix.append(row_array)

func create_labels():
	"""Create 3D text labels on the ground surface"""
	# Main group labels - positioned on ground near objects
	create_surface_label("Disk", Vector3(-4, 0.02, 3.2), 0.5)
	create_surface_label("Disk Controllers", Vector3(-1.8, 0.02, -3.5), 0.4)
	create_surface_label("CPU", Vector3(3.5, 0.02, 3.0), 0.5)
	create_surface_label("Mem", Vector3(2.5, 0.02, 5.2), 0.5)
	create_surface_label("Load", Vector3(-1.5, 0.02, 5.0), 0.4)
	create_surface_label("Network Output", Vector3(0.5, 0.02, -3.8), 0.4)

	# Network column headers
	create_surface_label("Bytes", Vector3(-0.05, 0.02, -3.2), 0.25)
	create_surface_label("Packets", Vector3(0.35, 0.02, -3.2), 0.25)
	create_surface_label("Errors", Vector3(0.75, 0.02, -3.2), 0.25)

func create_surface_label(text: String, position: Vector3, size: float):
	"""Create a label that sits flat on the surface"""
	var label = Label3D.new()
	label.text = text
	label.font_size = int(size * 48)
	label.outline_size = 3

	# Make it lie flat on the ground (rotate 90 degrees around X axis)
	label.rotation_degrees = Vector3(-90, 0, 0)

	# No billboard - it stays on the ground
	label.billboard = BaseMaterial3D.BILLBOARD_DISABLED

	label.position = position
	label.modulate = Color(1, 1, 1, 0.95)
	labels_container.add_child(label)

# Animation functions - now they grow from ground (y=0)
func animate_disk(value: float):
	if disk_cylinder:
		# Scale from 0.3x to 2.0x base height
		var target_scale = 0.3 + value * 1.7
		disk_cylinder.scale.y = lerp(disk_cylinder.scale.y, target_scale, 0.1)
		# Adjust position so bottom stays at y=0
		disk_cylinder.position.y = (disk_base_height * disk_cylinder.scale.y) / 2.0
		update_color_by_value(disk_cylinder, value, COLOR_DISK)

func animate_disk_controller(index: int, value: float):
	if index < disk_controllers.size():
		var controller = disk_controllers[index]
		var target_scale = 0.3 + value * 1.7
		controller.scale.y = lerp(controller.scale.y, target_scale, 0.1)
		controller.position.y = (disk_controller_base_height * controller.scale.y) / 2.0
		update_color_by_value(controller, value, COLOR_LOW)

func animate_cpu_segment(index: int, value: float):
	if index < cpu_bars.size():
		var bar = cpu_bars[index]
		var target_scale = 0.3 + value * 2.0
		bar.scale.y = lerp(bar.scale.y, target_scale, 0.1)
		# Adjust position so bottom stays at y=0
		bar.position.y = (cpu_base_height * bar.scale.y) / 2.0
		update_color_by_value(bar, value, COLOR_LOW)

func animate_memory(value: float):
	if memory_block:
		var target_scale = 0.5 + value * 1.5
		memory_block.scale.y = lerp(memory_block.scale.y, target_scale, 0.1)
		memory_block.position.y = (memory_base_height * memory_block.scale.y) / 2.0
		update_color_by_value(memory_block, value, COLOR_MEM)

func animate_load_cube(index: int, value: float):
	if index < load_cubes.size():
		var cube = load_cubes[index]
		var target_scale = 0.3 + value * 1.7
		cube.scale.y = lerp(cube.scale.y, target_scale, 0.1)
		cube.position.y = (load_base_height * cube.scale.y) / 2.0
		update_color_by_value(cube, value, COLOR_MEDIUM)

func animate_network_cell(row: int, col: int, value: float):
	if row < network_matrix.size() and col < network_matrix[row].size():
		var cube = network_matrix[row][col]
		var target_scale = 0.3 + value * 1.7
		cube.scale.y = lerp(cube.scale.y, target_scale, 0.1)
		cube.position.y = (network_base_height * cube.scale.y) / 2.0
		update_color_by_value(cube, value, COLOR_LOW)

func update_color_by_value(mesh: MeshInstance3D, value: float, base_color: Color):
	"""Update color based on utilization value (0.0-1.0)"""
	var material = mesh.get_surface_override_material(0) as StandardMaterial3D
	if material:
		var color: Color
		if value < 0.33:
			color = COLOR_LOW  # Green
		elif value < 0.66:
			color = COLOR_MEDIUM  # Blue
		else:
			color = COLOR_HIGH  # Red

		# Blend with base color for variety
		color = color.lerp(base_color, 0.3)
		material.albedo_color = color

# Helper functions to create primitives that grow from ground
func create_cylinder_from_ground(radius: float, height: float, color: Color) -> MeshInstance3D:
	var mesh_instance = MeshInstance3D.new()
	var cylinder_mesh = CylinderMesh.new()
	cylinder_mesh.top_radius = radius
	cylinder_mesh.bottom_radius = radius
	cylinder_mesh.height = height
	mesh_instance.mesh = cylinder_mesh

	var material = StandardMaterial3D.new()
	material.albedo_color = color
	material.metallic = 0.2
	material.roughness = 0.7
	mesh_instance.set_surface_override_material(0, material)

	# Position so bottom is at y=0
	mesh_instance.position.y = height / 2.0

	return mesh_instance

func create_box_from_ground(size: Vector3, color: Color) -> MeshInstance3D:
	var mesh_instance = MeshInstance3D.new()
	var box_mesh = BoxMesh.new()
	box_mesh.size = size
	mesh_instance.mesh = box_mesh

	var material = StandardMaterial3D.new()
	material.albedo_color = color
	material.metallic = 0.2
	material.roughness = 0.7
	mesh_instance.set_surface_override_material(0, material)

	# Position so bottom is at y=0
	mesh_instance.position.y = size.y / 2.0

	return mesh_instance

func create_platform(size: Vector3) -> MeshInstance3D:
	"""Create a platform base for grouped objects"""
	var mesh_instance = MeshInstance3D.new()
	var box_mesh = BoxMesh.new()
	box_mesh.size = size
	mesh_instance.mesh = box_mesh

	var material = StandardMaterial3D.new()
	material.albedo_color = COLOR_PLATFORM
	material.metallic = 0.4
	material.roughness = 0.3
	mesh_instance.set_surface_override_material(0, material)

	return mesh_instance

# Public API for setting real metric values
func set_disk_usage(value: float):
	"""Set disk utilization (0.0 to 1.0)"""
	animate_disk(clamp(value, 0.0, 1.0))

func set_cpu_usage(core_index: int, value: float):
	"""Set CPU core utilization (0.0 to 1.0)"""
	animate_cpu_segment(core_index, clamp(value, 0.0, 1.0))

func set_memory_usage(value: float):
	"""Set memory utilization (0.0 to 1.0)"""
	animate_memory(clamp(value, 0.0, 1.0))

func set_load_average(index: int, value: float):
	"""Set load average value (normalized 0.0 to 1.0)"""
	animate_load_cube(index, clamp(value, 0.0, 1.0))

func set_network_metric(interface_index: int, metric_type: int, value: float):
	"""Set network metric (interface 0-6, type 0-2: bytes/packets/errors, value 0.0-1.0)"""
	animate_network_cell(interface_index, metric_type, clamp(value, 0.0, 1.0))
