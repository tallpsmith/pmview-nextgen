# Archive-Mode Metric Browsing

## Problem

The MetricBrowserDialog (editor tool) and runtime MetricBrowser use `/pmapi/` endpoints
for metric discovery. These query the live PMCD agent — they show what's running *now*,
not what was *recorded*. In archive mode (the default use case), the source of truth is
the Valkey-backed archive data served by pmproxy's `/series/` API.

pmproxy can aggregate archives from multiple hosts. Metrics available on one archived host
may not exist on another. Users need to browse metrics scoped to a specific host's archive.

## Design

### Approach: Separate `PcpSeriesClient` (Option B)

The `/series/` endpoints are stateless HTTP — no PMAPI context, no connection lifecycle.
A dedicated `PcpSeriesClient` keeps this cleanly separated from the context-managed
`PcpClientConnection` used for live `/pmapi/` operations.

### New Components

#### 1. `PcpSeriesClient` (PcpClient library)

**Location:** `src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs`

Stateless HTTP client for pmproxy `/series/*` endpoints. Takes a `Uri baseUrl` and
caller-owned `HttpClient`. Follows the same caller-owns-`HttpClient` pattern as
`PcpClientConnection`.

| Method | Endpoint | Returns |
|--------|----------|---------|
| `GetHostnamesAsync()` | `/series/labels?names=hostname` | `IReadOnlyList<string>` |
| `QuerySeriesAsync(expr)` | `/series/query?expr=...` | `IReadOnlyList<string>` (series IDs) |
| `GetMetricNamesAsync(seriesIds)` | `/series/metrics?series=...` | `IReadOnlyList<SeriesMetricName>` |
| `GetDescriptorsAsync(seriesIds)` | `/series/descs?series=...` | `IReadOnlyList<SeriesDescriptor>` |
| `GetInstancesAsync(seriesIds)` | `/series/instances?series=...` | `Dictionary<string, SeriesInstanceInfo>` |

**Return type definitions:**

```csharp
// /series/metrics response: [{"series": "<hash>", "name": "disk.dev.read"}, ...]
public record SeriesMetricName(string SeriesId, string Name);

// /series/descs response: [{"series": "<hash>", "pmid": "...", "indom": "...",
//   "semantics": "counter", "type": "u64", "units": "Kbyte / sec"}, ...]
public record SeriesDescriptor(
    string SeriesId,
    string? Pmid,
    string? Indom,
    string? Semantics,
    string? Type,
    string? Units);
```

Existing `PcpSeriesQuery` remains the parser layer. `PcpSeriesClient` delegates to
existing parse methods where available — specifically `PcpSeriesQuery.ParseInstancesResponse()`
and `BuildInstancesUrl()` for the `/series/instances` path. New parse methods added to
`PcpSeriesQuery` for `/labels`, `/metrics`, `/descs` responses only.

#### 2. `ArchiveMetricDiscoverer` (PcpClient library)

**Location:** `src/pcp-client-dotnet/src/PcpClient/ArchiveMetricDiscoverer.cs`

Orchestrates multi-step discovery into simple high-level operations using `PcpSeriesClient`.
Holds internal state: caches series IDs per hostname from the discovery step so that
`DescribeMetricAsync` can reuse them without re-querying.

| Method | Purpose |
|--------|---------|
| `GetHostnamesAsync()` | Returns archived host names |
| `DiscoverMetricsForHostAsync(hostname)` | Queries `*{hostname=="..."}`, resolves to sorted deduplicated metric names. Caches the series-ID-to-metric-name mapping internally. |
| `DescribeMetricAsync(metricName, hostname)` | Looks up cached series IDs for this metric+host, calls `/series/descs` + `/series/instances`. Falls back to `metricName{hostname=="..."}` query if cache miss. Returns `MetricDetail`. |

```csharp
public record MetricDetail(
    string Name,
    string? Semantics,
    string? Type,
    string? Units,
    IReadOnlyList<SeriesInstanceInfo> Instances);
```

#### 3. `NamespaceTreeBuilder` (PcpClient library)

**Location:** `src/pcp-client-dotnet/src/PcpClient/NamespaceTreeBuilder.cs`

Pure function: converts flat metric name list into a hierarchical tree by splitting on `.`.

```csharp
public record NamespaceNode(
    string Name,
    string FullPath,
    IReadOnlyList<NamespaceNode> Children,
    bool IsLeaf);
```

Input: `["disk.dev.read", "disk.dev.write", "kernel.all.load"]`
Output: tree with `disk` → `dev` → `read`, `write` and `kernel` → `all` → `load`

### MetricBrowserDialog Changes

The editor dialog becomes mode-aware based on `ProjectSettings.GetSetting("pmview/mode")`:

**Live mode (mode=1):** Unchanged — `PcpClientConnection` + `/pmapi/` lazy tree.

**Archive mode (mode=0):**
1. Creates `PcpSeriesClient` + `ArchiveMetricDiscoverer`
2. Host dropdown (`OptionButton`) appears at top of dialog
3. `GetHostnamesAsync()` → populates dropdown
4. On host selection → `DiscoverMetricsForHostAsync(hostname)` → `NamespaceTreeBuilder` → full tree
5. On metric selection → `DescribeMetricAsync()` → description + instances panel
6. Confirm writes metric name + instance back to `PcpBindingResource`

**Layout strategy:** The host `OptionButton` is always created in `_Ready()` as the first
child of the `VBoxContainer`, hidden by default. In archive mode it becomes visible; in
live mode it stays hidden. This avoids conditional layout logic.

**Resource lifecycle:** New fields `_seriesClient` and `_discoverer` are stored alongside
the existing `_client`/`_httpClient`. All four are disposed in both `CleanupAndClose()` and
`_ExitTree()`. `PcpSeriesClient` borrows the caller-owned `_httpClient` (same pattern as
`PcpClientConnection`).

### Prototype Cleanup

Remove runtime prototype code superseded by editor-integrated bindings and ProjectSettings:

**Delete:**
- `godot-project/addons/pmview-bridge/MetricBrowser.cs` — runtime C# bridge
- `godot-project/scripts/scenes/metric_browser.gd` — runtime GDScript UI
- `godot-project/scenes/metric_browser.tscn` — runtime browser scene
- `godot-project/scripts/scenes/playback_controls.gd` — manual timestamp UI
- `godot-project/scenes/playback_controls.tscn` — playback controls scene

**Remove from `metric_scene_controller.gd`:**
- `@onready` declarations: `metric_browser_bridge` (line 11), `metric_browser_ui` (line 13), `playback_controls_ui` (line 14)
- `_ready()`: `if metric_browser_ui:` block (lines 34-36), `if playback_controls_ui:` block (lines 38-40)
- `_ready()`: signal connection `metric_poller.connect("PlaybackPositionChanged", ...)` (line 30)
- `_unhandled_input()`: F2 keycode branch, F3 keycode branch (lines 58-60)
- Functions: `_toggle_metric_browser()`, `_toggle_playback_controls()`, `_on_metric_chosen()`, `_on_playback_position_changed()`
- Connection state handler: `if state == "Connected" and metric_browser_bridge:` block (lines 87-88)

**Remove from `main.tscn`:**
- MetricBrowser, MetricBrowser UI, and PlaybackControls nodes

**Keep:**
- `MetricPoller` (live + archive fetching), `SceneBinder` (value application)
- `MetricPoller.StartPlayback/PausePlayback/ResumePlayback/SetPlaybackSpeed/SetLoop`
  (called by `_apply_launch_settings()`, driven by ProjectSettings)

### pmproxy `/series/` API Reference

Discovery chain for archive browsing:

1. `/series/labels?names=hostname` → `{"hostname": ["host1", "host2"]}`
2. `/series/query?expr=*{hostname=="host1"}` → series ID hashes
3. `/series/metrics?series=<ids>` → metric names per series
4. `/series/descs?series=<ids>` → type, semantics, units, indom
5. `/series/instances?series=<ids>` → instance names/IDs

Query expression syntax supports label qualifiers: `metric.name{hostname=="value"}`

## Testing Strategy

**Pure .NET (xUnit):**

| Test class | Coverage |
|------------|----------|
| `PcpSeriesClientTests` | URL building + response parsing for `/series/labels`, `/series/metrics`, `/series/descs`, `/series/instances` (delegates to `PcpSeriesQuery`). Mock `HttpMessageHandler`. Include test for query expression URL encoding with special characters in hostname. |
| `ArchiveMetricDiscovererTests` | Orchestration: host discovery, metric discovery (dedup, sort), detail lookup with cache hit and cache miss paths. Uses mock `HttpMessageHandler` (same pattern as `PcpSeriesClientTests` since `PcpSeriesClient` has no interface). |
| `NamespaceTreeBuilderTests` | Pure function: flat names → tree. Edge cases: empty, single, deep nesting, shared prefixes |

**Existing tests untouched:** `SeriesQueryTests` (series value/instance parsing),
`ArchiveDiscoveryTests` (sampling interval inference) stay green. Note:
`ArchiveDiscovery` (time bounds) is unrelated to the new `ArchiveMetricDiscoverer`
(metric name discovery) despite the similar naming.

**Godot-side:** Editor dialog verified manually until CI worktree integration lands.

## Decisions

- **Approach B** chosen: separate `PcpSeriesClient` rather than extending `IPcpClient` or abstracting behind `IMetricDiscovery`
- **Option A** for tree building: fetch all metrics upfront, build tree client-side (archive data is finite, hundreds not thousands of metrics)
- **Host selection first** in archive mode — user picks host, then browses that host's metric namespace
- Runtime MetricBrowser and PlaybackControls removed — superseded by editor bindings + ProjectSettings
