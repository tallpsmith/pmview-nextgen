# Data Model: CPU Stacked Bar

**Date**: 2026-03-16

## Entities

### MetricStackGroupDefinition (existing)

Groups multiple metrics within a zone into a vertically stacked composite.

| Field | Type | Description |
|-------|------|-------------|
| GroupName | string | Display label for the stack group (e.g., "CPU") |
| Mode | StackMode | Proportional (total = sum) or Normalised (total = fixed) |
| MetricLabels | string[] | Ordered bottom-to-top; matches MetricShapeMapping.Label |

### ZoneDefinition.StackGroups (existing, currently null for CPU)

Optional list of `MetricStackGroupDefinition` on each zone. When present, the layout calculator groups matching metrics into `PlacedStack` items instead of individual `PlacedShape` items.

### Colour Changes

| Metric | Current Colour | New Colour | RGB |
|--------|---------------|------------|-----|
| kernel.all.cpu.sys | Orange (0.976, 0.451, 0.086) | Red | (1.0, 0.0, 0.0) |
| kernel.all.cpu.user | Orange (0.976, 0.451, 0.086) | Green | (0.0, 0.8, 0.0) |
| kernel.all.cpu.nice | Orange (0.976, 0.451, 0.086) | Cyan | (0.0, 0.8, 0.8) |

Same colours apply to per-CPU variants (`kernel.percpu.cpu.*`).

## State Transitions

None — this is a visual rendering change, not a stateful entity change.

## Relationships

```
ZoneDefinition (CPU)
  ├── Metrics: [Sys, User, Nice] (MetricShapeMapping, each ShapeType.Bar)
  └── StackGroups: [MetricStackGroupDefinition("CPU", Proportional, ["Sys", "User", "Nice"])]
         │
         ▼ (LayoutCalculator groups by StackGroup)
      PlacedStack("CPU", [PlacedShape(Sys), PlacedShape(User), PlacedShape(Nice)])
         │
         ▼ (TscnWriter emits)
      StackGroupNode (scene node)
        ├── GroundedBar (Sys, red, height bound to kernel.all.cpu.sys)
        ├── GroundedBar (User, green, height bound to kernel.all.cpu.user)
        └── GroundedBar (Nice, cyan, height bound to kernel.all.cpu.nice)
```

At runtime, `StackGroupNode._process()` repositions children vertically each frame based on their current `scale.y` values.
