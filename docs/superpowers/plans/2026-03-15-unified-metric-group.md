# Unified MetricGroupNode Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify foreground and background zones into a single MetricGroupNode component hierarchy, moving intra-zone layout from the C# projector into GDScript components so users can tune spacing in the Godot inspector.

**Architecture:** Three new GDScript components — MetricGroupNode (orchestrator), MetricGrid (layout engine + labels + filtering), GroundBezel (auto-sizing slab). The projector simplifies to emit the component tree with data bindings and defaults; it no longer computes intra-zone positions, labels, or bezel geometry. Inter-zone positioning (CenterRowOnXZero) stays in C#.

**Tech Stack:** GDScript (Godot 4.4+), C# (.NET 8.0 for Godot libs, .NET 10 for tests), xUnit

---

## File Map

### New Files (GDScript)
- `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/ground_bezel.gd` — auto-sizing MeshInstance3D slab
- `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_grid.gd` — grid layout + column/row header labels + filtering
- `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_group_node.gd` — orchestrator: title, bezel sizing, label gap passthrough

### Modified Files (C#)
- `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs` — emit MetricGroupNode/MetricGrid/GroundBezel tree; remove label/bezel generation methods
- `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` — remove intra-zone shape positioning; simplify ComputeGroundExtent to nominal estimate
- `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs` — remove GridColumns/GridColumnSpacing/GridRowSpacing/MetricLabels/InstanceLabels from PlacedZone (components own these)

### Modified Files (Tests)
- `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs` — update for new component tree structure
- `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` — retire intra-zone position tests; update inter-zone tests for simplified model

### Deleted Files
- `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grid_layout_3d.gd` — replaced by metric_grid.gd

---

## Chunk 1: GDScript Components

### Task 1: Create GroundBezel Component

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/ground_bezel.gd`

GDScript components are manually tested in Godot — no xUnit tests.

- [ ] **Step 1: Write ground_bezel.gd**

```gdscript
@tool
class_name GroundBezel
extends MeshInstance3D

## Colour of the ground slab.
@export var bezel_colour: Color = Color(0.3, 0.3, 0.3, 1.0):
	set(value):
		bezel_colour = value
		_apply_colour()

## Padding around the content extent on each side.
@export var padding: float = 0.6:
	set(value):
		padding = value
		_rebuild_mesh()

var _width: float = 0.0
var _depth: float = 0.0

func _ready() -> void:
	_rebuild_mesh()
	_apply_colour()

## Called by MetricGroupNode when the grid extent changes.
func resize(width: float, depth: float) -> void:
	if _width == width and _depth == depth:
		return  # skip rebuild if dimensions unchanged
	_width = width
	_depth = depth
	_rebuild_mesh()

func _rebuild_mesh() -> void:
	if _width <= 0.0 or _depth <= 0.0:
		mesh = null
		return
	var padded_w := _width + padding * 2.0
	var padded_d := _depth + padding * 2.0
	var box := BoxMesh.new()
	box.size = Vector3(padded_w, 0.02, padded_d)
	mesh = box

func _apply_colour() -> void:
	var mat := get_surface_override_material(0)
	if mat == null:
		mat = StandardMaterial3D.new()
		set_surface_override_material(0, mat)
	if mat is StandardMaterial3D:
		mat.albedo_color = bezel_colour
```

- [ ] **Step 2: Verify file saved, commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/ground_bezel.gd
git commit -m "Add GroundBezel component — auto-sizing MeshInstance3D slab with colour and padding exports"
```

---

### Task 2: Create MetricGrid Component

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_grid.gd`

- [ ] **Step 1: Write metric_grid.gd**

```gdscript
@tool
class_name MetricGrid
extends Node3D

## Spacing between columns along +X axis (projector overrides to 2.0 for background grids).
@export var column_spacing: float = 1.2:
	set(value):
		column_spacing = value
		_arrange()

## Spacing between rows along -Z axis.
@export var row_spacing: float = 2.5:
	set(value):
		row_spacing = value
		_arrange()

## Gap between back/right bezel edge and metric/instance labels.
@export var label_gap: float = 1.0:
	set(value):
		label_gap = value
		_arrange()

## Glob filter: only show metrics matching this pattern (empty = show all).
@export var metric_include_filter: String = ""
## Glob filter: hide metrics matching this pattern (takes precedence over include).
@export var metric_exclude_filter: String = ""
## Glob filter: only show instances matching this pattern (empty = show all).
@export var instance_include_filter: String = ""
## Glob filter: hide instances matching this pattern (takes precedence over include).
@export var instance_exclude_filter: String = ""

## Metric labels for column headers (set by projector, read-only in practice).
@export var metric_labels: PackedStringArray = PackedStringArray()
## Instance labels for row headers (set by projector, read-only in practice).
@export var instance_labels: PackedStringArray = PackedStringArray()

var _column_header_nodes: Array[Label3D] = []
var _row_header_nodes: Array[Label3D] = []

func _ready() -> void:
	_arrange()
	child_entered_tree.connect(_on_child_changed)
	child_exiting_tree.connect(_on_child_changed)

func _on_child_changed(_node: Node) -> void:
	_arrange.call_deferred()

func get_column_count() -> int:
	return maxi(metric_labels.size(), 1)

func get_row_count() -> int:
	var cols := get_column_count()
	var shape_count := _get_shape_children().size()
	if shape_count == 0:
		return maxi(instance_labels.size(), 1)
	@warning_ignore("integer_division")
	return ceili(float(shape_count) / float(cols))

func get_extent() -> Vector2:
	var cols := get_column_count()
	var rows := get_row_count()
	var w := (cols - 1) * column_spacing + 0.8  # 0.8 = shape width
	var d := (rows - 1) * row_spacing + 0.8
	return Vector2(w, d)

func _arrange() -> void:
	var shapes := _get_shape_children()
	var cols := get_column_count()
	_apply_filters(shapes)
	var visible_idx := 0
	for shape in shapes:
		if not shape.visible:
			continue
		@warning_ignore("integer_division")
		var col := visible_idx % cols
		@warning_ignore("integer_division")
		var row := visible_idx / cols
		shape.position = Vector3(
			col * column_spacing,
			0,
			-row * row_spacing
		)
		visible_idx += 1
	_rebuild_column_headers()
	_rebuild_row_headers()

func _get_shape_children() -> Array:
	var result: Array = []
	for child in get_children():
		if child is Node3D and not child is Label3D and not child is MeshInstance3D:
			result.append(child)
	return result

func _apply_filters(shapes: Array) -> void:
	# Metric filtering: match against node name suffix or shape metadata
	for shape in shapes:
		var metric_name := _extract_metric_label(shape)
		var instance_name := _extract_instance_label(shape)
		var show := true
		if metric_include_filter != "" and not metric_name.matchn(metric_include_filter):
			show = false
		if metric_exclude_filter != "" and metric_name.matchn(metric_exclude_filter):
			show = false
		if instance_include_filter != "" and instance_name != "" and not instance_name.matchn(instance_include_filter):
			show = false
		if instance_exclude_filter != "" and instance_name != "" and instance_name.matchn(instance_exclude_filter):
			show = false
		shape.visible = show

func _extract_metric_label(shape: Node3D) -> String:
	# Convention: node name ends with _MetricLabel (e.g., CPU_User → "User")
	var parts := shape.name.split("_")
	if parts.size() > 1:
		return parts[-1]
	return shape.name

func _extract_instance_label(shape: Node3D) -> String:
	# Convention: for per-instance shapes, name is Zone_Instance_Metric
	var parts := shape.name.split("_")
	if parts.size() > 2:
		return parts[-2]
	return ""

func _rebuild_column_headers() -> void:
	# Remove old headers
	for label in _column_header_nodes:
		if is_instance_valid(label):
			label.queue_free()
	_column_header_nodes.clear()

	if metric_labels.is_empty():
		return

	var rows := get_row_count()
	var z := -(rows - 1) * row_spacing - label_gap

	for i in metric_labels.size():
		var label := Label3D.new()
		label.name = "ColHeader_%d" % i
		label.text = metric_labels[i]
		label.font_size = 40
		label.pixel_size = 0.01
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		# Perpendicular from back edge: flat on floor, text reads away from bezel (-Z).
		# Basis: local_X = world -Z (text direction), local_Y = world +Y (not visible when flat),
		#         local_Z = world -X (face-up component — but Label3D renders in XY plane).
		# NOTE: label transforms are notoriously tricky — VERIFY IN GODOT before shipping.
		# If text reads wrong direction, flip local_X sign.
		var x := float(i) * column_spacing
		label.transform = Transform3D(
			Vector3(0, 0, -1),  # local X → world -Z (text reads into background)
			Vector3(0, 1, 0),   # local Y → world +Y
			Vector3(1, 0, 0),   # local Z → world +X
			Vector3(x, 0.01, z)
		)
		add_child(label)
		_column_header_nodes.append(label)

func _rebuild_row_headers() -> void:
	# Remove old headers
	for label in _row_header_nodes:
		if is_instance_valid(label):
			label.queue_free()
	_row_header_nodes.clear()

	if instance_labels.is_empty():
		return

	var cols := get_column_count()
	var x := (cols - 1) * column_spacing + 0.8 + label_gap

	for i in instance_labels.size():
		var label := Label3D.new()
		label.name = "RowHeader_%d" % i
		label.text = instance_labels[i]
		label.font_size = 40
		label.pixel_size = 0.01
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		# Perpendicular from right edge: flat on floor, text reads away from bezel (+X).
		# Basis: local_X = world +X (text direction), local_Y = world +Y,
		#         local_Z = world +Z (face direction).
		# NOTE: label transforms are notoriously tricky — VERIFY IN GODOT before shipping.
		# If text reads wrong direction, flip local_X sign.
		var z := -float(i) * row_spacing
		label.transform = Transform3D(
			Vector3(1, 0, 0),   # local X → world +X (text reads rightward)
			Vector3(0, 0, 1),   # local Y → world +Z (up when flat = toward camera)
			Vector3(0, -1, 0),  # local Z → world -Y (face points up from floor)
			Vector3(x, 0.01, z)
		)
		add_child(label)
		_row_header_nodes.append(label)
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_grid.gd
git commit -m "Add MetricGrid component — dynamic grid layout with column/row headers and glob filtering"
```

---

### Task 3: Create MetricGroupNode Component

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_group_node.gd`

- [ ] **Step 1: Write metric_group_node.gd**

```gdscript
@tool
class_name MetricGroupNode
extends Node3D

## Title text displayed on the foreground edge of the bezel.
@export var title_text: String = "":
	set(value):
		title_text = value
		if _title_label:
			_title_label.text = title_text

## Gap between the bezel front edge and the title label.
@export var title_gap: float = 1.5

## Gap passed to MetricGrid for metric/instance label offset from bezel edge.
@export var label_gap: float = 1.0:
	set(value):
		label_gap = value
		var grid := _find_grid()
		if grid:
			grid.label_gap = label_gap

var _title_label: Label3D = null

func _ready() -> void:
	_create_title_label()
	# Sync label_gap to grid
	var grid := _find_grid()
	if grid:
		grid.label_gap = label_gap

func _process(_delta: float) -> void:
	var grid := _find_grid()
	if grid == null:
		return
	var extent := grid.get_extent()
	_update_title_position(extent, grid)
	_update_bezel(extent, grid)

func _create_title_label() -> void:
	_title_label = Label3D.new()
	_title_label.name = "TitleLabel"
	_title_label.text = title_text
	_title_label.font_size = 56
	_title_label.pixel_size = 0.01
	_title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title_label)

func _update_title_position(extent: Vector2, grid: MetricGrid) -> void:
	if _title_label == null:
		return
	# Title sits on the foreground edge (+Z), parallel to the bezel, centred on width
	var centre_x := extent.x / 2.0
	var front_z := title_gap
	# Flat on floor, reads along X axis
	_title_label.transform = Transform3D(
		Vector3(1, 0, 0),
		Vector3(0, 0, 1),
		Vector3(0, -1, 0),
		Vector3(centre_x, 0.01, front_z)
	)

func _update_bezel(extent: Vector2, grid: MetricGrid) -> void:
	var bezel := _find_bezel()
	if bezel == null:
		return
	bezel.resize(extent.x, extent.y)
	# Centre bezel on the grid content
	bezel.position = Vector3(extent.x / 2.0, -0.01, -extent.y / 2.0 + 0.4)

func _find_grid() -> MetricGrid:
	for child in get_children():
		if child is MetricGrid:
			return child
	return null

func _find_bezel() -> GroundBezel:
	for child in get_children():
		if child is GroundBezel:
			return child
	return null
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/metric_group_node.gd
git commit -m "Add MetricGroupNode — orchestrator for title, grid, and bezel components"
```

---

### Task 4: Delete GridLayout3D

**Files:**
- Delete: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grid_layout_3d.gd`

- [ ] **Step 1: Remove grid_layout_3d.gd**

```bash
git rm src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grid_layout_3d.gd
git commit -m "Remove GridLayout3D — replaced by MetricGrid component"
```

---

## Chunk 2: C# Model + TscnWriter Refactor

### Task 5: Simplify PlacedZone Model

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs`

PlacedZone keeps MetricLabels, InstanceLabels, and spacing values — TscnWriter needs them to emit MetricGrid `@export` properties. Only `GridColumns` retires (derived by MetricGrid from `metric_labels.size()` at runtime). `GridColumnSpacing`/`GridRowSpacing` rename to `ColumnSpacing`/`RowSpacing`.

**IMPORTANT:** All PlacedZone constructors in tests must use **named parameters** to avoid silent positional breakage after field removal/reordering.

- [ ] **Step 2: Remove GridColumns from PlacedZone**

In `SceneLayout.cs`, remove the `GridColumns` parameter from `PlacedZone`. Tests and code that used `GridColumns` to distinguish foreground/background zones should use `InstanceLabels?.Count > 0` or check `MetricLabels` instead.

Edit `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs`:

Remove `int? GridColumns` from the PlacedZone record parameters. Keep MetricLabels, InstanceLabels, GridColumnSpacing (rename to ColumnSpacing), GridRowSpacing (rename to RowSpacing).

```csharp
public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,
    float? ColumnSpacing,
    float? RowSpacing,
    IReadOnlyList<PlacedItem> Items,
    float GroundWidth = 0f,
    float GroundDepth = 0f,
    IReadOnlyList<string>? MetricLabels = null,
    IReadOnlyList<string>? InstanceLabels = null,
    bool RotateYNinetyDeg = false)
{
    public IReadOnlyList<PlacedShape> Shapes => Items.OfType<PlacedShape>().ToList();
    public bool HasGrid => MetricLabels is { Count: > 0 };
}
```

- [ ] **Step 3: Update LayoutCalculator for new PlacedZone shape**

In `LayoutCalculator.cs`:
- Remove `GridColumns:` parameter from PlacedZone construction (use `HasGrid` property instead)
- Rename `GridColumnSpacing:` → `ColumnSpacing:`, `GridRowSpacing:` → `RowSpacing:`
- Populate `MetricLabels` for **all** zones (foreground too — MetricGrid needs column headers everywhere)
- Update `ZoneWidth()` to use `HasGrid` instead of `GridColumns.HasValue`

- [ ] **Step 4: Fix all compilation errors in tests**

Update all test files that construct PlacedZone or check GridColumns:
- `LayoutCalculatorTests.cs`: Replace `z.GridColumns == null` with `!z.HasGrid`, `z.GridColumns != null` with `z.HasGrid`, remove `GridColumns` from any PlacedZone construction
- `TscnWriterTests.cs`: Update PlacedZone constructors in test helpers to use new parameter names

- [ ] **Step 5: Run tests, verify all pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" --verbosity normal
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Simplify PlacedZone — remove GridColumns, rename spacing fields, add HasGrid property"
```

---

### Task 6: Refactor TscnWriter to Emit MetricGroupNode Tree

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

This is the largest task. TscnWriter changes from emitting raw zones with inline labels/bezels to emitting MetricGroupNode > GroundBezel + MetricGrid > shapes.

- [ ] **Step 1: Update TscnWriter tests first (TDD)**

**Complete test disposition** — every existing test that breaks, and what replaces it:

| Existing Test | Action | Replacement |
|---|---|---|
| `Write_GridZone_HasGridLayout3DScript` | **Rewrite** → check `metric_grid.gd` + `metric_group_node.gd` | `Write_Zone_EmitsMetricGridChild` |
| `Write_EmitsZoneLabelNode_WithGroundPlaneProperties` | **Rewrite** → check `title_text` property on MetricGroupNode | `Write_Zone_EmitsMetricGroupNode_WithTitleText` |
| `Write_ZoneLabel_CentredOnShapeSpan` | **Rewrite** → no inline label; verify `title_text` is set | Covered by `Write_Zone_EmitsMetricGroupNode_WithTitleText` |
| `Write_ZoneLabel_IsPlacedBeyondBezelEdge` | **Rewrite** → no inline label | Covered by `Write_Zone_EmitsMetricGroupNode_WithTitleText` |
| `Write_RotatedZoneLabel_PlacedOnForegroundSide` | **Rewrite** → no inline label | Covered by `Write_Zone_EmitsMetricGroupNode_WithTitleText` |
| `Write_ForegroundShape_EmitsMetricLabel` | **Rewrite** → no inline shape labels; verify `metric_labels` on grid | `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_ShapeLabel_HasIncreasedFontSize` | **Rewrite** → label font is MetricGrid's job | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_ShapeWithLeftLabelPlacement_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_ShapeWithRightLabelPlacement_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_ShapeFrontLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_LeftPlacementLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_RightPlacementLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_FrontPlacementLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_RotatedZone_LeftLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_RotatedZone_RightLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_RotatedZone_FrontLabel_*` | **Rewrite** → no inline labels | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_GridZone_EmitsColumnHeaderLabels` | **Rewrite** → no inline headers | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_GridZone_EmitsRowHeaderLabels` | **Rewrite** → no inline headers | `Write_Zone_MetricGrid_HasInstanceLabels` |
| `Write_GridZone_ColumnHeaders_AreAtBackEdge_*` | **Rewrite** → no inline headers | Covered by `Write_Zone_MetricGrid_HasMetricLabels` |
| `Write_GridZone_RowHeaders_AreOnRightSide_*` | **Rewrite** → no inline headers | `Write_Zone_MetricGrid_HasInstanceLabels` |
| `Write_GridColumnHeader_HasIncreasedFontSize` | **Rewrite** → no inline headers | Covered by above |
| `Write_GridRowHeader_HasIncreasedFontSize` | **Rewrite** → no inline headers | Covered by above |
| `Write_EmitsGroundBezelMeshPerZone` | **Rewrite** → check GroundBezel component node | `Write_Zone_EmitsGroundBezelChild` |
| `Write_GroundBezel_HasDarkGreyMaterial` | **Rewrite** → no inline material sub_resource | `Write_Zone_EmitsGroundBezelChild` |
| `Write_ZoneNameWithInvalidIdChars_BezelSubResourceIdsAreSanitised` | **Rewrite** → no bezel sub_resources | Delete (no bezel sub_resources to sanitise) |
| `Write_ZeroGroundExtent_NoBezelEmitted` | **Rewrite** → GroundBezel always emitted (self-sizes at runtime) | Delete (GroundBezel is always a child) |
| `Write_ShapeHasPcpBindableChild` | **Update path** → `parent="CPU/CPUGrid/CPU_User"` | Same test, updated parent path |
| `Write_PlacedStack_EmitsStackGroupNode_WithScript` | **Update path** → `parent="CPU/CPUGrid"` | Same test, updated parent path |
| `Write_PlacedStack_EmitsEachMemberBarWithPcpBindable` | **Update path** → `parent="CPU/CPUGrid/CpuStack"` | Same test, updated parent path |
| `Write_PlacedStack_MemberPcpBindables_AreChildrenOfStack` | **Update path** → `parent="CPU/CPUGrid/CpuStack/CPU_User"` | Same test, updated parent path |
| `Write_LoadSteps_EqualsExtResourcesPlusSubResourcesPlusWorldEnv` | **Update formula** → 9 ext + 1 sub + 0 bezel + 2 ambient + 1 env = 13 | Same test, assertion `load_steps=13` |
| `Write_LoadSteps_WithBezel_IncludesBezelSubResources` | **Rewrite** → no bezel sub_resources; bezel is always emitted | Delete (bezel sub_resources gone) |
| `Write_LoadSteps_WithPlacedStack_CountsCorrectly` | **Update formula** → 10 ext + 3 sub + 0 bezel + 2 ambient + 1 env = 16 | Same test, assertion `load_steps=16` |

**Tests that survive unchanged** (except MinimalLayout helper update):
`Write_StartsWithGdSceneHeader`, `Write_HasExtResourceForPcpBindable`, `Write_HasExtResourceForPcpBindingResource`, `Write_HasExtResourceForGroundedBar_WithBuildingBlocksPath`, `Write_SubResourceHasScriptAndBindingFields`, `Write_ContainsRootNode3D`, `Write_ContainsZoneNode`, `Write_ShapeInstancesParentScene`, `Write_CylinderShape_UsesGroundedCylinder_WithBuildingBlocksPath`, `Write_InstanceBinding_HasInstanceName`, `Write_ZoneTransformHasPosition`, `Write_ShapeNode_EmitsColourProperty`, `Write_WithCamera_*`, `Write_WithoutCamera_*`, `Write_RootNode_HasControllerScript`, `Write_HasMetricPollerChildNode`, `Write_HasSceneBinderChildNode`, `Write_MetricPoller_*`, `Write_TimestampLabel_*`, `Write_HostnameLabel_*`, `Write_RotatedZone_EmitsYRotationTransform`, `Write_RotatedZoneAtOrigin_*`, `Write_PlacedStack_StackMode*`, `Write_PlacedStack_RegistersStackGroupScript_*`

**New tests to add:**

```csharp
[Fact]
public void Write_Zone_EmitsMetricGroupNode_WithTitleText()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("[node name=\"CPU\" type=\"Node3D\" parent=\".\"]", tscn);
    Assert.Contains("metric_group_node.gd", tscn);
    Assert.Contains("title_text = \"CPU\"", tscn);
}

[Fact]
public void Write_Zone_EmitsGroundBezelChild()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("[node name=\"CPUBezel\" type=\"MeshInstance3D\" parent=\"CPU\"]", tscn);
    Assert.Contains("ground_bezel.gd", tscn);
}

[Fact]
public void Write_Zone_EmitsMetricGridChild()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("[node name=\"CPUGrid\" type=\"Node3D\" parent=\"CPU\"]", tscn);
    Assert.Contains("metric_grid.gd", tscn);
}

[Fact]
public void Write_Zone_MetricGrid_HasMetricLabels()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("CPU", "CPU", Vec3.Zero, null, null,
            [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                "kernel.all.cpu.user", null, "User",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            MetricLabels: ["User", "Sys", "Nice"])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("metric_labels = PackedStringArray(\"User\", \"Sys\", \"Nice\")", tscn);
}

[Fact]
public void Write_Zone_MetricGrid_HasInstanceLabels()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("PerCPU", "Per-CPU", Vec3.Zero, 2.0f, 2.5f,
            [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("instance_labels = PackedStringArray(\"cpu0\", \"cpu1\")", tscn);
}

[Fact]
public void Write_Shapes_AreChildrenOfMetricGrid()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("parent=\"CPU/CPUGrid\"", tscn);
}

[Fact]
public void Write_Stack_IsChildOfMetricGrid()
{
    var tscn = TscnWriter.Write(LayoutWithCpuStack());
    Assert.Contains("[node name=\"CpuStack\" type=\"Node3D\" parent=\"CPU/CPUGrid\"]", tscn);
}
```

- [ ] **Step 2: Run tests, confirm they fail**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" --verbosity normal
```

Expected: new tests FAIL (TscnWriter still emits old structure).

- [ ] **Step 3: Update TscnWriter implementation**

Key changes to `TscnWriter.cs`:

1. **Register new scripts** in `RegisterControllerResources`:
   - `metric_group_script` → `res://addons/pmview-bridge/building_blocks/metric_group_node.gd`
   - `metric_grid_script` → `res://addons/pmview-bridge/building_blocks/metric_grid.gd`
   - `ground_bezel_script` → `res://addons/pmview-bridge/building_blocks/ground_bezel.gd`

2. **Remove methods:**
   - `WriteZoneLabelNode()` — title is a MetricGroupNode `@export`
   - `WriteShapeLabel()` — labels are MetricGrid's job
   - `WriteGridColumnHeaders()` — MetricGrid creates these
   - `WriteGridRowHeaders()` — MetricGrid creates these
   - `WriteGroundBezel()` — GroundBezel is a component
   - `CollectBezelSubResources()` — no inline bezel mesh/material
   - `WriteBezelSubResources()` — no inline bezel mesh/material

3. **Rewrite `WriteZone()`:**
   ```csharp
   private static void WriteZone(StringBuilder sb, PlacedZone zone,
       ExtResourceRegistry registry, List<SubResourceEntry> subResources)
   {
       WriteMetricGroupNode(sb, zone, registry);
       WriteGroundBezelNode(sb, zone);
       WriteMetricGridNode(sb, zone, registry);

       foreach (var item in zone.Items)
       {
           var gridPath = $"{zone.Name}/{zone.Name}Grid";
           switch (item)
           {
               case PlacedStack stack:
                   WriteStack(sb, stack, zone, registry, subResources, gridPath);
                   break;
               case PlacedShape shape:
                   WriteShape(sb, shape, zone, registry, subResources, parentOverride: gridPath);
                   break;
           }
       }
   }
   ```

4. **New `WriteMetricGroupNode()`:**
   Emit zone as MetricGroupNode with `title_text`, `title_gap`, `label_gap` properties.

5. **New `WriteGroundBezelNode()`:**
   Emit GroundBezel child with `bezel_colour` property (no mesh sub_resources — component creates mesh at runtime).

6. **New `WriteMetricGridNode()`:**
   Emit MetricGrid child with `column_spacing`, `row_spacing`, `metric_labels` (PackedStringArray), `instance_labels` (PackedStringArray).

7. **Update `WriteShape()`:**
   Remove position emission (shapes at Vec3.Zero — MetricGrid positions them).

8. **Update `WriteStack()`:**
   Parent is now the grid path, not the zone directly.

9. **Update `WriteHeader()`:**
   Remove bezel sub_resource count from load_steps formula. No `CollectBezelSubResources` data to count.

10. **Update `WriteNodes()`:**
    Remove bezelResources parameter. Remove grid_script registration (replaced by metric_grid_script).

- [ ] **Step 4: Run tests, verify all pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" --verbosity normal
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Refactor TscnWriter to emit MetricGroupNode/MetricGrid/GroundBezel component tree"
```

---

## Chunk 3: LayoutCalculator Simplification + Cleanup

### Task 7: Simplify LayoutCalculator

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Identify tests to retire**

**Complete test disposition:**

| Test | Action |
|---|---|
| `Calculate_SimpleZones_ShapesHaveAutoSpacedPositions` | **Rewrite** → verify all shapes at Vec3.Zero (MetricGrid positions them) |
| `Calculate_GridSpacing_WiderThanShapeWidth` | **Rewrite** → check `ColumnSpacing`/`RowSpacing` instead of `GridColumnSpacing`/`GridRowSpacing` |
| `Calculate_PerCpuZone_GridColumnsEqualsMetricCount` | **Rewrite** → check `MetricLabels.Count == 3` instead of `GridColumns == 3` |
| `Calculate_SimpleZones_HaveEmptyGridLabels` | **Rewrite** → foreground zones now HAVE MetricLabels, InstanceLabels remain empty |
| `Calculate_ForegroundRow_CenteredOnGroundWidthFootprint` | **Update** → GroundWidth values change with nominal formula |
| `Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive` | **Update** → GroundWidth values change |
| `Calculate_BackgroundZone_HasGroundExtent` | **Update** → extent is nominal, still > 0 |

**Mechanical `GridColumns` → `HasGrid` updates** (content survives, predicate changes):
- `Calculate_ForegroundRow_CenteredOnXZero`: `z.GridColumns == null` → `!z.HasGrid`
- `Calculate_ForegroundZones_AtZEqualsZero`: same
- `Calculate_ForegroundZoneOrder_*`: same
- `Calculate_ForegroundRow_CenteredOnGroundWidthFootprint`: same
- `Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive`: same
- `Calculate_NetInAggregateZone_IsForeground_*`: same
- `Calculate_BackgroundZones_AtNegativeZOffset`: `z.GridColumns != null` → `z.HasGrid`
- `Calculate_AdjacentBackgroundZones_*`: `z.GridColumns.HasValue` → `z.HasGrid`

**New test to add:**

```csharp
[Fact]
public void Calculate_ForegroundZone_HasMetricLabels()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var cpu = layout.Zones.Single(z => z.Name == "CPU");
    Assert.Equal(new[] { "User", "Sys", "Nice" }, cpu.MetricLabels);
}

[Fact]
public void Calculate_ForegroundZone_GroundWidthIsNominalFromMetricCount()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var cpu = layout.Zones.Single(z => z.Name == "CPU");
    // 3 metrics → nominal width based on metric count * column spacing
    Assert.True(cpu.GroundWidth > 2f, $"CPU GroundWidth {cpu.GroundWidth} should reflect 3-metric zone");
}
```

**Tests that survive unchanged** (no GridColumns references):
`Calculate_SetsHostname`, `Calculate_ForegroundShapes_HaveUniqueNodeNames`, `Calculate_CpuZone_HasThreeShapes_NoStacks`, `Calculate_LoadZone_HasThreeShapes_WithInstanceNames`, `Calculate_MemoryZone_HasThreeShapes_SourceRangeMaxFromPhysmem`, `Calculate_SimpleZones_HaveNoRotation`, `Calculate_PerCpuZone_ShapeCountEqualsInstancesTimesMetrics`, `Calculate_BackgroundShapes_HaveInstanceNames`, `Calculate_BackgroundShapes_LocalPositionsAreZero`, `Calculate_InstanceNameShortening_StripsPrefixPath`, `Calculate_EmptyInstances_ProducesZeroShapes`, `Calculate_PerCpuZone_HasMetricLabels`, `Calculate_PerCpuZone_HasInstanceLabels`

- [ ] **Step 2: Update tests per disposition above**

- [ ] **Step 3: Simplify LayoutCalculator implementation**

Key changes:
1. `BuildForegroundItems()` — shapes all go to `Vec3.Zero` (MetricGrid positions them). Remove `ShapeSpacing` auto-positioning (`i * ShapeSpacing` → `Vec3.Zero`).
2. Populate `MetricLabels` for **all** zones (foreground too — MetricGrid needs column headers everywhere). Update `PlaceZone()` to set MetricLabels from `zone.Metrics.Select(m => m.Label)` unconditionally.
3. `ComputeGroundExtent()` — **foreground nominal formula:** `zone.Metrics.Count * ShapeSpacing` for width (NOT `items.Max(ItemFootprintMaxX)` — items are all at Vec3.Zero now). Must use `zone.Metrics.Count`, not item count, because stacked metrics produce fewer items than metrics. **Background formula unchanged:** `(cols - 1) * GridColumnSpacing + 0.8 + padding * 2`. The method signature changes to accept `ZoneDefinition zone` in addition to items.
4. `PlaceZone()` — use `HasGrid` property for branching instead of `GridColumns.HasValue`.
5. Rename `GridColumnSpacing`/`GridRowSpacing` constants to `ColumnSpacing`/`RowSpacing`.

- [ ] **Step 4: Run tests, verify all pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" --verbosity normal
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Simplify LayoutCalculator — nominal ground extent, foreground MetricLabels, remove intra-zone positioning"
```

---

### Task 8: Final Verification + Cleanup

- [ ] **Step 1: Full test run**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" --verbosity normal
```

- [ ] **Step 2: Generate a test scene and inspect**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  --install-addon \
  -o /tmp/test_host_view.tscn 2>&1 || echo "Expected: may fail without pmproxy, but shows any compilation issues"
```

- [ ] **Step 3: Verify .tscn structure has new components**

Read the generated .tscn and verify:
- Zones have `metric_group_node.gd` script
- Each zone has a GroundBezel child with `ground_bezel.gd`
- Each zone has a MetricGrid child with `metric_grid.gd`, `metric_labels`, `instance_labels`
- Shapes are children of the MetricGrid, not the zone directly
- No inline bezel BoxMesh sub_resources
- No inline Label3D nodes for zone labels, shape labels, or grid headers

- [ ] **Step 4: Commit any final fixes**

```bash
git add -A
git commit -m "Final cleanup — unified MetricGroupNode component hierarchy complete"
```
