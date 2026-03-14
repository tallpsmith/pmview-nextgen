# Scene Layout Polish Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganise the host-view scene layout (System zone merge, network aggregates, aligned columns), increase font sizes, tighten spacing, and add a neon timestamp floor label and a floating film-title hostname — both driven by a new text-binding pipeline in the bridge addon.

**Architecture:** Three independent streams: (1) profile/layout/font changes in the host-projector (pure .NET, xUnit-tested); (2) ambient label emission in TscnWriter (pure .NET, xUnit-tested); (3) text-binding pipeline in the Godot bridge addon (C#, GdUnit4, requires Godot to run). Stream 3 depends on stream 2 for the binding IDs emitted in `.tscn` files.

**Tech Stack:** C# (.NET 8/10), xUnit (host-projector tests), GdUnit4 (addon tests), Godot 4 `.tscn` format

---

## Test commands

```bash
# Host-projector tests (VM-runnable)
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal

# Full solution (VM-runnable, no integration)
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" -v normal
```

> **Note:** Addon tests (`src/pmview-bridge-addon/`) use GdUnit4 and require the Godot editor. Write the tests; user runs them in Godot. Mark those steps with `[Godot]`.

---

## File map

| File | Change |
|------|--------|
| `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs` | Replace 3 zones with SystemZone (9 metrics); add Net-In/Net-Out aggregate foreground zones; reorder |
| `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs` | ZoneGap 3.0→2.0, ShapeSpacing 1.5→1.2 |
| `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs` | Font size increases; emit TimestampLabel + HostnameLabel with text bindings; set Hostname on MetricPoller |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs` | Delete 4 obsolete tests; add replacements + 6 new tests for System zone and Net aggregate zones |
| `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs` | Update font_size assertions; add 7 tests for ambient label emission |
| `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs` | Add `Hostname` export; inject `pmview.meta.*` virtual metrics on every emit cycle |
| `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs` | Detect `TargetProperty == "text"` bindings; route to `ApplyTextMetric` instead of numeric path |
| `src/pmview-bridge-addon/test/SceneBinderTests.cs` | Add tests for text binding extraction and MetricPoller virtual metrics `[Godot]` |

---

## Chunk 1: Zone Profile & Layout Constants

### Task 1: Rewrite LinuxProfile zones

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Delete the four tests that will break after the rewrite**

Remove these methods from `LayoutCalculatorForegroundTests` in `LayoutCalculatorTests.cs`:
- `Calculate_CpuForegroundZone_HasThreeShapes`
- `Calculate_LoadZone_ShapesCarryInstanceNames`
- `Calculate_MemoryZone_SourceRangeMaxSetFromPhysmem`
- `Calculate_ForegroundZone_HasGroundExtent`

- [ ] **Step 2: Write the failing replacement and new tests**

Add all of the following to `LayoutCalculatorForegroundTests`:

```csharp
[Fact]
public void Calculate_SystemZone_HasNineShapes()
{
    // CPU(3) + Load(3) + Memory(3) = 9 shapes in the merged System zone.
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var system = layout.Zones.Single(z => z.Name == "System");
    Assert.Equal(9, system.Shapes.Count);
}

[Fact]
public void Calculate_SystemZone_IsForeground_AtZZero()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var system = layout.Zones.Single(z => z.Name == "System");
    Assert.Equal(0f, system.Position.Z);
    Assert.Null(system.GridColumns);
}

[Fact]
public void Calculate_SystemZone_LoadShapes_CarryInstanceNames()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var system = layout.Zones.Single(z => z.Name == "System");
    var loadInstances = system.Shapes
        .Where(s => s.MetricName == "kernel.all.load")
        .Select(s => s.InstanceName)
        .ToList();
    Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, loadInstances);
}

[Fact]
public void Calculate_SystemZone_MemoryShapes_SourceRangeMaxFromPhysmem()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var system = layout.Zones.Single(z => z.Name == "System");
    var memShapes = system.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();
    Assert.Equal(3, memShapes.Count);
    Assert.All(memShapes, s => Assert.Equal(16_000_000_000f, s.SourceRangeMax));
}

[Fact]
public void Calculate_SystemZone_HasGroundExtent()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var system = layout.Zones.Single(z => z.Name == "System");
    // 9 shapes — ground must span them all + padding
    Assert.True(system.GroundWidth > 9f,
        $"GroundWidth {system.GroundWidth} should be > 9 for a 9-bar zone");
    Assert.True(system.GroundDepth > 0f);
}

[Fact]
public void Calculate_ForegroundZoneOrder_IsSystemDiskNetInNetOut()
{
    // Foreground zones ordered left-to-right: System, Disk, Net-In, Net-Out
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var foreground = layout.Zones
        .Where(z => z.GridColumns == null)
        .OrderBy(z => z.Position.X)
        .Select(z => z.Name)
        .ToList();
    Assert.Equal(new[] { "System", "Disk", "Net-In", "Net-Out" }, foreground);
}

[Fact]
public void Calculate_NetInAggregateZone_IsForeground_HasTwoShapes()
{
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    // Net-In foreground zone (not the per-instance background zone "Network In")
    var netIn = layout.Zones.Single(z => z.Name == "Net-In" && z.GridColumns == null);
    Assert.Equal(0f, netIn.Position.Z);
    Assert.Equal(2, netIn.Shapes.Count);
}
```

- [ ] **Step 3: Run tests — confirm failures**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | grep -E "FAIL|passed|failed"
```

Expected: the 7 new tests fail with `InvalidOperationException: Sequence contains no matching element` (no "System" or "Net-In" foreground zone yet).

- [ ] **Step 4: Rewrite LinuxProfile**

Replace the full `GetZones()` and zone factory methods. Keep `DiskTotalsZone()`, `PerCpuZone()`, `PerDiskZone()`, `NetworkInZone()`, `NetworkOutZone()` unchanged. Remove `CpuZone()`, `LoadZone()`, `MemoryZone()`.

```csharp
public static IReadOnlyList<ZoneDefinition> GetZones() =>
[
    SystemZone(),
    DiskTotalsZone(),
    NetworkInAggregateZone(),
    NetworkOutAggregateZone(),
    PerCpuZone(),
    PerDiskZone(),
    NetworkInZone(),
    NetworkOutZone(),
];

private static ZoneDefinition SystemZone() => new(
    Name: "System",
    Row: ZoneRow.Foreground,
    Type: ZoneType.Aggregate,
    Metrics:
    [
        new("kernel.all.cpu.user", ShapeType.Bar, "User",    Orange, 0f, 100f, 0.2f, 5.0f),
        new("kernel.all.cpu.sys",  ShapeType.Bar, "Sys",     Orange, 0f, 100f, 0.2f, 5.0f),
        new("kernel.all.cpu.nice", ShapeType.Bar, "Nice",    Orange, 0f, 100f, 0.2f, 5.0f),
        new("kernel.all.load", ShapeType.Bar, "1m",  Indigo, 0f, 10f, 0.2f, 5.0f, InstanceName: "1 minute"),
        new("kernel.all.load", ShapeType.Bar, "5m",  Indigo, 0f, 10f, 0.2f, 5.0f, InstanceName: "5 minute"),
        new("kernel.all.load", ShapeType.Bar, "15m", Indigo, 0f, 10f, 0.2f, 5.0f, InstanceName: "15 minute"),
        new("mem.util.used",   ShapeType.Bar, "Used",    Green, 0f, 0f, 0.2f, 5.0f),
        new("mem.util.cached", ShapeType.Bar, "Cached",  Green, 0f, 0f, 0.2f, 5.0f),
        new("mem.util.bufmem", ShapeType.Bar, "Buffers", Green, 0f, 0f, 0.2f, 5.0f),
    ],
    InstanceMetricSource: null);

private static ZoneDefinition NetworkInAggregateZone() => new(
    Name: "Net-In",
    Row: ZoneRow.Foreground,
    Type: ZoneType.Aggregate,
    Metrics:
    [
        new("network.all.in.bytes",   ShapeType.Bar, "Bytes", Blue, 0f, 125_000_000f, 0.2f, 5.0f),
        new("network.all.in.packets", ShapeType.Bar, "Pkts",  Blue, 0f, 100_000f,     0.2f, 5.0f),
    ],
    InstanceMetricSource: null);

private static ZoneDefinition NetworkOutAggregateZone() => new(
    Name: "Net-Out",
    Row: ZoneRow.Foreground,
    Type: ZoneType.Aggregate,
    Metrics:
    [
        new("network.all.out.bytes",   ShapeType.Bar, "Bytes", Rose, 0f, 125_000_000f, 0.2f, 5.0f),
        new("network.all.out.packets", ShapeType.Bar, "Pkts",  Rose, 0f, 100_000f,     0.2f, 5.0f),
    ],
    InstanceMetricSource: null);
```

- [ ] **Step 5: Run all tests — all pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | grep -E "FAIL|passed|failed"
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Profiles/LinuxProfile.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "merge CPU/Load/Mem into System zone; add Net-In/Out aggregate foreground zones"
```

---

### Task 2: Tighten spacing constants

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs`

- [ ] **Step 1: Write a failing test for the tighter inter-zone gap**

Add to `LayoutCalculatorForegroundTests`:

```csharp
[Fact]
public void Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive()
{
    // ZoneGap reduced from 3.0 to 2.0. The gap (empty space) between adjacent
    // foreground zones should now be <= 2.5. With old ZoneGap=3.0 it was ~3.0
    // so this test will fail until the constant is updated.
    var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
    var foreground = layout.Zones
        .Where(z => z.GridColumns == null)
        .OrderBy(z => z.Position.X)
        .ToList();

    for (int i = 0; i < foreground.Count - 1; i++)
    {
        var left  = foreground[i];
        var right = foreground[i + 1];
        // Zone position marks the start of its first shape at local X=0.
        // The zone's footprint ends at Position.X + max(shape.LocalPosition.X).
        var leftFootprintEnd = left.Position.X +
            (left.Shapes.Count > 0 ? left.Shapes.Max(s => s.LocalPosition.X) : 0f);
        var gap = right.Position.X - leftFootprintEnd;
        Assert.True(gap <= 2.5f,
            $"Gap '{left.Name}'→'{right.Name}' is {gap:F2}, expected <= 2.5 (ZoneGap=2.0)");
    }
}
```

- [ ] **Step 2: Run — confirm it fails**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln \
  --filter "Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive" -v normal
```

Expected: FAIL — gap is ~3.0 with old `ZoneGap = 3.0f`.

- [ ] **Step 3: Update spacing constants in LayoutCalculator.cs**

```csharp
private const float ShapeSpacing = 1.2f;   // was 1.5f — tighter bar grouping
private const float ZoneGap      = 2.0f;   // was 3.0f — less dead air between zones
```

- [ ] **Step 4: Update the existing `Calculate_AdjacentBackgroundZones_StrideIncludesRowHeaderReservation` test**

Find the local constant in that test and change it:

```csharp
const float ZoneGap = 2.0f;   // was 3.0f — matches LayoutCalculator constant
```

- [ ] **Step 5: Run all tests — pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | grep -E "FAIL|passed|failed"
```

- [ ] **Step 6: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Layout/LayoutCalculator.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Layout/LayoutCalculatorTests.cs
git commit -m "tighten scene spacing: ZoneGap 3→2, ShapeSpacing 1.5→1.2"
```

---

## Chunk 2: TscnWriter Visual Polish

### Task 3: Increase font sizes

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Update existing zone-label test and add failing tests for other label types**

In `TscnWriterTests.cs`:

1. In `Write_EmitsZoneLabelNode_WithGroundPlaneProperties`, change:
   ```csharp
   Assert.Contains("font_size = 56", tscn);   // was font_size = 32
   ```

2. Add these new tests:

```csharp
[Fact]
public void Write_ShapeLabel_HasIncreasedFontSize()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
            [new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                "kernel.all.load", "1 minute", "1m",
                new RgbColour(0.388f, 0.400f, 0.945f),
                0f, 10f, 0.2f, 5.0f)])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("[node name=\"Load_1mLabel\" type=\"Label3D\"", tscn);
    Assert.Contains("font_size = 40", tscn);
    Assert.Contains("pixel_size = 0.01", tscn);
}

[Fact]
public void Write_GridColumnHeader_HasIncreasedFontSize()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
            3, 1.5f, 2.0f,
            [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User", "Sys", "Nice"],
            InstanceLabels: ["cpu0"])
    ]);
    var tscn = TscnWriter.Write(layout);
    // Column header nodes have type="Label3D" and are children of Per_CPU
    Assert.Contains("Per_CPUColLabel0", tscn);
    // All Label3D font_size entries in the tscn should be 40 (not 24)
    Assert.DoesNotContain("font_size = 24", tscn);
    Assert.Contains("font_size = 40", tscn);
}

[Fact]
public void Write_GridRowHeader_HasIncreasedFontSize()
{
    var layout = new SceneLayout("testhost", [
        new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
            3, 1.5f, 2.0f,
            [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                "kernel.percpu.cpu.user", "cpu0", "cpu0",
                new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
            GroundWidth: 5f, GroundDepth: 8f,
            MetricLabels: ["User"],
            InstanceLabels: ["cpu0", "cpu1"])
    ]);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("Per_CPURowLabel0", tscn);
    Assert.DoesNotContain("font_size = 24", tscn);
    Assert.Contains("font_size = 40", tscn);
}
```

- [ ] **Step 2: Run — confirm failures**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln \
  --filter "Write_EmitsZoneLabelNode|Write_ShapeLabel_HasIncreasedFontSize|Write_GridColumnHeader_HasIncreasedFontSize|Write_GridRowHeader_HasIncreasedFontSize" -v normal 2>&1 | grep -E "FAIL|PASS"
```

Expected: all four FAIL.

- [ ] **Step 3: Increase font sizes in TscnWriter.cs**

In `WriteZoneLabelNode`:
```csharp
sb.AppendLine("pixel_size = 0.01");
sb.AppendLine("font_size = 56");   // was 32
```

In `WriteGridColumnHeaders`:
```csharp
sb.AppendLine("pixel_size = 0.01");   // was 0.008
sb.AppendLine("font_size = 40");      // was 24
```

In `WriteGridRowHeaders`:
```csharp
sb.AppendLine("pixel_size = 0.01");   // was 0.008
sb.AppendLine("font_size = 40");      // was 24
```

In `WriteShapeLabel`:
```csharp
sb.AppendLine("pixel_size = 0.01");   // was 0.008
sb.AppendLine("font_size = 40");      // was 24
```

- [ ] **Step 4: Run all tests — pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | grep -E "FAIL|passed|failed"
```

- [ ] **Step 5: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "bump all label font sizes for distance legibility"
```

---

### Task 4: Emit TimestampLabel and HostnameLabel nodes

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `TscnWriterTests.cs`:

```csharp
[Fact]
public void Write_HasTimestampLabelNode()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("[node name=\"TimestampLabel\" type=\"Label3D\" parent=\".\"]", tscn);
}

[Fact]
public void Write_TimestampLabel_IsFlat_WithNeonOrangeAndLargeFont()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    // Flat on floor: same rotation matrix as zone labels (rotated -90° around X):
    // Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, ...)
    Assert.Contains("Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0.02, -4)", tscn);
    Assert.Contains("font_size = 96", tscn);
    Assert.Contains("pixel_size = 0.02", tscn);
    Assert.Contains("outline_size = 8", tscn);
    // Orange: f97316 = (0.976, 0.451, 0.086)
    Assert.Contains("modulate = Color(0.976, 0.451, 0.086, 1)", tscn);
}

[Fact]
public void Write_TimestampLabel_HasPcpBindableForTimestampMetric()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("MetricName = \"pmview.meta.timestamp\"", tscn);
    Assert.Contains("TargetProperty = \"text\"", tscn);
    Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"TimestampLabel\"]", tscn);
}

[Fact]
public void Write_HasHostnameLabelNode()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("[node name=\"HostnameLabel\" type=\"Label3D\" parent=\".\"]", tscn);
}

[Fact]
public void Write_HostnameLabel_IsBillboard_FloatingAtYTen()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("billboard = 1", tscn);
    Assert.Contains("font_size = 128", tscn);
    Assert.Contains("outline_size = 12", tscn);
    Assert.Contains("uppercase = true", tscn);
    // The HostnameLabel's transform places it at Y=10, directly above scene centre.
    // Full transform: Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 10, 0)
    Assert.Contains("[node name=\"HostnameLabel\" type=\"Label3D\" parent=\".\"]", tscn);
    Assert.Contains("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 10, 0)", tscn);
}

[Fact]
public void Write_HostnameLabel_HasPcpBindableForHostnameMetric()
{
    var tscn = TscnWriter.Write(MinimalLayout());
    Assert.Contains("MetricName = \"pmview.meta.hostname\"", tscn);
    Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"HostnameLabel\"]", tscn);
}

[Fact]
public void Write_MetricPoller_HasHostnameProperty()
{
    var layout = new SceneLayout("my-server", MinimalLayout().Zones);
    var tscn = TscnWriter.Write(layout);
    Assert.Contains("Hostname = \"my-server\"", tscn);
}
```

- [ ] **Step 2: Run — confirm failures**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln \
  --filter "Write_HasTimestampLabel|Write_TimestampLabel|Write_HasHostnameLabel|Write_HostnameLabel|Write_MetricPoller_HasHostname" -v normal 2>&1 | grep -E "FAIL|PASS"
```

Expected: all 7 FAIL.

- [ ] **Step 3: Add the AmbientLabelSpec record and factory to TscnWriter.cs**

Add a new private record and factory method at the bottom of `TscnWriter`, just before the existing `SubResourceEntry` record:

```csharp
private record AmbientLabelSpec(
    string NodeName,
    string MetricName,
    string SubResourceId,
    bool IsFlatOnFloor,
    int FontSize,
    float PixelSize,
    int OutlineSize,
    string? Modulate,     // null = white (no modulate line emitted)
    bool Uppercase,
    float YPosition);

private static IReadOnlyList<AmbientLabelSpec> BuildAmbientLabels() =>
[
    new("TimestampLabel", "pmview.meta.timestamp", "binding_TimestampLabel",
        IsFlatOnFloor: true,
        FontSize: 96, PixelSize: 0.02f, OutlineSize: 8,
        Modulate: "Color(0.976, 0.451, 0.086, 1)",
        Uppercase: false, YPosition: 0.02f),
    new("HostnameLabel", "pmview.meta.hostname", "binding_HostnameLabel",
        IsFlatOnFloor: false,
        FontSize: 128, PixelSize: 0.015f, OutlineSize: 12,
        Modulate: null,
        Uppercase: true, YPosition: 10f),
];
```

- [ ] **Step 4: Wire ambient labels into `Write()`**

Update `Write()` to collect and pass ambient labels:

```csharp
public static string Write(SceneLayout layout,
    string pmproxyEndpoint = "http://localhost:44322",
    CameraSetup? camera = null)
{
    var sb = new StringBuilder();
    var registry = new ExtResourceRegistry();
    RegisterControllerResources(registry);
    if (camera != null)
        registry.Require("camera_orbit_script", "Script",
            "res://addons/pmview-bridge/camera_orbit.gd");
    var subResources = CollectSubResources(layout, registry);
    var bezelResources = CollectBezelSubResources(layout);
    var ambientLabels = BuildAmbientLabels();                 // NEW

    WriteHeader(sb, registry, subResources, bezelResources, ambientLabels);  // updated sig
    WriteExtResources(sb, registry);
    WriteSubResources(sb, subResources);
    WriteBezelSubResources(sb, bezelResources);
    WriteAmbientSubResources(sb, ambientLabels);              // NEW
    WriteWorldEnvironmentSubResource(sb);
    WriteNodes(sb, layout, registry, subResources, bezelResources,
               pmproxyEndpoint, camera, ambientLabels);       // updated sig

    return sb.ToString();
}
```

Update `WriteHeader` signature to accept ambient labels and include them in `loadSteps`:

```csharp
private static void WriteHeader(StringBuilder sb, ExtResourceRegistry registry,
    List<SubResourceEntry> subResources, List<BezelSubResources> bezelResources,
    IReadOnlyList<AmbientLabelSpec> ambientLabels)
{
    // +1 for scene itself, +1 for WorldEnvironment Environment sub_resource
    var loadSteps = registry.Count + subResources.Count + bezelResources.Count * 2
                    + ambientLabels.Count + 2;
    sb.AppendLine($"[gd_scene load_steps={loadSteps} format=3]");
    sb.AppendLine();
}
```

- [ ] **Step 5: Add `WriteAmbientSubResources` method**

> Note: The sub_resource format (`type="Resource"` + `script = ExtResource("binding_res_script")`) matches the existing pattern used for shape binding sub_resources — this is correct for PcpBindingResource.

```csharp
private static void WriteAmbientSubResources(StringBuilder sb,
    IReadOnlyList<AmbientLabelSpec> labels)
{
    foreach (var label in labels)
    {
        sb.AppendLine($"[sub_resource type=\"Resource\" id=\"{label.SubResourceId}\"]");
        sb.AppendLine("script = ExtResource(\"binding_res_script\")");
        sb.AppendLine("resource_local_to_scene = true");
        sb.AppendLine($"MetricName = \"{label.MetricName}\"");
        sb.AppendLine("TargetProperty = \"text\"");
        sb.AppendLine("SourceRangeMin = 0");
        sb.AppendLine("SourceRangeMax = 1");
        sb.AppendLine("TargetRangeMin = 0");
        sb.AppendLine("TargetRangeMax = 1");
        sb.AppendLine("InstanceId = -1");
        sb.AppendLine("InitialValue = 0");
        sb.AppendLine();
    }
}
```

- [ ] **Step 6: Add `WriteAmbientLabels` method and update `WriteNodes` signature**

Update `WriteNodes` signature:
```csharp
private static void WriteNodes(StringBuilder sb, SceneLayout layout,
    ExtResourceRegistry registry, List<SubResourceEntry> subResources,
    List<BezelSubResources> bezelResources, string pmproxyEndpoint,
    CameraSetup? camera, IReadOnlyList<AmbientLabelSpec> ambientLabels)
```

In the MetricPoller section of `WriteNodes`, add `Hostname` after `Endpoint`:
```csharp
sb.AppendLine("[node name=\"MetricPoller\" type=\"Node\" parent=\".\"]");
sb.AppendLine("script = ExtResource(\"metric_poller_script\")");
sb.AppendLine($"Endpoint = \"{pmproxyEndpoint}\"");
sb.AppendLine($"Hostname = \"{layout.Hostname}\"");
sb.AppendLine();
```

After `foreach (var zone in layout.Zones) WriteZone(...)`, and before `if (camera != null) WriteCameraNode(...)`, add:
```csharp
WriteAmbientLabels(sb, ambientLabels);
```

New method:
```csharp
private static void WriteAmbientLabels(StringBuilder sb,
    IReadOnlyList<AmbientLabelSpec> labels)
{
    foreach (var label in labels)
    {
        sb.AppendLine($"[node name=\"{label.NodeName}\" type=\"Label3D\" parent=\".\"]");

        if (label.IsFlatOnFloor)
            // Rotated -90° around X (same as zone labels), centred at scene X=0, between rows at Z=-4
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, {F(label.YPosition)}, -4)");
        else
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {F(label.YPosition)}, 0)");

        if (!label.IsFlatOnFloor)
            sb.AppendLine("billboard = 1");

        sb.AppendLine($"pixel_size = {F(label.PixelSize)}");
        sb.AppendLine($"font_size = {label.FontSize}");
        sb.AppendLine($"outline_size = {label.OutlineSize}");
        sb.AppendLine("outline_modulate = Color(0, 0, 0, 1)");

        if (label.Modulate != null)
            sb.AppendLine($"modulate = {label.Modulate}");

        if (label.Uppercase)
            sb.AppendLine("uppercase = true");

        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine("text = \"\"");
        sb.AppendLine();

        sb.AppendLine($"[node name=\"PcpBindable\" type=\"Node\" parent=\"{label.NodeName}\"]");
        sb.AppendLine("script = ExtResource(\"bindable_script\")");
        sb.AppendLine($"PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(\"{label.SubResourceId}\")])");
        sb.AppendLine();
    }
}
```

- [ ] **Step 7: Run all tests — pass**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test src/pmview-host-projector/PmviewHostProjector.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | grep -E "FAIL|passed|failed"
```

- [ ] **Step 8: Commit**

```bash
git add src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs \
        src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs
git commit -m "emit TimestampLabel + HostnameLabel nodes with text binding stubs"
```

---

## Chunk 3: Text Binding Pipeline (Bridge Addon)

> **Note:** These tests use GdUnit4 and require Godot. Write the code; user runs tests in Godot editor. Steps marked `[Godot]` cannot be verified in the VM.

### Task 5: SceneBinder — text binding support

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`
- Modify: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write tests for text binding `[Godot]`**

Add to `SceneBinderTests.cs`:

```csharp
// ── Text binding ───────────────────────────────────────────────────────

[TestCase]
public void ApplyMetrics_TextBinding_SetsLabelText()
{
    var binder = new SceneBinder();
    var label = new Label3D();
    label.Name = "TimestampLabel";
    binder.AddTextBindingForTest("pmview.meta.timestamp", label);

    var metrics = new Godot.Collections.Dictionary();
    metrics["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
    {
        ["text_value"] = "2025-03-14 · 14:23:07"
    };

    binder.ApplyMetrics(metrics);

    AssertThat(label.Text).IsEqual("2025-03-14 · 14:23:07");
}

[TestCase]
public void ApplyMetrics_TextBinding_NoTextValueKey_DoesNotThrow()
{
    var binder = new SceneBinder();
    var label = new Label3D();
    label.Name = "TimestampLabel";
    binder.AddTextBindingForTest("pmview.meta.timestamp", label);

    var metrics = new Godot.Collections.Dictionary();
    metrics["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
    {
        ["instances"] = new Godot.Collections.Dictionary()
    };

    binder.ApplyMetrics(metrics);   // must not throw
    AssertThat(label.Text).IsEqual("");
}

[TestCase]
public void ApplyMetrics_TextBinding_MetricAbsent_DoesNotThrow()
{
    var binder = new SceneBinder();
    var label = new Label3D();
    label.Name = "TimestampLabel";
    binder.AddTextBindingForTest("pmview.meta.timestamp", label);

    binder.ApplyMetrics(new Godot.Collections.Dictionary());  // empty dict
    AssertThat(label.Text).IsEqual("");
}
```

- [ ] **Step 2: Add `AddTextBindingForTest` internal helper to SceneBinder.cs**

Add just before the closing `}` of `SceneBinder`:

```csharp
/// <summary>
/// Test-only helper. Registers a text binding directly, bypassing
/// BindFromSceneProperties scene traversal.
/// </summary>
internal void AddTextBindingForTest(string metricName, Node targetNode)
{
    var fakeBinding = new MetricBinding(
        (string)targetNode.Name, metricName, "text",
        SourceRangeMin: 0, SourceRangeMax: 1,
        TargetRangeMin: 0, TargetRangeMax: 1,
        InstanceId: -1, InstanceName: null);
    var resolved = PropertyVocabulary.Resolve(fakeBinding);
    _activeBindings.Add(new ActiveBinding(resolved, targetNode));
}
```

- [ ] **Step 3: Add `ApplyTextMetric` private method to SceneBinder.cs**

```csharp
private static void ApplyTextMetric(ActiveBinding active,
    Godot.Collections.Dictionary metrics)
{
    var metricKey = active.Resolved.Binding.Metric;
    if (!metrics.ContainsKey(metricKey))
        return;

    var metricData = metrics[metricKey].AsGodotDictionary();
    if (!metricData.ContainsKey("text_value"))
        return;

    active.TargetNode.Set("text", metricData["text_value"].AsString());
}
```

- [ ] **Step 4: Route text bindings in `ApplyMetrics` — show exact insertion point**

The existing `ApplyMetrics` loop starts:
```csharp
foreach (var active in _activeBindings)
{
    var binding = active.Resolved.Binding;

    if (!metrics.ContainsKey(binding.Metric))
        continue;

    var metricData = metrics[binding.Metric].AsGodotDictionary();
    var instances = metricData["instances"].AsGodotDictionary();   // ← would throw for text metrics
```

Insert the text-binding guard immediately **after `var binding = active.Resolved.Binding;`** and **before `if (!metrics.ContainsKey(...))`**, so text bindings never reach the `instances` access:

```csharp
foreach (var active in _activeBindings)
{
    var binding = active.Resolved.Binding;

    // Text bindings carry a string value — handled separately, no normalisation.
    if (binding.Property == "text")
    {
        ApplyTextMetric(active, metrics);
        continue;
    }

    if (!metrics.ContainsKey(binding.Metric))
        continue;

    var metricData = metrics[binding.Metric].AsGodotDictionary();
    var instances = metricData["instances"].AsGodotDictionary();
    // ... rest of existing numeric path unchanged
```

- [ ] **Step 5: Skip numeric initial-value application for text bindings in `BindFromSceneProperties`**

In `BindFromSceneProperties`, after `_activeBindings.Add(new ActiveBinding(resolved, ownerNode));` and `metricNames.Add(metricBinding.Metric);`:

```csharp
_activeBindings.Add(new ActiveBinding(resolved, ownerNode));
metricNames.Add(metricBinding.Metric);

// Text bindings have no numeric initial value — skip normalise/apply.
if (metricBinding.Property == "text")
    continue;

var normalisedInitial = Normalise(metricBinding.InitialValue, ...);
// ... rest unchanged
```

- [ ] **Step 6: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs \
        src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "SceneBinder: route TargetProperty=text bindings through ApplyTextMetric"
```

> Run GdUnit4 tests in Godot to verify.

---

### Task 6: MetricPoller — Hostname export + virtual metric injection

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`
- Modify: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Add `Hostname` export property**

In `MetricPoller.cs`, alongside the other exports:

```csharp
[Export] public string Hostname { get; set; } = "";
```

- [ ] **Step 2: Add `InjectVirtualMetrics` internal method**

Making it `internal` (consistent with `ExtractValue` and other testable internals) avoids reflection in tests:

```csharp
/// <summary>
/// Adds synthetic pmview.meta.* keys to the outgoing metric dictionary.
/// These are derived from local poller state, not fetched from pmproxy.
/// Note: virtual metrics are only injected when real metrics also have new data.
/// If the poller idle-holds (no new real metric samples), virtual metrics won't
/// update until the next real emit. This is acceptable for v1 timestamp display.
/// </summary>
internal void InjectVirtualMetrics(Godot.Collections.Dictionary dict)
{
    var now = _timeCursor.Mode == CursorMode.Playback
        ? _timeCursor.Position
        : DateTime.UtcNow;

    dict["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
    {
        ["text_value"] = now.ToString("yyyy-MM-dd · HH:mm:ss")
    };

    if (!string.IsNullOrEmpty(Hostname))
    {
        dict["pmview.meta.hostname"] = new Godot.Collections.Dictionary
        {
            ["text_value"] = Hostname
        };
    }

    dict["pmview.meta.endpoint"] = new Godot.Collections.Dictionary
    {
        ["text_value"] = Endpoint
    };
}
```

- [ ] **Step 3: Filter `pmview.meta.*` names from pmproxy fetch calls**

In `FetchLiveMetrics`, change:
```csharp
var values = await _client!.FetchAsync(MetricNames);
```
to:
```csharp
var realMetrics = MetricNames
    .Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal))
    .ToArray();
var values = await _client!.FetchAsync(realMetrics);
```

In `FetchHistoricalMetrics`, change the outer loop:
```csharp
foreach (var metricName in MetricNames
    .Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal)))
```

Also update `InitialiseRateConverter` to filter the same way:
```csharp
var descriptors = await _client.DescribeMetricsAsync(
    MetricNames.Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal)).ToArray());
```

- [ ] **Step 4: Call `InjectVirtualMetrics` at every emit point**

In `FetchLiveMetrics`, just before `_lastEmittedMetrics = dict; EmitSignal(...)`:
```csharp
if (converted.Count > 0)
{
    var dict = MarshalMetricValues(converted);
    InjectVirtualMetrics(dict);
    _lastEmittedMetrics = dict;
    EmitSignal(SignalName.MetricsUpdated, dict);
}
```

In `FetchHistoricalMetrics`, just before the final emit:
```csharp
if (dict.Count > 0)
{
    InjectVirtualMetrics(dict);
    GD.Print($"[MetricPoller] Historical update: {dict.Count} metrics with new data");
    _lastEmittedMetrics = dict;
    EmitSignal(SignalName.MetricsUpdated, dict);
}
```

In `ReplayLastMetrics`, inject fresh virtual metrics into a copy so the timestamp refreshes:
```csharp
public void ReplayLastMetrics()
{
    _lastEmittedTimestamp.Clear();

    if (_lastEmittedMetrics != null && _lastEmittedMetrics.Count > 0)
    {
        var dict = new Godot.Collections.Dictionary(_lastEmittedMetrics);
        InjectVirtualMetrics(dict);
        GD.Print($"[MetricPoller] Replaying {dict.Count} cached metrics for new scene");
        EmitSignal(SignalName.MetricsUpdated, dict);
    }
}
```

- [ ] **Step 5: Write tests for virtual metric injection `[Godot]`**

Add to `SceneBinderTests.cs`:

```csharp
// ── MetricPoller virtual metrics ───────────────────────────────────────

[TestCase]
public void InjectVirtualMetrics_IncludesTimestampTextValue()
{
    var poller = new MetricPoller();
    var dict = new Godot.Collections.Dictionary();

    poller.InjectVirtualMetrics(dict);

    AssertThat(dict.ContainsKey("pmview.meta.timestamp")).IsTrue();
    var entry = dict["pmview.meta.timestamp"].AsGodotDictionary();
    AssertThat(entry.ContainsKey("text_value")).IsTrue();
    AssertThat(entry["text_value"].AsString()).IsNotEmpty();
}

[TestCase]
public void InjectVirtualMetrics_IncludesHostname_WhenSet()
{
    var poller = new MetricPoller();
    poller.Hostname = "my-server";
    var dict = new Godot.Collections.Dictionary();

    poller.InjectVirtualMetrics(dict);

    AssertThat(dict.ContainsKey("pmview.meta.hostname")).IsTrue();
    var entry = dict["pmview.meta.hostname"].AsGodotDictionary();
    AssertThat(entry["text_value"].AsString()).IsEqual("my-server");
}

[TestCase]
public void InjectVirtualMetrics_OmitsHostname_WhenEmpty()
{
    var poller = new MetricPoller();   // Hostname = "" by default
    var dict = new Godot.Collections.Dictionary();

    poller.InjectVirtualMetrics(dict);

    AssertThat(dict.ContainsKey("pmview.meta.hostname")).IsFalse();
}
```

- [ ] **Step 6: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs \
        src/pmview-bridge-addon/test/SceneBinderTests.cs
git commit -m "MetricPoller: inject pmview.meta.* virtual metrics; filter them from pmproxy fetch"
```

> Run GdUnit4 tests in Godot to verify.

---

## Final verification

- [ ] **Run full solution test suite**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration" -v normal 2>&1 | tail -5
```

Expected: all tests pass, 0 failed.

- [ ] **Generate a test scene and inspect in Godot**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- \
  --pmproxy http://localhost:44322 \
  -o /tmp/host_view_test.tscn
```

Open `/tmp/host_view_test.tscn` in Godot. Verify:
- Four foreground zones: System (9 bars, orange+indigo+green), Disk, Net-In, Net-Out
- Four background grids aligned behind them
- `TimestampLabel` flat on the floor between rows, orange neon
- `HostnameLabel` floating above, white outlined, uppercased, billboard
- Font sizes visibly larger than before
