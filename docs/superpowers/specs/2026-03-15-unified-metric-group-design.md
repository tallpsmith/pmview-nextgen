# Unified MetricGroupNode Design

**Date:** 2026-03-15
**Status:** Approved
**Branch:** scene-layout-polish

## Problem

Zone labels ("CPU", "Load", "Memory") collide with metric labels ("User", "Sys", "Nice") because they sit at nearly the same Z offset (1.5 vs 1.3). Beyond the immediate collision, the foreground and background zone implementations are structurally duplicated — both need a title, metric column headers, a ground bezel, and a grid of shapes. The only difference is that background zones have instance domain rows.

## Design

Unify foreground and background zones into a single reusable component hierarchy. Move intra-zone layout responsibility from the C# projector into GDScript components, so users can tune spacing and appearance in the Godot inspector without re-running the projector.

### Responsibility Split

| Concern | Projector (C#) | Components (GDScript) |
|---------|---------------|----------------------|
| Discover metrics + instances | Yes | No |
| Profile → zone/stack definitions | Yes | No |
| Emit component tree + PCP bindings | Yes | No |
| Set default colours, titles, stack modes | Yes | No |
| Inter-zone positioning (CenterRowOnXZero) | Yes | No |
| Intra-zone spatial layout | No | Yes |
| Label creation + positioning | No | Yes |
| Bezel auto-sizing | No | Yes |
| Include/exclude filtering | No | Yes |

### Component Hierarchy

```
HostView
├── MetricPoller
├── SceneBinder
├── CPU (MetricGroupNode: title="CPU")
│   ├── CPUTitle (Label3D — created by MetricGroupNode in _ready)
│   ├── CPUBezel (GroundBezel)
│   └── CPUGrid (MetricGrid)
│       ├── CPU_Cpu (StackGroupNode)
│       │   ├── CPU_User (grounded_bar)
│       │   └── CPU_Sys (grounded_bar)
│       └── CPU_Nice (grounded_bar)
├── PerCPU (MetricGroupNode: title="Per-CPU")
│   ├── PerCPUTitle (Label3D — created by MetricGroupNode in _ready)
│   ├── PerCPUBezel (GroundBezel)
│   └── PerCPUGrid (MetricGrid)
│       ├── cpu0_User, cpu0_Sys, cpu0_Nice (row 0)
│       ├── cpu1_User, cpu1_Sys, cpu1_Nice (row 1)
│       ├── cpu2_User, cpu2_Sys, cpu2_Nice (row 2)
│       └── cpu3_User, cpu3_Sys, cpu3_Nice (row 3)
└── ...
```

### Components

#### MetricGroupNode (`metric_group_node.gd`)

Orchestrator. Owns three children: Title (Label3D, created procedurally in `_ready`), MetricGrid, and GroundBezel.

Each frame, reads MetricGrid's extent, positions the title at the front edge (offset by `title_gap`), and tells GroundBezel to size itself. Passes `label_gap` to MetricGrid so it can position metric/instance labels at the correct offset from the bezel edge.

**@export properties:**
- `title_text: String` — group name, user-editable
- `title_gap: float = 1.5` — distance from bezel front edge to title
- `label_gap: float = 1.0` — passed to MetricGrid for metric/instance label offset from bezel edge

#### MetricGrid (`metric_grid.gd`)

Layout engine. Dynamically arranges child shapes into columns × rows each frame. Owns metric column header labels (back edge, perpendicular/sticking out) and instance row header labels (right edge, perpendicular/sticking out).

Column count and row count are computed from internal state, not set externally.

**@export properties:**
- `column_spacing: float = 1.2` — horizontal gap between columns
- `row_spacing: float = 2.5` — depth gap between rows
- `metric_include_filter: String` — glob; if non-empty, only show matching metrics
- `metric_exclude_filter: String` — glob; hide matching metrics (takes precedence)
- `instance_include_filter: String` — glob; if non-empty, only show matching instances
- `instance_exclude_filter: String` — glob; hide matching instances (takes precedence)

**Read-only computed properties:**
- `get_column_count() -> int`
- `get_row_count() -> int`
- `get_extent() -> Vector2` — footprint width and depth

**Filtering:** filtered children are hidden (`visible = false`), not removed. Re-shown if filter changes. Glob matching only; regex deferred to future need.

**Dynamic layout:** children can be added/removed and the grid re-flows automatically.

#### GroundBezel (`ground_bezel.gd`)

MeshInstance3D with a BoxMesh that auto-sizes based on grid extent plus its own padding.

**@export properties:**
- `bezel_colour: Color` — slab colour, default dark grey
- `padding: float = 0.6` — padding around the grid extent

**API:** `resize(width: float, depth: float)` called by MetricGroupNode.

#### StackGroupNode (unchanged)

Existing component. Lives as a child within a MetricGrid cell. Stacks bars vertically as it does today.

**MetricGrid child filtering:** MetricGrid arranges children that are `Node3D` instances (grounded_bar, grounded_cylinder, StackGroupNode). It skips `Label3D` and `MeshInstance3D` children (its own generated labels). This matches the existing `GridLayout3D` filtering pattern.

### Label Positioning

All three label types sit **outside** the bezel:

- **Title** (front edge, +Z): runs parallel to the bezel edge, centred on bezel width. Like a nameplate.
- **Metric names** (back edge, -Z): perpendicular to the bezel edge, sticking out. Centred on their respective column. Like Excel column headers.
- **Instance domains** (right edge, +X): perpendicular to the bezel edge, sticking out. Centred on their respective row. Like Excel row headers. Ordered ascending into the background (instance 0 nearest camera).

### Shape Colours

Projector sets a default colour per shape. The colour is exposed as an `@export` on the shape component so users can override it in the Godot inspector.

### Inter-Zone Positioning

`CenterRowOnXZero` needs zone widths to space zones apart. The GDScript bezel auto-sizes for visual rendering, but the C# projector still needs a **nominal extent** to prevent zones overlapping. The projector computes a lightweight estimate from metric count × column spacing (foreground) or instance count × row spacing (background). This doesn't need to match the bezel exactly — it just needs to be close enough for inter-zone gaps. The `ComputeGroundExtent` method simplifies to this nominal calculation rather than being deleted entirely.

### What Retires

- `grid_layout_3d.gd` — **deleted**, replaced by the new `metric_grid.gd` (not a rename — substantially different responsibility)
- `TscnWriter.WriteZoneLabelNode()` — MetricGroupNode owns title
- `TscnWriter.WriteShapeLabel()` — MetricGrid owns metric labels
- `TscnWriter.WriteGridColumnHeaders()` — MetricGrid owns these
- `TscnWriter.WriteGridRowHeaders()` — MetricGrid owns these
- `TscnWriter.WriteGroundBezel()` — GroundBezel component self-sizes
- `TscnWriter.CollectBezelSubResources()` — bezel mesh/material no longer generated in .tscn
- Most of `LayoutCalculator` shape positioning logic (individual shape X/Z offsets)

### What Survives

- `StackGroupNode` — unchanged, lives in grid cells
- `LayoutCalculator.CenterRowOnXZero()` — inter-zone positioning stays in C#
- `LayoutCalculator.ComputeGroundExtent()` — simplified to nominal extent for inter-zone spacing only
- `TscnWriter.CollectSubResources()` — PCP bindings still generated
- `TscnWriter.WriteShape()` — simplified (no position computation)
- `TscnWriter.WriteStack()` — simplified (no position computation)
- Profile/zone definitions — unchanged data model
- Metric discovery — unchanged

### Testing Strategy

- **C# (xUnit, VM-runnable):** discovery, binding, inter-zone layout, .tscn structure (correct component hierarchy, correct properties set). TscnWriter tests shift from verifying positions to verifying component tree structure and property values.
- **GDScript (Godot, manual):** visual layout correctness, label orientation, bezel sizing, filter behaviour, spacing tunability.

**Test disposition:** existing LayoutCalculator tests that verify intra-zone shape positions (e.g. shape X offsets, `ShapeSpacing * i`) retire as that logic moves to GDScript. Tests verifying inter-zone spacing (`CenterRowOnXZero`, zone gap assertions) survive with updated nominal extent values. Tests verifying ground extent survive but simplify to check the nominal calculation. Specific test-by-test disposition will be determined during implementation planning.

### Canonical Spacing Defaults

The current codebase has mismatched defaults between `grid_layout_3d.gd` (column_spacing=1.5, row_spacing=2.0) and `LayoutCalculator.cs` (GridColumnSpacing=2.0, GridRowSpacing=2.5). The projector values win — they reflect the intended visual spacing. MetricGrid defaults:
- `column_spacing: float = 2.0` (background grids) / `1.2` (foreground — set by projector per zone)
- `row_spacing: float = 2.5`
