# Hostname-Filtered Fleet Queries Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MetricPoller use hostname-filtered series API queries so each fleet host gets its own metric values instead of all hosts showing identical data.

**Architecture:** FleetMetricPoller performs centralised discovery (series_id → hostname mapping via `/series/labels`), distributes immutable copies to MetricPoller shards, which then poll with cached series IDs and emit `MetricsUpdated(hostname, metrics)` per host per tick. A separate `ShardPollCompleted` signal preserves the existing scrape budget tracking model.

**Tech Stack:** C# (.NET 8.0), Godot 4.6, PcpSeriesQuery (pure .NET), GDScript

**Spec:** `docs/superpowers/specs/2026-03-22-fleet-metric-poller-design.md`

---

## Chunk 1: PcpSeriesQuery — New URL Builders and Parsers

### Task 1: BuildMultiHostFilteredQueryUrl

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests for BuildMultiHostFilteredQueryUrl**

Add to the `SeriesQueryTests` class after the existing hostname-filtered tests (~line 763):

```csharp
// ── Multi-host filtered query URL building ──

[Fact]
public void BuildMultiHostFilteredQueryUrl_TwoHosts_BuildsOrChainedFilter()
{
    var baseUrl = new Uri("http://localhost:44322");
    var url = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
        baseUrl, "kernel.all.cpu.idle", ["host-01", "host-02"]);

    var urlStr = url.AbsoluteUri;
    Assert.Contains("/series/query", urlStr);
    // Decoded expr should be: kernel.all.cpu.idle{hostname=="host-01" || hostname=="host-02"}
    var decoded = Uri.UnescapeDataString(urlStr);
    Assert.Contains("hostname==\"host-01\"", decoded);
    Assert.Contains("hostname==\"host-02\"", decoded);
    Assert.Contains("||", decoded);
}

[Fact]
public void BuildMultiHostFilteredQueryUrl_SingleHost_NoOrOperator()
{
    var baseUrl = new Uri("http://localhost:44322");
    var url = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
        baseUrl, "kernel.all.load", ["app-server-1"]);

    var decoded = Uri.UnescapeDataString(url.AbsoluteUri);
    Assert.Contains("hostname==\"app-server-1\"", decoded);
    Assert.DoesNotContain("||", decoded);
}

[Fact]
public void BuildMultiHostFilteredQueryUrl_EmptyArray_ReturnsUnfilteredQuery()
{
    var baseUrl = new Uri("http://localhost:44322");
    var url = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
        baseUrl, "kernel.all.load", []);

    var decoded = Uri.UnescapeDataString(url.AbsoluteUri);
    Assert.Contains("kernel.all.load", decoded);
    Assert.DoesNotContain("hostname", decoded);
}

[Fact]
public void BuildMultiHostFilteredQueryUrl_SpecialCharsInHostname_EncodesCorrectly()
{
    var baseUrl = new Uri("http://localhost:44322");
    var url = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
        baseUrl, "kernel.all.load", ["web server.local", "app-01"]);

    var urlStr = url.AbsoluteUri;
    Assert.DoesNotContain(" ", urlStr);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "BuildMultiHostFilteredQueryUrl" --no-restore`
Expected: FAIL — method does not exist

- [ ] **Step 3: Implement BuildMultiHostFilteredQueryUrl**

Add to `PcpSeriesQuery` class in `PcpSeriesQuery.cs`, after `BuildHostnameFilteredQueryUrl` (~line 76):

```csharp
/// <summary>
/// Builds a /series/query URL with an OR-chained hostname label filter for
/// multiple hosts. Used during fleet discovery to batch hostname queries.
/// Empty hostnames array returns an unfiltered query.
/// </summary>
public static Uri BuildMultiHostFilteredQueryUrl(
    Uri baseUrl, string metricName, string[] hostnames)
{
    if (hostnames.Length == 0)
        return BuildQueryUrl(baseUrl, metricName);

    var clauses = hostnames
        .Select(h => $"hostname==\"{h}\"");
    var filter = $"{metricName}{{{string.Join(" || ", clauses)}}}";
    return new Uri(baseUrl, $"/series/query?expr={Uri.EscapeDataString(filter)}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "BuildMultiHostFilteredQueryUrl" --no-restore`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "Add BuildMultiHostFilteredQueryUrl for batched fleet discovery"
```

### Task 2: BuildPerSeriesLabelsUrl and ParsePerSeriesHostnameLabels

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `SeriesQueryTests` after the multi-host tests:

```csharp
// ── Per-series labels URL and parsing ──

[Fact]
public void BuildPerSeriesLabelsUrl_FormatsCorrectly()
{
    var baseUrl = new Uri("http://localhost:44322");
    var url = PcpSeriesQuery.BuildPerSeriesLabelsUrl(
        baseUrl, ["abc123", "def456"]);

    var urlStr = url.AbsoluteUri;
    Assert.Contains("/series/labels", urlStr);
    Assert.Contains("series=", urlStr);
    Assert.Contains("abc123", urlStr);
    Assert.Contains("def456", urlStr);
}

[Fact]
public void ParsePerSeriesHostnameLabels_ExtractsHostnames()
{
    // pmproxy /series/labels?series=... returns array of per-series label objects
    var json = """
    [
        {"series": "abc123", "hostname": "host-01"},
        {"series": "def456", "hostname": "host-02"},
        {"series": "ghi789", "hostname": "host-01"}
    ]
    """;

    var result = PcpSeriesQuery.ParsePerSeriesHostnameLabels(json);

    Assert.Equal(3, result.Count);
    Assert.Equal("host-01", result["abc123"]);
    Assert.Equal("host-02", result["def456"]);
    Assert.Equal("host-01", result["ghi789"]);
}

[Fact]
public void ParsePerSeriesHostnameLabels_MissingHostnameLabel_SkipsEntry()
{
    var json = """
    [
        {"series": "abc123", "hostname": "host-01"},
        {"series": "def456", "agent": "pmcd"}
    ]
    """;

    var result = PcpSeriesQuery.ParsePerSeriesHostnameLabels(json);

    Assert.Single(result);
    Assert.Equal("host-01", result["abc123"]);
}

[Fact]
public void ParsePerSeriesHostnameLabels_EmptyArray_ReturnsEmpty()
{
    var json = "[]";
    var result = PcpSeriesQuery.ParsePerSeriesHostnameLabels(json);
    Assert.Empty(result);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "PerSeries" --no-restore`
Expected: FAIL — methods do not exist

- [ ] **Step 3: Implement both methods**

Add to `PcpSeriesQuery` class after `BuildLabelsUrl` (~line 242):

```csharp
/// <summary>
/// Builds a /series/labels URL to look up labels for specific series IDs.
/// DISTINCT from BuildLabelsUrl which queries by label name (?names=).
/// This queries by series IDs (?series=) to get all labels for those series.
/// </summary>
public static Uri BuildPerSeriesLabelsUrl(
    Uri baseUrl, IEnumerable<string> seriesIds)
{
    var ids = string.Join(",", seriesIds);
    return new Uri(baseUrl, $"/series/labels?series={Uri.EscapeDataString(ids)}");
}

/// <summary>
/// Parses a /series/labels?series=... response to extract hostname labels.
/// Returns series_id → hostname mapping. Skips entries without a hostname label.
/// DISTINCT from ParseLabelsResponse which returns a flat list of label values.
/// </summary>
public static Dictionary<string, string> ParsePerSeriesHostnameLabels(string json)
{
    using var doc = JsonDocument.Parse(json);
    var result = new Dictionary<string, string>();

    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var seriesId = item.GetProperty("series").GetString();
        if (seriesId == null)
            continue;

        if (item.TryGetProperty("hostname", out var hostnameProp))
        {
            var hostname = hostnameProp.GetString();
            if (hostname != null)
                result[seriesId] = hostname;
        }
    }

    return result;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "PerSeries|BuildMultiHostFilteredQueryUrl" --no-restore`
Expected: PASS (all 8 tests)

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "Add per-series labels URL builder and hostname parser for fleet discovery"
```

## Chunk 2: MetricPoller Signal Signature Change

### Task 3: Update MetricsUpdated signal to include hostname

This is the breaking change — all subscribers must be updated. The signal gains a
`string hostname` first parameter. Existing single-host paths pass `Hostname` property.

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`
- Modify: `src/pmview-bridge-addon/test/MetricPollerTests.cs`
- Modify: `src/pmview-bridge-addon/test/e2e/LiveMetricPollingTests.cs`
- Modify: `src/pmview-bridge-addon/test/e2e/BindingPipelineTests.cs`
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd`
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Update the delegate signature in MetricPoller.cs**

Change line 21-22:

```csharp
// Before:
public delegate void MetricsUpdatedEventHandler(
    Godot.Collections.Dictionary metrics);

// After:
public delegate void MetricsUpdatedEventHandler(
    string hostname, Godot.Collections.Dictionary metrics);
```

- [ ] **Step 2: Update all EmitSignal calls in MetricPoller.cs**

There are three `EmitSignal(SignalName.MetricsUpdated, ...)` calls. Update each
to pass `Hostname` (or `""` if empty) as the first argument:

Line ~488 (ReplayLastMetrics):
```csharp
EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
```

Line ~533 (FetchLiveMetrics):
```csharp
EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
```

Line ~776 (FetchHistoricalMetrics):
```csharp
EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
```

- [ ] **Step 3: Update MetricPollerTests.cs subscriber**

Line 320 — update lambda signature:
```csharp
// Before:
poller.MetricsUpdated += _ => emitted = true;
// After:
poller.MetricsUpdated += (_, _) => emitted = true;
```

- [ ] **Step 4: Update LiveMetricPollingTests.cs subscribers**

Line 80:
```csharp
// Before:
poller.MetricsUpdated += metrics => receivedMetrics = metrics;
// After:
poller.MetricsUpdated += (_, metrics) => receivedMetrics = metrics;
```

Line 107 — same change.

- [ ] **Step 5: Update BindingPipelineTests.cs subscriber**

Line 62:
```csharp
// Before:
poller.MetricsUpdated += metrics =>
// After:
poller.MetricsUpdated += (_, metrics) =>
```

- [ ] **Step 6: Update FleetMetricPoller.cs subscriber and handler**

Line 148:
```csharp
// Before:
shard.MetricsUpdated += OnShardMetricsUpdated;
// After:
shard.MetricsUpdated += OnShardMetricsUpdated;
```

Line 184 — update handler signature:
```csharp
// Before:
private void OnShardMetricsUpdated(Godot.Collections.Dictionary metrics)
// After:
private void OnShardMetricsUpdated(string hostname, Godot.Collections.Dictionary metrics)
```

(The body of this handler will be reworked in Task 6, but the signature must match now.)

- [ ] **Step 7: Update host_view_controller.gd**

Line 25 — The `poller.connect("MetricsUpdated", binder.ApplyMetrics)` call passes
`MetricsUpdated(hostname, metrics)` to `ApplyMetrics(metrics)` — arity mismatch.
Wrap it:

```gdscript
# Before:
poller.connect("MetricsUpdated", binder.ApplyMetrics)

# After — lambda ignores hostname, passes metrics through:
poller.connect("MetricsUpdated", func(_hostname: String, metrics: Dictionary) -> void:
    binder.ApplyMetrics(metrics)
)
```

- [ ] **Step 8: Update HostViewController.gd**

Line 68 — update signal connection and handler:

```gdscript
# Before:
metric_poller.MetricsUpdated.connect(_on_metrics_updated_for_detail)

# After:
metric_poller.MetricsUpdated.connect(_on_metrics_updated_for_detail)
```

Line 558 — update handler signature:
```gdscript
# Before:
func _on_metrics_updated_for_detail(_metrics: Dictionary) -> void:

# After:
func _on_metrics_updated_for_detail(_hostname: String, _metrics: Dictionary) -> void:
```

- [ ] **Step 9: Build and run tests**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" --no-restore`
Expected: PASS — all existing tests pass with updated signatures

- [ ] **Step 10: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs \
        src/pmview-bridge-addon/test/MetricPollerTests.cs \
        src/pmview-bridge-addon/test/e2e/LiveMetricPollingTests.cs \
        src/pmview-bridge-addon/test/e2e/BindingPipelineTests.cs \
        src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs \
        src/pmview-bridge-addon/addons/pmview-bridge/host_view_controller.gd \
        src/pmview-app/scripts/HostViewController.gd
git commit -m "Add hostname parameter to MetricsUpdated signal

All subscribers updated for new (hostname, metrics) signature.
Single-host paths pass Hostname property; fleet path will use
per-host hostname from cached series map."
```

### Task 4: Add ShardPollCompleted signal to MetricPoller

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`

- [ ] **Step 1: Add signal delegate**

Add after the `PlaybackPositionChangedEventHandler` delegate (~line 36):

```csharp
[Signal]
public delegate void ShardPollCompletedEventHandler();
```

No tests needed yet — this signal is only emitted by the new `FetchSeriesMetricsForHosts`
method (Task 7). The declaration is a prerequisite for Task 6.

- [ ] **Step 2: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs
git commit -m "Add ShardPollCompleted signal declaration for fleet budget tracking"
```

## Chunk 3: FleetMetricPoller — Centralised Discovery and Per-Host Signal Routing

### Task 5: Add FleetMetricPoller centralised discovery

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`
- Test: `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs`

This task adds `DiscoverSeriesMapping()` to FleetMetricPoller. Because it makes HTTP
calls, the pure logic (partitioning series maps by shard) is tested via a static helper
on `FleetMetricNormaliser` (pure .NET, no Godot HTTP deps).

- [ ] **Step 1: Write failing test for PartitionSeriesMapByShard**

Add to `FleetMetricNormaliserTests.cs`:

```csharp
// ── Series map partitioning ───────────────────────────────────────────

[TestCase]
public void PartitionSeriesMapByShard_DistributesCorrectly()
{
    var shardAssignment = new FleetMetricNormaliser.ShardAssignment(
        [["host-01", "host-02"], ["host-03"]],
        []);

    var seriesIdToHostname = new Dictionary<string, string>
    {
        ["aaa"] = "host-01",
        ["bbb"] = "host-02",
        ["ccc"] = "host-01",
        ["ddd"] = "host-03",
    };

    var seriesIdsPerMetric = new Dictionary<string, List<string>>
    {
        ["cpu.idle"] = ["aaa", "bbb", "ccc", "ddd"],
    };

    var partitioned = FleetMetricNormaliser.PartitionSeriesMapByShard(
        shardAssignment, seriesIdToHostname, seriesIdsPerMetric);

    // Shard 0 has host-01 and host-02 → series aaa, bbb, ccc
    AssertThat(partitioned).HasSize(2);
    var shard0 = partitioned[0];
    AssertThat(shard0.SeriesIdToHostname).HasSize(3);
    AssertThat(shard0.SeriesIdToHostname["aaa"]).IsEqual("host-01");
    AssertThat(shard0.SeriesIdsPerMetric["cpu.idle"]).HasSize(3);

    // Shard 1 has host-03 → series ddd
    var shard1 = partitioned[1];
    AssertThat(shard1.SeriesIdToHostname).HasSize(1);
    AssertThat(shard1.SeriesIdToHostname["ddd"]).IsEqual("host-03");
    AssertThat(shard1.SeriesIdsPerMetric["cpu.idle"]).HasSize(1);
}

[TestCase]
public void PartitionSeriesMapByShard_EmptyMap_ReturnsEmptyPartitions()
{
    var shardAssignment = new FleetMetricNormaliser.ShardAssignment(
        [["host-01"]], []);
    var seriesIdToHostname = new Dictionary<string, string>();
    var seriesIdsPerMetric = new Dictionary<string, List<string>>();

    var partitioned = FleetMetricNormaliser.PartitionSeriesMapByShard(
        shardAssignment, seriesIdToHostname, seriesIdsPerMetric);

    AssertThat(partitioned).HasSize(1);
    AssertThat(partitioned[0].SeriesIdToHostname).IsEmpty();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "PartitionSeriesMapByShard" --no-restore`
Expected: FAIL — method does not exist

- [ ] **Step 3: Implement PartitionSeriesMapByShard on FleetMetricNormaliser**

Add to `FleetMetricNormaliser.cs`:

```csharp
/// <summary>
/// Result of partitioning the global series map for one shard.
/// Contains only the series IDs relevant to hosts assigned to that shard.
/// All collections are independent copies — no shared mutable state.
/// </summary>
public record ShardSeriesMap(
    IReadOnlyDictionary<string, string> SeriesIdToHostname,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SeriesIdsPerMetric);

/// <summary>
/// Partitions a global series_id → hostname map into per-shard subsets
/// based on the shard assignment. Each shard gets an independent immutable copy
/// containing only the series IDs for hosts assigned to that shard.
/// </summary>
public static IReadOnlyList<ShardSeriesMap> PartitionSeriesMapByShard(
    ShardAssignment assignment,
    Dictionary<string, string> seriesIdToHostname,
    Dictionary<string, List<string>> seriesIdsPerMetric)
{
    var result = new List<ShardSeriesMap>();

    foreach (var shardHosts in assignment.Shards)
    {
        var hostSet = new HashSet<string>(shardHosts);

        // Filter series_id → hostname to only this shard's hosts
        var filteredMap = seriesIdToHostname
            .Where(kv => hostSet.Contains(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Filter series IDs per metric to only this shard's series
        var filteredSeriesIds = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (metric, allIds) in seriesIdsPerMetric)
        {
            var shardIds = allIds
                .Where(id => filteredMap.ContainsKey(id))
                .ToList();
            if (shardIds.Count > 0)
                filteredSeriesIds[metric] = shardIds;
        }

        result.Add(new ShardSeriesMap(filteredMap, filteredSeriesIds));
    }

    return result;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "PartitionSeriesMapByShard" --no-restore`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs \
        src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs
git commit -m "Add PartitionSeriesMapByShard for distributing discovery results to shards"
```

### Task 6: Add InitialiseWithCachedSeriesMap to MetricPoller

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`
- Test: `src/pmview-bridge-addon/test/MetricPollerTests.cs`

- [ ] **Step 1: Write failing test**

Add to `MetricPollerTests.cs`:

```csharp
// ── Cached series map initialisation ──────────────────────────────────

[TestCase]
[RequireGodotRuntime]
public async Task InitialiseWithCachedSeriesMap_StoresMap()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var mock = new MockPcpClient();
    var poller = new TestableMetricPoller(mock);
    runner.Scene().AddChild(poller);

    var seriesIdToHostname = new Dictionary<string, string>
    {
        ["abc123"] = "host-01",
        ["def456"] = "host-02",
    };
    var seriesIdsPerMetric = new Dictionary<string, IReadOnlyList<string>>
    {
        ["kernel.all.cpu.idle"] = new List<string> { "abc123", "def456" },
    };

    poller.InitialiseWithCachedSeriesMap(seriesIdToHostname, seriesIdsPerMetric);

    AssertThat(poller.HasCachedSeriesMap).IsTrue();
    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "InitialiseWithCachedSeriesMap" --no-restore`
Expected: FAIL — method and property do not exist

- [ ] **Step 3: Implement InitialiseWithCachedSeriesMap**

Add to `MetricPoller.cs` after `UpdateMetricNames` (~line 103):

```csharp
private IReadOnlyDictionary<string, string>? _cachedSeriesIdToHostname;
private IReadOnlyDictionary<string, IReadOnlyList<string>>? _cachedSeriesIdsPerMetric;

/// <summary>
/// Whether this poller has a pre-cached series map from FleetMetricPoller
/// discovery. When true, uses FetchSeriesMetricsForHosts instead of the
/// standard live/historical fetch paths.
/// </summary>
public bool HasCachedSeriesMap => _cachedSeriesIdToHostname != null;

/// <summary>
/// Initialise with a pre-resolved series map from centralised discovery.
/// Caller must pass independent immutable copies — no shared state between shards.
/// Skips per-shard discovery; shard goes straight to polling with cached data.
/// </summary>
public void InitialiseWithCachedSeriesMap(
    IReadOnlyDictionary<string, string> seriesIdToHostname,
    IReadOnlyDictionary<string, IReadOnlyList<string>> seriesIdsPerMetric)
{
    _cachedSeriesIdToHostname = seriesIdToHostname;
    _cachedSeriesIdsPerMetric = seriesIdsPerMetric;
}
```

Add `using System.Collections.ObjectModel;` to the top if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "InitialiseWithCachedSeriesMap" --no-restore`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs \
        src/pmview-bridge-addon/test/MetricPollerTests.cs
git commit -m "Add InitialiseWithCachedSeriesMap for fleet discovery cache injection"
```

## Chunk 4: MetricPoller — FetchSeriesMetricsForHosts and Fetch Path Routing

### Task 7: Implement FetchSeriesMetricsForHosts

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`
- Test: `src/pmview-bridge-addon/test/MetricPollerTests.cs`

This is the core new fetch path. It uses the cached series map to query per-metric
values, partitions by hostname, and fires `MetricsUpdated(hostname, metrics)` per host.

**IMPORTANT:** All fleet metrics (`kernel.all.cpu.idle`, `disk.all.read_bytes`, etc.) are
counters. Without rate conversion, raw counter values produce nonsense normalisations.
This method MUST use `_rateConverter` and `ComputeRatesFromSeriesValues()` for counters,
with a wider time window (2.5× interval) to gather 2+ samples, mirroring
`FetchHistoricalMetrics`.

For multi-instance metrics (`network.interface.in.bytes`), multiple series values per
hostname/metric must be **summed**, not overwritten. The `NormaliseHostMetrics` in
FleetMetricPoller calls `ExtractRate` which sums all instances — so the dict must contain
separate instance keys, or a pre-summed singular value.

- [ ] **Step 1: Write failing test for fetch path routing**

Add to `MetricPollerTests.cs`:

```csharp
// ── Fetch path routing ────────────────────────────────────────────────

[TestCase]
[RequireGodotRuntime]
public async Task HasCachedSeriesMap_False_ByDefault()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var mock = new MockPcpClient();
    var poller = new TestableMetricPoller(mock);
    runner.Scene().AddChild(poller);

    AssertThat(poller.HasCachedSeriesMap).IsFalse();
    await runner.AwaitIdleFrame();
}

[TestCase]
[RequireGodotRuntime]
public async Task HasCachedSeriesMap_True_AfterInitialise()
{
    var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
    var mock = new MockPcpClient();
    var poller = new TestableMetricPoller(mock);
    runner.Scene().AddChild(poller);

    poller.InitialiseWithCachedSeriesMap(
        new Dictionary<string, string> { ["abc"] = "host-01" },
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["cpu.idle"] = new List<string> { "abc" }
        });

    AssertThat(poller.HasCachedSeriesMap).IsTrue();
    await runner.AwaitIdleFrame();
}
```

- [ ] **Step 2: Run tests to verify they fail/pass**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "HasCachedSeriesMap" --no-restore`
Expected: PASS (property exists from Task 6)

- [ ] **Step 3: Add the FetchSeriesMetricsForHosts method**

Add after `FetchHistoricalMetrics` (~line 778):

```csharp
/// <summary>
/// Fetches metric values using the cached series map from centralised discovery.
/// Queries one /series/values call per metric with all cached series IDs,
/// partitions results by hostname, and fires MetricsUpdated per host.
/// Handles counter metrics via rate conversion (needs 2+ samples).
/// Used for both fleet live mode (near-now window) and fleet playback.
/// </summary>
private async Task FetchSeriesMetricsForHosts()
{
    if (_cachedSeriesIdToHostname == null || _cachedSeriesIdsPerMetric == null)
        return;

    var endpointUri = new Uri(Endpoint);

    // Time window: live = near-now, playback = cursor position
    var windowEnd = _timeCursor.Mode == CursorMode.Live
        ? DateTime.UtcNow
        : _timeCursor.Position;

    // Collect values per hostname across all metrics
    var hostMetrics = new Dictionary<string, Godot.Collections.Dictionary>();

    foreach (var (metricName, seriesIds) in _cachedSeriesIdsPerMetric)
    {
        try
        {
            // Counters need 2 samples for rate conversion — widen the window
            var isCounter = _rateConverter?.IsCounter(metricName) == true;
            var windowSeconds = isCounter
                ? (_archiveSamplingIntervalSeconds > 0
                    ? _archiveSamplingIntervalSeconds * 2.5 : 5.0)
                : (_archiveSamplingIntervalSeconds > 0
                    ? _archiveSamplingIntervalSeconds * 1.5 : 2.0);

            var valuesUrl = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
                endpointUri, seriesIds, windowEnd, windowSeconds: windowSeconds);

            var response = await _sharedHttpClient.GetAsync(valuesUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.LogError("Series values query failed for {Metric}: {StatusCode}",
                    metricName, response.StatusCode);
                continue;
            }

            var json = await response.Content.ReadAsStringAsync();
            var seriesValues = PcpSeriesQuery.ParseValuesResponse(json);

            if (seriesValues.Count == 0)
                continue;

            // Counters: compute per-second rates from consecutive samples
            // Instant/discrete: take latest timestamp's values directly
            IReadOnlyList<SeriesValue> resolvedValues;
            if (isCounter)
            {
                resolvedValues = PcpSeriesQuery.ComputeRatesFromSeriesValues(
                    seriesValues);
                if (resolvedValues.Count == 0)
                    continue;
            }
            else
            {
                var latestTimestamp = seriesValues.Max(v => v.Timestamp);
                resolvedValues = seriesValues
                    .Where(v => Math.Abs(v.Timestamp - latestTimestamp) < 1.0
                        && !v.IsString)
                    .ToList();
            }

            // Partition by hostname using cached series_id → hostname map.
            // Sum values per hostname/metric for multi-instance metrics
            // (e.g. network.interface.in.bytes has one series per interface).
            var hostSums = new Dictionary<string, double>();
            foreach (var sv in resolvedValues)
            {
                var lookupKey = sv.InstanceId ?? sv.SeriesId;
                // Try instance hash first, then series hash
                if (!_cachedSeriesIdToHostname.TryGetValue(lookupKey, out var hostname)
                    && !_cachedSeriesIdToHostname.TryGetValue(sv.SeriesId, out hostname))
                {
                    continue; // Unknown series — skip
                }

                if (hostSums.TryGetValue(hostname, out var existing))
                    hostSums[hostname] = existing + sv.NumericValue;
                else
                    hostSums[hostname] = sv.NumericValue;
            }

            // Build the metric dict entries per hostname
            foreach (var (hostname, summedValue) in hostSums)
            {
                if (!hostMetrics.TryGetValue(hostname, out var hostDict))
                {
                    hostDict = new Godot.Collections.Dictionary();
                    hostMetrics[hostname] = hostDict;
                }

                hostDict[metricName] = new Godot.Collections.Dictionary
                {
                    ["timestamp"] = windowEnd
                        .Subtract(DateTime.UnixEpoch).TotalSeconds,
                    ["instances"] = new Godot.Collections.Dictionary
                    {
                        [-1] = summedValue
                    },
                    ["name_to_id"] = new Godot.Collections.Dictionary()
                };
            }
        }
        catch (Exception ex)
        {
            Log.LogError("Series fetch error for {Metric}: {Message}",
                metricName, ex.Message);
        }
    }

    // Emit per-host signals
    foreach (var (hostname, metrics) in hostMetrics)
    {
        InjectVirtualMetricsForHost(metrics, hostname);
        _lastEmittedMetrics = metrics;
        EmitSignal(SignalName.MetricsUpdated, hostname, metrics);
    }

    EmitSignal(SignalName.ShardPollCompleted);
}
```

- [ ] **Step 2: Add InjectVirtualMetricsForHost helper**

Add after `InjectVirtualMetrics` (~line 900):

```csharp
/// <summary>
/// Variant of InjectVirtualMetrics that takes an explicit hostname
/// parameter instead of reading the Hostname property. Used by the fleet
/// series path where each signal carries a different host's data.
/// </summary>
internal void InjectVirtualMetricsForHost(
    Godot.Collections.Dictionary dict, string hostname)
{
    var now = _timeCursor.Mode == CursorMode.Playback
        ? _timeCursor.Position
        : DateTime.UtcNow;

    dict["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
    {
        ["text_value"] = now.ToString("yyyy-MM-dd · HH:mm:ss")
    };

    if (!string.IsNullOrEmpty(hostname))
    {
        dict["pmview.meta.hostname"] = new Godot.Collections.Dictionary
        {
            ["text_value"] = hostname
        };
    }

    dict["pmview.meta.endpoint"] = new Godot.Collections.Dictionary
    {
        ["text_value"] = Endpoint
    };
}
```

- [ ] **Step 3: Wire the fetch path selection in OnPollTimerTimeout**

Modify `OnPollTimerTimeout()` — replace the existing fetch path selection (~lines 438-447):

```csharp
// Before:
if (_timeCursor.Mode == CursorMode.Live)
{
    await FetchLiveMetrics();
}
else
{
    await FetchHistoricalMetrics();
}

// After:
if (HasCachedSeriesMap)
{
    await FetchSeriesMetricsForHosts();
}
else if (_timeCursor.Mode == CursorMode.Live)
{
    await FetchLiveMetrics();
}
else
{
    await FetchHistoricalMetrics();
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet build pmview-nextgen.ci.slnf --no-restore`
Expected: Build succeeded

- [ ] **Step 5: Run all non-integration tests**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" --no-restore`
Expected: PASS — existing tests unaffected (no cached map = old paths)

- [ ] **Step 6: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs \
        src/pmview-bridge-addon/test/MetricPollerTests.cs
git commit -m "Add FetchSeriesMetricsForHosts for hostname-filtered fleet polling

Queries per-metric values with cached series IDs, partitions by hostname,
emits MetricsUpdated per host. Counter rate conversion and multi-instance
summing handled. ShardPollCompleted fires after all hosts."
```

## Chunk 5: FleetMetricPoller — Discovery Wiring and Signal Routing

### Task 8: Wire FleetMetricPoller discovery and per-host signal routing

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`

- [ ] **Step 1: Add DiscoverSeriesMapping method**

Replace the existing `DiscoverHostCapacities()` method (~line 164-176) with:

```csharp
/// <summary>
/// Centralised discovery: queries series IDs for all fleet metrics across
/// all assigned hostnames, maps series_id → hostname via /series/labels,
/// then distributes immutable per-shard subsets to each MetricPoller.
/// </summary>
private async Task DiscoverSeriesMapping()
{
    if (_assignment == null) return;

    var allHostnames = _assignment.Shards.SelectMany(s => s).ToArray();
    var endpointUri = new Uri(Endpoint);
    var allSeriesIds = new HashSet<string>();
    var seriesIdsPerMetric = new Dictionary<string, List<string>>();

    // Phase 1: Query series IDs per metric, batched by hostname
    foreach (var metricName in FleetMetricNames)
    {
        var metricSeriesIds = new List<string>();
        for (var i = 0; i < allHostnames.Length; i += MaxHostsPerQueryBatch)
        {
            var batch = allHostnames
                .Skip(i)
                .Take(MaxHostsPerQueryBatch)
                .ToArray();
            try
            {
                var queryUrl = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
                    endpointUri, metricName, batch);
                var response = await _sharedHttpClient.GetAsync(queryUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Log.LogWarning(
                        "Discovery query failed for {Metric} batch {Batch}: {Status}",
                        metricName, i / MaxHostsPerQueryBatch, response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var ids = PcpSeriesQuery.ParseQueryResponse(json);
                metricSeriesIds.AddRange(ids);
                foreach (var id in ids)
                    allSeriesIds.Add(id);
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    "Discovery query error for {Metric}: {Message}",
                    metricName, ex.Message);
            }
        }

        if (metricSeriesIds.Count > 0)
            seriesIdsPerMetric[metricName] = metricSeriesIds;
    }

    if (allSeriesIds.Count == 0)
    {
        Log.LogWarning("Discovery found no series IDs — fleet polling will be idle");
        return;
    }

    // Phase 2: Map series_id → hostname via /series/labels
    Dictionary<string, string> seriesIdToHostname;
    try
    {
        var labelsUrl = PcpSeriesQuery.BuildPerSeriesLabelsUrl(
            endpointUri, allSeriesIds);
        var labelsResponse = await _sharedHttpClient.GetAsync(labelsUrl);
        if (!labelsResponse.IsSuccessStatusCode)
        {
            Log.LogWarning(
                "Discovery labels query failed: {Status}",
                labelsResponse.StatusCode);
            return;
        }

        var labelsJson = await labelsResponse.Content.ReadAsStringAsync();
        seriesIdToHostname = PcpSeriesQuery.ParsePerSeriesHostnameLabels(labelsJson);
    }
    catch (Exception ex)
    {
        Log.LogWarning("Discovery labels error: {Message}", ex.Message);
        return;
    }

    Log.LogInformation(
        "Discovery complete: {SeriesCount} series IDs mapped to {HostCount} hostnames across {MetricCount} metrics",
        seriesIdToHostname.Count, allHostnames.Length, seriesIdsPerMetric.Count);

    // Phase 3: Partition by shard and distribute
    var partitioned = FleetMetricNormaliser.PartitionSeriesMapByShard(
        _assignment, seriesIdToHostname, seriesIdsPerMetric);

    for (var i = 0; i < _shards.Count && i < partitioned.Count; i++)
    {
        _shards[i].InitialiseWithCachedSeriesMap(
            partitioned[i].SeriesIdToHostname,
            partitioned[i].SeriesIdsPerMetric);
        Log.LogInformation(
            "Shard {Index}: {SeriesCount} series IDs for {HostCount} hosts",
            i, partitioned[i].SeriesIdToHostname.Count,
            _assignment.Shards[i].Length);
    }
}
```

- [ ] **Step 2: Add MaxHostsPerQueryBatch constant and shared HttpClient**

Add at the top of `FleetMetricPoller` class:

```csharp
private const int MaxHostsPerQueryBatch = 20;
private readonly System.Net.Http.HttpClient _sharedHttpClient = new();
```

- [ ] **Step 3: Update DeferredStartShards to call DiscoverSeriesMapping**

Replace `DeferredStartShards` (~line 157):

```csharp
private async void DeferredStartShards()
{
    await DiscoverSeriesMapping();
    // Shards already auto-started via MetricPoller._Ready()
}
```

- [ ] **Step 4: Update FleetMetricsUpdated signal delegate**

Change line 20-21:
```csharp
// Before:
public delegate void FleetMetricsUpdatedEventHandler(
    Godot.Collections.Dictionary metrics);

// After:
public delegate void FleetMetricsUpdatedEventHandler(
    string hostname, Godot.Collections.Dictionary metrics);
```

- [ ] **Step 5: Replace OnShardMetricsUpdated with per-host handler + budget handler**

Replace the existing `OnShardMetricsUpdated` method and `NormaliseAllHosts` method
(~lines 184-282) with:

```csharp
private void OnShardMetricsUpdated(string hostname, Godot.Collections.Dictionary metrics)
{
    var normalised = NormaliseHostMetrics(hostname, metrics);
    EmitSignal(SignalName.FleetMetricsUpdated, hostname, normalised);
}

private void OnShardPollCompleted()
{
    _shardsCompletedThisTick++;

    if (_shardsCompletedThisTick < _shards.Count)
        return;

    var elapsed = _scrapeStopwatch?.ElapsedMilliseconds ?? 0;
    _budgetTracker?.RecordScrapeCompleted(elapsed);

    if (_budgetTracker is { IsLagging: true })
    {
        Log.LogWarning(
            "Fleet scrape took {Elapsed}ms, exceeding {Interval}ms interval",
            elapsed, PollIntervalMs);
        EmitSignal(SignalName.ScrapeBudgetExceeded);
    }

    _shardsCompletedThisTick = 0;
    _scrapeStopwatch = Stopwatch.StartNew();
}

/// <summary>
/// Normalise one host's raw metric values to 0.0–1.0 range.
/// </summary>
private Godot.Collections.Dictionary NormaliseHostMetrics(
    string hostname, Godot.Collections.Dictionary rawMetrics)
{
    var ncpu = _hostNcpu.GetValueOrDefault(hostname, 1);
    var hostDict = new Godot.Collections.Dictionary();

    var idleRate = ExtractRate(rawMetrics, "kernel.all.cpu.idle");
    hostDict["cpu"] = FleetMetricNormaliser.NormaliseCpu(idleRate, ncpu);

    var pgIn = ExtractRate(rawMetrics, "mem.vmstat.pgpgin");
    var pgOut = ExtractRate(rawMetrics, "mem.vmstat.pgpgout");
    hostDict["memory"] = FleetMetricNormaliser.NormaliseRate(
        pgIn, pgOut, PagingMaxPagesPerSec);

    var diskRead = ExtractRate(rawMetrics, "disk.all.read_bytes");
    var diskWrite = ExtractRate(rawMetrics, "disk.all.write_bytes");
    hostDict["disk"] = FleetMetricNormaliser.NormaliseRate(
        diskRead, diskWrite, DiskMaxBytesPerSec);

    var netIn = ExtractRate(rawMetrics, "network.interface.in.bytes");
    var netOut = ExtractRate(rawMetrics, "network.interface.out.bytes");
    hostDict["network"] = FleetMetricNormaliser.NormaliseRate(
        netIn, netOut, NetworkMaxBytesPerSec);

    return hostDict;
}
```

- [ ] **Step 6: Update CreateShardPollers to connect both signals**

Update the signal wiring in `CreateShardPollers` (~line 148-151):

```csharp
// Before:
shard.MetricsUpdated += OnShardMetricsUpdated;
if (i == 0)
    shard.ConnectionStateChanged += state =>
        EmitSignal(SignalName.ConnectionStateChanged, state);

// After:
shard.MetricsUpdated += OnShardMetricsUpdated;
shard.ShardPollCompleted += OnShardPollCompleted;
if (i == 0)
    shard.ConnectionStateChanged += state =>
        EmitSignal(SignalName.ConnectionStateChanged, state);
```

- [ ] **Step 7: Remove old NormaliseAllHosts and _shardResults**

Delete `NormaliseAllHosts` method and the `_shardResults` dictionary field — no longer
used. Keep `ExtractRate` (still used by `NormaliseHostMetrics`).

- [ ] **Step 8: Add Dispose for shared HttpClient**

Add or update `_ExitTree` equivalent:
```csharp
public override void _ExitTree()
{
    foreach (var shard in _shards)
        shard.QueueFree();
    _shards.Clear();
    _sharedHttpClient.Dispose();
}
```

- [ ] **Step 9: Build and run tests**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" --no-restore`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs
git commit -m "Wire FleetMetricPoller centralised discovery and per-host signal routing

DiscoverSeriesMapping replaces DiscoverHostCapacities. Discovery maps
series_id → hostname via /series/labels, partitions to shards.
OnShardMetricsUpdated now routes per-host. ShardPollCompleted drives
budget tracking."
```

## Chunk 6: GDScript Signal Handler Updates

### Task 9: Update FleetViewController.gd for per-host signals

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`

- [ ] **Step 1: Add _host_lookup dictionary**

Add field after the existing `_hosts` declaration (~line 24):

```gdscript
var _host_lookup: Dictionary = {}
```

- [ ] **Step 2: Populate _host_lookup in _build_grid**

Add at the end of the `for i in range(count)` loop in `_build_grid` (~line 71):

```gdscript
_host_lookup[hostnames[i]] = host_node
```

- [ ] **Step 3: Update signal connection in _setup_fleet_poller**

Update the signal connection (~line 307):

```gdscript
# Before:
fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)

# After:
fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)
```
(Connection stays the same — handler signature changes below.)

- [ ] **Step 4: Replace _on_fleet_metrics_updated handler**

Replace the handler (~lines 317-331):

```gdscript
# Before:
func _on_fleet_metrics_updated(metrics: Dictionary) -> void:
    _fleet_update_count += 1
    if _fleet_update_count <= 3 or _fleet_update_count % 10 == 0:
        var sample_host: String = ""
        for key: String in metrics:
            sample_host = key
            break
        if not sample_host.is_empty():
            print("[FleetView] Update #%d — sample host '%s': %s" % [
                _fleet_update_count, sample_host, metrics[sample_host]])
    for host: Node3D in _hosts:
        var data: Dictionary = metrics.get(host.hostname, {})
        for metric_name: String in data:
            host.set_metric_value(metric_name, data[metric_name])

# After:
func _on_fleet_metrics_updated(hostname: String, metrics: Dictionary) -> void:
    _fleet_update_count += 1
    if _fleet_update_count <= 3 or _fleet_update_count % 10 == 0:
        print("[FleetView] Update #%d — host '%s': %s" % [
            _fleet_update_count, hostname, metrics])
    var host: Node3D = _host_lookup.get(hostname)
    if not host:
        return
    for metric_name: String in metrics:
        host.set_metric_value(metric_name, metrics[metric_name])
```

- [ ] **Step 5: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd
git commit -m "Update FleetViewController for per-host FleetMetricsUpdated signals

Add _host_lookup dict for O(1) hostname routing. Handler now receives
one host per signal instead of all hosts in one dict."
```

### Task 10: Update HostViewController.gd for hostname-aware filtering

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Read current handler to confirm exact location**

Check `HostViewController.gd` lines around 558 for the full handler body.

- [ ] **Step 2: Update handler signature**

```gdscript
# Before:
func _on_metrics_updated_for_detail(_metrics: Dictionary) -> void:

# After:
func _on_metrics_updated_for_detail(_hostname: String, _metrics: Dictionary) -> void:
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Update HostViewController MetricsUpdated handler for hostname parameter"
```

### Task 11: Full build verification and diagnostic print cleanup timing

**Files:** None modified — verification only

- [ ] **Step 1: Full CI build and test**

Run: `PATH="/opt/homebrew/bin:$PATH" dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" --no-restore`
Expected: PASS — all tests green

- [ ] **Step 2: Verify no diagnostic prints need removing yet**

The `print()` statements in `FleetViewController.gd` `_setup_fleet_poller` are
kept intentionally until end-to-end visual verification passes in the Godot editor.
Do NOT remove them in this plan — they are the only boot confirmation mechanism
until the new discovery + polling path is proven working.

- [ ] **Step 3: Commit any fixups if needed, otherwise skip**

If the build required any fixups, commit them. Otherwise, this task is a no-op checkpoint.

---

## Known Issues (Out of Scope)

**Scrape budget stopwatch reset timing (pre-existing bug):**
The `_scrapeStopwatch` is reset at the **end** of a completed tick, not at the
**start** of the next one. This means elapsed time includes idle time between polls,
causing `ScrapeBudgetExceeded` to fire on every tick once the interval fills up.
This bug exists in the current code and is propagated here. Fix separately.

**`hinv.ncpu` discovery:**
CPU normalisation uses `ncpu = 1` for all hosts. For multi-core hosts, CPU
utilisation bars will look artificially high. Tracked separately from this refactor.

**`/series/labels?series=...` response shape:**
The `ParsePerSeriesHostnameLabels` implementation assumes the response is a JSON
array of `{series, hostname, ...}` objects. This matches the PCP Series API
documentation but has not been verified against a running pmproxy with real
multi-host data. If the actual shape differs, discovery will silently produce
empty maps. Verify during end-to-end testing with the dev-environment stack.
