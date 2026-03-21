# Shape Selection & Detail Panel Design

**Date:** 2026-03-21
**Status:** Approved

## Problem

Users have no way to inspect individual shapes (metrics) in the 3D scene. They can see bars going up and down and colours changing, but there's no way to drill into a specific shape and see what metric(s) drive it or what the current values are. This feature adds click-to-select with visual feedback and a live detail panel.

## Feature Summary

1. Click a shape to select it
2. Camera exits orbit, flies to a comfortable viewing distance at orbit elevation
3. Shape highlights with outline + emissive glow
4. 2D detail panel appears showing metric names and live values, grouped by binding property
5. Values update each poll tick
6. Deselect via ESC, click empty space, or click a different shape to switch

## Design Decisions

### Selection Trigger: Left-Click Raycasting

- StaticBody3D + CollisionShape3D added as **children** of each GroundedBar and GroundedCylinder scene root node. Being children means they inherit the parent's `scale.y` transform automatically, so the collision shape grows/shrinks with the bar as metric values change. No manual resizing needed.
- BoxShape3D for bars, CylinderShape3D for cylinders
- Dedicated physics layer (layer 2) for selectable shapes
- **Ghost shapes** (missing metric data, `ghost = true`) are still selectable — the detail panel shows the metric name(s) with "N/A" for values. This lets users identify what a placeholder shape *would* represent.
- HostViewController handles `_unhandled_input()` on left-click:
  1. Ray from camera through mouse position via `project_ray_origin()` + `project_ray_normal()`
  2. `PhysicsRayQueryParameters3D` masked to layer 2 → `intersect_ray()`
  3. Walk up from hit collider to find GroundedShape or StackGroupNode ancestor
  4. Hit shape → select. No hit → deselect.
  5. Call `get_viewport().set_input_as_handled()` after processing to prevent click propagation.

### Stacked Bar Selection: Whole Stack

- Click on any segment of a stacked bar → the entire StackGroupNode is selected
- All child segments get highlighted
- Detail panel shows all metrics across all segments, grouped by binding property

### Highlight Effect: Outline + Emissive Glow

**Emissive glow:**
- On select: `emission_enabled = true`, `emission = albedo_color`, `emission_energy_multiplier ≈ 1.5-2.0` on the shape's existing StandardMaterial3D
- WorldEnvironment bloom processes the emission automatically
- On deselect: revert `emission_enabled = false`

**Outline (inverted hull):**
- Second MeshInstance3D child, slightly scaled up (~1.05x), with a back-face-only shader rendering a solid bright colour
- Hidden by default, shown on select, hidden on deselect
- Custom `highlight.gdshader` uses the `FRONT_FACING` built-in (Godot 4) to discard front faces, rendering only back faces in a configurable colour

**API on GroundedShape:** `highlight(enabled: bool)` — toggles both effects, manages own state.

**For stacks:** StackGroupNode exposes `highlight(enabled: bool)` that delegates to all child shapes.

### Camera Behaviour

- On select: exit orbit mode → fly mode (automatic)
- Calculate target viewpoint:
  - Look-at: shape's global position (or stack centre)
  - Camera position: fixed horizontal distance (~8 units), at `_orbit_height` elevation, offset in the direction the camera is currently facing for a natural move
  - `_orbit_height` is a plain GDScript `var` (no access modifier enforcement) — HostViewController reads it directly from the camera reference it already holds. No API change needed on the camera.
- Use existing `fly_to_viewpoint(target_pos, look_at_pos)` for smooth 1.2s cinematic move
- User remains in free-fly after move — can WASD around while panel stays open
- On switching selection: new fly-to, seamless highlight swap

### Detail Panel: 2D Screen Overlay

**Position:** Top-right corner, fixed on CanvasLayer.

**Content:** Metric bindings grouped by target property:
```
┌──────────────────────────────┐
│ CPU • cpu0                    │
│──────────────────────────────│
│ height                        │
│   kernel.cpu.user      42.3   │
│   kernel.cpu.sys       18.1   │
│ colour                        │
│   kernel.cpu.utilisation 0.6  │
└──────────────────────────────┘
```

- Header: zone name + instance name (sourced from the binding data — `MetricBinding.ZoneName` and `MetricBinding.InstanceName` — not from scene tree node names)
- Property groups: one section per bound property (height, colour, etc.)
- Metric rows: metric name + current raw value (no unit formatting for now)
- Values refresh on each `MetricPoller.MetricsUpdated` signal

**Precondition:** The host_view scene must have a WorldEnvironment with glow/bloom enabled for the emissive highlight to be visible. Both RuntimeSceneBuilder and TscnWriter already create one.

**Panel integration:**
- Signals: `panel_opened` / `panel_closed`
- Non-exclusive and non-input-blocking — camera input stays enabled while panel is open
- ESC closes it (consistent with other panels)
- Opening Help (H) or RangeTuning (F1) implicitly closes detail panel and deselects

### SceneBinder API Addition

New public method to support the detail panel (and future consumers). Must return Godot-compatible types since GDScript consumers cannot use C# generics or private record types.

```csharp
public Godot.Collections.Dictionary GetBindingsForNode(Node node)
```

**Return shape** (Godot Dictionary of Godot Arrays of Godot Dictionaries):
```
{
  "zone": "CPU",
  "instance": "cpu0",
  "properties": {
    "height": [
      { "metric": "kernel.cpu.user", "value": 42.3 },
      { "metric": "kernel.cpu.sys", "value": 18.1 }
    ],
    "colour": [
      { "metric": "kernel.cpu.utilisation", "value": 0.6 }
    ]
  }
}
```

This follows the same pattern as the existing `GetSourceRanges()` method which already returns `Godot.Collections.Dictionary`.

**Raw value storage:** `ActiveBinding` (currently a private record) does not store the last raw metric value — it's consumed and discarded in `ApplyMetrics()`. The implementation must add a `LastRawValue` field to `ActiveBinding` (or a parallel lookup), updated during `ApplyMetrics()`, so that `GetBindingsForNode()` can return current values without duplicating the instance-resolution logic.

Zone and instance names are sourced from `MetricBinding.ZoneName` and `MetricBinding.InstanceName` on the resolved binding — no scene tree traversal needed.

### Deselection Rules

**ESC priority chain** (HostViewController processes in this order, first match wins):
1. Close Help panel (if open)
2. Close RangeTuning panel (if open)
3. **Deselect shape + close detail panel (if shape selected)** ← new
4. Return from viewpoint to orbit (if in a viewpoint fly-to)
5. Double-ESC to menu

| Trigger | Effect |
|---|---|
| ESC | Deselect, remove highlight, close panel. Camera stays put. |
| Click empty space | Same as ESC. |
| Click different shape | Swap: deselect old → select new (highlight, panel, fly-to). No intermediate state. |
| Open Help (H) or RangeTuning (F1) | Close panel, deselect. Those panels need input blocking. |
| Press Tab (return to orbit) | Close panel, deselect. Orbit = overview mode. |
| Viewpoint shortcut (1-4) | Close panel, deselect. Preset implies done inspecting. |
| WASD free-fly movement | No effect — panel stays open, highlight stays active. |

## File Changes

### New Files

| File | Responsibility |
|---|---|
| `addons/pmview-bridge/building_blocks/outline_mesh.gd` | Inverted-hull outline child mesh (show/hide/colour) |
| `addons/pmview-bridge/building_blocks/highlight.gdshader` | Back-face-only shader for outline effect |
| `addons/pmview-bridge/ui/detail_panel.gd` | 2D overlay: grouped bindings + live metric values |
| `addons/pmview-bridge/ui/detail_panel.tscn` | Scene layout for the detail panel |

### Modified Files

| File | Change |
|---|---|
| `grounded_shape.gd` | Add `highlight(enabled)` API — emission toggle + outline mesh |
| `grounded_bar.tscn` | Add StaticBody3D + BoxShape3D (physics layer 2) |
| `grounded_cylinder.tscn` | Add StaticBody3D + CylinderShape3D (physics layer 2) |
| `stack_group_node.gd` | Add `highlight(enabled)` delegating to children |
| `SceneBinder.cs` | Add `GetBindingsForNode(Node)` API + `LastRawValue` storage on ActiveBinding |
| `HostViewController.gd` | Click handling, selection state, detail panel lifecycle, deselection on mode changes |

### Unchanged

MetricPoller, MetricGrid, MetricGroupNode, GroundBezel, FlyOrbitCamera, TscnWriter, RuntimeSceneBuilder.

## Future Enhancements (Not in Scope)

- Tether line from 2D panel to 3D shape (option C from brainstorming — purely additive)
- Keyboard navigation to cycle through shapes ([/] keys)
- Unit formatting / percentage display for metric values
- Rendering scalability for large shape counts (tracked in GitHub issue #59)
