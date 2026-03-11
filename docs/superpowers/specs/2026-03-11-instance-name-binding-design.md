# Instance Name Binding Design

**Date:** 2026-03-11
**Status:** Approved

## Problem

PCP instance IDs are opaque numeric identifiers that vary between environments (host vs container, different PCP versions). The current TOML binding config requires users to know these magic numbers:

```toml
instance_id = 1  # What is this? Who knows.
```

Additionally, the archive playback path has a bug where all instance values for a multi-instance metric collapse to a single instance key, because the mapping code uses `sv.SeriesId` instead of `sv.InstanceId`.

## Solution

Add `instance_name` as the preferred instance selection mechanism in binding configs:

```toml
instance_name = "1 minute"  # Human-readable, portable
```

Fix the archive mapping bug so multi-instance metrics work correctly in playback.

## Design

### 1. Config Model

**Add** `instance_name` (string, exact match on PCP instance name).
**Remove** `instance_filter` (dead code, glob-matching multiple instances to one node is incoherent).
**Keep** `instance_id` as fallback escape hatch for edge cases.

`instance_name` and `instance_id` are mutually exclusive — at most one per binding.

`instance_name` is the **preferred** config mechanism. `instance_id` is secondary, documented as a fallback for metrics with non-unique or missing instance names.

**MetricBinding record:**
```csharp
public record MetricBinding(
    string SceneNode, string Metric, string Property,
    double SourceRangeMin, double SourceRangeMax,
    double TargetRangeMin, double TargetRangeMax,
    int? InstanceId,
    string? InstanceName);
```

### 2. Data Pipeline — Capturing Instance Names

**PcpSeriesQuery.ParseInstancesResponse** currently returns `Dictionary<string, int>` (series→id) and discards the `name` field from the `/series/instances` JSON response. Change to:

```csharp
public record SeriesInstanceInfo(int PcpInstanceId, string Name);
// Returns: Dictionary<string, SeriesInstanceInfo>
```

**MetricPoller `_seriesInstanceMap`** changes from `Dictionary<string, Dictionary<string, int>>` to `Dictionary<string, Dictionary<string, SeriesInstanceInfo>>`. The archive mapping loop then uses `info.PcpInstanceId` as the numeric instance key.

**MetricPoller also maintains a `_instanceNameMap`** — `Dictionary<string, Dictionary<string, int>>` mapping metric name → (instance name → PCP instance ID). This is the name→id lookup that SceneBinder uses.

**Archive path:** The `_instanceNameMap` is populated from `EnsureSeriesInstanceMapping` by inverting the `SeriesInstanceInfo` records — each `info.Name → info.PcpInstanceId` entry goes into the name map for that metric.

**Live path:** On first successful `FetchLiveMetrics`, for each metric that has any `instance_name` bindings, call `GetInstanceDomainAsync(metricName)` once and cache the result in `_instanceNameMap`. This is a one-time fetch per metric per connection, similar to how `InitialiseRateConverter` works. Guard with a `_instanceNameMapPopulated` set to avoid repeated calls.

### 3. Archive Mapping Bug Fix

In the archive value→instance mapping loop (`MetricPoller.cs` lines 458-467), the lookup key changes from `sv.SeriesId` to `sv.InstanceId ?? sv.SeriesId`. After the `ParseInstancesResponse` type change, the full fix:

```csharp
// _seriesInstanceMap is now Dictionary<string, Dictionary<string, SeriesInstanceInfo>>
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
}
```

This ensures each instance value gets its own key in the instances dictionary, rather than all collapsing to one.

### 4. SceneBinder Resolution — Transport Mechanism

The name→id mapping is emitted **inside the metrics dictionary** per metric. MetricPoller adds a `name_to_id` key alongside the existing `timestamp` and `instances` keys:

```csharp
dict[metricName] = new Godot.Collections.Dictionary
{
    ["timestamp"] = ...,
    ["instances"] = instances,
    ["name_to_id"] = nameToIdDict  // NEW: Godot.Collections.Dictionary<string, int>
};
```

SceneBinder's `ApplyMetrics` extracts this per-metric and passes it to `ExtractValue`:

```csharp
var nameToId = metricData.ContainsKey("name_to_id")
    ? metricData["name_to_id"].AsGodotDictionary()
    : new Godot.Collections.Dictionary();
double? rawValue = ExtractValue(binding, instances, nameToId);
```

`ExtractValue` resolution order:

1. `instance_name` set → resolve via `nameToId` lookup → numeric lookup in instances dict
2. `instance_id` set → direct numeric lookup (existing behaviour)
3. Neither set → singular metric (key -1) or first available

### 5. Scope

- Remove `instance_filter` from MetricBinding, BindingConfigLoader, and SceneBinder
- **Test changes:** existing `instance_filter` tests are **replaced** (not deleted) with equivalent `instance_name` tests:
  - `Load_ValidBinding_ParsesAllBindingFields` → assert `InstanceName` instead of `InstanceFilter`
  - `Load_BothInstanceFilterAndId_SkipsBinding` → becomes `Load_BothInstanceNameAndId_SkipsBinding`
  - `ModelTests` instance_filter cases → become instance_name cases
  - New tests: `ParseInstancesResponse` returns `SeriesInstanceInfo` with name, name→id lookup construction
- Update `test_bars.toml` to use `instance_name` (load average names are stable: "1 minute", "5 minute", "15 minute")
- **Keep `disk_io_panel.toml` on `instance_id`** — disk device names (`sda`, `nvme0n1`) are environment-specific, which is exactly the escape hatch `instance_id` exists for
- All changes covered by TDD (xUnit for pure .NET layers, manual verification for Godot bridge)
