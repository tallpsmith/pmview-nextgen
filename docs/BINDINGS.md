# Bindings

Bindings connect live PCP metrics to visual properties in a Godot scene — no external config files, no code changes. Everything is configured directly in the scene tree as custom resources.

## How It Works

**PcpBindable** is a `Node` you attach as a child of any `Node3D`. It holds an array of **PcpBindingResource** entries. At runtime, **SceneBinder** discovers all `PcpBindable` nodes in the scene and wires them to **MetricPoller** for live updates.

```
Node3D (your visual object)
└── PcpBindable
    ├── PcpBindingResource  (metric → property mapping)
    └── PcpBindingResource  (another metric → another property)
```

## PcpBindingResource Fields

| Field | Purpose | Example |
|-------|---------|---------|
| `MetricName` | PCP metric to fetch | `kernel.all.load` |
| `TargetProperty` | Property to drive | `height` |
| `SourceRangeMin` | Expected minimum metric value | `0.0` |
| `SourceRangeMax` | Expected maximum metric value | `10.0` |
| `TargetRangeMin` | Minimum mapped property value | `0.2` |
| `TargetRangeMax` | Maximum mapped property value | `5.0` |
| `InstanceName` | Instance filter — empty means all | `1 minute` |
| `InstanceId` | Instance ID filter — `-1` means none | `-1` |
| `InitialValue` | Value to use before first poll | `0.0` |

Values are linearly interpolated from source range to target range. Clamping is applied at both ends.

## Built-in Property Vocabulary

Friendly property names that map to underlying Godot properties:

| Property | Godot Mapping | Node requirement |
|----------|--------------|-----------------|
| `height` | `Scale.Y` | Node3D |
| `width` | `Scale.X` | Node3D |
| `depth` | `Scale.Z` | Node3D |
| `scale` | Uniform scale (X/Y/Z) | Node3D |
| `rotation_speed` | Y-axis rotation per frame | Node3D |
| `position_y` | `Position.Y` | Node3D |
| `color_temperature` | HSV hue, blue→red | MeshInstance3D + StandardMaterial3D |
| `opacity` | Alpha channel | MeshInstance3D + StandardMaterial3D |

## Custom Properties

Any property name not in the built-in vocabulary is passed through directly to `@export` vars on the node's attached GDScript. This lets scene-specific scripts receive metric values without modifying the bridge plugin.

```gdscript
# In your scene script
@export var load_average: float = 0.0

func _process(_delta):
    # load_average is updated live by SceneBinder
    update_visuals(load_average)
```

## Example: CPU Load Bar

```
CpuLoadBar (MeshInstance3D)
└── PcpBindable
    └── PcpBindingResource
          MetricName:      kernel.all.load
          TargetProperty:  height
          SourceRangeMin:  0.0
          SourceRangeMax:  10.0
          TargetRangeMin:  0.1
          TargetRangeMax:  5.0
          InstanceName:    1 minute
```

The bar's Y scale will track the 1-minute load average in real time.
