# Research: CPU Stacked Bar

**Date**: 2026-03-16

## Key Finding: Stacking Infrastructure Already Exists

The pmview-nextgen codebase already has complete stacking support. No new building blocks or shape types are needed.

### Existing Components

| Component | Location | Status |
|-----------|----------|--------|
| `MetricStackGroupDefinition` | `ZoneDefinition.cs:44-47` | Exists, unused by CPU zone |
| `StackGroupNode.gd` | `addons/pmview-bridge/building_blocks/` | Exists, proportional + normalised modes |
| `PlacedStack` | `SceneLayout.cs` | Exists, emitted by LayoutCalculator |
| `LayoutCalculator.BuildForegroundItems` | `LayoutCalculator.cs:88-132` | Already handles stack grouping |
| `TscnWriter.WriteStack` | `TscnWriter.cs:307-326` | Already emits StackGroupNode + children |
| `ZoneDefinition.StackGroups` | `ZoneDefinition.cs:55` | Optional property, null for CPU zone |

### Decision: Configuration Change, Not Architecture Change

- **Decision**: Add `StackGroups` to CPU and Per-CPU zone definitions in LinuxProfile, and update metric colours.
- **Rationale**: All stacking infrastructure exists. The spec assumed a new `ShapeType.StackedBar` would be needed, but the existing `ShapeType.Bar` shapes within a `MetricStackGroupDefinition` achieve the same result. The `StackGroupNode.gd` already handles vertical stacking in `_process()`.
- **Alternatives considered**:
  - New `ShapeType.StackedBar` with a custom building block scene — rejected because the existing StackGroupNode already composes individual bars into stacks.
  - Modifying `grounded_bar.tscn` to support segments — rejected because composition via StackGroupNode is cleaner and already works.

### Colour Values

Per spec requirements:
- **Sys** (bottom): Red → `RgbColour(1.0f, 0.0f, 0.0f)` — pure red
- **User** (middle): Green → `RgbColour(0.0f, 0.8f, 0.0f)` — slightly muted green for readability
- **Nice** (top): Cyan → `RgbColour(0.0f, 0.8f, 0.8f)` — slightly muted cyan

### Stack Mode

- **Proportional** mode is correct for CPU metrics — total bar height reflects actual total CPU usage (Sys + User + Nice), which is the intuitive behaviour.
- Normalised mode would make the bar a fixed height regardless of load — wrong for CPU utilisation.

### Segment Ordering

`MetricStackGroupDefinition.MetricLabels` is ordered bottom-to-top. For CPU:
```
MetricLabels: ["Sys", "User", "Nice"]
```

This matches `StackGroupNode._layout_proportional()` which iterates children in order, placing each on top of the previous.

### Impact on Per-CPU Background Zone

The `PerCpuZone` uses `ZoneType.PerInstance` with a `MetricGrid`. The grid creates one column per metric per instance. With stack groups, the LayoutCalculator's `BuildBackgroundShapes` will need to check for stack groups too — currently it only handles foreground stacking.

**Research needed**: Does `BuildBackgroundShapes` support `StackGroups`?

After reading `LayoutCalculator.cs:160-186`, the background layout does NOT currently check for stack groups. It creates individual `PlacedShape` items for each (instance, metric) pair. Supporting stacked bars in the per-CPU zone requires extending `BuildBackgroundShapes` to group metrics by stack group per instance.

### Decision: Per-CPU Stacking (P3)

- **Decision**: Extend `BuildBackgroundShapes` to support stack groups per instance.
- **Rationale**: The spec requires consistency between aggregate and per-CPU zones. The MetricGrid already handles child arrangement; we just need the layout calculator to emit `PlacedStack` items instead of individual shapes when a stack group is defined.
- **Complexity**: Moderate — the foreground stacking logic in `BuildForegroundItems` provides the pattern to follow.
