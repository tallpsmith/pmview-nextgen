# Range Tuning Panel Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a floating GDScript UI panel that lets users adjust SourceRangeMax for disk and network zones at runtime, so 3D shapes scale proportionally to their actual hardware.

**Architecture:** Zone names are baked into scene bindings at generation time (SharedZones → TscnWriter → PcpBindingResource). At runtime, a GDScript panel calls `SceneBinder.UpdateSourceRangeMax(zoneName, newMax)` which replaces immutable binding records via `with` expressions. The next MetricPoller tick picks up the new ranges through the existing `Normalise()` path.

**Tech Stack:** C# (.NET 8.0) for PcpGodotBridge/addon, C# (.NET 10.0) for projection-core/host-projector/tests, GDScript for UI panel, xUnit for unit tests, GdUnit4 for Godot integration tests.

**Spec:** `docs/superpowers/specs/2026-03-17-range-tuning-panel-design.md`

---

## Chunk 1: ZoneName Through the Binding Pipeline

Bottom-up: thread `ZoneName` from MetricBinding through PcpBindingConverter, PcpBindingResource, and into SceneBinder's active bindings. All changes are pure .NET or addon C# — no GDScript yet.

### Task 1: Add ZoneName to MetricBinding

**Files:**
- Modify: `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs:7-17`
- Test: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/PcpBindingConverterTests.cs`

- [ ] **Step 1: Write failing test — MetricBinding carries ZoneName**

Add to `PcpBindingConverterTests.cs`:

```csharp
[Fact]
public void ZoneName_IncludedInMetricBinding()
{
    var binding = PcpBindingConverter.ToMetricBinding(
        sceneNode: "TestNode", metricName: "disk.all.read_bytes",
        targetProperty: "height",
        sourceRangeMin: 0, sourceRangeMax: 500_000_000,
        targetRangeMin: 0.2, targetRangeMax: 5.0,
        instanceId: -1, instanceName: null, initialValue: 0.2,
        zoneName: "Disk");

    Assert.Equal("Disk", binding.ZoneName);
}

[Fact]
public void ZoneName_DefaultsToNull_WhenNotProvided()
{
    var binding = PcpBindingConverter.ToMetricBinding(
        sceneNode: "TestNode", metricName: "kernel.all.cpu.sys",
        targetProperty: "height",
        sourceRangeMin: 0, sourceRangeMax: 100,
        targetRangeMin: 0.2, targetRangeMax: 5.0,
        instanceId: -1, instanceName: null, initialValue: 0.2);

    Assert.Null(binding.ZoneName);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ --filter "ZoneName"`
Expected: FAIL — `ToMetricBinding` has no `zoneName` parameter, `MetricBinding` has no `ZoneName` field.

- [ ] **Step 3: Add ZoneName field to MetricBinding record**

In `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs`, add `ZoneName` as an optional parameter at the end:

```csharp
public record MetricBinding(
    string SceneNode,
    string Metric,
    string Property,
    double SourceRangeMin,
    double SourceRangeMax,
    double TargetRangeMin,
    double TargetRangeMax,
    int? InstanceId,
    string? InstanceName,
    double InitialValue = 0.0,
    string? ZoneName = null);
```

- [ ] **Step 4: Add zoneName parameter to PcpBindingConverter.ToMetricBinding()**

In `src/pcp-godot-bridge/src/PcpGodotBridge/PcpBindingConverter.cs`, add optional `zoneName` parameter and pass through:

```csharp
public static MetricBinding ToMetricBinding(
    string sceneNode,
    string metricName,
    string targetProperty,
    double sourceRangeMin,
    double sourceRangeMax,
    double targetRangeMin,
    double targetRangeMax,
    int instanceId,
    string? instanceName,
    double initialValue,
    string? zoneName = null)
{
    return new MetricBinding(
        SceneNode: sceneNode,
        Metric: metricName,
        Property: targetProperty,
        SourceRangeMin: sourceRangeMin,
        SourceRangeMax: sourceRangeMax,
        TargetRangeMin: targetRangeMin,
        TargetRangeMax: targetRangeMax,
        InstanceId: instanceId < 0 ? null : instanceId,
        InstanceName: string.IsNullOrWhiteSpace(instanceName) ? null : instanceName,
        InitialValue: initialValue,
        ZoneName: zoneName);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ --filter "ZoneName"`
Expected: PASS

- [ ] **Step 6: Run full PcpGodotBridge test suite — no regressions**

Run: `dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: All existing tests PASS (ZoneName defaults to null, so no existing call sites break).

- [ ] **Step 7: Commit**

```bash
git add src/pcp-godot-bridge/
git commit -m "Add optional ZoneName field to MetricBinding and PcpBindingConverter"
```

---

### Task 2: Add ZoneName Export to PcpBindingResource

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/PcpBindingResource.cs:5-41`

- [ ] **Step 1: Add [Export] ZoneName property to PcpBindingResource**

In `src/pmview-bridge-addon/addons/pmview-bridge/PcpBindingResource.cs`, add after the existing exports (before `ToMetricBinding`):

```csharp
[Export] public string ZoneName { get; set; } = "";
```

- [ ] **Step 2: Pass ZoneName through ToMetricBinding call**

Update the `ToMetricBinding` method in the same file to pass the new property:

```csharp
public MetricBinding ToMetricBinding(string nodeName)
{
    return PcpBindingConverter.ToMetricBinding(
        nodeName, MetricName, TargetProperty,
        SourceRangeMin, SourceRangeMax,
        TargetRangeMin, TargetRangeMax,
        InstanceId, InstanceName,
        InitialValue,
        zoneName: string.IsNullOrWhiteSpace(ZoneName) ? null : ZoneName);
}
```

- [ ] **Step 3: Build the addon project to verify compilation**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/PcpBindingResource.cs
git commit -m "Add ZoneName export to PcpBindingResource for runtime zone grouping"
```

---

### Task 3: Add SharedZones Resolver Methods

**Files:**
- Modify: `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/SharedZones.cs`
- Modify: `src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj`
- Create: `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/SharedZonesTests.cs`

Note: Check whether a test project already exists at `src/pmview-projection-core/tests/`. If not, create one.

- [ ] **Step 1: Add InternalsVisibleTo to projection-core csproj**

The test project at `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/` already exists and is in the solution. No creation needed.

In `src/pmview-projection-core/src/PmviewProjectionCore/PmviewProjectionCore.csproj`, add a new `<ItemGroup>`:

```xml
<ItemGroup>
    <InternalsVisibleTo Include="PmviewProjectionCore.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Write failing tests for ResolveZone and GetMetricNames**

Create `src/pmview-projection-core/tests/PmviewProjectionCore.Tests/Profiles/SharedZonesTests.cs`:

```csharp
using PmviewProjectionCore.Profiles;

namespace PmviewProjectionCore.Tests.Profiles;

public class SharedZonesTests
{
    [Theory]
    [InlineData("disk.all.read_bytes", "Disk")]
    [InlineData("disk.all.write_bytes", "Disk")]
    [InlineData("disk.dev.read_bytes", "Per-Disk")]
    [InlineData("disk.dev.write_bytes", "Per-Disk")]
    [InlineData("network.interface.in.bytes", "Network In")]
    [InlineData("network.interface.in.packets", "Network In")]
    [InlineData("network.interface.out.bytes", "Network Out")]
    [InlineData("network.interface.out.packets", "Network Out")]
    [InlineData("kernel.all.cpu.sys", "CPU")]
    public void ResolveZone_ReturnsCorrectZoneName(string metricName, string expectedZone)
    {
        var zone = SharedZones.ResolveZone(metricName);
        Assert.Equal(expectedZone, zone);
    }

    [Fact]
    public void ResolveZone_ReturnsNull_ForUnknownMetric()
    {
        var zone = SharedZones.ResolveZone("bogus.metric.name");
        Assert.Null(zone);
    }

    [Theory]
    [InlineData("Disk", new[] { "disk.all.read_bytes", "disk.all.write_bytes" })]
    [InlineData("Per-Disk", new[] { "disk.dev.read_bytes", "disk.dev.write_bytes" })]
    [InlineData("Network In", new[] { "network.interface.in.bytes", "network.interface.in.packets", "network.interface.in.errors" })]
    public void GetMetricNames_ReturnsAllMetricsInZone(string zoneName, string[] expectedMetrics)
    {
        var metrics = SharedZones.GetMetricNames(zoneName);
        Assert.Equal(expectedMetrics.OrderBy(m => m), metrics.OrderBy(m => m));
    }

    [Fact]
    public void GetMetricNames_ReturnsEmpty_ForUnknownZone()
    {
        var metrics = SharedZones.GetMetricNames("Nonexistent");
        Assert.Empty(metrics);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/pmview-projection-core/tests/PmviewProjectionCore.Tests/ --filter "SharedZones"`
Expected: FAIL — `ResolveZone` and `GetMetricNames` don't exist.

- [ ] **Step 4: Implement resolver methods on SharedZones**

Add to the bottom of `src/pmview-projection-core/src/PmviewProjectionCore/Profiles/SharedZones.cs`, inside the class body before the closing `}`:

```csharp
private static readonly ZoneDefinition[] AllZones =
[
    CpuZone(), LoadZone(), DiskTotalsZone(),
    PerCpuZone(), PerDiskZone(),
    NetworkInZone(), NetworkOutZone(),
];

internal static string? ResolveZone(string metricName)
{
    foreach (var zone in AllZones)
        foreach (var metric in zone.Metrics)
            if (metric.MetricName == metricName)
                return zone.Name;
    return null;
}

internal static string[] GetMetricNames(string zoneName)
{
    foreach (var zone in AllZones)
        if (zone.Name == zoneName)
            return zone.Metrics.Select(m => m.MetricName).ToArray();
    return [];
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/pmview-projection-core/tests/PmviewProjectionCore.Tests/ --filter "SharedZones"`
Expected: All PASS.

- [ ] **Step 6: Run full solution test suite — no regressions**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All existing tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/pmview-projection-core/
git commit -m "Add SharedZones resolver methods for zone-to-metric lookup"
```

---

### Task 4: Emit ZoneName in TscnWriter

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Test: `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`

- [ ] **Step 1: Write failing test — generated .tscn contains ZoneName property**

Add to `src/pmview-host-projector/tests/PmviewHostProjector.Tests/Emission/TscnWriterTests.cs`:

```csharp
[Fact]
public void Write_SubResource_ContainsZoneName()
{
    var layout = CreateLayoutWithSingleZone("TestZone", "test.metric.bytes",
        sourceRangeMin: 0f, sourceRangeMax: 100f,
        targetRangeMin: 0.2f, targetRangeMax: 5.0f);

    var result = TscnWriter.Write(layout);

    Assert.Contains("ZoneName = \"TestZone\"", result);
}
```

Note: No test helper exists — build a minimal `SceneLayout` inline using the pattern from existing tests (e.g. `Write_CylinderShape_UsesGroundedCylinder_WithBuildingBlocksPath`). Construct a `SceneLayout` with one `PlacedZone` containing one `PlacedShape`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/ --filter "ZoneName"`
Expected: FAIL — TscnWriter doesn't emit `ZoneName`.

- [ ] **Step 3: Add ZoneName to SubResourceEntry record**

In `TscnWriter.cs`, update the `SubResourceEntry` record (around line 370):

```csharp
private record SubResourceEntry(
    string Id,
    string MetricName,
    string? InstanceName,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax,
    string ZoneName);
```

- [ ] **Step 4: Thread zone name through CollectSubResources**

In `CollectSubResources`, capture `zone.Name` at the top of the outer `foreach (var zone in layout.Zones)` loop. Pass it into **every** `new SubResourceEntry(...)` call — including those inside the `PlacedStack` branch where stack members are iterated. The `zone` variable is only in scope at the outer level.

- [ ] **Step 5: Emit ZoneName in WriteSubResources**

In `WriteSubResources` method (around line 124), add after the `InitialValue` line:

```csharp
sb.AppendLine($"ZoneName = \"{entry.ZoneName}\"");
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/ --filter "ZoneName"`
Expected: PASS

- [ ] **Step 7: Run full host-projector tests — no regressions**

Run: `dotnet test src/pmview-host-projector/tests/PmviewHostProjector.Tests/`
Expected: All PASS.

- [ ] **Step 8: Commit**

```bash
git add src/pmview-host-projector/
git commit -m "Emit ZoneName property on PcpBindingResource in generated scenes"
```

---

### Task 5: Set ZoneName in RuntimeSceneBuilder

**Files:**
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs:252-275`

- [ ] **Step 1: Add zoneName parameter to CreateBindingResource**

In `src/pmview-app/scripts/RuntimeSceneBuilder.cs`, update `CreateBindingResource` signature and body:

```csharp
private static Resource CreateBindingResource(
    string metricName, string targetProperty,
    float sourceRangeMin, float sourceRangeMax,
    float targetRangeMin, float targetRangeMax,
    string? instanceName, float initialValue,
    string zoneName)
{
    // ... existing property sets ...
    res.Set("ZoneName", zoneName);
    return res;
}
```

- [ ] **Step 2: Thread zone name through BuildBinding and BuildShape callers**

Find all call sites of `CreateBindingResource` (via `BuildBinding` / `BuildShape`). The zone name is available from the `PlacedZone` being iterated — thread it through as a parameter.

- [ ] **Step 3: Build the app project to verify compilation**

Run: `dotnet build src/pmview-app/`
Expected: Build succeeds (no test project for pmview-app — it's a Godot project).

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/RuntimeSceneBuilder.cs
git commit -m "Set ZoneName on PcpBindingResource in RuntimeSceneBuilder"
```

---

## Chunk 2: SceneBinder API — BindingsReady, UpdateSourceRangeMax, GetSourceRanges

### Task 6: Add BindingsReady Signal and IsBound Property

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`
- Test: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write failing test — IsBound is false before binding and true after**

In `src/pmview-bridge-addon/test/SceneBinderTests.cs`, add tests. Note: SceneBinder tests use GdUnit4 which requires Godot runtime. The `IsBound` property can be tested without a full scene — just verify the property exists and defaults to false. The full integration (BindFromSceneProperties sets it to true) is verified by the user in the Godot editor, since it requires a real scene tree. Write the compilation-verifiable test:

```csharp
[TestCase]
public void IsBound_FalseBeforeBinding()
{
    var binder = new SceneBinder();
    Assert.That(binder.IsBound).IsFalse();
}
```

This test will fail to compile until `IsBound` is added, which is the TDD signal we need. The full BindFromSceneProperties → IsBound=true → BindingsReady signal chain requires a Godot scene tree and is verified by user testing.

- [ ] **Step 2: Add BindingsReady signal and IsBound property to SceneBinder**

In `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`, add after the existing `BindingError` signal declaration (around line 17):

```csharp
[Signal]
public delegate void BindingsReadyEventHandler();

public bool IsBound { get; private set; }
```

- [ ] **Step 3: Emit BindingsReady at end of BindFromSceneProperties**

At the end of `BindFromSceneProperties()`, just before the `return metricNames.ToArray();` line (around line 139), add:

```csharp
IsBound = true;
EmitSignal(SignalName.BindingsReady);
```

- [ ] **Step 4: Reset IsBound in UnloadCurrentScene**

In `UnloadCurrentScene()` (around line 255), add at the top of the method:

```csharp
IsBound = false;
```

- [ ] **Step 5: Build addon project to verify compilation**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs
git commit -m "Add BindingsReady signal and IsBound property to SceneBinder"
```

---

### Task 7: Implement UpdateSourceRangeMax on SceneBinder

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`
- Test: `src/pmview-bridge-addon/test/SceneBinderTests.cs`

- [ ] **Step 1: Write failing test — UpdateSourceRangeMax method exists and compiles**

`UpdateSourceRangeMax` mutates `_activeBindings` (a private list of private `ActiveBinding` records), so we cannot directly unit-test the mutation from outside the class without a Godot scene tree. However, we can verify the method exists and compiles, plus test the Normalise maths that underpins the range change. Full integration testing (bind a scene with disk bindings, call UpdateSourceRangeMax, verify shapes rescale) requires the Godot runtime and is verified by user testing.

Add to `SceneBinderTests.cs`:

```csharp
[TestCase]
public void Normalise_RangeChangeAffectsOutput()
{
    // Simulates the effect of changing SourceRangeMax from 500M to 1G.
    // Same raw value normalises to a smaller target when the range widens.
    var beforeUpdate = SceneBinder.Normalise(250_000_000, 0, 500_000_000, 0.2, 5.0);
    var afterUpdate = SceneBinder.Normalise(250_000_000, 0, 1_000_000_000, 0.2, 5.0);

    Assert.That(beforeUpdate).IsEqualTo(2.6);   // 50% of range → midpoint of 0.2–5.0
    Assert.That(afterUpdate).IsEqualTo(1.4);     // 25% of range → quarter of 0.2–5.0
    Assert.That(afterUpdate).IsLessThan(beforeUpdate);
}
```

Additionally, verify `UpdateSourceRangeMax` compiles by adding a compilation-check test:

```csharp
[TestCase]
public void UpdateSourceRangeMax_CanBeCalled()
{
    // Verifies the public API exists. Calling on an empty binder is a no-op.
    var binder = new SceneBinder();
    binder.UpdateSourceRangeMax("Disk", 1_000_000_000.0);
    // No exception = method exists and handles empty binding list gracefully.
}
```

- [ ] **Step 2: Implement UpdateSourceRangeMax**

Add to `SceneBinder.cs` as a public method:

```csharp
/// <summary>
/// Updates SourceRangeMax for bytes-throughput bindings in the named zone.
/// Only affects bindings whose metric name contains "bytes".
/// Takes effect on next poll tick via lazy re-normalisation.
/// </summary>
public void UpdateSourceRangeMax(string zoneName, double newMax)
{
    for (int i = 0; i < _activeBindings.Count; i++)
    {
        var active = _activeBindings[i];
        if (active.Resolved.Binding.ZoneName != zoneName) continue;
        if (!active.Resolved.Binding.Metric.Contains("bytes")) continue;

        var oldBinding = active.Resolved.Binding;
        var newBinding = oldBinding with { SourceRangeMax = newMax };
        var newResolved = active.Resolved with { Binding = newBinding };
        var newActive = active with { Resolved = newResolved };

        if (_smoothValues.TryGetValue(active, out var smoothState))
        {
            _smoothValues.Remove(active);
            _smoothValues[newActive] = smoothState;
        }

        _activeBindings[i] = newActive;
    }

    GD.Print($"[SceneBinder] Updated SourceRangeMax for zone '{zoneName}' to {newMax}");
}
```

- [ ] **Step 3: Build addon to verify compilation**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/
git commit -m "Implement UpdateSourceRangeMax for runtime source-range tuning"
```

---

### Task 8: Implement GetSourceRanges on SceneBinder

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`

- [ ] **Step 1: Implement GetSourceRanges**

Add to `SceneBinder.cs` as a public method:

```csharp
/// <summary>
/// Returns {zoneName: currentSourceRangeMax} for each zone with active bindings.
/// Only returns the SourceRangeMax from bytes-throughput bindings.
/// Zones with no active bindings are omitted.
/// </summary>
public Godot.Collections.Dictionary GetSourceRanges()
{
    var result = new Godot.Collections.Dictionary();
    foreach (var active in _activeBindings)
    {
        var binding = active.Resolved.Binding;
        if (binding.ZoneName == null) continue;
        if (!binding.Metric.Contains("bytes")) continue;
        if (result.ContainsKey(binding.ZoneName)) continue;

        result[binding.ZoneName] = binding.SourceRangeMax;
    }
    return result;
}
```

- [ ] **Step 2: Build addon to verify compilation**

Run: `dotnet build src/pmview-bridge-addon/pmview-nextgen.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs
git commit -m "Add GetSourceRanges for panel slider initialisation"
```

---

## Chunk 3: GDScript Range Tuning Panel

### Task 9: Create the Range Tuning Panel GDScript

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.tscn`

Note: No automated test for GDScript UI — user tests visually in host Godot editor. The panel talks to SceneBinder through the already-tested C# methods.

- [ ] **Step 1: Create the panel scene file**

Create `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.tscn`:

This is a `PanelContainer` with:
- A `VBoxContainer` layout
- A title `Label` ("Range Tuning")
- Three slider rows (Disk Total, Per-Disk, Network), each containing:
  - A zone name `Label` (colour-coded)
  - An `HSlider` (range 0.0–1.0, linear — log transform in script)
  - A readout `Label` showing human-readable value
- An `HBoxContainer` at bottom with an Apply `Button`

Write the `.tscn` directly. Use `PanelContainer` as root node with a `StyleBoxFlat` override for rounded corners and transparency.

- [ ] **Step 2: Create the panel script**

Create `src/pmview-bridge-addon/addons/pmview-bridge/ui/range_tuning_panel.gd`:

```gdscript
extends PanelContainer

## Floating panel for tuning disk/network source range maximums.
## Calls SceneBinder.UpdateSourceRangeMax() on Apply.

# -- Configuration --
## Log-scale range boundaries (bytes/sec)
const LOG_MIN: float = log(100_000.0)       # 100 KB/s
const LOG_MAX: float = log(50_000_000_000.0) # 50 GB/s

## Preset snap threshold in normalised slider space (0-1)
const SNAP_THRESHOLD: float = 0.02

# -- Preset definitions: {label: bytes_per_sec} --
const DISK_PRESETS: Dictionary = {
    "HDD": 150_000_000.0,
    "SATA SSD": 550_000_000.0,
    "NVMe Gen3": 3_500_000_000.0,
    "NVMe Gen4": 7_000_000_000.0,
    "NVMe Gen5": 14_000_000_000.0,
}

const NETWORK_PRESETS: Dictionary = {
    "1 Gbit": 125_000_000.0,
    "10 Gbit": 1_250_000_000.0,
    "25 Gbit": 3_125_000_000.0,
    "40 Gbit": 5_000_000_000.0,
    "100 Gbit": 12_500_000_000.0,
}

# -- Node references (set in _ready from scene tree) --
@onready var disk_total_slider: HSlider = %DiskTotalSlider
@onready var disk_total_readout: Label = %DiskTotalReadout
@onready var disk_total_row: Control = %DiskTotalRow

@onready var per_disk_slider: HSlider = %PerDiskSlider
@onready var per_disk_readout: Label = %PerDiskReadout
@onready var per_disk_row: Control = %PerDiskRow

@onready var network_slider: HSlider = %NetworkSlider
@onready var network_readout: Label = %NetworkReadout
@onready var network_row: Control = %NetworkRow

@onready var apply_button: Button = %ApplyButton

var _scene_binder: Node = null
var _initial_values: Dictionary = {}  # {zone_name: bytes_per_sec}


func _ready() -> void:
    apply_button.pressed.connect(_on_apply_pressed)
    disk_total_slider.value_changed.connect(
        _on_slider_changed.bind(disk_total_slider, disk_total_readout, DISK_PRESETS))
    per_disk_slider.value_changed.connect(
        _on_slider_changed.bind(per_disk_slider, per_disk_readout, DISK_PRESETS))
    network_slider.value_changed.connect(
        _on_slider_changed.bind(network_slider, network_readout, NETWORK_PRESETS))
    apply_button.disabled = true


func initialise(scene_binder: Node) -> void:
    _scene_binder = scene_binder
    if scene_binder.IsBound:
        _populate_from_binder()
    else:
        scene_binder.connect("BindingsReady", _populate_from_binder)


func _populate_from_binder() -> void:
    var ranges: Dictionary = _scene_binder.GetSourceRanges()
    _initial_values = ranges.duplicate()

    _set_slider_if_present(disk_total_slider, disk_total_readout, disk_total_row,
        ranges, "Disk", DISK_PRESETS)
    _set_slider_if_present(per_disk_slider, per_disk_readout, per_disk_row,
        ranges, "Per-Disk", DISK_PRESETS)

    # Network uses whichever is present — prefer "Network In"
    var net_key := "Network In" if ranges.has("Network In") else "Network Out"
    _set_slider_if_present(network_slider, network_readout, network_row,
        ranges, net_key, NETWORK_PRESETS)


func _set_slider_if_present(slider: HSlider, readout: Label, row: Control,
        ranges: Dictionary, zone: String, presets: Dictionary) -> void:
    if not ranges.has(zone):
        row.visible = false
        return
    row.visible = true
    var bytes_val: float = ranges[zone]
    slider.value = _bytes_to_slider(bytes_val)
    readout.text = _format_bytes(bytes_val)


# -- Log scale transforms --

func _bytes_to_slider(bytes_per_sec: float) -> float:
    if bytes_per_sec <= 0.0:
        return 0.0
    var log_val := log(bytes_per_sec)
    return clampf((log_val - LOG_MIN) / (LOG_MAX - LOG_MIN), 0.0, 1.0)


func _slider_to_bytes(slider_val: float) -> float:
    var log_val := LOG_MIN + slider_val * (LOG_MAX - LOG_MIN)
    return exp(log_val)


# -- Slider change handler with snap-to-preset --

var _snapping: bool = false  # Guard against re-entrant signal from set_value_no_signal

func _on_slider_changed(value: float, slider: HSlider, readout: Label,
        presets: Dictionary) -> void:
    if _snapping:
        return
    # Snap to nearest preset if close — move the slider thumb too
    for preset_bytes: float in presets.values():
        var preset_pos := _bytes_to_slider(preset_bytes)
        if absf(value - preset_pos) < SNAP_THRESHOLD:
            _snapping = true
            slider.set_value_no_signal(preset_pos)
            _snapping = false
            value = preset_pos
            break

    var bytes_val := _slider_to_bytes(value)
    readout.text = _format_bytes(bytes_val)
    apply_button.disabled = false


# -- Apply --

func _on_apply_pressed() -> void:
    if not _scene_binder:
        return

    var disk_bytes := _slider_to_bytes(disk_total_slider.value)
    var per_disk_bytes := _slider_to_bytes(per_disk_slider.value)
    var net_bytes := _slider_to_bytes(network_slider.value)

    if disk_total_row.visible:
        _scene_binder.UpdateSourceRangeMax("Disk", disk_bytes)
    if per_disk_row.visible:
        _scene_binder.UpdateSourceRangeMax("Per-Disk", per_disk_bytes)
    if network_row.visible:
        _scene_binder.UpdateSourceRangeMax("Network In", net_bytes)
        _scene_binder.UpdateSourceRangeMax("Network Out", net_bytes)

    apply_button.disabled = true


# -- Human-readable formatting --

static func _format_bytes(bytes_per_sec: float) -> String:
    if bytes_per_sec >= 1_000_000_000.0:
        return "%.1f GB/s" % (bytes_per_sec / 1_000_000_000.0)
    elif bytes_per_sec >= 1_000_000.0:
        return "%.0f MB/s" % (bytes_per_sec / 1_000_000.0)
    elif bytes_per_sec >= 1_000.0:
        return "%.0f KB/s" % (bytes_per_sec / 1_000.0)
    else:
        return "%.0f B/s" % bytes_per_sec
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/
git commit -m "Add range tuning panel GDScript with log-scale sliders and presets"
```

---

### Task 10: Wire Panel into Host View Controller

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd`

- [ ] **Step 1: Add panel discovery and initialisation to host_view_controller.gd**

Add after the existing wiring in `_ready()` (after `poller.StartPolling()`):

```gdscript
var tuning_panel = find_child("RangeTuningPanel")
if tuning_panel:
    print("[host_view_controller] Initialising RangeTuningPanel...")
    tuning_panel.initialise(binder)
    print("[host_view_controller] RangeTuningPanel wired")
```

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd
git commit -m "Wire range tuning panel into host view controller startup"
```

---

### Task 11: Add Panel to Generated Scenes (TscnWriter + RuntimeSceneBuilder)

**Files:**
- Modify: `src/pmview-host-projector/src/PmviewHostProjector/Emission/TscnWriter.cs`
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs`

- [ ] **Step 1: Add RangeTuningPanel node to TscnWriter scene output**

In `TscnWriter.cs`, add the panel as a `CanvasLayer` → `RangeTuningPanel` node in the generated .tscn. The panel script and scene are part of the addon, so reference them via `res://addons/pmview-bridge/ui/range_tuning_panel.tscn` (or instantiate via script reference).

Add a new method that emits the panel node, and call it from the main `Write()` method after the 3D scene nodes.

- [ ] **Step 2: Add RangeTuningPanel instantiation to RuntimeSceneBuilder**

In `RuntimeSceneBuilder.cs`, add a method to instantiate the panel. **Important:** Call `AddRangeTuningPanel(root)` inside `Build()` **before** the final `SetOwnerRecursive(root, root)` call — the centralized ownership sweep handles all owner-setting, so do not set `.Owner` manually on the panel nodes:

```csharp
private void AddRangeTuningPanel(Node sceneRoot)
{
    var panelScene = GD.Load<PackedScene>("res://addons/pmview-bridge/ui/range_tuning_panel.tscn");
    if (panelScene == null)
    {
        GD.PushWarning("[RuntimeSceneBuilder] RangeTuningPanel scene not found");
        return;
    }
    var canvas = new CanvasLayer();
    canvas.Name = "UILayer";
    var panel = panelScene.Instantiate();
    panel.Name = "RangeTuningPanel";
    canvas.AddChild(panel);
    sceneRoot.AddChild(canvas);
    // Owner is set by SetOwnerRecursive() in Build() — don't set manually here.
}
```

- [ ] **Step 3: Build both projects**

Run: `dotnet build src/pmview-host-projector/src/PmviewHostProjector/ && dotnet build src/pmview-app/`
Expected: Both build.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-host-projector/ src/pmview-app/
git commit -m "Include range tuning panel in generated and runtime-built scenes"
```

---

### Task 12: Full Solution Build + Test Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build pmview-nextgen.sln`
Expected: Clean build, zero errors.

- [ ] **Step 2: Run all non-integration tests**

Run: `dotnet test pmview-nextgen.sln --filter "FullyQualifiedName!~Integration"`
Expected: All tests PASS.

- [ ] **Step 3: Final commit if any loose ends**

Only if needed — all prior tasks should have committed their changes.

---

## Dependency Summary

```
Task 1 (MetricBinding ZoneName)
  ├── Task 2 (PcpBindingResource ZoneName) ── depends on Task 1
  ├── Task 3 (SharedZones resolvers) ── independent
  ├── Task 4 (TscnWriter ZoneName) ── depends on Tasks 1, 3
  └── Task 5 (RuntimeSceneBuilder ZoneName) ── depends on Task 2
Task 6 (BindingsReady signal) ── independent
Task 7 (UpdateSourceRangeMax) ── depends on Task 1 (ZoneName on binding)
Task 8 (GetSourceRanges) ── depends on Task 1
Task 9 (GDScript panel) ── depends on Tasks 7, 8
Task 10 (Wire into host_view_controller) ── depends on Task 9
Task 11 (Panel in generated scenes) ── depends on Task 9
Task 12 (Full verification) ── depends on all above
```
