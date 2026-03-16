# macOS First-Class Citizen Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give macOS its own metric profile with a proper memory zone, ghost placeholders for missing network aggregates, and shared zone infrastructure to avoid duplication.

**Architecture:** Three layers of work: (1) add `IsPlaceholder` flag through the model→layout→emission pipeline with ghost rendering in GDScript, (2) extract shared zones from `LinuxProfile` into a common helper so both profiles can reuse them, (3) build `MacOsProfile` with macOS-specific memory metrics and ghost network aggregate zones.

**Tech Stack:** C# (.NET 8.0 for models, .NET 10.0 for tests), GDScript for building blocks, xUnit for testing.

---

## File Structure

### Files to Create

| File | Responsibility |
|------|---------------|
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/SharedZones.cs` | Shared zone definitions + colour palette used by both Linux and macOS profiles |

### Files to Modify

| File | Change |
|------|--------|
| `src/pmview-host-projector/src/PmviewHostProjector/Models/ZoneDefinition.cs` | Add `IsPlaceholder` to `MetricShapeMapping` |
| `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs` | Add `IsPlaceholder` to `PlacedShape` |
| `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` | Thread `IsPlaceholder` through `BuildShape` |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs` | Skip binding emission for placeholders, emit ghost properties |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` | Delegate to `SharedZones` for shared zone methods + colours |
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/MacOsProfile.cs` | Full macOS profile with memory zone + ghost network zones |
| `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_shape.gd` | Add `ghost` export property for transparency + desaturation |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs` | Verify LinuxProfile still works after SharedZones extraction |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/HostProfileProviderTests.cs` | Update macOS zone expectations |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` | Test placeholder propagation |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs` | Test ghost shape emission |

---

## Chunk 1: Ghost Shape Mechanism (Model + Layout + Emission)

### Task 1: Add `IsPlaceholder` to `MetricShapeMapping`

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Models/ZoneDefinition.cs:25-36`

- [ ] **Step 1: Add `IsPlaceholder` parameter to `MetricShapeMapping` record**

```csharp
public record MetricShapeMapping(
    string MetricName,
    ShapeType Shape,
    string Label,
    RgbColour DefaultColour,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax,
    string? InstanceName = null,
    Vec3? Position = null,
    LabelPlacement LabelPlacement = LabelPlacement.Front,
    bool IsPlaceholder = false);
```

- [ ] **Step 2: Build to verify no compilation errors**

Run: `dotnet build pmview-nextgen.sln`
Expected: Build succeeds — default `false` means all existing callers are unaffected.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Models/ZoneDefinition.cs
git commit -m "Add IsPlaceholder flag to MetricShapeMapping for ghost shapes"
```

---

### Task 2: Add `IsPlaceholder` to `PlacedShape` and thread through `BuildShape`

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs:15-27`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` (`BuildShape` method)
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Write failing test — placeholder flag propagates through layout**

Add to `LayoutCalculatorForegroundTests`:

```csharp
[Fact]
public void Calculate_PlaceholderMetric_PropagatesIsPlaceholderToPlacedShape()
{
    var zone = new ZoneDefinition(
        Name: "Test", Row: ZoneRow.Foreground, Type: ZoneType.Aggregate,
        Metrics:
        [
            new("m.live", ShapeType.Bar, "Live", new RgbColour(0, 1, 0), 0f, 100f, 0.2f, 5.0f),
            new("m.ghost", ShapeType.Bar, "Ghost", new RgbColour(0, 0, 1), 0f, 100f, 0.2f, 5.0f,
                IsPlaceholder: true),
        ]);

    var layout = LayoutCalculator.Calculate([zone], MakeTopology());
    var placed = layout.Zones.Single();
    var shapes = placed.Shapes;

    Assert.False(shapes.Single(s => s.NodeName.Contains("Live")).IsPlaceholder);
    Assert.True(shapes.Single(s => s.NodeName.Contains("Ghost")).IsPlaceholder);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~PlaceholderMetric_Propagates"`
Expected: FAIL — `PlacedShape` does not have `IsPlaceholder` property.

- [ ] **Step 3: Add `IsPlaceholder` to `PlacedShape` record**

In `SceneLayout.cs`, add to the end of `PlacedShape`:

```csharp
public record PlacedShape(
    string NodeName,
    ShapeType Shape,
    Vec3 LocalPosition,
    string MetricName,
    string? InstanceName,
    string? DisplayLabel,
    RgbColour Colour,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax,
    LabelPlacement LabelPlacement = LabelPlacement.Front,
    bool IsPlaceholder = false) : PlacedItem(LocalPosition);
```

- [ ] **Step 4: Thread `IsPlaceholder` through `LayoutCalculator.BuildShape`**

In `LayoutCalculator.cs`, update `BuildShape` to include the flag:

```csharp
private static PlacedShape BuildShape(string zoneName, MetricShapeMapping metric, Vec3 localPos, HostTopology topology) =>
    new(
        NodeName:       SanitiseNodeName($"{zoneName}_{metric.Label}"),
        Shape:          metric.Shape,
        LocalPosition:  localPos,
        MetricName:     metric.MetricName,
        InstanceName:   metric.InstanceName,
        DisplayLabel:   metric.Label,
        Colour:         metric.DefaultColour,
        SourceRangeMin: metric.SourceRangeMin,
        SourceRangeMax: ResolveSourceRangeMax(metric, topology),
        TargetRangeMin: metric.TargetRangeMin,
        TargetRangeMax: metric.TargetRangeMax,
        LabelPlacement: metric.LabelPlacement,
        IsPlaceholder:  metric.IsPlaceholder);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~PlaceholderMetric_Propagates"`
Expected: PASS

- [ ] **Step 6: Run full test suite**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass — default `false` means no behaviour change for existing shapes.

- [ ] **Step 7: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Models/SceneLayout.cs \
        src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "Thread IsPlaceholder from MetricShapeMapping through PlacedShape"
```

---

### Task 3: Skip PcpBindingResource emission for placeholder shapes in TscnWriter

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — placeholder shapes emit no binding**

Add to `TscnWriterTests`:

```csharp
[Fact]
public void Write_PlaceholderShape_EmitsNoBindingOrPcpBindable()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone(
            Name: "Net", ZoneLabel: "Net-In", Position: Vec3.Zero,
            ColumnSpacing: null, RowSpacing: null,
            Items: [new PlacedShape("Net_Bytes", ShapeType.Bar, Vec3.Zero,
                "network.all.in.bytes", null, "Bytes",
                new RgbColour(0.5f, 0.5f, 0.5f),
                0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true)])
    ]);
    var tscn = TscnWriter.Write(layout);

    // Shape node should exist
    Assert.Contains("[node name=\"Net_Bytes\"", tscn);
    // Ghost property should be emitted
    Assert.Contains("ghost = true", tscn);
    // No PcpBindable child
    Assert.DoesNotContain("PcpBindable", tscn.Split("Net_Bytes\"")[1].Split("[node name=")[0]);
    // No sub_resource for this shape's binding
    Assert.DoesNotContain("binding_Net_Bytes", tscn);
}
```

- [ ] **Step 2: Write failing test — placeholder shapes don't inflate load_steps**

```csharp
[Fact]
public void Write_PlaceholderShape_DoesNotInflateLoadSteps()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone(
            Name: "Net", ZoneLabel: "Net-In", Position: Vec3.Zero,
            ColumnSpacing: null, RowSpacing: null,
            Items: [new PlacedShape("Net_Bytes", ShapeType.Bar, Vec3.Zero,
                "network.all.in.bytes", null, "Bytes",
                new RgbColour(0.5f, 0.5f, 0.5f),
                0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true)])
    ]);
    var tscn = TscnWriter.Write(layout);

    // ext_resources (9): controller, metric_poller, scene_binder,
    //                     metric_group, metric_grid, ground_bezel,
    //                     bar_scene, bindable_script, binding_res_script
    // sub_resources (0): placeholder has no binding
    // ambient (2): TimestampLabel, HostnameLabel
    // = 9 + 0 + 2 = 11
    Assert.Contains("load_steps=11 ", tscn);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~PlaceholderShape"`
Expected: FAIL — placeholders still emit bindings.

- [ ] **Step 4: Guard `CollectSubResources` to skip placeholders**

In `TscnWriter.cs`, inside `CollectSubResources`, after the `foreach (var shape in shapes)` line, add a placeholder guard:

```csharp
foreach (var shape in shapes)
{
    if (shape.IsPlaceholder)
    {
        // Still register the scene resource (bar/cylinder) but skip binding resources.
        var sceneId = SceneExtResourceId(shape.Shape);
        registry.Require(sceneId, "PackedScene", SceneExtResourcePath(shape.Shape));
        continue;
    }

    var sceneId2 = SceneExtResourceId(shape.Shape);
    registry.Require(sceneId2, "PackedScene", SceneExtResourcePath(shape.Shape));
    registry.Require("bindable_script", "Script", "res://addons/pmview-bridge/PcpBindable.cs");
    registry.Require("binding_res_script", "Script", "res://addons/pmview-bridge/PcpBindingResource.cs");

    list.Add(new SubResourceEntry(
        Id: SubResourceId(shape.NodeName),
        MetricName: shape.MetricName,
        InstanceName: shape.InstanceName,
        SourceRangeMin: shape.SourceRangeMin,
        SourceRangeMax: shape.SourceRangeMax,
        TargetRangeMin: shape.TargetRangeMin,
        TargetRangeMax: shape.TargetRangeMax));
}
```

- [ ] **Step 5: Guard `WriteShape` to skip PcpBindable for placeholders, emit ghost property**

In `TscnWriter.cs`, update `WriteShape`:

```csharp
private static void WriteShape(StringBuilder sb, PlacedShape shape,
    ExtResourceRegistry registry, List<SubResourceEntry> subResources,
    string parentOverride)
{
    var sceneId = SceneExtResourceId(shape.Shape);
    var shapePath = $"{parentOverride}/{shape.NodeName}";

    sb.AppendLine($"[node name=\"{shape.NodeName}\" parent=\"{parentOverride}\" instance=ExtResource(\"{sceneId}\")]");
    sb.AppendLine($"colour = Color({F(shape.Colour.R)}, {F(shape.Colour.G)}, {F(shape.Colour.B)}, 1)");

    if (shape.IsPlaceholder)
    {
        sb.AppendLine("ghost = true");
        sb.AppendLine();
        return;
    }

    sb.AppendLine();

    var subResId = SubResourceId(shape.NodeName);
    sb.AppendLine($"[node name=\"PcpBindable\" type=\"Node\" parent=\"{shapePath}\"]");
    sb.AppendLine("script = ExtResource(\"bindable_script\")");
    sb.AppendLine($"PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(\"{subResId}\")])");

    sb.AppendLine();
}
```

- [ ] **Step 6: Run placeholder tests to verify they pass**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~PlaceholderShape"`
Expected: PASS

- [ ] **Step 7: Run full test suite**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Skip binding emission for placeholder shapes, emit ghost property"
```

---

### Task 4: Add `ghost` export property to `GroundedShape.gd`

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_shape.gd`

Note: GDScript building blocks are tested visually in Godot (not in the xUnit suite). No automated test for this step.

- [ ] **Step 1: Add `ghost` property with transparency + desaturation**

```gdscript
@tool
class_name GroundedShape
extends Node3D

## Height of the shape in world units. Maps to scale.y.
## Child mesh at y=0.5 means scaling Y grows upward from Y=0.
@export var height: float = 1.0:
	set(value):
		height = maxf(value, 0.01)
		scale.y = height

## Colour applied to the mesh material.
@export var colour: Color = Color.WHITE:
	set(value):
		colour = value
		_apply_colour()

## Ghost mode: desaturates to grey and applies transparency.
## Used for placeholder shapes where the metric is unavailable.
@export var ghost: bool = false:
	set(value):
		ghost = value
		_apply_colour()

func _ready() -> void:
	scale.y = height
	_apply_colour()

func _apply_colour() -> void:
	var mesh_instance := _find_mesh_instance()
	if mesh_instance == null:
		return
	var mat := mesh_instance.get_surface_override_material(0)
	if mat == null:
		mat = StandardMaterial3D.new()
		mesh_instance.set_surface_override_material(0, mat)
	if mat is StandardMaterial3D:
		if ghost:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.albedo_color = Color(0.5, 0.5, 0.5, 0.25)
		else:
			mat.transparency = BaseMaterial3D.TRANSPARENCY_DISABLED
			mat.albedo_color = colour

func _find_mesh_instance() -> MeshInstance3D:
	for child in get_children():
		if child is MeshInstance3D:
			return child
	return null
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/building_blocks/grounded_shape.gd
git commit -m "Add ghost property to GroundedShape for placeholder transparency"
```

---

## Chunk 2: SharedZones Extraction + macOS Profile

### Task 5: Extract shared zones and colour palette from LinuxProfile

**Files:**
- Create: `src/pmview-host-projector/src/PmviewHostProjector/Profiles/SharedZones.cs`
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/LinuxProfileTests.cs`

- [ ] **Step 1: Run existing LinuxProfile tests to establish baseline**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~LinuxProfile"`
Expected: All pass.

- [ ] **Step 2: Create `SharedZones.cs` with colour palette and shared zone methods**

Extract the 7 zone methods and all colour constants that are shared between Linux and macOS. The methods that stay private to `LinuxProfile` are `MemoryZone()` (Linux-specific metrics) and `NetworkInAggregateZone()` / `NetworkOutAggregateZone()` (which macOS will ghost).

```csharp
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Profiles;

/// <summary>
/// Zone definitions and colour palette shared across platform profiles.
/// Both LinuxProfile and MacOsProfile delegate here for zones with
/// identical PCP metric names on both platforms.
/// </summary>
internal static class SharedZones
{
    // --- Colour Palette (Tailwind CSS) ---
    internal static readonly RgbColour Orange    = RgbColour.FromHex("#f97316");
    internal static readonly RgbColour Indigo    = RgbColour.FromHex("#6366f1");
    internal static readonly RgbColour Green     = RgbColour.FromHex("#22c55e");
    internal static readonly RgbColour Amber     = RgbColour.FromHex("#f59e0b");
    internal static readonly RgbColour DarkGreen = RgbColour.FromHex("#16a34a");
    internal static readonly RgbColour Blue      = RgbColour.FromHex("#3b82f6");
    internal static readonly RgbColour Rose      = RgbColour.FromHex("#f43f5e");
    internal static readonly RgbColour Red       = RgbColour.FromHex("#ef4444");
    internal static readonly RgbColour Cyan      = RgbColour.FromHex("#22d3ee");

    // --- Shared Zone Definitions ---

    internal static ZoneDefinition CpuZone() => new(
        Name: "CPU",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("kernel.all.cpu.sys",  ShapeType.Bar, "Sys",  Red,   0f, 100f, 0.2f, 5.0f),
            new("kernel.all.cpu.user", ShapeType.Bar, "User", Green, 0f, 100f, 0.2f, 5.0f),
            new("kernel.all.cpu.nice", ShapeType.Bar, "Nice", Cyan,  0f, 100f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null,
        StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]);

    internal static ZoneDefinition LoadZone() => new(
        Name: "Load",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("kernel.all.load", ShapeType.Bar, "1m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "1 minute"),
            new("kernel.all.load", ShapeType.Bar, "5m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "5 minute"),
            new("kernel.all.load", ShapeType.Bar, "15m", Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "15 minute"),
        ],
        InstanceMetricSource: null);

    internal static ZoneDefinition DiskTotalsZone() => new(
        Name: "Disk",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("disk.all.read_bytes",  ShapeType.Cylinder, "Read",  Amber, 0f, 500_000_000f, 0.2f, 5.0f),
            new("disk.all.write_bytes", ShapeType.Cylinder, "Write", Amber, 0f, 500_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    internal static ZoneDefinition PerCpuZone() => new(
        Name: "Per-CPU",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("kernel.percpu.cpu.sys",  ShapeType.Bar, "Sys",  Red,   0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.user", ShapeType.Bar, "User", Green, 0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.nice", ShapeType.Bar, "Nice", Cyan,  0f, 100f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "kernel.percpu.cpu.user",
        StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]);

    internal static ZoneDefinition PerDiskZone() => new(
        Name: "Per-Disk",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("disk.dev.read_bytes",  ShapeType.Cylinder, "Read",  DarkGreen, 0f, 500_000_000f, 0.2f, 5.0f),
            new("disk.dev.write_bytes", ShapeType.Cylinder, "Write", DarkGreen, 0f, 500_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "disk.dev.read_bytes");

    internal static ZoneDefinition NetworkInZone() => new(
        Name: "Network In",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("network.interface.in.bytes",   ShapeType.Bar, "Bytes",  Blue, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.interface.in.packets", ShapeType.Bar, "Pkts",   Blue, 0f, 100_000f,     0.2f, 5.0f),
            new("network.interface.in.errors",  ShapeType.Bar, "Errors", Red,  0f, 100f,         0.2f, 5.0f),
        ],
        InstanceMetricSource: "network.interface.in.bytes");

    internal static ZoneDefinition NetworkOutZone() => new(
        Name: "Network Out",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("network.interface.out.bytes",   ShapeType.Bar, "Bytes",  Rose, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.interface.out.packets", ShapeType.Bar, "Pkts",   Rose, 0f, 100_000f,     0.2f, 5.0f),
            new("network.interface.out.errors",  ShapeType.Bar, "Errors", Red,  0f, 100f,         0.2f, 5.0f),
        ],
        InstanceMetricSource: "network.interface.out.bytes");
}
```

- [ ] **Step 3: Update `LinuxProfile.cs` to delegate to SharedZones**

```csharp
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Profiles;

public static class LinuxProfile
{
    public static IReadOnlyList<ZoneDefinition> GetZones() =>
    [
        SharedZones.CpuZone(),
        SharedZones.LoadZone(),
        MemoryZone(),
        SharedZones.DiskTotalsZone(),
        NetworkInAggregateZone(),
        NetworkOutAggregateZone(),
        SharedZones.PerCpuZone(),
        SharedZones.PerDiskZone(),
        SharedZones.NetworkInZone(),
        SharedZones.NetworkOutZone(),
    ];

    private static ZoneDefinition MemoryZone() => new(
        Name: "Memory",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("mem.util.used",   ShapeType.Bar, "Used",    SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.cached", ShapeType.Bar, "Cached",  SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.bufmem", ShapeType.Bar, "Buffers", SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkInAggregateZone() => new(
        Name: "Net-In",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.in.bytes",   ShapeType.Bar, "Bytes", SharedZones.Blue, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.in.packets", ShapeType.Bar, "Pkts",  SharedZones.Blue, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkOutAggregateZone() => new(
        Name: "Net-Out",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.out.bytes",   ShapeType.Bar, "Bytes", SharedZones.Rose, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.out.packets", ShapeType.Bar, "Pkts",  SharedZones.Rose, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);
}
```

- [ ] **Step 4: Run all LinuxProfile and LayoutCalculator tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass — no behavioural change, just structural extraction.

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Profiles/SharedZones.cs \
        src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs
git commit -m "Extract shared zones and colour palette from LinuxProfile

Both LinuxProfile and MacOsProfile can now reuse zone definitions for
CPU, Load, Disk, Per-CPU, Per-Disk, and Network per-interface zones."
```

---

### Task 6: Build macOS profile with memory zone and ghost network zones

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Profiles/MacOsProfile.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/MacOsProfileTests.cs` (new file)
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/HostProfileProviderTests.cs`

- [ ] **Step 1: Write failing tests for MacOsProfile**

Create a new test file `MacOsProfileTests.cs`:

```csharp
using Xunit;
using PmviewHostProjector.Models;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector.Tests.Profiles;

public class MacOsProfileTests
{
    private readonly IReadOnlyList<ZoneDefinition> _zones = MacOsProfile.GetZones();

    [Fact]
    public void GetZones_ReturnsTenZones()
    {
        Assert.Equal(10, _zones.Count);
    }

    [Fact]
    public void GetZones_HasSameZoneNamesAsLinux()
    {
        var linux = LinuxProfile.GetZones().Select(z => z.Name);
        var macOs = _zones.Select(z => z.Name);
        Assert.Equal(linux, macOs);
    }

    [Fact]
    public void MemoryZone_HasFourMetrics_WiredActiveInactiveCompressed()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.Equal(4, memory.Metrics.Count);
        var labels = memory.Metrics.Select(m => m.Label).ToList();
        Assert.Equal(new[] { "Wired", "Active", "Inactive", "Compressed" }, labels);
    }

    [Fact]
    public void MemoryZone_MetricNames_AreDarwinSpecific()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        var names = memory.Metrics.Select(m => m.MetricName).ToList();
        Assert.Contains("mem.util.wired", names);
        Assert.Contains("mem.util.active", names);
        Assert.Contains("mem.util.inactive", names);
        Assert.Contains("mem.util.compressed", names);
    }

    [Fact]
    public void MemoryZone_SourceRangeMaxIsZero_ForPhysmemResolution()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.All(memory.Metrics, m => Assert.Equal(0f, m.SourceRangeMax));
    }

    [Fact]
    public void NetInAggregate_AllMetricsArePlaceholders()
    {
        var netIn = _zones.Single(z => z.Name == "Net-In");
        Assert.All(netIn.Metrics, m => Assert.True(m.IsPlaceholder,
            $"{m.MetricName} should be a placeholder on macOS"));
    }

    [Fact]
    public void NetOutAggregate_AllMetricsArePlaceholders()
    {
        var netOut = _zones.Single(z => z.Name == "Net-Out");
        Assert.All(netOut.Metrics, m => Assert.True(m.IsPlaceholder,
            $"{m.MetricName} should be a placeholder on macOS"));
    }

    [Fact]
    public void CpuZone_IsIdenticalToLinux()
    {
        var macCpu = _zones.Single(z => z.Name == "CPU");
        var linuxCpu = LinuxProfile.GetZones().Single(z => z.Name == "CPU");
        Assert.Equal(linuxCpu.Metrics.Count, macCpu.Metrics.Count);
        Assert.NotNull(macCpu.StackGroups);
    }

    [Fact]
    public void SharedZones_AreNotPlaceholders()
    {
        var sharedZoneNames = new[] { "CPU", "Load", "Disk", "Per-CPU", "Per-Disk", "Network In", "Network Out" };
        foreach (var name in sharedZoneNames)
        {
            var zone = _zones.Single(z => z.Name == name);
            Assert.All(zone.Metrics, m => Assert.False(m.IsPlaceholder,
                $"{name}/{m.MetricName} should not be a placeholder"));
        }
    }

    [Fact]
    public void LinuxProfile_HasNoPlaceholders()
    {
        var linux = LinuxProfile.GetZones();
        foreach (var zone in linux)
            Assert.All(zone.Metrics, m => Assert.False(m.IsPlaceholder,
                $"Linux {zone.Name}/{m.MetricName} should never be a placeholder"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~MacOsProfileTests"`
Expected: Multiple failures — MacOsProfile currently delegates to LinuxProfile.

- [ ] **Step 3: Implement MacOsProfile**

```csharp
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Profiles;

public static class MacOsProfile
{
    public static IReadOnlyList<ZoneDefinition> GetZones() =>
    [
        SharedZones.CpuZone(),
        SharedZones.LoadZone(),
        MemoryZone(),
        SharedZones.DiskTotalsZone(),
        NetworkInAggregateGhostZone(),
        NetworkOutAggregateGhostZone(),
        SharedZones.PerCpuZone(),
        SharedZones.PerDiskZone(),
        SharedZones.NetworkInZone(),
        SharedZones.NetworkOutZone(),
    ];

    private static ZoneDefinition MemoryZone() => new(
        Name: "Memory",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("mem.util.wired",      ShapeType.Bar, "Wired",      SharedZones.Red,   0f, 0f, 0.2f, 5.0f),
            new("mem.util.active",     ShapeType.Bar, "Active",     SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.inactive",   ShapeType.Bar, "Inactive",   SharedZones.Amber, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.compressed", ShapeType.Bar, "Compressed", SharedZones.Blue,  0f, 0f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkInAggregateGhostZone() => new(
        Name: "Net-In",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.in.bytes",   ShapeType.Bar, "Bytes", SharedZones.Blue, 0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true),
            new("network.all.in.packets", ShapeType.Bar, "Pkts",  SharedZones.Blue, 0f, 100_000f,     0.2f, 5.0f,
                IsPlaceholder: true),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkOutAggregateGhostZone() => new(
        Name: "Net-Out",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.out.bytes",   ShapeType.Bar, "Bytes", SharedZones.Rose, 0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true),
            new("network.all.out.packets", ShapeType.Bar, "Pkts",  SharedZones.Rose, 0f, 100_000f,     0.2f, 5.0f,
                IsPlaceholder: true),
        ],
        InstanceMetricSource: null);
}
```

- [ ] **Step 4: Update `HostProfileProviderTests` — macOS now has different memory zone**

The test `GetProfile_MacOs_HasSameZoneNamesAsLinux` should still pass (zone names match). Update `GetProfile_MacOs_ReturnsTenZones` if needed — it should still return 10 zones.

- [ ] **Step 5: Run all tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Profiles/MacOsProfile.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Profiles/MacOsProfileTests.cs
git commit -m "Build macOS profile with Darwin memory metrics and ghost network zones

Memory zone: Wired/Active/Inactive/Compressed (Darwin-specific).
Net-In/Net-Out: ghost placeholders (network.all.* absent on Darwin).
All other zones shared with Linux via SharedZones."
```

---

## Chunk 3: Integration Verification

### Task 7: End-to-end verification — macOS layout produces valid scene with ghosts

**Files:**
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write integration test — macOS scene has ghost shapes and no ghost bindings**

```csharp
[Fact]
public void Write_MacOsLayout_GhostNetworkShapes_EmitGhostPropertyAndNoBindings()
{
    var topology = new HostTopology(HostOs.MacOs, "macbook",
        ["cpu0", "cpu1"], ["disk0"], ["en0"],
        PhysicalMemoryBytes: 16_000_000_000L);
    var zones = MacOsProfile.GetZones();
    var layout = LayoutCalculator.Calculate(zones, topology);
    var tscn = TscnWriter.Write(layout);

    // Ghost shapes should have ghost = true
    Assert.Contains("ghost = true", tscn);

    // Memory zone should have Darwin-specific metrics
    Assert.Contains("mem.util.wired", tscn);
    Assert.Contains("mem.util.compressed", tscn);

    // No binding for ghost metrics
    Assert.DoesNotContain("binding_Net_In_Bytes", tscn);
    Assert.DoesNotContain("binding_Net_Out_Bytes", tscn);

    // But real metrics should still have bindings
    Assert.Contains("kernel.all.cpu.sys", tscn);
    Assert.Contains("binding_CPU_Sys", tscn);
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName~MacOsLayout_Ghost"`
Expected: PASS (if all prior tasks are complete).

- [ ] **Step 3: Run full test suite**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "Add end-to-end test for macOS layout with ghost network shapes"
```

---

## Summary

| Task | What | Files Changed |
|------|------|--------------|
| 1 | `IsPlaceholder` on `MetricShapeMapping` | `ZoneDefinition.cs` |
| 2 | `IsPlaceholder` on `PlacedShape` + `BuildShape` | `SceneLayout.cs`, `LayoutCalculator.cs`, tests |
| 3 | Ghost emission in `TscnWriter` | `TscnWriter.cs`, tests |
| 4 | `ghost` property in `GroundedShape.gd` | `grounded_shape.gd` |
| 5 | Extract `SharedZones` from `LinuxProfile` | `SharedZones.cs` (new), `LinuxProfile.cs` |
| 6 | Build `MacOsProfile` | `MacOsProfile.cs`, `MacOsProfileTests.cs` (new) |
| 7 | End-to-end integration test | `TscnWriterTests.cs` |
