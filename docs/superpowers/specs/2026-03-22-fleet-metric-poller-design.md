# Fleet MetricPoller — Design Spec

## Overview

A fleet-wide metric polling system that fetches aggregate health metrics for multiple hosts and drives the CompactHost bars in the fleet view with real data. Uses hostname-filtered series API queries so each host gets its own metric values.

## Architecture

```
FleetViewController (GDScript)
    │
    ▼
FleetMetricPoller (C#, Godot Node)
    │  1. Centralised discovery: series_id → hostname mapping
    │  2. Distributes cached maps to shards
    │  3. Routes per-host signals to CompactHosts
    ▼
MetricPoller[] (existing C#, one per shard)
    │  Polls with pre-cached series IDs
    │  Fires MetricsUpdated(hostname, metrics) per host per tick
    │  Fires ShardPollCompleted() once per tick after all hosts processed
    ▼
pmproxy series API (/series/query, /series/values, /series/labels)
```

### Two-Phase Data Flow

**Phase 1 — Centralised Discovery (once per session, in FleetMetricPoller):**
1. For each metric, batch all hostnames (groups of `MaxHostsPerQueryBatch = 20`):
   `BuildMultiHostFilteredQueryUrl(baseUrl, metric, hostnames)` → collect series IDs
2. One `/series/labels?series=id1,id2,...` call → `ParsePerSeriesHostnameLabels` →
   build `series_id → hostname` map
3. Partition map by hostname → shard assignment
4. Call `InitialiseWithCachedSeriesMap()` on each shard's MetricPoller,
   passing **independent copies** of the maps per shard (no shared mutable state)

**HTTP cost:** `ceil(total_hosts / 20) × 7 metrics` query calls + 1 labels call.
For 250 hosts: ~91 query calls + 1 labels call = **92 calls total** (regardless of shard count).

**Phase 2 — Polling (every tick, in each MetricPoller shard):**
1. For each metric: one `/series/values` call with all cached series IDs for that metric
2. Partition values by `series_id → hostname` from cached map
3. For each hostname: assemble metric dict, apply rate conversion, inject virtual
   metrics, fire `MetricsUpdated(hostname, metrics)`
4. After all hostnames processed: fire `ShardPollCompleted()` for budget tracking

**HTTP cost per shard per tick:** 7 calls (one per metric).

### Fetch Path Selection (MetricPoller)

| Has cached series map? | Cursor mode | Fetch path |
|------------------------|-------------|------------|
| Yes | Any (Live or Playback) | `FetchSeriesMetricsForHosts()` — new |
| No | Live | `FetchLiveMetrics()` — existing, unchanged |
| No | Playback | `FetchHistoricalMetrics()` — existing, unchanged |

The "has cached series map" path uses the series API regardless of cursor mode.
For fleet "live" mode, it queries with a recent time window (1.5× sampling interval
before now). This means fleet live data is a few seconds stale (archive logging
interval) — acceptable for fleet-at-a-glance.

## Layer 1: PcpSeriesQuery Changes (pure .NET)

**New methods:**

```csharp
// OR-chained hostname filter for batched discovery queries
public static Uri BuildMultiHostFilteredQueryUrl(
    Uri baseUrl, string metricName, string[] hostnames)
// Builds: metric{hostname=="h1" || hostname=="h2" || ...}

// Per-series labels lookup for hostname mapping
// DISTINCT from existing BuildLabelsUrl which queries by label name (?names=).
// This queries by series IDs (?series=) to get labels for specific series.
public static Uri BuildPerSeriesLabelsUrl(
    Uri baseUrl, IEnumerable<string> seriesIds)
// Builds: /series/labels?series=id1,id2,...

// Parse per-series labels response → series_id → hostname
// DISTINCT from existing ParseLabelsResponse which returns a flat list of
// label values. This returns a per-series mapping.
public static Dictionary<string, string> ParsePerSeriesHostnameLabels(string json)
```

**Existing methods — unchanged, listed for clarity:**
- `BuildHostnameFilteredQueryUrl` — single-host filter, used by ArchiveSourceDiscovery
- `BuildLabelsUrl` — label-value enumeration (`?names=`), different from new per-series query
- `ParseLabelsResponse` — flat label value list, different from new per-series parser
- `BuildValuesUrlWithTimeWindow` — polling phase
- `ParseQueryResponse` — discovery phase
- `ParseValuesResponse` — polling phase

**`SeriesInstanceInfo` record:** No changes needed. Hostname mapping comes from
`/series/labels`, not from instance metadata.

## Layer 2: MetricPoller Changes

**Property changes:**
- `string Hostname` — **kept as-is** for single-host path and `InjectVirtualMetrics()`.
  In the fleet path, `Hostname` is not used — the hostname comes from the cached
  series map and is passed explicitly per signal emission.
- New internal constant: `const int MaxHostsPerQueryBatch = 20`

**Signal changes:**
```csharp
// Before:
public delegate void MetricsUpdatedEventHandler(
    Godot.Collections.Dictionary metrics);

// After — adds hostname parameter:
public delegate void MetricsUpdatedEventHandler(
    string hostname, Godot.Collections.Dictionary metrics);

// New — tick completion signal for scrape budget tracking:
public delegate void ShardPollCompletedEventHandler();
```

**BREAKING CHANGE:** All existing GDScript handlers connected to `MetricsUpdated`
must be updated to accept the new `(hostname: String, metrics: Dictionary)` signature.
Godot 4 will silently fail to connect if the arity doesn't match.

**Existing single-host paths affected:**
- `FetchLiveMetrics()`: emits `MetricsUpdated(Hostname, dict)` — passes
  the existing `Hostname` property as the first argument
- `FetchHistoricalMetrics()`: same — passes `Hostname`
- `InjectVirtualMetrics()`: unchanged, continues reading `Hostname` property

**New method:**
```csharp
public void InitialiseWithCachedSeriesMap(
    IReadOnlyDictionary<string, string> seriesIdToHostname,
    IReadOnlyDictionary<string, IReadOnlyList<string>> seriesIdsPerMetric)
```
Called by FleetMetricPoller after centralised discovery. Caller passes
**independent immutable copies** per shard — no shared mutable state between
shards. Shard skips its own discovery and goes straight to polling.

**New fetch method:** `FetchSeriesMetricsForHosts()`
1. Determine time window based on cursor mode (Live → near-now, Playback → cursor)
2. For each metric: one `/series/values` call with cached series IDs
3. Partition values by series_id → hostname
4. Per hostname: assemble dict, rate-convert, call
   `InjectVirtualMetrics(dict, hostname)` (overload that takes explicit hostname),
   emit `MetricsUpdated(hostname, metrics)`
5. After all hostnames: emit `ShardPollCompleted()`

**Existing paths unchanged in logic** (only signal emission gains hostname param):
- `FetchLiveMetrics()` — single-host HostView live path
- `FetchHistoricalMetrics()` — single-host playback path
- `MarshalMetricValues()` — live path only
- `TimeCursor` — no changes

## Layer 3: FleetMetricPoller Changes

**New responsibility: centralised discovery.**

`DiscoverSeriesMapping()` replaces `DiscoverHostCapacities()`:
1. For each metric, batch all assigned hostnames (groups of 20) →
   `BuildMultiHostFilteredQueryUrl` → collect series IDs per batch
2. One `BuildPerSeriesLabelsUrl` call with all series IDs →
   `ParsePerSeriesHostnameLabels` → `series_id → hostname` map
3. Partition by hostname → shard assignment
4. Build immutable copies of the relevant subset for each shard
5. Call `InitialiseWithCachedSeriesMap` on each shard

**`hinv.ncpu` discovery:** Remains stubbed at `ncpu = 1` for all hosts, as in
the current implementation. Tracked separately — not part of this refactor.

**Signal handler changes:**

The existing `OnShardMetricsUpdated` is **replaced** by two handlers:

```csharp
// Per-host metric data — normalise and forward to GDScript
private void OnShardHostMetricsUpdated(
    string hostname, Godot.Collections.Dictionary metrics)
{
    var normalised = NormaliseHostMetrics(hostname, metrics);
    EmitSignal(SignalName.FleetMetricsUpdated, hostname, normalised);
}

// Tick completion — budget tracking (replaces _shardsCompletedThisTick counter)
private void OnShardPollCompleted()
{
    _shardsCompletedThisTick++;
    if (_shardsCompletedThisTick < _shards.Count)
        return;
    // All shards done — check budget, reset for next tick
    var elapsed = _scrapeStopwatch?.ElapsedMilliseconds ?? 0;
    _budgetTracker?.RecordScrapeCompleted(elapsed);
    if (_budgetTracker is { IsLagging: true })
        EmitSignal(SignalName.ScrapeBudgetExceeded);
    _shardsCompletedThisTick = 0;
    _scrapeStopwatch = Stopwatch.StartNew();
}
```

**Scrape budget tracking:** Model is preserved — FleetMetricPoller still counts
"all shards completed" per tick. The change is that completion is signalled by
`ShardPollCompleted` (once per shard per tick) rather than inferred from the
`MetricsUpdated` signal (which now fires N times per shard per tick).

**`NormaliseAllHosts` → `NormaliseHostMetrics`:** Simplified — operates on one
host's raw metrics at a time instead of iterating all hosts against a shared blob.
Same normalisation math, smaller scope.

**FleetMetricsUpdated signal change:**
```csharp
// Before:
public delegate void FleetMetricsUpdatedEventHandler(
    Godot.Collections.Dictionary metrics);  // all hosts in one dict

// After:
public delegate void FleetMetricsUpdatedEventHandler(
    string hostname, Godot.Collections.Dictionary metrics);  // one host per signal
```

**BREAKING CHANGE:** The GDScript handler in FleetViewController.gd **must be
replaced wholesale** — the old loop-over-all-hosts logic is incompatible with
the new per-host signal shape.

## Layer 4: GDScript Changes

### FleetViewController.gd

**BREAKING CHANGES — all must be coordinated:**

1. Add `_host_lookup: Dictionary` (hostname → CompactHost node), populated
   in `_build_grid()` **before** `_setup_fleet_poller()` is called:
   ```gdscript
   var _host_lookup: Dictionary = {}

   func _build_grid(hostnames: PackedStringArray) -> void:
       # ... existing grid building ...
       for i in range(count):
           # ... existing host_node creation ...
           _host_lookup[hostnames[i]] = host_node
   ```

2. Replace existing signal connection and handler:
   ```gdscript
   # In _setup_fleet_poller():
   fleet_poller.FleetMetricsUpdated.connect(_on_fleet_metrics_updated)

   # New handler — receives one host per signal:
   func _on_fleet_metrics_updated(hostname: String, metrics: Dictionary) -> void:
       var host: Node3D = _host_lookup.get(hostname)
       if not host:
           return
       for metric_name: String in metrics:
           host.set_metric_value(metric_name, metrics[metric_name])
   ```

3. **Diagnostic print removal:** Remove diagnostic `print()` statements from
   `_setup_fleet_poller` **only after** end-to-end visual verification passes.
   Do not remove during initial implementation — they are currently the only
   confirmation mechanism for poller boot.

### HostView GDScript (single-host scene)

**Hostname-aware filtering:**
- HostView is initialised with a `_my_hostname` property (from connection config
  in standalone mode, or from fleet focus selection)
- Signal handler updated for new signature and filters by hostname:
  ```gdscript
  func _on_metrics_updated(hostname: String, metrics: Dictionary) -> void:
      if hostname != _my_hostname:
          return
      # ... existing metric processing ...
  ```
- In standalone single-host mode, only ever receives signals for itself, but the
  filter is correct by construction — no silent assumption

## Metrics

### One-Time Discovery (startup)

| Data | Source | Purpose | Status |
|------|--------|---------|--------|
| Series ID → hostname mapping | `/series/labels?series=...` | Route values to correct host | This spec |
| `hinv.ncpu` per host | Series API | CPU normalisation denominator | Deferred (stubbed at 1) |

### Per-Tick Polling (7 metrics across 4 bars)

| Bar | Metric(s) | Type | Normalisation |
|-----|-----------|------|---------------|
| CPU | `kernel.all.cpu.idle` | Counter → rate | `1.0 - (idle_rate / (ncpu * 1000))` clamped 0.0–1.0 |
| Memory | `mem.vmstat.pgpgin` + `mem.vmstat.pgpgout` | Counter → rate | Combined pages/s ÷ `PagingMaxPagesPerSec` |
| Disk | `disk.all.read_bytes` + `disk.all.write_bytes` | Counter → rate | Combined bytes/s ÷ `DiskMaxBytesPerSec` |
| Network | `network.interface.in.bytes` + `network.interface.out.bytes` | Counter → rate | Combined bytes/s ÷ `NetworkMaxBytesPerSec` |

## Sharding

- **Default shard size**: 25 hosts per MetricPoller
- **Query batch size**: 20 hostnames per discovery URL (`MaxHostsPerQueryBatch`)
- **Min shards**: 1
- **Max shards**: 10 (hard cap)
- **Max hosts**: 250 (10 × 25). Excess dropped with per-host warning log.
- Host assignment: round-robin across shards for even distribution

## Scrape Budget Tracking

Model preserved from current implementation, with signal change:
- FleetMetricPoller timestamps when each poll tick starts
- Each shard emits `ShardPollCompleted` (not `MetricsUpdated`) when done
- FleetMetricPoller counts shard completions per tick (as before)
- When all shards complete, checks `elapsed > poll_interval`
- If exceeded: emits `ScrapeBudgetExceeded`, logs warning

## Warning Toast System

Unchanged from current implementation. Existing warning_toast.gd handles:
- Host cap exceeded toast
- Scrape lag toast

## Testing Strategy

### C# / xUnit (TDD)

**PcpSeriesQuery (new methods):**
- `BuildMultiHostFilteredQueryUrl`: verify OR-chained filter expression, URL encoding,
  batching at boundary (20 hosts), empty array, single host
- `BuildPerSeriesLabelsUrl`: verify comma-separated series IDs in URL
- `ParsePerSeriesHostnameLabels`: verify series_id → hostname extraction from JSON,
  missing hostname label, empty response

**MetricPoller:**
- `InitialiseWithCachedSeriesMap`: verify cached map is stored, discovery is skipped
- Signal signature: verify `MetricsUpdated` carries hostname parameter
- Fetch path selection: verify cached map → `FetchSeriesMetricsForHosts`,
  no map + live → `FetchLiveMetrics`, no map + playback → `FetchHistoricalMetrics`
- `ShardPollCompleted` emitted after all hosts processed per tick

**FleetMetricPoller:**
- Centralised discovery: mock HTTP responses, verify series_id → hostname map built
- Shard distribution: verify correct series IDs assigned to each shard (immutable copies)
- Per-host signal routing: verify `FleetMetricsUpdated` fires per hostname
- Budget tracking: verify `ShardPollCompleted` drives tick-completion counter

**Existing tests (unchanged in logic, updated for signal signature):**
- Sharding: shard count, host distribution, >250 drops
- Normalisation: CPU/memory/disk/network 0.0–1.0 values
- Scrape budget: skip-next-tick and signal emission

### GDScript (visual verification)

- CompactHost bars showing different values per host (not all identical)
- HostView in fleet focus filtering to selected host only
- End-to-end with dev-environment stack running
- Diagnostic prints kept until visual verification passes
