extends Camera3D

## Helicopter-style orbit: fixed altitude, constant rotation around orbit_center.
## Attach to a Camera3D node. Set orbit_center to the scene's focal point.

@export var orbit_speed: float = 20.0       ## degrees per second
@export var orbit_center: Vector3 = Vector3.ZERO

var _radius: float
var _height: float
var _angle: float

func _ready() -> void:
	_height = position.y
	_radius = Vector2(position.x - orbit_center.x, position.z - orbit_center.z).length()
	_angle = atan2(position.z - orbit_center.z, position.x - orbit_center.x)

func _process(delta: float) -> void:
	_angle += deg_to_rad(orbit_speed) * delta
	position = Vector3(
		orbit_center.x + _radius * cos(_angle),
		_height,
		orbit_center.z + _radius * sin(_angle)
	)
	look_at(orbit_center, Vector3.UP)
