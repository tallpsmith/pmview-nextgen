# Host View Visual Polish Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix colouring, layout bugs, add zone ground bezels, metric labels, and restore archive playback in the generated host-view scene.

**Architecture:** All visual changes flow through the C# host-projector pipeline (LayoutCalculator -> TscnWriter -> SceneEmitter). The GridLayout3D GDScript needs a fix to skip non-shape children. The metric_scene_controller.gd needs archive startup fixes. Pure .NET code is TDD'd with xUnit; GDScript changes are manually verified.

**Tech Stack:** C# (.NET 8.0 library / .NET 10.0 tests), xUnit, GDScript (Godot 4.x), .tscn scene format

---

## Chunk 1: Shape Colouring & Label-in-Grid Bug

These are the two most visible bugs — white shapes and off-by-one layout.

### Task 1: Emit shape colour in TscnWriter

The `grounded_shape.gd` has an `@export var colour: Color` property, but `TscnWriter.WriteShape()` never sets it. Every shape renders as `Color.WHITE`.

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs:160-173`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — shape node emits colour property**

Add to `TscnWriterTests.cs`:

```csharp
[Fact]
public void Write_ShapeNode_EmitsColourProperty()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
            [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                "kernel.all.cpu.user", null, null,
                new RgbColour(0.976f, 0.451f, 0.086f),
                0f, 100f, 0.2f, 5.0f)])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("colour = Color(0.976, 0.451, 0.086, 1)", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "Write_ShapeNode_EmitsColourProperty" -v n`
Expected: FAIL — output does not contain `colour = Color(...)`

- [ ] **Step 3: Implement — emit colour on shape nodes**

In `TscnWriter.cs`, modify `WriteShape()` to emit the colour after the instance line. The `PlacedShape` already carries `Colour`. Godot's .tscn format for a Color export is `colour = Color(r, g, b, a)`.

```csharp
// In WriteShape(), after the transform line and before the blank line:
sb.AppendLine($"colour = Color({F(shape.Colour.R)}, {F(shape.Colour.G)}, {F(shape.Colour.B)}, 1)");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "Write_ShapeNode_EmitsColourProperty" -v n`
Expected: PASS

- [ ] **Step 5: Run all TscnWriter tests to check no regressions**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "TscnWriterTests" -v n`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Fix white shapes: emit colour property in generated .tscn scenes

TscnWriter.WriteShape() never set the grounded_shape colour export,
so every shape defaulted to Color.WHITE. Now emits the profile's
DefaultColour on each shape node."
```

---

### Task 2: Fix GridLayout3D label-in-grid off-by-one bug

`grid_layout_3d.gd` `_arrange()` iterates all `Node3D` children. `Label3D` extends `Node3D`, so the zone label gets treated as grid slot 0, pushing every shape one position off. Shapes also overlap the label text since the label and first shape both get positioned by the grid.

**Files:**
- Modify: `godot-project/addons/pmview-bridge/building_blocks/grid_layout_3d.gd:31-42`

- [ ] **Step 1: Fix `_arrange()` to skip Label3D children**

The grid should only arrange children that are `GroundedShape` instances (or more generally, skip `Label3D` nodes). Since `GroundedShape` is a class_name, we can check for it. But to be robust, skip `Label3D` nodes — they're layout-inert decoration.

```gdscript
func _arrange() -> void:
	var idx := 0
	for child in get_children():
		if child is Node3D and not child is Label3D:
			var col := idx % columns
			var row := idx / columns
			child.position = Vector3(
				col * column_spacing,
				0,
				-row * row_spacing
			)
			idx += 1
```

- [ ] **Step 2: Commit**

```bash
git add godot-project/addons/pmview-bridge/building_blocks/grid_layout_3d.gd
git commit -m "Fix off-by-one grid layout: skip Label3D in grid arrangement

GridLayout3D._arrange() was positioning Label3D zone labels as grid
slot 0, shifting all shapes one position off. Label3D nodes are now
excluded from grid arrangement."
```

> **Note:** This is GDScript in the Godot addon — no xUnit test possible. User verifies visually in Godot editor. The grid_layout_3d.gd is a @tool script so the fix is visible in-editor immediately.

---

## Chunk 2: Zone Ground Bezels

Add a subtle dark-grey ground plane underneath each zone to visually group its shapes, matching the original pmview's thin rectangular grouping shape.

### Task 3: Add ground bezel to PlacedZone model

The layout calculator needs to compute the bounding rectangle for each zone so the TscnWriter can emit a ground plane. We add width/depth to `PlacedZone`.

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs:22-29`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Write failing test — PlacedZone carries ground extent**

Add to `LayoutCalculatorForegroundTests`:

```csharp
[Fact]
public void Calculate_ForegroundZone_HasGroundExtent()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var load = layout.Zones.Single(z => z.Name == "Load");
    // Load zone has 3 shapes at X=0, 1.5, 3.0. Ground should span them + padding.
    Assert.True(load.GroundWidth > 3.0f, $"GroundWidth {load.GroundWidth} should be > 3.0");
    Assert.True(load.GroundDepth > 0f, $"GroundDepth {load.GroundDepth} should be > 0");
}
```

Add to `LayoutCalculatorBackgroundTests`:

```csharp
[Fact]
public void Calculate_BackgroundZone_HasGroundExtent()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
    var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
    // 4 instances x 3 metrics, grid 3 cols => 4 rows x 3 cols
    Assert.True(perCpu.GroundWidth > 0f);
    Assert.True(perCpu.GroundDepth > 0f);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GroundExtent" -v n`
Expected: FAIL — `PlacedZone` does not have `GroundWidth`/`GroundDepth` properties

- [ ] **Step 3: Add GroundWidth and GroundDepth to PlacedZone record**

In `SceneLayout.cs`:

```csharp
public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,
    int? GridColumns,
    float? GridColumnSpacing,
    float? GridRowSpacing,
    IReadOnlyList<PlacedShape> Shapes,
    float GroundWidth = 0f,
    float GroundDepth = 0f);
```

- [ ] **Step 4: Compute ground extent in LayoutCalculator**

In `LayoutCalculator.cs`, modify `PlaceZone()` to compute ground size. For foreground zones, it's based on shape span. For background (grid) zones, it's based on grid columns/rows.

```csharp
private static PlacedZone PlaceZone(ZoneDefinition zone, HostTopology topology)
{
    var shapes = zone.Row == ZoneRow.Foreground
        ? BuildForegroundShapes(zone, topology)
        : BuildBackgroundShapes(zone, topology);

    var (groundWidth, groundDepth) = ComputeGroundExtent(zone, shapes, topology);

    return new PlacedZone(
        Name:              zone.Name,
        ZoneLabel:         zone.Name,
        Position:          Vec3.Zero,
        GridColumns:       zone.Row == ZoneRow.Background ? zone.Metrics.Count : null,
        GridColumnSpacing: zone.Row == ZoneRow.Background ? GridColumnSpacing : null,
        GridRowSpacing:    zone.Row == ZoneRow.Background ? GridRowSpacing : null,
        Shapes:            shapes,
        GroundWidth:       groundWidth,
        GroundDepth:       groundDepth);
}

private const float GroundPadding = 0.6f;

private static (float Width, float Depth) ComputeGroundExtent(
    ZoneDefinition zone, IReadOnlyList<PlacedShape> shapes, HostTopology topology)
{
    if (shapes.Count == 0) return (0f, 0f);

    if (zone.Row == ZoneRow.Foreground)
    {
        var maxX = shapes.Max(s => s.LocalPosition.X);
        // Shapes are 0.8 wide (bar mesh), so add 0.8 + padding on each side
        return (maxX + 0.8f + GroundPadding * 2, 0.8f + GroundPadding * 2);
    }

    // Background grid: columns * spacing wide, rows * spacing deep
    var cols = zone.Metrics.Count;
    var instances = ResolveInstances(zone, topology);
    var rows = instances.Count;
    var width = (cols - 1) * GridColumnSpacing + 0.8f + GroundPadding * 2;
    var depth = (rows - 1) * GridRowSpacing + 0.8f + GroundPadding * 2;
    return (width, depth);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GroundExtent" -v n`
Expected: PASS

- [ ] **Step 6: Fix any existing tests broken by new record parameter**

The new `GroundWidth`/`GroundDepth` have defaults of `0f`, so existing `PlacedZone` constructions in tests should still compile. Run full suite:

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs \
        src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "Compute ground bezel extent per zone in layout calculator

Each PlacedZone now carries GroundWidth/GroundDepth representing the
bounding rectangle for visual grouping. Foreground zones span shapes +
padding; background zones span grid dimensions + padding."
```

---

### Task 4: Emit ground bezel mesh in TscnWriter

Write a flat, dark-grey `MeshInstance3D` (BoxMesh with tiny Y height) underneath each zone.

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — ground bezel emitted per zone**

```csharp
[Fact]
public void Write_EmitsGroundBezelMeshPerZone()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
            [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                "kernel.all.cpu.user", null, null,
                new RgbColour(0.976f, 0.451f, 0.086f),
                0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 2.0f, GroundDepth: 2.0f)
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("[node name=\"CPUGround\" type=\"MeshInstance3D\" parent=\"CPU\"]", tscn);
    Assert.Contains("BoxMesh", tscn);
}

[Fact]
public void Write_GroundBezel_HasDarkGreyMaterial()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
            [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                "kernel.all.cpu.user", null, null,
                new RgbColour(0.976f, 0.451f, 0.086f),
                0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 2.0f, GroundDepth: 2.0f)
    ]);
    var tscn = TscnWriter.Write(layout);
    // Dark grey colour, matching original pmview's "not quite black, a little grey"
    Assert.Contains("albedo_color = Color(0.15, 0.15, 0.15, 1)", tscn);
}

[Fact]
public void Write_ZeroGroundExtent_NoBezelEmitted()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Empty", "Empty", Vec3.Zero, null, null, null, [],
            GroundWidth: 0f, GroundDepth: 0f)
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.DoesNotContain("Ground", tscn);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GroundBezel" -v n`
Expected: FAIL

- [ ] **Step 3: Implement ground bezel emission in TscnWriter**

**Important sub-resource architecture note:** The current `SubResourceEntry` record and `CollectSubResources()` only handle `PcpBindingResource` entries. The bezel needs inline `BoxMesh` and `StandardMaterial3D` sub_resources. These MUST appear in the `[sub_resource]` section BEFORE the `[node]` section (Godot .tscn format requirement), and the `load_steps` count in the header must include them.

**Approach:** Add a separate `List<BezelSubResource>` collection alongside the existing `SubResourceEntry` list. Modify the pipeline:

1. In `Write()`, call a new `CollectBezelSubResources(layout)` that returns bezel entries for zones with non-zero ground extent.
2. Update `WriteHeader()` to include bezel sub_resource count in `load_steps`.
3. Add `WriteBezelSubResources()` that emits the `[sub_resource type="BoxMesh"]` and `[sub_resource type="StandardMaterial3D"]` blocks between `WriteSubResources()` and `WriteNodes()`.
4. In `WriteZone()`, call `WriteGroundBezel()` after the zone label, referencing bezel sub_resource IDs.

```csharp
// New record for bezel sub-resources (one mesh + one material per zone):
private record BezelSubResources(
    string ZoneName,
    string MeshId,       // e.g. "bezel_mesh_CPU"
    string MaterialId,   // e.g. "bezel_mat_CPU"
    float Width,
    float Height,        // 0.02 — thin slab
    float Depth);

// In WriteZone(), add after WriteZoneLabelNode() and before the shape loop:
if (zone.GroundWidth > 0f && zone.GroundDepth > 0f)
    WriteGroundBezel(sb, zone);
```

The ground bezel `MeshInstance3D` is centred under the zone's shapes. For foreground zones, centre X is at half the shape span. For grid zones, centre on the grid extent. The bezel's Y position is -0.01 (just below ground) to avoid Z-fighting. The material colour is `Color(0.15, 0.15, 0.15, 1)` — "not quite black, a little grey" matching the original pmview.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GroundBezel" -v n`
Expected: PASS

- [ ] **Step 5: Run full TscnWriter and SceneEmitter test suites**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Emit dark-grey ground bezel per zone in generated scenes

Each zone with non-zero GroundWidth/GroundDepth gets a thin BoxMesh
MeshInstance3D at ground level with a dark grey material, matching the
original pmview's visual grouping rectangles."
```

---

## Chunk 3: Metric Labels

Add per-shape labels for foreground zones (alongside each bar), and column-header + row-header labels for background grid zones.

### Task 5: Emit per-shape label in foreground zones

Each shape in a foreground zone (Load, CPU, Memory, Disk) needs a small Label3D showing its `DisplayLabel` (e.g. "1m", "5m", "15m" for Load; "User", "Sys", "Nice" for CPU).

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — foreground shapes get label sibling**

```csharp
[Fact]
public void Write_ForegroundShape_EmitsMetricLabel()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
            [new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                "kernel.all.load", "1 minute", "1m",
                new RgbColour(0.388f, 0.400f, 0.945f),
                0f, 10f, 0.2f, 5.0f)])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("[node name=\"Load_1mLabel\" type=\"Label3D\" parent=\"Load\"]", tscn);
    Assert.Contains("text = \"1m\"", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "ForegroundShape_EmitsMetricLabel" -v n`
Expected: FAIL

- [ ] **Step 3: Implement — emit Label3D sibling for each foreground shape**

In `WriteShape()`, when the zone is NOT a grid zone (no GridColumns), emit a Label3D after the shape. Position it at the shape's local X, slightly in front (Z+1), lying flat on the ground (same transform convention as zone labels but smaller font).

```csharp
// After WriteShape() emits the PcpBindable child, if zone is foreground and shape has a DisplayLabel:
if (!zone.GridColumns.HasValue && shape.DisplayLabel is not null)
    WriteShapeLabel(sb, shape, zone.Name);

private static void WriteShapeLabel(StringBuilder sb, PlacedShape shape, string zoneName)
{
    var pos = shape.LocalPosition;
    sb.AppendLine($"[node name=\"{shape.NodeName}Label\" type=\"Label3D\" parent=\"{zoneName}\"]");
    sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(pos.X)}, 0.01, 0.6)");
    sb.AppendLine("pixel_size = 0.008");
    sb.AppendLine("font_size = 24");
    sb.AppendLine($"text = \"{shape.DisplayLabel}\"");
    sb.AppendLine("horizontal_alignment = 1");
    sb.AppendLine();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "ForegroundShape_EmitsMetricLabel" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Emit per-shape metric labels in foreground zones

Each foreground shape now gets a Label3D sibling showing its
DisplayLabel (e.g. '1m', 'User', 'Read'). Positioned in front
of the shape on the ground plane."
```

---

### Task 6: Emit column-header and row-header labels for grid zones

Background (per-instance) grid zones need:
- **Column headers** at the top: metric labels (e.g. "User", "Sys", "Nice" for Per-CPU)
- **Row headers** on the side: instance names (e.g. "cpu0", "cpu1", "cpu2", "cpu3")

These labels must NOT be `Node3D` children of the grid zone (or GridLayout3D will arrange them). They should be Label3D children of the grid zone but excluded by the grid layout (which we fixed in Task 2 to skip Label3D).

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs` (add MetricLabels and InstanceLabels to PlacedZone)
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Write failing layout test — grid zones carry metric and instance labels**

Add to `LayoutCalculatorBackgroundTests`:

```csharp
[Fact]
public void Calculate_PerCpuZone_HasMetricLabels()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
    var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
    Assert.Equal(new[] { "User", "Sys", "Nice" }, perCpu.MetricLabels);
}

[Fact]
public void Calculate_PerCpuZone_HasInstanceLabels()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
    var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
    Assert.Equal(new[] { "cpu0", "cpu1" }, perCpu.InstanceLabels);
}
```

Add to `LayoutCalculatorForegroundTests` (not BackgroundTests — this tests foreground behaviour):

```csharp
[Fact]
public void Calculate_ForegroundZone_HasEmptyGridLabels()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var load = layout.Zones.Single(z => z.Name == "Load");
    Assert.Empty(load.MetricLabels);
    Assert.Empty(load.InstanceLabels);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "MetricLabels|InstanceLabels|EmptyGridLabels" -v n`
Expected: FAIL — properties don't exist

- [ ] **Step 3: Add MetricLabels and InstanceLabels to PlacedZone**

In `SceneLayout.cs`:

```csharp
public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,
    int? GridColumns,
    float? GridColumnSpacing,
    float? GridRowSpacing,
    IReadOnlyList<PlacedShape> Shapes,
    float GroundWidth = 0f,
    float GroundDepth = 0f,
    IReadOnlyList<string>? MetricLabels = null,
    IReadOnlyList<string>? InstanceLabels = null);
```

Default `null` → treat as empty in TscnWriter. Populate in LayoutCalculator for background zones.

- [ ] **Step 4: Populate labels in LayoutCalculator.PlaceZone()**

For background zones, extract metric labels from `zone.Metrics` and instance labels from `ResolveInstances()`:

```csharp
var metricLabels = zone.Row == ZoneRow.Background
    ? zone.Metrics.Select(m => m.Label).ToList()
    : [];
var instanceLabels = zone.Row == ZoneRow.Background
    ? ResolveInstances(zone, topology).Select(ShortenInstanceName).ToList()
    : [];

// Pass to PlacedZone constructor:
MetricLabels: metricLabels,
InstanceLabels: instanceLabels
```

- [ ] **Step 5: Run layout tests to verify they pass**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "MetricLabels|InstanceLabels|EmptyGridLabels" -v n`
Expected: PASS

- [ ] **Step 6: Write failing TscnWriter test — grid zone emits column and row headers**

```csharp
[Fact]
public void Write_GridZone_EmitsColumnHeaderLabels()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
            3, 1.5f, 2.0f,
            [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f),
                0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("text = \"User\"", tscn);
    Assert.Contains("text = \"Sys\"", tscn);
    Assert.Contains("text = \"Nice\"", tscn);
}

[Fact]
public void Write_GridZone_EmitsRowHeaderLabels()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
            3, 1.5f, 2.0f,
            [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f),
                0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("text = \"cpu0\"", tscn);
    Assert.Contains("text = \"cpu1\"", tscn);
}
```

- [ ] **Step 7: Implement grid header emission in TscnWriter**

Add `WriteGridColumnHeaders()` and `WriteGridRowHeaders()` methods, called from `WriteZone()` for grid zones. Column headers go along the top (at each column X position, at Z slightly in front of row 0). Row headers go along the left side (at each row Z position, at X slightly left of column 0).

These are Label3D children of the zone node. Since Task 2 already excludes Label3D from grid arrangement, they won't be repositioned.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GridZone_Emits" -v n`
Expected: PASS

- [ ] **Step 9: Run full suite**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS

- [ ] **Step 10: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs \
        src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Add metric column headers and instance row headers to grid zones

Background per-instance zones now emit Label3D column headers (metric
names like User/Sys/Nice) and row headers (instance names like cpu0/cpu1).
Labels are children of the grid zone but skipped by GridLayout3D
arrangement (Task 2 fix)."
```

---

## Chunk 4: Zone Label Positioning & Grid Spacing

### Task 7: Fix zone label position to not overlap shapes

The zone label is currently at `transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0.01, 1)` — that's at local position (0, 0.01, 1), lying flat, facing up. But shapes start at local X=0, Z=0. The label sits IN FRONT of the shapes (Z=1 is towards the camera). In the original pmview, zone names are centered below the zone.

For foreground zones: move label to centre of the zone's shape span, at Z in front.
For grid zones: move label to centre of the grid, at Z in front of the grid.

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs:149-158`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — zone label centred on zone width**

```csharp
[Fact]
public void Write_ZoneLabel_CentredOnShapeSpan()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
            [
                new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                    "kernel.all.load", "1 minute", "1m",
                    new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
                new PlacedShape("Load_5m", ShapeType.Bar, new Vec3(1.5f, 0, 0),
                    "kernel.all.load", "5 minute", "5m",
                    new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
                new PlacedShape("Load_15m", ShapeType.Bar, new Vec3(3.0f, 0, 0),
                    "kernel.all.load", "15 minute", "15m",
                    new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
            ],
            GroundWidth: 4.6f, GroundDepth: 2.0f)
    ]);
    var tscn = TscnWriter.Write(layout);
    // Label should be at X = 1.5 (centre of 0..3.0 span), Z = positive (in front)
    Assert.Contains("text = \"Load\"", tscn);
    // The label X should be at the midpoint of the shape span
    Assert.Contains("1.5, 0.01, 1.5", tscn);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "ZoneLabel_Centred" -v n`
Expected: FAIL — label is at fixed (0, 0.01, 1)

- [ ] **Step 3: Modify WriteZoneLabelNode() to compute centre position**

```csharp
private static void WriteZoneLabelNode(StringBuilder sb, PlacedZone zone)
{
    var centreX = zone.Shapes.Count > 0
        ? zone.Shapes.Max(s => s.LocalPosition.X) / 2f
        : 0f;
    var labelZ = 1.5f; // In front of shapes

    sb.AppendLine($"[node name=\"{zone.Name}Label\" type=\"Label3D\" parent=\"{zone.Name}\"]");
    sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(centreX)}, 0.01, {F(labelZ)})");
    sb.AppendLine("pixel_size = 0.01");
    sb.AppendLine("font_size = 32");
    sb.AppendLine($"text = \"{zone.ZoneLabel}\"");
    sb.AppendLine("horizontal_alignment = 1");
    sb.AppendLine();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "ZoneLabel_Centred" -v n`
Expected: PASS

- [ ] **Step 5: Run full suite — check for regressions**

The existing test `Write_EmitsZoneLabelNode_WithGroundPlaneProperties` does NOT assert a specific transform value — it only checks for Label3D, text, font_size, pixel_size, and horizontal_alignment. So it should pass without changes. Confirm with the full suite.

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Centre zone labels on shape span instead of fixed origin

Zone labels now compute their X position from the midpoint of the
zone's shape span, preventing overlap with shapes at position 0.
Label Z pushed to 1.5 to sit in front of shapes."
```

---

### Task 8: Increase grid spacing to accommodate metric labels

With column headers and row headers added, the grid needs more breathing room between rows and columns. The current `GridColumnSpacing = 1.5` and `GridRowSpacing = 2.0` may be too tight once labels are present.

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs:13-15`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Write failing test — grid spacing accommodates labels**

```csharp
[Fact]
public void Calculate_GridSpacing_WiderThanShapeWidth()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
    var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
    // Column spacing should be at least 2.0 to fit label text between columns
    Assert.True(perCpu.GridColumnSpacing >= 2.0f,
        $"Column spacing {perCpu.GridColumnSpacing} should be >= 2.0 for label clearance");
    // Row spacing should be at least 2.5 to fit row header labels
    Assert.True(perCpu.GridRowSpacing >= 2.5f,
        $"Row spacing {perCpu.GridRowSpacing} should be >= 2.5 for label clearance");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests --filter "GridSpacing_WiderThanShapeWidth" -v n`
Expected: FAIL — current spacing is 1.5 / 2.0

- [ ] **Step 3: Increase spacing constants**

In `LayoutCalculator.cs`:

```csharp
private const float GridColumnSpacing  = 2.0f;
private const float GridRowSpacing     = 2.5f;
```

- [ ] **Step 4: Run test to verify it passes, then full suite**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS (check no hardcoded spacing assertions break)

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "Widen grid spacing to accommodate metric and instance labels

Column spacing 1.5 -> 2.0, row spacing 2.0 -> 2.5 to provide
clearance for the column header and row header Label3D nodes
added in the previous commit."
```

---

## Chunk 5: Archive Playback Restoration

### Task 9: Investigate and fix archive playback startup

The `metric_scene_controller.gd` has `default_scene = "res://scenes/test_bars.tscn"`. When the user generates `host-view.tscn` with the host projector and places it in `godot-project/scenes/`, they need to update `default_scene` to point to it.

The archive playback logic in `_apply_launch_settings()` looks correct — it calls `StartPlayback` with a timestamp 24h ago when no timestamp is configured. But the issue may be:

1. The generated scene path doesn't match `default_scene`
2. The `pmview/mode` ProjectSetting defaults to `0` (Archive) but `StartPlayback` is async and `DiscoverArchiveMetadata` needs the metric names to already be set (from `_start_polling_metrics`)
3. Timing: `_start_polling_metrics` calls `StartPolling` (which sets up a timer), then `_apply_launch_settings` calls `StartPlayback` which is async. The order matters.

**Files:**
- Modify: `godot-project/scripts/scenes/metric_scene_controller.gd`

- [ ] **Step 1: Review the _ready() execution order**

Current order in `_ready()`:
1. `_read_launch_settings()` — reads ProjectSettings
2. Connects signals
3. `_load_scene_with_properties(default_scene)` — loads scene, binds, gets metric names
4. `_start_polling_metrics(metric_names)` — calls `UpdateMetricNames` then `StartPolling`
5. `_apply_launch_settings()` — calls `StartPlayback` (archive) or `ResetToLive`

The problem: `StartPolling` begins the live poll timer. Then `StartPlayback` switches to archive mode. But `StartPolling` may fire a live fetch before `StartPlayback`'s async `DiscoverArchiveMetadata` completes. This race condition could cause the poller to get stuck in a confused state.

**Critical detail from code review:** `MetricPoller.StartPlayback()` does NOT start the poll timer — it only discovers archive metadata and sets the time cursor mode. The poll timer (which drives `OnPollTimerTimeout` → `FetchHistoricalMetrics`) is started by `StartPolling()` → `ConnectToEndpoint()` → `StartPollTimer()`. So `StartPolling` MUST be called for both archive and live modes — the poll timer is what actually fetches data in either mode.

**Fix:** Call `StartPolling` first to establish the connection and start the timer, THEN call `StartPlayback` to switch to archive mode. The first timer tick may do a live fetch, but once `StartPlayback` completes, subsequent ticks will use the archive path. This is effectively what the current code does, so the real fix is about the `default_scene` path and ensuring the launch settings are applied correctly.

- [ ] **Step 2: Fix _ready() to ensure correct startup sequence**

The key changes:
1. Always call `StartPolling` to establish connection and start the timer
2. Then call `StartPlayback` (archive) or `ResetToLive` to set the mode
3. Fix `default_scene` to point to generated host-view scene

```gdscript
func _ready() -> void:
	_read_launch_settings()
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	scene_binder.connect("BindingError", _on_binding_error)

	_update_status_display()

	if default_scene != "":
		print("[MetricSceneController] Loading scene: %s" % default_scene)
		var metric_names = _load_scene_with_properties(default_scene)
		if metric_names.size() > 0:
			print("[MetricSceneController] Found %d metrics from scene properties" % metric_names.size())
			_start_polling_metrics(metric_names)
		else:
			print("[MetricSceneController] No bindings found in scene: %s" % default_scene)

	_apply_launch_settings()
```

The `_start_polling_metrics` call is kept — it calls `UpdateMetricNames` + `StartPolling`, establishing the connection and starting the timer. Then `_apply_launch_settings` switches the mode:

```gdscript
func _apply_launch_settings() -> void:
	if _launch_endpoint != "http://localhost:44322":
		print("[MetricSceneController] Overriding endpoint: %s" % _launch_endpoint)
		metric_poller.set("Endpoint", _launch_endpoint)

	if _launch_mode == 0:  # Archive
		var timestamp = _launch_timestamp
		if timestamp == "":
			var now = Time.get_unix_time_from_system()
			var day_ago = now - 86400.0
			timestamp = Time.get_datetime_string_from_unix_time(int(day_ago)) + "Z"
			print("[MetricSceneController] Empty timestamp, using 24h ago: %s" % timestamp)

		metric_poller.call("SetPlaybackSpeed", _launch_speed)
		metric_poller.call("SetLoop", _launch_loop)
		metric_poller.call("StartPlayback", timestamp)
		print("[MetricSceneController] Archive mode: timestamp=%s speed=%.1f loop=%s" % [
			timestamp, _launch_speed, _launch_loop])
	elif _launch_mode == 1:  # Live
		metric_poller.call("ResetToLive")
		print("[MetricSceneController] Live mode: archive settings ignored")
```

- [ ] **Step 3: Ensure default_scene points to generated host-view scene**

Change the default to the expected output location of the host projector:

```gdscript
@export var default_scene: String = "res://scenes/host-view.tscn"
```

> **Note:** The user needs to regenerate the scene with `dotnet run --project src/pmview-host-projector -o godot-project/scenes/host-view.tscn` after the colour/label/bezel fixes are in place.

- [ ] **Step 4: Commit**

```bash
git add godot-project/scripts/scenes/metric_scene_controller.gd
git commit -m "Fix archive playback: don't start live polling in archive mode

_ready() was calling StartPolling (live timer) then StartPlayback
(archive async), creating a race. Now UpdateMetricNames is set first,
then _apply_launch_settings handles starting either archive playback
or live polling. Default scene changed to host-view.tscn."
```

---

## Chunk 6: Regenerate & Verify

### Task 10: Regenerate the host-view scene and verify

After all the above changes, regenerate the scene to pick up colours, labels, and bezels.

**Files:**
- No code changes — this is a verification step.

- [ ] **Step 1: Run full test suite**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests -v n`
Expected: All PASS

- [ ] **Step 2: Regenerate the host-view scene**

Run the host projector against the dev environment pmproxy:

```bash
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
    --pmproxy http://localhost:44322 \
    -o godot-project/scenes/host-view.tscn
```

- [ ] **Step 3: Verify generated .tscn contains expected elements**

Quick sanity checks on the output:
- `grep "colour = Color" godot-project/scenes/host-view.tscn` — should find coloured shapes
- `grep "Ground" godot-project/scenes/host-view.tscn` — should find ground bezels
- `grep "albedo_color = Color(0.15" godot-project/scenes/host-view.tscn` — dark grey bezels
- `grep "Label3D" godot-project/scenes/host-view.tscn` — should find many labels

- [ ] **Step 4: Open in Godot and visually verify**

User opens `main.tscn` (which loads `host-view.tscn`), checks:
- Shapes are coloured per profile (orange CPU, indigo Load, green Memory, etc.)
- No shapes overlapping labels
- Dark grey ground bezels under each zone group
- Metric labels visible on foreground shapes and grid column/row headers
- Archive playback starts and advances through historical data

- [ ] **Step 5: Commit the generated scene**

```bash
git add godot-project/scenes/host-view.tscn
git commit -m "Regenerate host-view scene with colours, bezels, and labels"
```

---

## Summary of Changes

| Task | What | Where | TDD? |
|------|------|-------|------|
| 1 | Emit shape colours | TscnWriter.cs | Yes |
| 2 | Skip Label3D in grid layout | grid_layout_3d.gd | No (GDScript) |
| 3 | Compute ground bezel extent | LayoutCalculator.cs, SceneLayout.cs | Yes |
| 4 | Emit ground bezel mesh | TscnWriter.cs | Yes |
| 5 | Foreground shape labels | TscnWriter.cs | Yes |
| 6 | Grid column/row header labels | TscnWriter.cs, LayoutCalculator.cs, SceneLayout.cs | Yes |
| 7 | Centre zone labels | TscnWriter.cs | Yes |
| 8 | Widen grid spacing | LayoutCalculator.cs | Yes |
| 9 | Fix archive playback startup | metric_scene_controller.gd | No (GDScript) |
| 10 | Regenerate & verify | (none — verification) | N/A |

**Dependency order:** Tasks 1-2 are independent. Task 3 before 4. Task 2 before 6 (grid must skip Label3D). Task 7 updates existing label test. Task 8 is independent. Task 9 is independent. Task 10 is last.
