# Instance Name Binding Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `instance_name` as the preferred TOML binding config field for selecting PCP metric instances by human-readable name, and fix the archive playback bug where multi-instance values collapse to a single key.

**Architecture:** Four-layer change — config model (pure .NET), series query parsing (pure .NET), MetricPoller (Godot bridge), SceneBinder (Godot bridge). Pure .NET layers get full xUnit TDD. Godot bridge layers are manually verified.

**Tech Stack:** C# (.NET 8.0), xUnit, Tomlyn, Godot 4.4+

**Spec:** `docs/superpowers/specs/2026-03-11-instance-name-binding-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs` | Remove `InstanceFilter`, add `InstanceName` |
| Modify | `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs` | Parse `instance_name`, remove `instance_filter`, update validation |
| Modify | `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ModelTests.cs` | Replace instance_filter tests with instance_name |
| Modify | `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs` | Replace instance_filter tests with instance_name |
| Modify | `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs` | Add `SeriesInstanceInfo` record, update `ParseInstancesResponse` |
| Modify | `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs` | Update instance parsing tests for new return type |
| Modify | `godot-project/addons/pmview-bridge/MetricPoller.cs` | Fix archive mapping bug, build name→id lookup, emit with metrics |
| Modify | `godot-project/addons/pmview-bridge/SceneBinder.cs` | Resolve `instance_name` via name→id lookup |
| Modify | `godot-project/bindings/test_bars.toml` | Use `instance_name` instead of `instance_id` |

---

## Task 1: Update MetricBinding model — remove `InstanceFilter`, add `InstanceName`

**Files:**
- Modify: `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs:7-16`
- Modify: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ModelTests.cs:14-66`

- [ ] **Step 1: Update the model tests — replace InstanceFilter with InstanceName**

In `ModelTests.cs`, replace the three instance-related tests:

`MetricBinding_StoresAllFields` (line 14) — change constructor call: replace `InstanceFilter: "sd*"` with `InstanceName: "1 minute"`, assert `InstanceName` instead of `InstanceFilter`.

`MetricBinding_WithInstanceFilter` (line 39) — rename to `MetricBinding_WithInstanceName`, construct with `InstanceName: "5 minute"` instead of `InstanceFilter: "sd*"`, assert accordingly.

`MetricBinding_WithInstanceId` (line 49) — keep as-is, but update constructor to use new parameter names (no `InstanceFilter`).

`MetricBinding_ValueEquality` (line 59) — update constructor calls to match new signature.

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: BUILD FAILS — `InstanceName` not yet defined on `MetricBinding` record. This is a compile error, not a test failure.

- [ ] **Step 3: Update MetricBinding record**

In `MetricBinding.cs`, change the record:

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
    string? InstanceName);
```

Remove `InstanceFilter` entirely. Note the parameter order change — `InstanceId` before `InstanceName`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: Model tests PASS. BindingConfigLoader tests will FAIL (expected — they still reference InstanceFilter). That's fine, we fix those in Task 2.

- [ ] **Step 5: Commit**

```bash
git add src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ModelTests.cs
git commit -m "replace InstanceFilter with InstanceName on MetricBinding

instance_name is the preferred human-readable instance selector;
instance_id kept as fallback for edge cases"
```

---

## Task 2: Update BindingConfigLoader — parse `instance_name`, remove `instance_filter`

**Files:**
- Modify: `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs:135-185, 230-244`
- Modify: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs`

- [ ] **Step 1: Update loader tests — replace instance_filter with instance_name**

In `BindingConfigLoaderTests.cs`:

**`Load_ValidBinding_ParsesAllBindingFields`** (line 66): Change TOML from `instance_filter = "sd*"` to `instance_name = "1 minute"`. Change assertion from `Assert.Equal("sd*", binding.InstanceFilter)` to `Assert.Equal("1 minute", binding.InstanceName)`.

**`Load_BindingWithInstanceId_ParsesCorrectly`** (line 97): Update assertion to check `Assert.Null(binding.InstanceName)` instead of `Assert.Null(binding.InstanceFilter)`.

**`Load_BothInstanceFilterAndId_SkipsBinding`** (line 447): Rename to `Load_BothInstanceNameAndId_SkipsBinding`. Change TOML from `instance_filter = "sd*"` to `instance_name = "1 minute"`. Update error message assertion from `"instance_filter and instance_id are mutually exclusive"` to `"instance_name and instance_id are mutually exclusive"`.

**Any other tests** that construct TOML with `instance_filter` or assert on `InstanceFilter` — update to `instance_name`/`InstanceName`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: BUILD FAILS — loader still references `instance_filter` which no longer exists on `MetricBinding`. This is a compile error, not a test failure.

- [ ] **Step 3: Update BindingConfigLoader**

In `BindingConfigLoader.cs`, update the `ValidateBinding` method (lines 135-185):

Replace the instance field parsing block (lines 158-170):

```csharp
var hasName = binding.TryGetValue("instance_name", out var nameObj);
var hasId = binding.TryGetValue("instance_id", out var idObj);

if (hasName && hasId)
{
    messages.Add(new ValidationMessage(ValidationSeverity.Error,
        "instance_name and instance_id are mutually exclusive",
        context));
    return null;
}

var instanceName = hasName ? nameObj?.ToString() : null;
int? instanceId = hasId ? Convert.ToInt32(idObj) : null;
```

Update the `MetricBinding` constructor call (around line 183) to pass `InstanceId: instanceId, InstanceName: instanceName` instead of the old `InstanceFilter`/`InstanceId` parameters.

Update `LogBindingInfo` (lines 230-244) to log `instance_name` context:
- If `InstanceName` is set, append `(instance: '{binding.InstanceName}')`
- If `InstanceId` is set, append `(instance: {binding.InstanceId})` (existing)

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: ALL tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs
git commit -m "parse instance_name in TOML, remove instance_filter support

instance_name and instance_id are mutually exclusive;
instance_filter was dead code (glob matching multiple instances
to one node is incoherent)"
```

---

## Task 3: Update `ParseInstancesResponse` to return `SeriesInstanceInfo` with name

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs:86-99`
- Modify: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs:265-319`

- [ ] **Step 1: Update tests for new return type**

In `SeriesQueryTests.cs`, update instance-related tests:

**`ParseInstancesResponse_MapsSeriesIdToPcpInstanceId`** (line 265): Rename to `ParseInstancesResponse_MapsSeriesIdToInstanceInfo`. The test JSON already contains `"name"` fields. Change assertions from:
```csharp
Assert.Equal(0, result["series_aaa"]);
Assert.Equal(1, result["series_bbb"]);
```
to:
```csharp
Assert.Equal(0, result["series_aaa"].PcpInstanceId);
Assert.Equal("1 minute", result["series_aaa"].Name);
Assert.Equal(1, result["series_bbb"].PcpInstanceId);
Assert.Equal("5 minute", result["series_bbb"].Name);
```

**`ParseInstancesResponse_DuplicateSeriesId_LastWins`** (line 292): Update assertions to use `.PcpInstanceId` and `.Name`.

**`ParseInstancesResponse_EmptyArray`** (line 284): No change needed (empty result is empty regardless of type).

Add the `"name"` field to any test JSON fixtures that don't already have it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "ParseInstancesResponse"`
Expected: BUILD FAILS — `SeriesInstanceInfo` doesn't exist yet, and assertions reference `.PcpInstanceId`/`.Name` on what's still a `Dictionary<string, int>`. This is a compile error, not a test failure.

- [ ] **Step 3: Add `SeriesInstanceInfo` record and update `ParseInstancesResponse`**

In `PcpSeriesQuery.cs`, add the record near the top (after the class opening):

```csharp
public record SeriesInstanceInfo(int PcpInstanceId, string Name);
```

Update `ParseInstancesResponse` (lines 86-99):

```csharp
public static Dictionary<string, SeriesInstanceInfo> ParseInstancesResponse(string json)
{
    using var doc = JsonDocument.Parse(json);
    var result = new Dictionary<string, SeriesInstanceInfo>();

    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var seriesId = item.GetProperty("series").GetString()!;
        var pcpInstanceId = item.GetProperty("id").GetInt32();
        var name = item.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString() ?? ""
            : "";
        result[seriesId] = new SeriesInstanceInfo(pcpInstanceId, name);
    }

    return result;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "ParseInstancesResponse"`
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "capture instance name from /series/instances response

ParseInstancesResponse now returns SeriesInstanceInfo with both
PcpInstanceId and Name instead of just int"
```

---

## Task 4: Fix archive mapping bug — use `sv.InstanceId` for instance lookup

**Files:**
- Modify: `godot-project/addons/pmview-bridge/MetricPoller.cs:39, 458-470, 501-538`
- Modify: `godot-project/addons/pmview-bridge/SceneBinder.cs:169-172`

This task modifies Godot-dependent code (no xUnit). Verified by manual testing.

- [ ] **Step 1: Fix `CreatePerInstanceBindings` constructor call in SceneBinder**

In `SceneBinder.cs`, the `CreatePerInstanceBindings` method (line 169-172) uses positional construction of `MetricBinding`. With the record signature change from Task 1, position 8 is now `InstanceId` (int?) and position 9 is `InstanceName` (string?). Update to use named arguments:

```csharp
var binding = new PcpGodotBridge.MetricBinding(
    clone.Name, metricName, property,
    sourceMin, sourceMax, targetMin, targetMax,
    InstanceId: instanceId, InstanceName: null);
```

- [ ] **Step 2: Update `_seriesInstanceMap` type and add cache clearing**

In `MetricPoller.cs`, change field declaration (line 39):

```csharp
// Before:
private readonly Dictionary<string, Dictionary<string, int>> _seriesInstanceMap = new();

// After:
private readonly Dictionary<string, Dictionary<string, SeriesInstanceInfo>> _seriesInstanceMap = new();
```

Add `using PcpClient;` at the top if not already present (for `SeriesInstanceInfo`).

- [ ] **Step 3: Update `EnsureSeriesInstanceMapping`**

In `EnsureSeriesInstanceMapping` (lines 501-538):

Line 516 (fallback for failed HTTP): change to:
```csharp
_seriesInstanceMap[metricName] = new Dictionary<string, SeriesInstanceInfo>();
```

Line 536 (fallback for exceptions): same change.

The `ParseInstancesResponse` call on line 521 already returns the new type, so line 523 (`_seriesInstanceMap[metricName] = mapping`) works as-is.

Update the diagnostic log (lines 525-529):
```csharp
var pairs = mapping.Select(kv => $"{kv.Key[..8]}..→{kv.Value.PcpInstanceId} ({kv.Value.Name})");
```

- [ ] **Step 4: Fix the instance mapping loop — use `sv.InstanceId`**

In `FetchHistoricalMetrics` (lines 458-470), replace the mapping loop:

```csharp
var instanceMap = _seriesInstanceMap.GetValueOrDefault(metricName);
var instances = new Godot.Collections.Dictionary();
foreach (var sv in resolvedValues)
{
    var lookupKey = sv.InstanceId ?? sv.SeriesId;
    int instanceKey = instanceMap != null
        && instanceMap.TryGetValue(lookupKey, out var info)
        ? info.PcpInstanceId
        : -1;
    instances[instanceKey] = sv.NumericValue;
    GD.Print($"[MetricPoller] {metricName}: instance {instanceKey} " +
        $"= {sv.NumericValue:F4}");
}
```

- [ ] **Step 5: Build and verify compilation**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet build godot-project/pmview-nextgen.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 6: Commit**

```bash
git add godot-project/addons/pmview-bridge/MetricPoller.cs godot-project/addons/pmview-bridge/SceneBinder.cs
git commit -m "fix archive mapping bug for multi-instance metrics

use sv.InstanceId (instance-level series ID) instead of
sv.SeriesId for the instance mapping lookup — fixes all
instance values collapsing to one key; also fix
CreatePerInstanceBindings constructor for new MetricBinding
signature"
```

---

## Task 5: Emit name→id mapping with metrics and resolve in SceneBinder

**Files:**
- Modify: `godot-project/addons/pmview-bridge/MetricPoller.cs:458-480, 556-580`
- Modify: `godot-project/addons/pmview-bridge/SceneBinder.cs:97-129, 307-335`

This task modifies Godot-dependent code (no xUnit). Verified by manual testing.

- [ ] **Step 1: Build and emit `name_to_id` dict in archive path**

In `FetchHistoricalMetrics`, after the instance mapping loop (around line 472), build the name→id dict from the cached mapping:

```csharp
// Build name→id lookup for SceneBinder
var nameToId = new Godot.Collections.Dictionary();
if (instanceMap != null)
{
    foreach (var kv in instanceMap.Values)
        nameToId[kv.Name] = kv.PcpInstanceId;
}
```

Update the metric dict emission (lines 474-480) to include `name_to_id`:

```csharp
if (instances.Count > 0)
{
    dict[metricName] = new Godot.Collections.Dictionary
    {
        ["timestamp"] = cursorPosition
            .Subtract(DateTime.UnixEpoch).TotalSeconds,
        ["instances"] = instances,
        ["name_to_id"] = nameToId
    };
}
```

- [ ] **Step 2: Emit `name_to_id` in live path**

Add a field to MetricPoller for caching live instance names:

```csharp
private readonly Dictionary<string, Dictionary<string, int>> _liveInstanceNames = new();
private readonly HashSet<string> _liveInstanceNamesPopulated = new();
```

Add cache clearing to session reset methods. In `StartPlayback` (line 83), after the existing `_seriesInstanceMap.Clear()`:

```csharp
_liveInstanceNames.Clear();
_liveInstanceNamesPopulated.Clear();
```

In `UpdateEndpoint` (line 142), add the same clearing after `StopPolling()`:

```csharp
_liveInstanceNames.Clear();
_liveInstanceNamesPopulated.Clear();
```

In `FetchLiveMetrics` (around line 273), after fetching values, populate the name map on first call per metric. Insert before the marshal call:

```csharp
foreach (var metricName in MetricNames)
{
    if (_liveInstanceNamesPopulated.Contains(metricName))
        continue;
    _liveInstanceNamesPopulated.Add(metricName);
    try
    {
        var indom = await _client!.GetInstanceDomainAsync(metricName);
        if (indom != null)
        {
            var nameMap = new Dictionary<string, int>();
            foreach (var inst in indom.Instances)
                nameMap[inst.Name] = inst.Id;
            _liveInstanceNames[metricName] = nameMap;
            GD.Print($"[MetricPoller] Live instance names for {metricName}: " +
                string.Join(", ", nameMap.Select(kv => $"{kv.Key}→{kv.Value}")));
        }
    }
    catch (Exception ex)
    {
        GD.PushWarning($"[MetricPoller] Instance domain lookup failed for {metricName}: {ex.Message}");
    }
}
```

Update `MarshalMetricValues` (lines 556-580) to accept the live name map and include `name_to_id`:

Change the method signature and add name→id emission:

```csharp
private Godot.Collections.Dictionary MarshalMetricValues(
    IReadOnlyList<MetricValue> values)
{
    var dict = new Godot.Collections.Dictionary();

    foreach (var metric in values)
    {
        var instances = new Godot.Collections.Dictionary();
        foreach (var iv in metric.InstanceValues)
        {
            var key = iv.InstanceId ?? -1;
            instances[key] = iv.Value;
        }

        var nameToId = new Godot.Collections.Dictionary();
        if (_liveInstanceNames.TryGetValue(metric.Name, out var nameMap))
        {
            foreach (var kv in nameMap)
                nameToId[kv.Key] = kv.Value;
        }

        var metricDict = new Godot.Collections.Dictionary
        {
            ["timestamp"] = metric.Timestamp,
            ["instances"] = instances,
            ["name_to_id"] = nameToId
        };

        dict[metric.Name] = metricDict;
    }

    return dict;
}
```

Remove the `static` modifier from `MarshalMetricValues` since it now accesses `_liveInstanceNames`.

- [ ] **Step 3: Update SceneBinder to resolve `instance_name`**

In `SceneBinder.cs`, update `ApplyMetrics` (lines 97-129). After extracting `instances`, also extract `name_to_id`:

```csharp
var instances = metricData["instances"].AsGodotDictionary();
var nameToId = metricData.ContainsKey("name_to_id")
    ? metricData["name_to_id"].AsGodotDictionary()
    : new Godot.Collections.Dictionary();

double? rawValue = ExtractValue(binding, instances, nameToId);
```

Update `ExtractValue` (lines 307-335) — new signature and logic:

```csharp
private static double? ExtractValue(MetricBinding binding,
    Godot.Collections.Dictionary instances,
    Godot.Collections.Dictionary nameToId)
{
    if (binding.InstanceName != null)
    {
        if (!nameToId.ContainsKey(binding.InstanceName))
        {
            GD.Print($"[SceneBinder] {binding.SceneNode}: instance name " +
                $"'{binding.InstanceName}' not found for {binding.Metric} " +
                $"(available: {string.Join(", ", nameToId.Keys)})");
            return null;
        }
        var resolvedId = nameToId[binding.InstanceName].AsInt32();
        return instances.ContainsKey(resolvedId)
            ? instances[resolvedId].AsDouble()
            : null;
    }

    if (binding.InstanceId != null)
    {
        return instances.ContainsKey(binding.InstanceId.Value)
            ? instances[binding.InstanceId.Value].AsDouble()
            : null;
    }

    // Singular metric (key -1) or first available instance
    if (instances.ContainsKey(-1))
        return instances[-1].AsDouble();

    foreach (var key in instances.Keys)
        return instances[key].AsDouble();

    return null;
}
```

Remove the old `instance_filter` branch entirely.

- [ ] **Step 4: Build and verify compilation**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet build godot-project/pmview-nextgen.sln`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add godot-project/addons/pmview-bridge/MetricPoller.cs godot-project/addons/pmview-bridge/SceneBinder.cs
git commit -m "resolve instance_name bindings via name-to-id lookup

MetricPoller emits name_to_id mapping alongside instance values
for both live and archive paths; SceneBinder resolves
instance_name to numeric ID at bind time"
```

---

## Task 6: Update `test_bars.toml` to use `instance_name`

**Files:**
- Modify: `godot-project/bindings/test_bars.toml`

- [ ] **Step 1: Update TOML config**

Replace `instance_id` with `instance_name` in `test_bars.toml`:

```toml
# CPU load bar visualisation
# Maps kernel.all.load instances to vertical bar height

[meta]
scene = "res://scenes/test_bars.tscn"
poll_interval_ms = 250
description = "CPU load averages as vertical bars"

[[bindings]]
scene_node = "LoadBar1Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_name = "1 minute"

[[bindings]]
scene_node = "LoadBar5Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_name = "5 minute"

[[bindings]]
scene_node = "LoadBar15Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_name = "15 minute"
```

Note: `disk_io_panel.toml` stays on `instance_id` — disk device names are environment-specific.

- [ ] **Step 2: Run all pure .NET tests as final sanity check**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln && dotnet test src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/`
Expected: ALL PASS

- [ ] **Step 3: Commit**

```bash
git add godot-project/bindings/test_bars.toml
git commit -m "use instance_name in test_bars.toml for load averages

human-readable instance names instead of fragile numeric IDs
that vary between host and container environments"
```

---

## Manual Verification Checklist (post-implementation)

After all tasks, user verifies in Godot:

1. Launch test_bars scene in archive playback mode
2. Confirm all three load average bars move independently
3. Check Godot output for `[MetricPoller] Instance mapping for kernel.all.load:` showing name→id mapping
4. Check `[SceneBinder]` logs show no "instance name not found" warnings
5. Launch disk_io_panel scene — confirm `instance_id` bindings still work
