# Fleet MetricPoller — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fleet-wide metric polling system that fetches aggregate health metrics for multiple hosts and drives the CompactHost bars in the fleet view with real normalised data.

**Architecture:** A `FleetMetricNormaliser` (pure C#, no Godot) handles sharding logic, host-capacity discovery, metric normalisation, and scrape budget tracking — all heavily unit-tested. A thin `FleetMetricPoller` (Godot Node) orchestrates MetricPoller instances and emits signals. A `WarningToast` (GDScript addon building block) provides generic transient warnings. `FleetViewController` wires it all together.

**Tech Stack:** C# .NET 8.0 (FleetMetricNormaliser, FleetMetricPoller), GDScript (WarningToast, FleetViewController wiring), gdUnit4 (C# unit tests), Godot 4.6

**Spec:** `docs/superpowers/specs/2026-03-22-fleet-metric-poller-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs` | Pure C# — sharding, normalisation, host-cap enforcement, scrape budget. No Godot deps. |
| `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs` | Godot Node — owns MetricPoller shards, aggregates signals, emits `FleetMetricsUpdated` |
| `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs` | gdUnit4 tests for sharding, normalisation, budget tracking |
| `src/pmview-bridge-addon/test/FleetMetricPollerTests.cs` | gdUnit4 tests for signal aggregation and poller orchestration |
| `src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.gd` | Generic warning toast — top-left HUD, orange/red severity, fade, cooldown |
| `src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.tscn` | Warning toast scene |

### Modified Files

| File | Changes |
|------|---------|
| `src/pmview-app/scripts/FleetViewController.gd` | Create FleetMetricPoller, connect signals, drive CompactHost bars |
| `src/pmview-app/scenes/fleet_view.tscn` | Add WarningToast instance to HUD |

---

## Chunk 1: FleetMetricNormaliser — Pure C# Logic

All sharding, normalisation, and budget logic in a Godot-free class. Fully testable with gdUnit4.

### Task 1: Sharding logic

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs`
- Create: `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs`

- [ ] **Step 1: Write failing test — shard assignment for small fleet**

```csharp
// FleetMetricNormaliserTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests;

[TestSuite]
public partial class FleetMetricNormaliserTests
{
    [TestCase]
    public void AssignShards_FiveHosts_SingleShard()
    {
        var hostnames = new[] { "h1", "h2", "h3", "h4", "h5" };
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(1);
        AssertThat(result.Shards[0]).HasSize(5);
        AssertThat(result.DroppedHosts).IsEmpty();
    }

    [TestCase]
    public void AssignShards_FiftyHosts_TwoShards()
    {
        var hostnames = Enumerable.Range(1, 50).Select(i => $"host-{i:D2}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(2);
        AssertThat(result.Shards[0]).HasSize(25);
        AssertThat(result.Shards[1]).HasSize(25);
        AssertThat(result.DroppedHosts).IsEmpty();
    }

    [TestCase]
    public void AssignShards_OverLimit_DropsExcessHosts()
    {
        var hostnames = Enumerable.Range(1, 260).Select(i => $"host-{i:D3}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(10);
        var totalAssigned = result.Shards.Sum(s => s.Length);
        AssertThat(totalAssigned).IsEqual(250);
        AssertThat(result.DroppedHosts).HasSize(10);
        AssertThat(result.DroppedHosts[0]).IsEqual("host-251");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd src/pmview-bridge-addon && dotnet test --filter "AssignShards" --verbosity quiet
```

Expected: FAIL — `FleetMetricNormaliser` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// FleetMetricNormaliser.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Pure C# logic for fleet metric polling: sharding, normalisation,
/// host-cap enforcement, and scrape budget tracking.
/// No Godot dependencies — fully unit-testable.
/// </summary>
public static class FleetMetricNormaliser
{
    public const int DefaultMaxHostsPerShard = 25;
    public const int DefaultMaxShards = 10;

    public record ShardAssignment(
        string[][] Shards,
        string[] DroppedHosts);

    public static ShardAssignment AssignShards(
        string[] hostnames,
        int maxHostsPerShard = DefaultMaxHostsPerShard,
        int maxShards = DefaultMaxShards)
    {
        var maxHosts = maxHostsPerShard * maxShards;
        string[] dropped = [];
        string[] accepted = hostnames;

        if (hostnames.Length > maxHosts)
        {
            accepted = hostnames[..maxHosts];
            dropped = hostnames[maxHosts..];
        }

        var shardCount = Math.Max(1,
            (int)Math.Ceiling((double)accepted.Length / maxHostsPerShard));
        shardCount = Math.Min(shardCount, maxShards);

        var shards = new string[shardCount][];
        for (var i = 0; i < shardCount; i++)
        {
            var start = i * maxHostsPerShard;
            var length = Math.Min(maxHostsPerShard, accepted.Length - start);
            shards[i] = accepted[start..(start + length)];
        }

        return new ShardAssignment(shards, dropped);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd src/pmview-bridge-addon && dotnet test --filter "AssignShards" --verbosity quiet
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs \
        src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs
git commit -m "Add FleetMetricNormaliser with shard assignment logic

Round-robin distribution of hosts across 1-10 shards, hard cap at
250 hosts with dropped host tracking."
```

---

### Task 2: CPU normalisation

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs`
- Modify: `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs`

- [ ] **Step 1: Write failing tests for CPU normalisation**

```csharp
[TestCase]
public void NormaliseCpu_HalfIdle_ReturnsHalfUtilisation()
{
    // 4 cores, idle rate = 2000ms/s (half of 4*1000)
    var result = FleetMetricNormaliser.NormaliseCpu(
        idleRate: 2000.0, ncpu: 4);
    AssertThat(result).IsEqualApprox(0.5, 0.001);
}

[TestCase]
public void NormaliseCpu_FullyBusy_ReturnsOne()
{
    // idle rate = 0
    var result = FleetMetricNormaliser.NormaliseCpu(
        idleRate: 0.0, ncpu: 8);
    AssertThat(result).IsEqualApprox(1.0, 0.001);
}

[TestCase]
public void NormaliseCpu_FullyIdle_ReturnsZero()
{
    var result = FleetMetricNormaliser.NormaliseCpu(
        idleRate: 4000.0, ncpu: 4);
    AssertThat(result).IsEqualApprox(0.0, 0.001);
}

[TestCase]
public void NormaliseCpu_ZeroCores_ReturnsZero()
{
    var result = FleetMetricNormaliser.NormaliseCpu(
        idleRate: 1000.0, ncpu: 0);
    AssertThat(result).IsEqualApprox(0.0, 0.001);
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement NormaliseCpu**

```csharp
public static double NormaliseCpu(double idleRate, int ncpu)
{
    if (ncpu <= 0) return 0.0;
    var maxIdle = ncpu * 1000.0;
    var utilisation = 1.0 - (idleRate / maxIdle);
    return Math.Clamp(utilisation, 0.0, 1.0);
}
```

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs \
        src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs
git commit -m "Add CPU normalisation to FleetMetricNormaliser

1.0 - (idle_rate / (ncpu * 1000)) clamped to 0-1."
```

---

### Task 3: Rate-based normalisation (paging, disk, network)

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs`
- Modify: `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[TestCase]
public void NormaliseRate_HalfMax_ReturnsHalf()
{
    var result = FleetMetricNormaliser.NormaliseRate(
        combinedRate: 5000.0, maxRate: 10000.0);
    AssertThat(result).IsEqualApprox(0.5, 0.001);
}

[TestCase]
public void NormaliseRate_ExceedsMax_ClampedToOne()
{
    var result = FleetMetricNormaliser.NormaliseRate(
        combinedRate: 20000.0, maxRate: 10000.0);
    AssertThat(result).IsEqualApprox(1.0, 0.001);
}

[TestCase]
public void NormaliseRate_Zero_ReturnsZero()
{
    var result = FleetMetricNormaliser.NormaliseRate(
        combinedRate: 0.0, maxRate: 10000.0);
    AssertThat(result).IsEqualApprox(0.0, 0.001);
}

[TestCase]
public void NormaliseRate_ZeroMax_ReturnsZero()
{
    var result = FleetMetricNormaliser.NormaliseRate(
        combinedRate: 5000.0, maxRate: 0.0);
    AssertThat(result).IsEqualApprox(0.0, 0.001);
}

[TestCase]
public void NormaliseRate_CombinesTwoInputs()
{
    var result = FleetMetricNormaliser.NormaliseRate(
        rate1: 3000.0, rate2: 2000.0, maxRate: 10000.0);
    AssertThat(result).IsEqualApprox(0.5, 0.001);
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement NormaliseRate**

```csharp
public static double NormaliseRate(double combinedRate, double maxRate)
{
    if (maxRate <= 0.0) return 0.0;
    return Math.Clamp(combinedRate / maxRate, 0.0, 1.0);
}

public static double NormaliseRate(double rate1, double rate2, double maxRate)
{
    return NormaliseRate(rate1 + rate2, maxRate);
}
```

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs \
        src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs
git commit -m "Add rate-based normalisation to FleetMetricNormaliser

Generic combined-rate / max-rate normaliser for paging, disk, network."
```

---

### Task 4: Scrape budget tracking

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs`
- Modify: `src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[TestCase]
public void ScrapeBudget_UnderBudget_NoSkip()
{
    var budget = new FleetMetricNormaliser.ScrapeBudgetTracker(
        pollIntervalMs: 2000);
    budget.RecordScrapeCompleted(elapsedMs: 1500);

    AssertThat(budget.ShouldSkipNextTick).IsFalse();
    AssertThat(budget.IsLagging).IsFalse();
}

[TestCase]
public void ScrapeBudget_OverBudget_SkipsNextTick()
{
    var budget = new FleetMetricNormaliser.ScrapeBudgetTracker(
        pollIntervalMs: 2000);
    budget.RecordScrapeCompleted(elapsedMs: 2500);

    AssertThat(budget.ShouldSkipNextTick).IsTrue();
    AssertThat(budget.IsLagging).IsTrue();
}

[TestCase]
public void ScrapeBudget_ConsumeSkip_ClearsFlag()
{
    var budget = new FleetMetricNormaliser.ScrapeBudgetTracker(
        pollIntervalMs: 2000);
    budget.RecordScrapeCompleted(elapsedMs: 2500);

    AssertThat(budget.ShouldSkipNextTick).IsTrue();
    budget.ConsumeSkip();
    AssertThat(budget.ShouldSkipNextTick).IsFalse();
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement ScrapeBudgetTracker**

```csharp
public class ScrapeBudgetTracker
{
    private readonly int _pollIntervalMs;

    public bool ShouldSkipNextTick { get; private set; }
    public bool IsLagging { get; private set; }

    public ScrapeBudgetTracker(int pollIntervalMs)
    {
        _pollIntervalMs = pollIntervalMs;
    }

    public void RecordScrapeCompleted(long elapsedMs)
    {
        IsLagging = elapsedMs > _pollIntervalMs;
        if (IsLagging)
            ShouldSkipNextTick = true;
    }

    public void ConsumeSkip()
    {
        ShouldSkipNextTick = false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricNormaliser.cs \
        src/pmview-bridge-addon/test/FleetMetricNormaliserTests.cs
git commit -m "Add scrape budget tracking to FleetMetricNormaliser

Detects when scrape time exceeds poll interval, flags skip-next-tick."
```

---

## Chunk 2: FleetMetricPoller — Godot Node

The Godot-side orchestrator that owns MetricPoller shards, performs discovery, and emits normalised per-host metrics.

### Task 5: FleetMetricPoller skeleton with shard creation

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`
- Create: `src/pmview-bridge-addon/test/FleetMetricPollerTests.cs`

- [ ] **Step 1: Write failing test for shard creation**

```csharp
// FleetMetricPollerTests.cs
using System;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using PmviewNextgen.Bridge;

namespace PmviewNextgen.Tests;

[TestSuite]
public partial class FleetMetricPollerTests
{
    [TestCase]
    public void ConfigureShards_CreatesCorrectNumberOfPollers()
    {
        var poller = new FleetMetricPoller();
        var hostnames = Enumerable.Range(1, 30)
            .Select(i => $"host-{i:D2}").ToArray();

        poller.ConfigureShards(hostnames, "http://localhost:44322", 2000);

        AssertThat(poller.ShardCount).IsEqual(2);
        AssertThat(poller.TotalHostCount).IsEqual(30);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

- [ ] **Step 3: Write FleetMetricPoller skeleton**

```csharp
// FleetMetricPoller.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Microsoft.Extensions.Logging;
using PmviewApp;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Godot Node that orchestrates multiple MetricPoller shards for fleet-wide
/// metric collection. Handles host sharding, discovery, normalisation,
/// and scrape budget tracking.
/// </summary>
public partial class FleetMetricPoller : Node
{
    [Signal]
    public delegate void FleetMetricsUpdatedEventHandler(
        Godot.Collections.Dictionary metrics);

    [Signal]
    public delegate void HostsDroppedEventHandler(int count, string[] hostnames);

    [Signal]
    public delegate void ScrapeBudgetExceededEventHandler();

    [Signal]
    public delegate void ConnectionStateChangedEventHandler(string state);

    [Export] public string Endpoint { get; set; } = "http://localhost:44322";
    [Export] public int PollIntervalMs { get; set; } = 2000;
    [Export] public int MaxHostsPerShard { get; set; } = 25;
    [Export] public int MaxShards { get; set; } = 10;
    [Export] public double DiskMaxBytesPerSec { get; set; } = 500_000_000;
    [Export] public double NetworkMaxBytesPerSec { get; set; } = 125_000_000;
    [Export] public double PagingMaxPagesPerSec { get; set; } = 10_000;

    private ILogger? _log;
    private ILogger Log => _log ??= PmviewLogger.GetLogger("FleetMetricPoller");

    private readonly List<MetricPoller> _shards = new();
    private FleetMetricNormaliser.ScrapeBudgetTracker? _budgetTracker;
    private readonly Dictionary<string, int> _hostNcpu = new();

    public int ShardCount => _shards.Count;
    public int TotalHostCount => _shards.Sum(
        s => s.MetricNames.Length > 0 ? 1 : 0); // placeholder

    /// <summary>
    /// Metrics polled per host for fleet view.
    /// </summary>
    public static readonly string[] FleetMetricNames =
    [
        "kernel.all.cpu.idle",
        "mem.vmstat.pgpgin",
        "mem.vmstat.pgpgout",
        "disk.all.read_bytes",
        "disk.all.write_bytes",
        "network.interface.in.bytes",
        "network.interface.out.bytes",
    ];

    public void ConfigureShards(
        string[] hostnames, string endpoint, int pollIntervalMs)
    {
        Endpoint = endpoint;
        PollIntervalMs = pollIntervalMs;
        _budgetTracker = new FleetMetricNormaliser.ScrapeBudgetTracker(
            pollIntervalMs);

        var assignment = FleetMetricNormaliser.AssignShards(
            hostnames, MaxHostsPerShard, MaxShards);

        if (assignment.DroppedHosts.Length > 0)
        {
            Log.LogWarning(
                "Fleet host cap reached. Dropped {Count} hosts: {Hosts}",
                assignment.DroppedHosts.Length,
                string.Join(", ", assignment.DroppedHosts));
            EmitSignal(SignalName.HostsDropped,
                assignment.DroppedHosts.Length, assignment.DroppedHosts);
        }

        // Shard creation — actual MetricPoller wiring comes in Task 6
        Log.LogInformation(
            "Fleet configured: {HostCount} hosts across {ShardCount} shards",
            hostnames.Length - assignment.DroppedHosts.Length,
            assignment.Shards.Length);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs \
        src/pmview-bridge-addon/test/FleetMetricPollerTests.cs
git commit -m "Add FleetMetricPoller skeleton with shard configuration

Godot Node that manages MetricPoller shards. ConfigureShards accepts
hostnames, enforces cap, emits HostsDropped signal."
```

---

### Task 6: Wire MetricPoller shards and signal aggregation

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs`

This task wires up actual MetricPoller child nodes per shard, connects their `MetricsUpdated` signals, aggregates results, normalises per-host values, and emits the unified `FleetMetricsUpdated` signal.

- [ ] **Step 1: Add MetricPoller shard creation to ConfigureShards**

In `ConfigureShards`, after the assignment is computed, create MetricPoller child nodes:

```csharp
foreach (var shardHosts in assignment.Shards)
{
    var shard = new MetricPoller
    {
        Name = $"Shard_{_shards.Count}",
        Endpoint = endpoint,
        PollIntervalMs = pollIntervalMs,
        MetricNames = FleetMetricNames,
    };
    // Store hostname-to-shard mapping for result partitioning
    foreach (var host in shardHosts)
        _hostToShard[host] = _shards.Count;

    shard.MetricsUpdated += OnShardMetricsUpdated;
    AddChild(shard);
    _shards.Add(shard);
}
```

- [ ] **Step 2: Implement OnShardMetricsUpdated aggregation**

Add fields and the handler that collects shard results, normalises, and emits:

```csharp
private readonly Dictionary<string, int> _hostToShard = new();
private readonly Dictionary<string, Godot.Collections.Dictionary> _pendingResults = new();
private Stopwatch? _scrapeStopwatch;
private int _shardsCompletedThisTick;

private void OnShardMetricsUpdated(Godot.Collections.Dictionary metrics)
{
    _shardsCompletedThisTick++;

    // Partition metrics by hostname from series instance metadata
    PartitionMetricsByHost(metrics);

    if (_shardsCompletedThisTick < _shards.Count)
        return; // wait for all shards

    // All shards reported — normalise and emit
    var elapsed = _scrapeStopwatch?.ElapsedMilliseconds ?? 0;
    _budgetTracker?.RecordScrapeCompleted(elapsed);

    if (_budgetTracker is { IsLagging: true })
    {
        Log.LogWarning("Scrape took {Elapsed}ms, exceeding {Interval}ms interval",
            elapsed, PollIntervalMs);
        EmitSignal(SignalName.ScrapeBudgetExceeded);
    }

    var normalised = NormaliseAllHosts();
    EmitSignal(SignalName.FleetMetricsUpdated, normalised);

    _pendingResults.Clear();
    _shardsCompletedThisTick = 0;
}
```

- [ ] **Step 3: Implement PartitionMetricsByHost and NormaliseAllHosts**

The exact partition logic depends on how the series API returns hostname labels. The MetricPoller's `MetricsUpdated` dictionary uses `pmview.meta.hostname` for the hostname label. For fleet mode with hostname-filtered queries, each shard's results will contain data for its assigned hosts.

```csharp
private void PartitionMetricsByHost(Godot.Collections.Dictionary metrics)
{
    // MetricPoller returns: { "metric_name": { "timestamp":..., "instances":..., "name_to_id":... } }
    // For fleet, each shard polls for its assigned hosts.
    // The hostname is in the pmview.meta.hostname virtual metric.
    // For now, store the raw metrics keyed by the shard's hostnames.
    // Full hostname partitioning will be refined when wired to real data.
    foreach (var host in _hostToShard.Keys.Where(
        h => _hostToShard[h] == _shardsCompletedThisTick - 1))
    {
        _pendingResults[host] = metrics;
    }
}

private Godot.Collections.Dictionary NormaliseAllHosts()
{
    var result = new Godot.Collections.Dictionary();

    foreach (var (hostname, rawMetrics) in _pendingResults)
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

        result[hostname] = hostDict;
    }

    return result;
}

private static double ExtractRate(
    Godot.Collections.Dictionary metrics, string metricName)
{
    if (!metrics.ContainsKey(metricName))
        return 0.0;
    var metricDict = metrics[metricName].AsGodotDictionary();
    if (!metricDict.ContainsKey("instances"))
        return 0.0;
    var instances = metricDict["instances"].AsGodotDictionary();
    // Singular metric (no instance domain): key is -1
    // Sum all instances for aggregate metrics with instance domains
    double sum = 0.0;
    foreach (var key in instances.Keys)
        sum += instances[key].AsDouble();
    return sum;
}
```

- [ ] **Step 4: Add StartPolling method that triggers discovery then starts shards**

```csharp
public async void StartPolling(string[] hostnames)
{
    ConfigureShards(hostnames, Endpoint, PollIntervalMs);

    // Discovery: fetch hinv.ncpu per host
    await DiscoverHostCapacities(hostnames);

    // Start each shard's polling
    _scrapeStopwatch = Stopwatch.StartNew();
    foreach (var shard in _shards)
        shard.CallDeferred("StartPolling");
}

private async Task DiscoverHostCapacities(string[] hostnames)
{
    // One-time query for hinv.ncpu across all hosts via series API
    // For now, default to 1 — refined when wired to real data
    Log.LogInformation("Discovering host capacities for {Count} hosts",
        hostnames.Length);
    foreach (var host in hostnames)
        _hostNcpu[host] = 1;
}
```

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/FleetMetricPoller.cs
git commit -m "Wire MetricPoller shards with signal aggregation

Each shard's MetricsUpdated is collected, metrics partitioned by
hostname, normalised via FleetMetricNormaliser, and emitted as
unified FleetMetricsUpdated signal. Scrape budget tracked."
```

---

## Chunk 3: Warning Toast Building Block

### Task 7: Create generic WarningToast

**Files:**
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.gd`
- Create: `src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.tscn`

- [ ] **Step 1: Create warning_toast.gd**

```gdscript
# warning_toast.gd
# Generic warning toast system — top-left HUD overlay with severity levels,
# auto-fade, and cooldown for recurring warnings.
class_name WarningToast
extends Control

enum Severity { WARNING, ERROR }

const SEVERITY_PREFIXES := {
    Severity.WARNING: "⚠ ",
    Severity.ERROR: "✖ ",
}

const SEVERITY_COLOURS := {
    Severity.WARNING: Color(0.9, 0.5, 0.1, 0.4),
    Severity.ERROR: Color(0.9, 0.15, 0.1, 0.4),
}

var _active_toasts: Array[Control] = []
var _cooldowns: Dictionary = {}  # cooldown_key → expiry timestamp
const COOLDOWN_DURATION := 30.0
const TOAST_SPACING := 4


func show_toast(message: String, severity: int = Severity.WARNING,
        duration: float = 5.0, cooldown_key: String = "") -> void:
    # Check cooldown
    if not cooldown_key.is_empty():
        var now := Time.get_ticks_msec() / 1000.0
        if _cooldowns.has(cooldown_key) and now < _cooldowns[cooldown_key]:
            return
        _cooldowns[cooldown_key] = now + COOLDOWN_DURATION

    var toast := _create_toast_panel(message, severity)
    add_child(toast)
    _active_toasts.append(toast)
    _reposition_toasts()

    # Fade out after duration
    var tween := create_tween()
    tween.tween_interval(duration - 0.5)
    tween.tween_property(toast, "modulate:a", 0.0, 0.5)
    tween.tween_callback(_remove_toast.bind(toast))


func clear_all() -> void:
    for toast: Control in _active_toasts:
        toast.queue_free()
    _active_toasts.clear()


func _create_toast_panel(message: String, severity: int) -> PanelContainer:
    var panel := PanelContainer.new()
    var style := StyleBoxFlat.new()
    var bg_colour: Color = SEVERITY_COLOURS.get(severity, SEVERITY_COLOURS[Severity.WARNING])
    style.bg_color = bg_colour
    style.corner_radius_top_left = 4
    style.corner_radius_top_right = 4
    style.corner_radius_bottom_right = 4
    style.corner_radius_bottom_left = 4
    style.content_margin_left = 12.0
    style.content_margin_right = 12.0
    style.content_margin_top = 6.0
    style.content_margin_bottom = 6.0
    panel.add_theme_stylebox_override("panel", style)

    var label := Label.new()
    var prefix: String = SEVERITY_PREFIXES.get(severity, "⚠ ")
    label.text = prefix + message
    var font := load("res://assets/fonts/PressStart2P-Regular.ttf")
    if font:
        label.add_theme_font_override("font", font)
    label.add_theme_font_size_override("font_size", 10)
    label.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
    panel.add_child(label)

    return panel


func _reposition_toasts() -> void:
    var y_offset := 0.0
    for toast: Control in _active_toasts:
        toast.position = Vector2(20, 50 + y_offset)
        y_offset += toast.size.y + TOAST_SPACING


func _remove_toast(toast: Control) -> void:
    _active_toasts.erase(toast)
    toast.queue_free()
    _reposition_toasts()
```

- [ ] **Step 2: Create warning_toast.tscn**

```ini
[gd_scene format=3]

[ext_resource type="Script" path="res://addons/pmview-bridge/ui/warning_toast.gd" id="1"]

[node name="WarningToast" type="Control"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
mouse_filter = 2
script = ExtResource("1")
```

- [ ] **Step 3: Verify in Godot (visual)**

Open the addon project, instance `warning_toast.tscn`, call `show_toast("TEST WARNING")` from a test script. Verify orange pill appears top-left and fades after 5s.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.gd \
        src/pmview-bridge-addon/addons/pmview-bridge/ui/warning_toast.tscn
git commit -m "Add generic WarningToast building block

Top-left HUD overlay with WARNING (orange) and ERROR (red) severity
levels, auto-fade, cooldown for recurring warnings. Reusable across
scenes."
```

---

## Chunk 4: Wire FleetViewController to FleetMetricPoller

### Task 8: Connect fleet poller to CompactHost bars

**Files:**
- Modify: `src/pmview-app/scripts/FleetViewController.gd`
- Modify: `src/pmview-app/scenes/fleet_view.tscn`

- [ ] **Step 1: Add WarningToast to fleet_view.tscn**

Add `warning_toast.tscn` instance to the HUD CanvasLayer:

```ini
[ext_resource type="PackedScene" path="res://addons/pmview-bridge/ui/warning_toast.tscn" id="6"]

[node name="WarningToast" parent="HUD/Control" instance=ExtResource("6")]
unique_name_in_owner = true
```

- [ ] **Step 2: Add fleet poller wiring to FleetViewController.gd**

Add references and wiring after the existing `_setup_time_control(config)` call in `_ready()`:

```gdscript
@onready var warning_toast: Control = %WarningToast

# After _setup_time_control(config) in _ready():
func _setup_fleet_poller(config: Dictionary) -> void:
    var hostnames: PackedStringArray = config.get("hostnames", PackedStringArray())
    if hostnames.is_empty():
        return  # Mock mode — no polling

    var endpoint: String = config.get("endpoint", "http://localhost:44322")
    var fleet_poller := FleetMetricPoller.new()
    fleet_poller.name = "FleetMetricPoller"
    add_child(fleet_poller)
    fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)
    fleet_poller.ScrapeBudgetExceeded.connect(_on_scrape_lagging)
    fleet_poller.HostsDropped.connect(_on_hosts_dropped)
    fleet_poller.StartPolling(hostnames)


func _on_fleet_metrics_updated(metrics: Dictionary) -> void:
    for host: Node3D in _hosts:
        var data: Dictionary = metrics.get(host.hostname, {})
        for metric_name: String in data:
            host.set_metric_value(metric_name, data[metric_name])


func _on_scrape_lagging() -> void:
    if warning_toast:
        warning_toast.show_toast(
            "METRIC POLLING LAGGING", WarningToast.Severity.WARNING,
            5.0, "scrape_lag")


func _on_hosts_dropped(count: int, hostnames: Array) -> void:
    if warning_toast:
        warning_toast.show_toast(
            "250 HOST LIMIT - %d HOSTS DROPPED (SEE LOGS)" % count,
            WarningToast.Severity.WARNING, 10.0)
```

- [ ] **Step 3: Call _setup_fleet_poller in _ready()**

Add after the existing `_setup_time_control(config)`:

```gdscript
    _setup_fleet_poller(config)
```

- [ ] **Step 4: Verify build**

```bash
cd src/pmview-bridge-addon && dotnet build --verbosity quiet
```

- [ ] **Step 5: Commit**

```bash
git add src/pmview-app/scripts/FleetViewController.gd \
        src/pmview-app/scenes/fleet_view.tscn
git commit -m "Wire FleetMetricPoller to CompactHost bars

FleetViewController creates FleetMetricPoller, connects signals,
routes normalised metric values to CompactHost.set_metric_value().
WarningToast shows host-drop and scrape-lag warnings."
```

---

## Chunk 5: Integration & Polish

### Task 9: End-to-end verification with dev-environment stack

This is a manual verification task — no automated tests.

- [ ] **Step 1: Start dev-environment stack**

```bash
cd dev-environment && podman compose up -d
```

Wait for pmproxy to be ready on port 54322.

- [ ] **Step 2: Launch fleet view in Godot**

Open `src/pmview-app/` in Godot. Set fleet_view.tscn as main scene. Provide test config via SceneManager or modify `_generate_mock_hostnames()` to return real hostnames from the dev stack.

- [ ] **Step 3: Verify CompactHost bars animate with real data**

- CPU bars should reflect actual CPU utilisation from archive playback
- Memory (paging) bars should be near zero (normal) unless archive has swap activity
- Disk bars should show read/write activity
- Network bars should show traffic

- [ ] **Step 4: Verify warning toasts (optional — requires >250 hosts or slow pmproxy)**

- [ ] **Step 5: Run CI build**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" --verbosity quiet
```

- [ ] **Step 6: Final commit with any polish**

```bash
git add -A
git commit -m "Fleet metric poller integration polish"
```

---

## Summary

| Chunk | Tasks | Description |
|-------|-------|-------------|
| 1 | 1-4 | FleetMetricNormaliser — pure C# with full TDD (sharding, normalisation, budget) |
| 2 | 5-6 | FleetMetricPoller — Godot Node orchestrator, shard wiring, signal aggregation |
| 3 | 7 | WarningToast — generic addon building block |
| 4 | 8 | FleetViewController wiring — connect poller to CompactHost bars + toasts |
| 5 | 9 | End-to-end verification with dev-environment stack |
