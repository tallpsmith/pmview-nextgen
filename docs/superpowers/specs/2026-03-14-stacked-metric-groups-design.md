# Stacked Metric Groups Design

**Date:** 2026-03-14
**Issue:** #15
**Branch:** scene-layout-polish
**Status:** Approved

---

## Problem

Multi-component metrics (CPU user/sys/nice, memory used/cached/bufmem) are currently rendered as side-by-side independent bars. This wastes horizontal space and obscures the "total" nature of the components — e.g. that user+sys+nice together represent total CPU utilisation.

Stacked bars better represent these relationships, match the original pmview visual language, and give a more compact footprint.

---

## Goals

- Stack ordered metric bars vertically so each component sits on top of the one below it.
- Support two stacking modes: **Proportional** (total height reflects real utilisation) and **Normalised** (stack always fills full height, showing proportions only).
- Migrate the Linux CPU bars (user/sys/nice) to the stacked model as the first concrete use.
- Keep `SceneBinder` unchanged — stacking is a layout concern, not a binding concern.

---

## Non-Goals

- Per-CPU (per-instance) stacking is out of scope for this iteration.
- Animation smoothing within `StackGroupNode` — it reads what `SceneBinder` smooths.

---

## Design

### 1. Data Model

A common abstract parent `PlacedItem` is introduced. Both standalone bars and stacked groups derive from it. `PlacedZone` holds a single `Items` collection and is agnostic to the distinction.

```csharp
// Common parent — position is the only shared concern
public abstract record PlacedItem(Vector3 Position);

// Unchanged except derives from PlacedItem.
// Existing fields include: MetricName, Colour, InstanceName, SourceRange, TargetRange, Width, Depth.
// Width is already a field — StackGroupNode uses it to set the combined footprint on child bars.
public record PlacedShape(
    Vector3 Position,
    string MetricName,
    // ... existing fields unchanged ...
) : PlacedItem(Position);

// New
public enum StackMode { Proportional, Normalised }

public record PlacedStack(
    Vector3 Position,
    IReadOnlyList<PlacedShape> Members,  // ordered bottom → top
    StackMode Mode
) : PlacedItem(Position);

// PlacedZone simplified
public record PlacedZone(
    IReadOnlyList<PlacedItem> Items,     // shapes + stacks in one list
    GroundedBezel Bezel,
    string Label
);
```

`TscnWriter` and `LayoutCalculator` pattern-match on the concrete type via exhaustive `switch` expressions. The compiler enforces coverage when new `PlacedItem` subtypes are added.

### 2. Layout Calculator

A `PlacedStack` occupies a single column in the zone layout, but its width is the **combined footprint** of its members plus inter-bar spacing — so the visual space previously occupied by three side-by-side bars is preserved as one wider bar.

```
combined_width = (n × bar_width) + ((n - 1) × bar_spacing)
```

This ensures the zone's total ground footprint is unchanged when migrating from side-by-side to stacked.

### 3. Scene Generation (TscnWriter)

`TscnWriter.WriteStack` emits a `StackGroupNode` parent node, then calls the existing `WriteShape` for each member:

```
CpuStack (StackGroupNode)
│   stack_mode = Proportional
│   target_height = <zone max height>
├─ CpuUser (grounded_bar.tscn)   ← Members[0], position.y = 0
│   └─ PcpBindable → height binding: kernel.all.cpu.user
├─ CpuSys  (grounded_bar.tscn)   ← Members[1], position.y = 0
│   └─ PcpBindable → height binding: kernel.all.cpu.sys
└─ CpuNice (grounded_bar.tscn)   ← Members[2], position.y = 0
    └─ PcpBindable → height binding: kernel.all.cpu.nice
```

All child bars are generated at `position.y = 0`. `StackGroupNode` owns their Y positions at runtime — `TscnWriter` does not compute heights.

`SceneBinder` discovers `PcpBindable` nodes recursively and binds `height` on each child bar exactly as it does for standalone bars. No changes required to `SceneBinder`.

### 4. StackGroupNode (GDScript)

A new `Node3D` subclass in the addon. Runs `_process` every frame, reads `scale.y` from each child (the live, smoothed value), and sets `position.y` to the correct stacked offset.

```gdscript
@tool
extends Node3D

enum StackMode { PROPORTIONAL, NORMALISED }

@export var stack_mode: StackMode = StackMode.PROPORTIONAL
@export var target_height: float = 4.0

func _process(_delta: float) -> void:
    var children := get_children()
    if children.is_empty():
        return
    match stack_mode:
        StackMode.PROPORTIONAL:
            _layout_proportional(children)
        StackMode.NORMALISED:
            _layout_normalised(children)

## Proportional: total stack height reflects real utilisation.
func _layout_proportional(children: Array) -> void:
    var offset := 0.0
    for child in children:
        child.position.y = offset
        offset += child.scale.y

## Normalised: stack always fills target_height regardless of total utilisation.
func _layout_normalised(children: Array) -> void:
    var total_h := children.reduce(func(acc, c): return acc + c.scale.y, 0.0)
    if total_h < 0.001:
        return  # avoid div/0 when system is idle
    var scale_factor := target_height / total_h
    var offset := 0.0
    for child in children:
        var h := child.scale.y * scale_factor
        child.scale.y = h
        child.position.y = offset
        offset += h
```

**Key properties:**

- `@tool` — layout runs in the Godot editor for design-time preview.
- Reads `scale.y` (the smoothed, live value) so stacking follows the animation naturally.
- One-frame lag due to Godot's parent-before-children `_process` order — invisible over a smoothed multi-frame transition.
- Normalised mode overrides `child.scale.y` each frame by design — it intentionally wins over `SceneBinder`'s smoothed value to maintain the full-height constraint. This is not a bug.
- No metric knowledge, no coupling to `SceneBinder` or `MetricPoller`.

### 5. LinuxProfile Migration

The System CPU zone is the first migration target. Three independent `PlacedShape` bars are replaced by one `PlacedStack`:

```csharp
// Before
new PlacedShape(position: (0.0, 0, 0), metric: "kernel.all.cpu.user", colour: Blue,  ...),
new PlacedShape(position: (1.2, 0, 0), metric: "kernel.all.cpu.sys",  colour: Red,   ...),
new PlacedShape(position: (2.4, 0, 0), metric: "kernel.all.cpu.nice", colour: Green, ...),

// After
new PlacedStack(
    Position: (0.0, 0, 0),
    Mode: StackMode.Proportional,
    Members: [
        // position.y = 0 for all members — StackGroupNode owns Y at runtime
        new PlacedShape(metric: "kernel.all.cpu.user", colour: Blue,  width: combinedWidth, ...),
        new PlacedShape(metric: "kernel.all.cpu.sys",  colour: Red,   width: combinedWidth, ...),
        new PlacedShape(metric: "kernel.all.cpu.nice", colour: Green, width: combinedWidth, ...),
    ]
)
```

---

## Testing

### PmviewHostProjector.Tests (xUnit, net10.0)

**LayoutCalculator:**
- `PlacedStack` footprint = sum of member widths + inter-bar spacing
- `PlacedStack` combined footprint matches the old 3-bar side-by-side footprint (regression guard)

**TscnWriter:**
- `WriteStack` emits a `StackGroupNode` with the correct `stack_mode` property
- `WriteStack` emits N child `grounded_bar` nodes each with a `PcpBindable`
- All child bars have `position.y = 0` at generation time
- All child bars have width = combined footprint width

**LinuxProfile:**
- System CPU zone contains a `PlacedStack`, not three `PlacedShape`s
- Stack members are in order: user, sys, nice

### PmviewBridge layout logic (xUnit)

The `StackGroupNode` layout math is extracted into a pure C# helper testable without Godot:

- Proportional: Y offsets accumulate correctly across N bars
- Proportional: first bar always has `position.y = 0`
- Normalised: all bars together fill `target_height`
- Normalised: proportions are preserved after rescaling
- Normalised: no divide-by-zero when all bars are at height 0

---

## Implementation Order

1. **Data model** — `PlacedItem`, `PlacedStack`, update `PlacedZone` (tests first)
2. **LayoutCalculator** — `PlacedStack` footprint handling (tests first)
3. **TscnWriter** — `WriteStack` method (tests first)
4. **StackGroupNode** — GDScript node + extracted C# layout helper (tests first)
5. **LinuxProfile** — migrate CPU bars to `PlacedStack` (tests first)
6. **Manual validation** — run projector against dev stack, open in Godot, verify live stacking
