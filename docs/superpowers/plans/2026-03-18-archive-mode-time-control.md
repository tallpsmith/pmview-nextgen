# Archive Mode & Time Control Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable archive playback from the standalone app with source selection and Time Machine-style timeline navigation.

**Architecture:** Two-phase build. Phase 1 adds archive source discovery (`ArchiveSourceDiscovery` in PcpClient), URL-encoded hostname filtering in `PcpSeriesQuery`, archive launch flow in the main menu, and pipeline branching in `LoadingPipeline`. Phase 2 adds `TimeCursor` IN/OUT point support, `MetricPoller` step/jump methods, and a GDScript `TimeControl` overlay for timeline scrubbing. Both phases share `TimeCursor` and `MetricPoller` as the integration point.

**Tech Stack:** C# (.NET 8.0 for PcpClient, .NET 10.0 for tests), GDScript for UI, xUnit for C# tests, Godot 4.6+

**Spec:** `docs/superpowers/specs/2026-03-18-archive-mode-time-control-design.md`

---

## Chunk 1: Phase 1 — Archive Mode Launch

### File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs` | Add `BuildHostnameFilteredQueryUrl()` with RFC 3986 encoding |
| Create | `src/pcp-client-dotnet/src/PcpClient/ArchiveSourceDiscovery.cs` | Higher-level orchestrator: list hosts, probe time bounds |
| Modify | `src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs` | Add `GetValuesAsync()` for time-windowed value fetching |
| Test | `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs` | Tests for hostname-filtered URL building |
| Create | `src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveSourceDiscoveryTests.cs` | Tests for discovery orchestration |
| Modify | `src/pmview-app/scripts/LoadingPipeline.cs` | Branch on mode, accept config params |
| Modify | `src/pmview-app/scripts/LoadingController.gd` | Pass mode/hostname/start_time from config |
| Modify | `src/pmview-app/scripts/MainMenuController.gd` | Wire archive button, hostname dropdown, time input |
| Modify | `src/pmview-app/scenes/main_menu.tscn` | Enable ArchiveButton, add archive panel nodes |
| Modify | `src/pmview-app/scripts/SceneManager.gd` | No code change needed — already passes dict |
| Modify | `src/pmview-app/scripts/HostViewController.gd` | Start playback if archive mode |

---

### Task 1: PcpSeriesQuery — Hostname-Filtered Query URL Builder

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests for `BuildHostnameFilteredQueryUrl`**

Add to `SeriesQueryTests.cs` after the existing URL building section (~line 209):

```csharp
// ── Hostname-filtered query URL building ──

[Fact]
public void BuildHostnameFilteredQueryUrl_EncodesFilterExpression()
{
    var baseUrl = new Uri("http://localhost:44322");

    var url = PcpSeriesQuery.BuildHostnameFilteredQueryUrl(
        baseUrl, "kernel.all.load", "saas-prod-01");

    var urlStr = url.AbsoluteUri;
    Assert.Contains("/series/query", urlStr);
    // Must percent-encode { } " = for pmproxy compatibility
    Assert.Contains("%7B", urlStr);  // {
    Assert.Contains("%7D", urlStr);  // }
    Assert.Contains("%22", urlStr);  // "
    Assert.Contains("%3D%3D", urlStr);  // ==
    Assert.Contains("saas-prod-01", urlStr);
}

[Fact]
public void BuildHostnameFilteredQueryUrl_HostnameWithSpecialChars_EncodesCorrectly()
{
    var baseUrl = new Uri("http://localhost:44322");

    var url = PcpSeriesQuery.BuildHostnameFilteredQueryUrl(
        baseUrl, "kernel.all.load", "web server.local");

    var urlStr = url.AbsoluteUri;
    // Hostname with spaces must also be encoded
    Assert.DoesNotContain(" ", urlStr);
    Assert.Contains("web%20server.local", urlStr);
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
export PATH="/opt/homebrew/bin:$PATH"
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "BuildHostnameFilteredQueryUrl" -v n
```

Expected: compilation error — `BuildHostnameFilteredQueryUrl` does not exist.

- [ ] **Step 3: Implement `BuildHostnameFilteredQueryUrl`**

Add to `PcpSeriesQuery.cs` after `BuildQueryUrl` (~line 65):

```csharp
/// <summary>
/// Builds a /series/query URL with a hostname label filter.
/// pmproxy requires full RFC 3986 percent-encoding of the filter expression —
/// { } " = must all be encoded. Returns 400 Bad Request otherwise.
/// </summary>
public static Uri BuildHostnameFilteredQueryUrl(Uri baseUrl, string metricName, string hostname)
{
    // Build the raw expression: metric{hostname=="value"}
    // Then manually percent-encode the filter characters
    var filter = $"{metricName}{{hostname==\"{hostname}\"}}";
    var encoded = EncodeSeriesExpression(filter);
    return new Uri(baseUrl, $"/series/query?expr={encoded}");
}

/// <summary>
/// Percent-encodes a pmseries expression for use in pmproxy URLs.
/// Encodes all RFC 3986 reserved characters that pmproxy does not tolerate
/// in query parameters: { } " = and spaces.
/// </summary>
internal static string EncodeSeriesExpression(string expression)
{
    // Uri.EscapeDataString handles most characters but we need to verify
    // it encodes everything pmproxy requires
    return Uri.EscapeDataString(expression);
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "BuildHostnameFilteredQueryUrl" -v n
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "Add hostname-filtered query URL builder with RFC 3986 encoding

pmproxy requires full percent-encoding of label filter expressions.
Encodes { } \" = and spaces to prevent 400 Bad Request responses."
```

---

### Task 2: PcpSeriesClient — Time-Windowed Values Fetching

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/PcpSeriesClientTests.cs`

- [ ] **Step 1: Write failing test for `GetValuesAsync`**

Add to `PcpSeriesClientTests.cs`:

```csharp
[Fact]
public async Task GetValuesAsync_WithTimeWindow_ReturnsValues()
{
    var handler = new MockHttpHandler(req =>
    {
        var url = req.RequestUri!.ToString();
        Assert.Contains("/series/values", url);
        Assert.Contains("start=", url);
        Assert.Contains("finish=", url);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"series": "abc123", "timestamp": 1773348826000.0, "value": "0.42"}]""",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);
    var position = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);
    var result = await client.GetValuesAsync(
        new[] { "abc123" }, position, windowSeconds: 60.0);
    Assert.Single(result);
    Assert.Equal(0.42, result[0].NumericValue, precision: 2);
}
```

- [ ] **Step 2: Run test — verify it fails**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "GetValuesAsync_WithTimeWindow" -v n
```

Expected: compilation error — `GetValuesAsync` does not exist.

- [ ] **Step 3: Implement `GetValuesAsync`**

Add to `PcpSeriesClient.cs`:

```csharp
public async Task<IReadOnlyList<SeriesValue>> GetValuesAsync(
    IEnumerable<string> seriesIds, DateTime position, double windowSeconds = 2.0,
    CancellationToken cancellationToken = default)
{
    var url = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
        _baseUrl, seriesIds, position, windowSeconds);
    var response = await _httpClient.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return PcpSeriesQuery.ParseValuesResponse(json);
}
```

- [ ] **Step 4: Run test — verify it passes**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "GetValuesAsync_WithTimeWindow" -v n
```

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/PcpSeriesClientTests.cs
git commit -m "Add time-windowed values fetching to PcpSeriesClient

Composes BuildValuesUrlWithTimeWindow + ParseValuesResponse into
a single async method for archive time bounds probing."
```

---

### Task 3: ArchiveSourceDiscovery — Host Listing and Time Bounds

**Files:**
- Create: `src/pcp-client-dotnet/src/PcpClient/ArchiveSourceDiscovery.cs`
- Create: `src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveSourceDiscoveryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ArchiveSourceDiscoveryTests.cs`:

```csharp
using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for ArchiveSourceDiscovery: host listing and time bounds probing.
/// </summary>
public class ArchiveSourceDiscoveryTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    [Fact]
    public async Task GetHostnamesAsync_ReturnsHostnames()
    {
        var handler = new MockHttpHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"hostname": ["saas-prod-01", "container-abc"]}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var result = await discovery.GetHostnamesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("saas-prod-01", result);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_ReturnsMinMaxTimestamps()
    {
        var requestIndex = 0;
        var handler = new MockHttpHandler(req =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                // First call: /series/query for hostname-filtered series
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""["series_abc"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
            // Second call: /series/values with time window
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                    {"series": "series_abc", "timestamp": 1773348826000.0, "value": "0.5"},
                    {"series": "series_abc", "timestamp": 1773435226000.0, "value": "0.6"},
                    {"series": "series_abc", "timestamp": 1773521626000.0, "value": "0.7"}
                ]
                """, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("saas-prod-01");

        Assert.NotNull(bounds);
        // First timestamp: 1773348826000 ms
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773348826000).UtcDateTime,
            bounds.Value.Start);
        // Last timestamp: 1773521626000 ms
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773521626000).UtcDateTime,
            bounds.Value.End);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_NoSeriesFound_ReturnsNull()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("nonexistent");

        Assert.Null(bounds);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_NoValues_ReturnsNull()
    {
        var requestIndex = 0;
        var handler = new MockHttpHandler(_ =>
        {
            requestIndex++;
            if (requestIndex == 1)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""["series_abc"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("empty-host");

        Assert.Null(bounds);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_UsesHostnameFilteredQuery()
    {
        string? capturedQueryUrl = null;
        var requestIndex = 0;
        var handler = new MockHttpHandler(req =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                capturedQueryUrl = req.RequestUri!.AbsoluteUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        await discovery.DiscoverTimeBoundsAsync("saas-prod-01");

        Assert.NotNull(capturedQueryUrl);
        // Must use hostname-filtered query with proper encoding
        Assert.Contains("kernel.all.load", capturedQueryUrl);
        Assert.Contains("%7B", capturedQueryUrl);  // {
        Assert.Contains("saas-prod-01", capturedQueryUrl);
    }

    [Fact]
    public async Task ComputeDefaultStartTime_ClampsTo24HoursBeforeEnd()
    {
        // Archive range: Mar 10 to Mar 18 (8 days)
        var start = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc);

        var defaultStart = ArchiveSourceDiscovery.ComputeDefaultStartTime(start, end);

        // Should be end - 24h = Mar 17
        Assert.Equal(new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc), defaultStart);
    }

    [Fact]
    public async Task ComputeDefaultStartTime_ShortArchive_ClampsToArchiveStart()
    {
        // Archive range: only 2 hours
        var start = new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 18, 12, 0, 0, DateTimeKind.Utc);

        var defaultStart = ArchiveSourceDiscovery.ComputeDefaultStartTime(start, end);

        // Can't go back 24h — clamp to archive start
        Assert.Equal(start, defaultStart);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "ArchiveSourceDiscovery" -v n
```

Expected: compilation error — `ArchiveSourceDiscovery` does not exist.

- [ ] **Step 3: Implement `ArchiveSourceDiscovery`**

Create `src/pcp-client-dotnet/src/PcpClient/ArchiveSourceDiscovery.cs`:

```csharp
namespace PcpClient;

/// <summary>
/// Higher-level orchestrator for archive source discovery.
/// Composes PcpSeriesClient calls to list hostnames and probe time bounds.
/// Uses hostname-filtered queries to isolate data for a specific source.
/// </summary>
public class ArchiveSourceDiscovery
{
    private const string ProbeMetric = "kernel.all.load";
    private const double ProbeWindowDays = 30.0;

    private readonly PcpSeriesClient _seriesClient;
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    public ArchiveSourceDiscovery(Uri baseUrl, HttpClient httpClient)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _seriesClient = new PcpSeriesClient(baseUrl, httpClient);
    }

    public async Task<IReadOnlyList<string>> GetHostnamesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _seriesClient.GetHostnamesAsync(cancellationToken);
    }

    /// <summary>
    /// Probes the time bounds of archive data for a given hostname.
    /// Queries a well-known metric with a hostname filter, then fetches
    /// values over a wide window to find the earliest and latest timestamps.
    /// Returns null if no data is found for the hostname.
    /// </summary>
    public async Task<(DateTime Start, DateTime End)?> DiscoverTimeBoundsAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        // Step 1: Find series for this hostname
        var queryUrl = PcpSeriesQuery.BuildHostnameFilteredQueryUrl(
            _baseUrl, ProbeMetric, hostname);
        var queryResponse = await _httpClient.GetAsync(queryUrl, cancellationToken);
        queryResponse.EnsureSuccessStatusCode();
        var queryJson = await queryResponse.Content.ReadAsStringAsync(cancellationToken);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);

        if (seriesIds.Count == 0)
            return null;

        // Step 2: Fetch values over a wide window
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-ProbeWindowDays);
        var valuesUrl = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
            _baseUrl, seriesIds, now,
            windowSeconds: ProbeWindowDays * 86400);
        var valuesResponse = await _httpClient.GetAsync(valuesUrl, cancellationToken);
        valuesResponse.EnsureSuccessStatusCode();
        var valuesJson = await valuesResponse.Content.ReadAsStringAsync(cancellationToken);
        var values = PcpSeriesQuery.ParseValuesResponse(valuesJson);

        if (values.Count == 0)
            return null;

        return ArchiveDiscovery.DetectTimeBounds(values);
    }

    /// <summary>
    /// Computes the default playback start time: archive end minus 24 hours,
    /// clamped to the archive start if the archive is shorter than 24 hours.
    /// </summary>
    public static DateTime ComputeDefaultStartTime(DateTime archiveStart, DateTime archiveEnd)
    {
        var candidate = archiveEnd.AddHours(-24);
        return candidate < archiveStart ? archiveStart : candidate;
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "ArchiveSourceDiscovery" -v n
```

Expected: 6 tests pass.

- [ ] **Step 5: Run full test suite to check for regressions**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" -v n
```

- [ ] **Step 6: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/ArchiveSourceDiscovery.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveSourceDiscoveryTests.cs
git commit -m "Add ArchiveSourceDiscovery for host listing and time bounds probing

Composes PcpSeriesClient calls with hostname-filtered queries to
discover available archive sources and their time ranges."
```

---

### Task 4: LoadingPipeline — Accept Mode Config and Branch

**Files:**
- Modify: `src/pmview-app/scripts/LoadingPipeline.cs`
- Modify: `src/pmview-app/scripts/LoadingController.gd`

Note: LoadingPipeline is in the Godot app project which cannot be unit tested in the VM (no Godot SDK). These changes are verified via the full build and manual testing in the host Godot editor.

- [ ] **Step 1: Update `LoadingPipeline.StartPipeline()` signature**

In `LoadingPipeline.cs`, change the method signature and add archive branching:

```csharp
public async void StartPipeline(string endpoint, string mode = "live",
    string hostname = "", string startTime = "")
{
    var currentPhase = 0;
    PcpClientConnection? client = null;

    try
    {
        // Phase 0: CONNECTING
        currentPhase = 0;
        var phaseStart = DateTime.UtcNow;
        client = new PcpClientConnection(new Uri(endpoint));
        await client.ConnectAsync();
        await EnforceMinPhaseDelay(phaseStart);
        EmitSignal(SignalName.PhaseCompleted, 0, "CONNECTING");

        HostTopology topology;

        if (mode == "archive")
        {
            // Archive path: discover topology via /series/* endpoints
            currentPhase = 1;
            phaseStart = DateTime.UtcNow;
            var discoverer = new ArchiveMetricDiscoverer(new Uri(endpoint));
            topology = await discoverer.DiscoverForHostAsync(hostname);
            await EnforceMinPhaseDelay(phaseStart);
            EmitSignal(SignalName.PhaseCompleted, 1, "TOPOLOGY");
        }
        else
        {
            // Live path: existing discovery
            currentPhase = 1;
            phaseStart = DateTime.UtcNow;
            topology = await MetricDiscovery.DiscoverAsync(client);
            await EnforceMinPhaseDelay(phaseStart);
            EmitSignal(SignalName.PhaseCompleted, 1, "TOPOLOGY");
        }

        // Phase 2: INSTANCES (already resolved as part of discovery)
        currentPhase = 2;
        await EnforceMinPhaseDelay(DateTime.UtcNow);
        EmitSignal(SignalName.PhaseCompleted, 2, "INSTANCES");

        // Phase 3: PROFILE
        currentPhase = 3;
        phaseStart = DateTime.UtcNow;
        var profileProvider = new HostProfileProvider();
        var zones = profileProvider.GetProfile(topology.Os);
        await EnforceMinPhaseDelay(phaseStart);
        EmitSignal(SignalName.PhaseCompleted, 3, "PROFILE");

        // Phase 4: LAYOUT
        currentPhase = 4;
        phaseStart = DateTime.UtcNow;
        var layout = LayoutCalculator.Calculate(zones, topology);
        await EnforceMinPhaseDelay(phaseStart);
        EmitSignal(SignalName.PhaseCompleted, 4, "LAYOUT");

        // Phase 5: BUILDING
        currentPhase = 5;
        phaseStart = DateTime.UtcNow;
        BuiltScene = RuntimeSceneBuilder.Build(layout, endpoint);
        await EnforceMinPhaseDelay(phaseStart);
        EmitSignal(SignalName.PhaseCompleted, 5, "BUILDING");

        // Disconnect — the MetricPoller in the built scene manages its own connection
        await client.DisposeAsync();

        EmitSignal(SignalName.PipelineCompleted);
    }
    catch (Exception ex)
    {
        GD.PrintErr($"Pipeline failed at phase {currentPhase}: {ex.Message}");
        EmitSignal(SignalName.PipelineError, currentPhase, ex.Message);

        if (client != null)
        {
            try { await client.DisposeAsync(); }
            catch { /* swallow cleanup errors */ }
        }
    }
}
```

- [ ] **Step 2: Update `LoadingController.gd` to pass config**

Replace the `_ready` function's pipeline call:

```gdscript
func _ready() -> void:
	var config := SceneManager.connection_config
	var endpoint: String = config.get("endpoint", "http://localhost:44322")
	var mode: String = config.get("mode", "live")
	var hostname: String = config.get("hostname", "")
	var start_time: String = config.get("start_time", "")

	pipeline.PhaseCompleted.connect(_on_phase_completed)
	pipeline.PipelineCompleted.connect(_on_pipeline_completed)
	pipeline.PipelineError.connect(_on_pipeline_error)

	pipeline.StartPipeline(endpoint, mode, hostname, start_time)
```

- [ ] **Step 3: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/LoadingPipeline.cs \
        src/pmview-app/scripts/LoadingController.gd
git commit -m "Branch LoadingPipeline on mode for archive topology discovery

Accepts mode/hostname/startTime config. Archive mode uses
ArchiveMetricDiscoverer for topology, live mode unchanged."
```

---

### Task 5: Main Menu — Archive Mode UI

**Files:**
- Modify: `src/pmview-app/scripts/MainMenuController.gd`
- Modify: `src/pmview-app/scenes/main_menu.tscn`

Note: This is pure GDScript UI work. Verified visually in the host Godot editor.

- [ ] **Step 1: Enable ArchiveButton and add archive panel nodes to scene**

In `main_menu.tscn`, change the ArchiveButton to remove `disabled = true`. Add new nodes for the archive panel (hostname dropdown, range label, start time input) as children of the VBoxContainer, between ModeButtons and HSeparator2.

The archive panel nodes should be:
- `ArchivePanel` (VBoxContainer, initially hidden)
  - `HostLabel` (Label: "SOURCE HOST")
  - `HostDropdown` (OptionButton)
  - `RangeLabel` (Label: shows discovered range, initially empty)
  - `StartTimeLabel` (Label: "START TIME (ISO 8601)")
  - `StartTimeInput` (LineEdit: editable ISO 8601 timestamp)

- [ ] **Step 2: Wire MainMenuController.gd**

Replace `MainMenuController.gd` to add archive mode handling:

```gdscript
extends Node3D

## Main menu controller — rotates 3D title letters, handles connection
## form submission, drives KITT scanner hover, and manages archive mode UI.

const ROTATION_SPEED := 0.3
const PHASE_OFFSET := 0.4

@onready var title_group: Node3D = $TitleGroup
@onready var endpoint_input: LineEdit = %EndpointInput
@onready var launch_panel: Panel = %LaunchPanel
@onready var kitt_rect: ColorRect = %KittRect
@onready var live_button: Button = %LiveButton
@onready var archive_button: Button = %ArchiveButton
@onready var archive_panel: VBoxContainer = %ArchivePanel
@onready var host_dropdown: OptionButton = %HostDropdown
@onready var range_label: Label = %RangeLabel
@onready var start_time_input: LineEdit = %StartTimeInput

var _sweep_tween: Tween = null
var _archive_start: String = ""
var _archive_end: String = ""


func _ready() -> void:
	launch_panel.mouse_entered.connect(_on_launch_hover)
	launch_panel.mouse_exited.connect(_on_launch_unhover)
	launch_panel.gui_input.connect(_on_launch_gui_input)

	live_button.pressed.connect(_on_live_pressed)
	archive_button.pressed.connect(_on_archive_pressed)
	host_dropdown.item_selected.connect(_on_host_selected)

	archive_panel.visible = false


func _process(delta: float) -> void:
	_rotate_title_letters(delta)


func _rotate_title_letters(delta: float) -> void:
	title_group.rotate_y((ROTATION_SPEED + sin(Time.get_ticks_msec() * 0.001) * 0.15) * delta)


# --- Mode switching ---

func _on_live_pressed() -> void:
	archive_panel.visible = false


func _on_archive_pressed() -> void:
	archive_panel.visible = true
	_fetch_hostnames()


func _fetch_hostnames() -> void:
	range_label.text = ""
	start_time_input.text = ""
	host_dropdown.clear()
	host_dropdown.add_item("Loading...")
	host_dropdown.disabled = true

	# Use HTTPRequest node for async fetch
	var http := HTTPRequest.new()
	add_child(http)
	http.request_completed.connect(_on_hostnames_response.bind(http))
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:44322"
	http.request(url + "/series/labels?names=hostname")


func _on_hostnames_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest) -> void:
	http.queue_free()
	host_dropdown.clear()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		host_dropdown.add_item("(connection error)")
		return

	var json = JSON.parse_string(body.get_string_from_utf8())
	if json == null or not json.has("hostname"):
		host_dropdown.add_item("(no hosts found)")
		return

	var hostnames: Array = json["hostname"]
	if hostnames.is_empty():
		host_dropdown.add_item("(no hosts found)")
		return

	host_dropdown.disabled = false
	for hostname in hostnames:
		host_dropdown.add_item(hostname)

	# Auto-select first and probe
	_on_host_selected(0)


func _on_host_selected(index: int) -> void:
	var hostname := host_dropdown.get_item_text(index)
	range_label.text = "Probing..."
	_probe_time_bounds(hostname)


func _probe_time_bounds(hostname: String) -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:44322"

	# Step 1: Query series for this hostname (URL-encoded filter)
	var filter := "kernel.all.load{hostname==\"%s\"}" % hostname
	var encoded_filter := filter.uri_encode()
	var query_url := url + "/series/query?expr=" + encoded_filter

	var http := HTTPRequest.new()
	add_child(http)
	http.request_completed.connect(
		_on_series_query_response.bind(http, url, hostname))
	http.request(query_url)


func _on_series_query_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest, endpoint: String, hostname: String) -> void:
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		range_label.text = "Query failed"
		return

	var series_ids = JSON.parse_string(body.get_string_from_utf8())
	if series_ids == null or series_ids.is_empty():
		range_label.text = "No data for this host"
		return

	# Step 2: Fetch values with 30-day window
	var series_id: String = series_ids[0]
	var now := Time.get_unix_time_from_system()
	var start := now - (30 * 86400)
	var values_url := "%s/series/values?series=%s&start=%s&finish=%s" % [
		endpoint, series_id, "%.3f" % start, "%.3f" % now]

	var http2 := HTTPRequest.new()
	add_child(http2)
	http2.request_completed.connect(
		_on_values_response.bind(http2))
	http2.request(values_url)


func _on_values_response(result: int, response_code: int,
		_headers: PackedStringArray, body: PackedByteArray,
		http: HTTPRequest) -> void:
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
		range_label.text = "Probe failed"
		return

	var values = JSON.parse_string(body.get_string_from_utf8())
	if values == null or values.is_empty():
		range_label.text = "No archive data found"
		return

	# Find min/max timestamps
	var min_ts: float = values[0]["timestamp"]
	var max_ts: float = values[0]["timestamp"]
	for v in values:
		var ts: float = v["timestamp"]
		if ts < min_ts:
			min_ts = ts
		if ts > max_ts:
			max_ts = ts

	# Convert epoch ms to ISO 8601
	var start_dt := Time.get_datetime_string_from_unix_time(int(min_ts / 1000.0))
	var end_dt := Time.get_datetime_string_from_unix_time(int(max_ts / 1000.0))
	_archive_start = start_dt
	_archive_end = end_dt
	range_label.text = "RANGE: %s → %s" % [start_dt, end_dt]

	# Default start time: end - 24h, clamped to archive start
	var default_start_epoch := max_ts / 1000.0 - 86400.0
	if default_start_epoch < min_ts / 1000.0:
		default_start_epoch = min_ts / 1000.0
	start_time_input.text = Time.get_datetime_string_from_unix_time(
		int(default_start_epoch)) + "Z"


# --- LAUNCH button hover: KITT scanner effect ---

func _on_launch_hover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return
	mat.set_shader_parameter("intensity", 1.0)
	_kill_sweep_tween()
	_sweep_tween = create_tween().set_loops()
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", 1.4, 0.9)
	_sweep_tween.tween_property(mat, "shader_parameter/sweep_position", -0.4, 0.9)


func _on_launch_unhover() -> void:
	var mat := kitt_rect.material as ShaderMaterial
	if not mat:
		return
	_kill_sweep_tween()
	var fade := create_tween()
	fade.tween_property(mat, "shader_parameter/intensity", 0.0, 0.3)


func _kill_sweep_tween() -> void:
	if _sweep_tween and _sweep_tween.is_valid():
		_sweep_tween.kill()
		_sweep_tween = null


# --- LAUNCH button press ---

func _on_launch_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed \
			and event.button_index == MOUSE_BUTTON_LEFT:
		_launch()


func _launch() -> void:
	var url := endpoint_input.text.strip_edges()
	if url.is_empty():
		url = "http://localhost:44322"

	if archive_button.button_pressed:
		var hostname := host_dropdown.get_item_text(host_dropdown.selected)
		var start_time := start_time_input.text.strip_edges()
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "archive",
			"hostname": hostname,
			"start_time": start_time,
		})
	else:
		SceneManager.go_to_loading({"endpoint": url, "mode": "live"})
```

- [ ] **Step 3: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/MainMenuController.gd \
        src/pmview-app/scenes/main_menu.tscn
git commit -m "Wire archive mode UI in main menu

Enable ArchiveButton, add hostname dropdown with pmproxy source
discovery, time bounds probing, and ISO 8601 start time input."
```

---

### Task 6: HostViewController — Start Archive Playback

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Add archive mode playback start**

Add to `HostViewController.gd` in `_ready()`, after `add_child(scene)`:

```gdscript
func _ready() -> void:
	print("[HostView] _ready called")
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("[HostView] No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	SceneManager.built_scene = null
	print("[HostView] Built scene name: %s, script: %s, child count: %d" % [
		scene.name, scene.get_script(), scene.get_child_count()])
	for child in scene.get_children():
		print("[HostView]   child: %s (type: %s, script: %s)" % [
			child.name, child.get_class(), child.get_script()])

	print("[HostView] Adding built scene to tree...")
	add_child(scene)
	print("[HostView] Built scene added to tree")

	# Archive mode: start playback at configured time
	var config := SceneManager.connection_config
	if config.get("mode", "live") == "archive":
		var poller = scene.find_child("MetricPoller", true, false)
		if poller:
			var start_time: String = config.get("start_time", "")
			print("[HostView] Starting archive playback at: %s" % start_time)
			poller.StartPlayback(start_time)
		else:
			push_error("[HostView] MetricPoller not found in built scene")
```

- [ ] **Step 2: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Start archive playback from HostViewController

Reads mode and start_time from SceneManager config, finds
MetricPoller in built scene, calls StartPlayback for archive mode."
```

---

### Task 7: Full Build + Test Validation

- [ ] **Step 1: Run full CI build and tests**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" -v n
```

Expected: all tests pass, no regressions.

- [ ] **Step 2: Commit any fixes if needed**

---

## Chunk 2: Phase 2 — Time Control

### File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs` | Add InPoint/OutPoint, StepByInterval |
| Test | `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs` | Tests for IN/OUT and stepping |
| Modify | `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs` | Add StepPlayback, JumpToTimestamp, SetInOutRange, ClearRange |
| Create | `src/pmview-app/scripts/time_control.gd` | TimeControl panel — bars, mouse, playhead |
| Create | `src/pmview-app/scenes/time_control.tscn` | TimeControl scene |
| Modify | `src/pmview-app/scripts/HostViewController.gd` | Keyboard shortcuts, TimeControl signal wiring |
| Modify | `src/pmview-app/scripts/RuntimeSceneBuilder.cs` | Add TimeControl to UILayer in archive mode |

---

### Task 8: TimeCursor — InPoint/OutPoint and StepByInterval

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs`

- [ ] **Step 1: Write failing tests for InPoint/OutPoint**

Add to `TimeCursorTests.cs`:

```csharp
// ── IN/OUT point properties ──

[Fact]
public void NewTimeCursor_InPoint_DefaultsToNull()
{
    var cursor = new TimeCursor();
    Assert.Null(cursor.InPoint);
}

[Fact]
public void NewTimeCursor_OutPoint_DefaultsToNull()
{
    var cursor = new TimeCursor();
    Assert.Null(cursor.OutPoint);
}

[Fact]
public void SetInOutRange_SetsInPointAndOutPoint()
{
    var cursor = new TimeCursor();
    var inPoint = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
    var outPoint = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);

    cursor.SetInOutRange(inPoint, outPoint);

    Assert.Equal(inPoint, cursor.InPoint);
    Assert.Equal(outPoint, cursor.OutPoint);
    Assert.Equal(outPoint, cursor.EndBound);
    Assert.True(cursor.Loop);
}

[Fact]
public void ClearInOutRange_ResetsToNull()
{
    var cursor = new TimeCursor();
    var inPoint = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
    var outPoint = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);
    cursor.SetInOutRange(inPoint, outPoint);

    cursor.ClearInOutRange();

    Assert.Null(cursor.InPoint);
    Assert.Null(cursor.OutPoint);
    Assert.Null(cursor.EndBound);
    Assert.False(cursor.Loop);
}

[Fact]
public void AdvanceBy_WithInPoint_LoopsBackToInPoint()
{
    var cursor = new TimeCursor();
    var start = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
    var inPoint = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
    var outPoint = new DateTime(2026, 3, 14, 0, 1, 0, DateTimeKind.Utc); // 1 min range

    cursor.StartPlayback(start);
    cursor.SetInOutRange(inPoint, outPoint);

    // Jump position to near the out point
    cursor.JumpTo(outPoint.AddSeconds(-5));
    // Advance 30 seconds — past the out point
    cursor.AdvanceBy(TimeSpan.FromSeconds(30));

    // Should loop back to IN point, not StartTime
    Assert.Equal(inPoint, cursor.Position);
}

// ── StepByInterval ──

[Fact]
public void StepByInterval_Forward_AdvancesPosition()
{
    var cursor = new TimeCursor();
    var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
    cursor.StartPlayback(start);
    cursor.Pause();

    cursor.StepByInterval(60.0, 1);

    Assert.Equal(start.AddSeconds(60), cursor.Position);
}

[Fact]
public void StepByInterval_Backward_RewindsPosition()
{
    var cursor = new TimeCursor();
    var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
    cursor.StartPlayback(start);
    cursor.AdvanceBy(TimeSpan.FromMinutes(5));
    cursor.Pause();

    cursor.StepByInterval(60.0, -1);

    Assert.Equal(start.AddMinutes(5).AddSeconds(-60), cursor.Position);
}

[Fact]
public void StepByInterval_InLiveMode_DoesNothing()
{
    var cursor = new TimeCursor();

    // Should not throw
    cursor.StepByInterval(60.0, 1);

    Assert.Equal(CursorMode.Live, cursor.Mode);
}

// ── JumpTo ──

[Fact]
public void JumpTo_SetsPositionDirectly()
{
    var cursor = new TimeCursor();
    var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
    cursor.StartPlayback(start);

    var target = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc);
    cursor.JumpTo(target);

    Assert.Equal(target, cursor.Position);
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "InPoint|OutPoint|StepByInterval|JumpTo|SetInOutRange|ClearInOutRange" -v n
```

Expected: compilation errors — new methods don't exist.

- [ ] **Step 3: Implement TimeCursor additions**

Update `TimeCursor.cs`:

```csharp
namespace PcpClient;

public class TimeCursor
{
    private double _playbackSpeed = 1.0;

    public CursorMode Mode { get; private set; } = CursorMode.Live;
    public DateTime Position { get; private set; }
    public DateTime? StartTime { get; private set; }
    public DateTime? EndBound { get; set; }
    public bool Loop { get; set; }
    public DateTime? InPoint { get; private set; }
    public DateTime? OutPoint { get; private set; }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => _playbackSpeed = Math.Clamp(value, 0.1, 100.0);
    }

    public void StartPlayback(DateTime startTime)
    {
        Mode = CursorMode.Playback;
        StartTime = startTime;
        Position = startTime;
    }

    public void Pause()
    {
        if (Mode != CursorMode.Playback)
            throw new InvalidOperationException(
                $"Cannot pause from {Mode} mode. Must be in Playback mode.");
        Mode = CursorMode.Paused;
    }

    public void Resume()
    {
        if (Mode != CursorMode.Paused)
            throw new InvalidOperationException(
                $"Cannot resume from {Mode} mode. Must be in Paused mode.");
        Mode = CursorMode.Playback;
    }

    public void ResetToLive()
    {
        Mode = CursorMode.Live;
        StartTime = null;
        EndBound = null;
        Loop = false;
        InPoint = null;
        OutPoint = null;
    }

    public void AdvanceBy(TimeSpan elapsed)
    {
        if (Mode != CursorMode.Playback)
            return;

        var scaledTicks = (long)(elapsed.Ticks * _playbackSpeed);
        var newPosition = Position.Add(TimeSpan.FromTicks(scaledTicks));

        if (Loop && EndBound.HasValue && newPosition > EndBound.Value)
        {
            // Loop back to InPoint if set, otherwise StartTime
            var loopTarget = InPoint ?? StartTime;
            if (loopTarget.HasValue)
                Position = loopTarget.Value;
            else
                Position = newPosition;
        }
        else
        {
            Position = newPosition;
        }
    }

    /// <summary>
    /// Sets the IN/OUT range for loop playback.
    /// Enables looping and sets EndBound to the OUT point.
    /// </summary>
    public void SetInOutRange(DateTime inPoint, DateTime outPoint)
    {
        InPoint = inPoint;
        OutPoint = outPoint;
        EndBound = outPoint;
        Loop = true;
    }

    /// <summary>
    /// Clears the IN/OUT range and disables looping.
    /// </summary>
    public void ClearInOutRange()
    {
        InPoint = null;
        OutPoint = null;
        EndBound = null;
        Loop = false;
    }

    /// <summary>
    /// Steps the position by a fixed interval. Used for frame-by-frame
    /// scrubbing (arrow keys). Works in Playback or Paused mode.
    /// Direction: +1 = forward, -1 = backward.
    /// </summary>
    public void StepByInterval(double intervalSeconds, int direction)
    {
        if (Mode == CursorMode.Live)
            return;

        Position = Position.AddSeconds(intervalSeconds * direction);
    }

    /// <summary>
    /// Jumps the position to a specific timestamp.
    /// Works in Playback or Paused mode.
    /// </summary>
    public void JumpTo(DateTime target)
    {
        if (Mode == CursorMode.Live)
            return;

        Position = target;
    }
}

public enum CursorMode
{
    Live,
    Playback,
    Paused
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test src/pcp-client-dotnet/tests/PcpClient.Tests/ --filter "TimeCursor" -v n
```

Expected: all TimeCursor tests pass (existing + new).

- [ ] **Step 5: Run full test suite**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" -v n
```

- [ ] **Step 6: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/TimeCursor.cs \
        src/pcp-client-dotnet/tests/PcpClient.Tests/TimeCursorTests.cs
git commit -m "Add IN/OUT points, StepByInterval, and JumpTo to TimeCursor

Loop-back prefers InPoint over StartTime when range is set.
StepByInterval enables frame-by-frame archive scrubbing."
```

---

### Task 9: MetricPoller — Playback Control Methods

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`

Note: MetricPoller is in the addon project which requires Godot runtime for testing. Changes are verified via build compilation and manual testing.

- [ ] **Step 1: Add new playback control methods**

Add to `MetricPoller.cs` after the existing `SetLoop` method:

```csharp
/// <summary>
/// Steps the playback position by a fixed interval.
/// Direction: +1 = forward, -1 = backward.
/// Pauses playback if currently playing, then fetches data at new position.
/// </summary>
public async void StepPlayback(double intervalSeconds, int direction)
{
    if (_timeCursor.Mode == CursorMode.Live)
        return;

    if (_timeCursor.Mode == CursorMode.Playback)
        _timeCursor.Pause();

    _timeCursor.StepByInterval(intervalSeconds, direction);
    EmitSignal(SignalName.PlaybackPositionChanged,
        _timeCursor.Position.ToString("o"), "Paused");

    try
    {
        await FetchHistoricalMetrics();
    }
    catch (Exception ex)
    {
        EmitSignal(SignalName.ErrorOccurred, $"Step fetch error: {ex.Message}");
    }
}

/// <summary>
/// Jumps the playback position to a specific timestamp.
/// Pauses playback if currently playing, then fetches data at new position.
/// </summary>
public async void JumpToTimestamp(string isoTimestamp)
{
    if (DateTime.TryParse(isoTimestamp, null,
        System.Globalization.DateTimeStyles.AdjustToUniversal, out var target))
    {
        if (_timeCursor.Mode == CursorMode.Live)
            return;

        if (_timeCursor.Mode == CursorMode.Playback)
            _timeCursor.Pause();

        _timeCursor.JumpTo(target);
        _lastEmittedTimestamp.Clear();
        EmitSignal(SignalName.PlaybackPositionChanged,
            target.ToString("o"), "Paused");

        try
        {
            await FetchHistoricalMetrics();
        }
        catch (Exception ex)
        {
            EmitSignal(SignalName.ErrorOccurred, $"Jump fetch error: {ex.Message}");
        }
    }
    else
    {
        EmitSignal(SignalName.ErrorOccurred,
            $"Invalid timestamp format: {isoTimestamp}");
    }
}

/// <summary>
/// Sets the IN/OUT range for loop playback.
/// Enables looping between the two timestamps.
/// </summary>
public void SetInOutRange(string inPointIso, string outPointIso)
{
    if (DateTime.TryParse(inPointIso, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal, out var inPoint)
        && DateTime.TryParse(outPointIso, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal, out var outPoint))
    {
        _timeCursor.SetInOutRange(inPoint, outPoint);

        if (_timeCursor.Mode == CursorMode.Paused)
            _timeCursor.Resume();
    }
    else
    {
        EmitSignal(SignalName.ErrorOccurred,
            $"Invalid range format: {inPointIso} → {outPointIso}");
    }
}

/// <summary>
/// Clears the IN/OUT range and resumes playback.
/// </summary>
public void ClearRange()
{
    _timeCursor.ClearInOutRange();

    if (_timeCursor.Mode == CursorMode.Paused)
        _timeCursor.Resume();

    EmitSignal(SignalName.PlaybackPositionChanged,
        _timeCursor.Position.ToString("o"), "Playback");
}
```

- [ ] **Step 2: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs
git commit -m "Add StepPlayback, JumpToTimestamp, SetInOutRange, ClearRange

Thin wrappers around TimeCursor state changes that also trigger
data fetching and emit position signals for UI updates."
```

---

### Task 10: TimeControl GDScript Scene

**Files:**
- Create: `src/pmview-app/scripts/time_control.gd`
- Create: `src/pmview-app/scenes/time_control.tscn`

- [ ] **Step 1: Create `time_control.gd`**

```gdscript
extends Control

## Time Control panel — Time Machine-style timeline navigation.
## Anchored to right edge, translucent overlay with timeline bars.
## Archive mode only.

signal playhead_jumped(timestamp: String)
signal range_set(in_time: String, out_time: String)
signal range_cleared()
signal panel_opened()
signal panel_closed()

const EDGE_TRIGGER_WIDTH := 60  # pixels from right edge to trigger
const BAR_MAX_LENGTH := 60.0    # max bar length in pixels
const BAR_MIN_LENGTH := 8.0     # min bar length
const BAR_HEIGHT := 3.0
const BAR_SPACING := 4.0
const ATTRACTION_RADIUS := 120.0  # pixels of mouse influence
const PANEL_ALPHA := 0.85

var archive_start: float = 0.0   # epoch seconds
var archive_end: float = 0.0     # epoch seconds
var playhead_position: float = 0.0  # epoch seconds
var in_point: float = -1.0       # epoch seconds, -1 = not set
var out_point: float = -1.0      # epoch seconds, -1 = not set
var _is_visible := false
var _f2_dismissed := false  # user explicitly dismissed via F2

enum RangeState { NO_RANGE, IN_SET, RANGE_COMPLETE }
var _range_state: RangeState = RangeState.NO_RANGE

# Colours
var colour_active := Color(0.514, 0.22, 0.925, 0.7)   # purple, translucent
var colour_inactive := Color(0.3, 0.3, 0.3, 0.3)      # grey, translucent
var colour_playhead := Color(0.976, 0.451, 0.086, 0.9) # orange
var colour_in_point := Color(0.298, 0.686, 0.314, 0.9) # green
var colour_out_point := Color(0.937, 0.325, 0.314, 0.9) # red
var colour_bg := Color(0.05, 0.04, 0.08, 0.0)          # transparent by default

@onready var timestamp_label: Label = $TimestampLabel


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_PASS
	visible = false


func _process(_delta: float) -> void:
	if not _is_visible:
		# Check mouse proximity to right edge
		var viewport_size := get_viewport_rect().size
		var mouse_pos := get_global_mouse_position()
		if mouse_pos.x > viewport_size.x - EDGE_TRIGGER_WIDTH and not _f2_dismissed:
			_show_panel()
	else:
		var viewport_size := get_viewport_rect().size
		var mouse_pos := get_global_mouse_position()
		if mouse_pos.x < viewport_size.x - EDGE_TRIGGER_WIDTH - 40:
			_hide_panel()

	if _is_visible:
		queue_redraw()
		_update_timestamp_tooltip()


func _show_panel() -> void:
	if _is_visible:
		return
	_is_visible = true
	visible = true
	panel_opened.emit()


func _hide_panel() -> void:
	if not _is_visible:
		return
	_is_visible = false
	visible = false
	panel_closed.emit()


func toggle_panel() -> void:
	if _is_visible:
		_f2_dismissed = true
		_hide_panel()
	else:
		_f2_dismissed = false
		_show_panel()


func update_playhead(position_iso: String, _mode: String) -> void:
	# Parse ISO timestamp to epoch seconds
	var dict := Time.get_datetime_dict_from_datetime_string(position_iso, false)
	if dict.is_empty():
		return
	playhead_position = Time.get_unix_time_from_datetime_dict(dict)
	if _is_visible:
		queue_redraw()


func set_archive_bounds(start_epoch: float, end_epoch: float) -> void:
	archive_start = start_epoch
	archive_end = end_epoch


func _draw() -> void:
	if archive_end <= archive_start:
		return

	var rect := get_rect()
	var panel_width := 80.0
	var panel_x := rect.size.x - panel_width
	var mouse_pos := get_local_mouse_position()

	# Draw translucent background
	draw_rect(Rect2(panel_x, 0, panel_width, rect.size.y),
		Color(0.05, 0.04, 0.08, PANEL_ALPHA * 0.6))

	# Calculate bar positions
	var usable_height := rect.size.y - 40  # 20px padding top/bottom
	var bar_count := int(usable_height / (BAR_HEIGHT + BAR_SPACING))
	if bar_count <= 0:
		return

	var time_range := archive_end - archive_start
	var bar_right := rect.size.x - 8.0

	for i in bar_count:
		var y := 20.0 + i * (BAR_HEIGHT + BAR_SPACING)
		var t := archive_start + (float(i) / float(bar_count - 1)) * time_range

		# Determine bar colour based on IN/OUT range
		var colour: Color
		if _range_state == RangeState.NO_RANGE:
			colour = colour_active
		elif in_point >= 0 and out_point >= 0:
			colour = colour_active if t >= in_point and t <= out_point else colour_inactive
		elif in_point >= 0:
			colour = colour_active if t >= in_point else colour_inactive
		else:
			colour = colour_active

		# Mouse attraction — bars extend toward mouse
		var distance := abs(mouse_pos.y - y)
		var attraction := clampf(1.0 - distance / ATTRACTION_RADIUS, 0.0, 1.0)
		var bar_length: float = BAR_MIN_LENGTH + (BAR_MAX_LENGTH - BAR_MIN_LENGTH) * attraction

		# Special bars: playhead, IN, OUT
		var is_playhead := abs(t - playhead_position) < time_range / float(bar_count)
		var is_in := in_point >= 0 and abs(t - in_point) < time_range / float(bar_count)
		var is_out := out_point >= 0 and abs(t - out_point) < time_range / float(bar_count)

		if is_playhead:
			colour = colour_playhead
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.8)
		elif is_in:
			colour = colour_in_point
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.65)
		elif is_out:
			colour = colour_out_point
			bar_length = maxf(bar_length, BAR_MAX_LENGTH * 0.65)

		draw_rect(Rect2(bar_right - bar_length, y, bar_length, BAR_HEIGHT), colour)


func _update_timestamp_tooltip() -> void:
	if not timestamp_label:
		return

	var rect := get_rect()
	var mouse_pos := get_local_mouse_position()
	var usable_height := rect.size.y - 40
	var relative_y := clampf((mouse_pos.y - 20.0) / usable_height, 0.0, 1.0)
	var time_range := archive_end - archive_start
	var hover_time := archive_start + relative_y * time_range
	var hover_dt := Time.get_datetime_string_from_unix_time(int(hover_time))

	timestamp_label.text = hover_dt + "Z"
	timestamp_label.position = Vector2(
		rect.size.x - 200, mouse_pos.y - 10)


func _gui_input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	if not event.pressed or event.button_index != MOUSE_BUTTON_LEFT:
		return

	# Calculate timestamp from click position
	var rect := get_rect()
	var usable_height := rect.size.y - 40
	var relative_y := clampf((event.position.y - 20.0) / usable_height, 0.0, 1.0)
	var time_range := archive_end - archive_start
	var clicked_time := archive_start + relative_y * time_range
	var clicked_iso := Time.get_datetime_string_from_unix_time(int(clicked_time)) + "Z"

	if event.shift_pressed:
		_handle_shift_click(clicked_time, clicked_iso)
	else:
		playhead_jumped.emit(clicked_iso)


func _handle_shift_click(time_epoch: float, time_iso: String) -> void:
	match _range_state:
		RangeState.NO_RANGE:
			in_point = time_epoch
			out_point = archive_end
			_range_state = RangeState.IN_SET
			var out_iso := Time.get_datetime_string_from_unix_time(
				int(archive_end)) + "Z"
			range_set.emit(time_iso, out_iso)
		RangeState.IN_SET:
			out_point = time_epoch
			_range_state = RangeState.RANGE_COMPLETE
			var in_iso := Time.get_datetime_string_from_unix_time(
				int(in_point)) + "Z"
			range_set.emit(in_iso, time_iso)
		RangeState.RANGE_COMPLETE:
			# Start new range
			in_point = time_epoch
			out_point = archive_end
			_range_state = RangeState.IN_SET
			var out_iso := Time.get_datetime_string_from_unix_time(
				int(archive_end)) + "Z"
			range_set.emit(time_iso, out_iso)


func reset_range() -> void:
	in_point = -1.0
	out_point = -1.0
	_range_state = RangeState.NO_RANGE
	range_cleared.emit()
```

- [ ] **Step 2: Create `time_control.tscn`**

Create the scene file with a Control root, anchored to fill the viewport, plus a Label child for the timestamp tooltip.

```
[gd_scene format=3]

[ext_resource type="Script" path="res://scripts/time_control.gd" id="1"]

[node name="TimeControl" type="Control"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 1
script = ExtResource("1")

[node name="TimestampLabel" type="Label" parent="."]
layout_mode = 0
offset_right = 200.0
offset_bottom = 20.0
theme_override_font_sizes/font_size = 10
horizontal_alignment = 2
```

- [ ] **Step 3: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/time_control.gd \
        src/pmview-app/scenes/time_control.tscn
git commit -m "Add TimeControl panel for Time Machine-style archive navigation

Translucent right-edge overlay with timeline bars, mouse attraction
effect, playhead display, and SHIFT+Click IN/OUT point state machine."
```

---

### Task 11: RuntimeSceneBuilder — Add TimeControl to UILayer

**Files:**
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs`

- [ ] **Step 1: Add TimeControl to UILayer in archive mode**

Add a new method and call it from `Build()`. The mode is passed through the pipeline — add a `mode` parameter to `Build()`:

Update `Build` signature to accept mode:

```csharp
public static Node3D Build(SceneLayout layout, string pmproxyEndpoint,
    string mode = "live", IProgress<float>? progress = null)
```

Add after `AddRangeTuningPanel(root)`:

```csharp
if (mode == "archive")
    AddTimeControl(root);
```

Add the method:

```csharp
private static void AddTimeControl(Node3D sceneRoot)
{
    var timeControlScene = GD.Load<PackedScene>("res://scenes/time_control.tscn");
    if (timeControlScene == null)
    {
        GD.PushWarning("[RuntimeSceneBuilder] TimeControl scene not found");
        return;
    }

    // Find the existing UILayer canvas
    var uiLayer = sceneRoot.FindChild("UILayer", true, false) as CanvasLayer;
    if (uiLayer == null)
    {
        GD.PushWarning("[RuntimeSceneBuilder] UILayer not found — creating one");
        uiLayer = new CanvasLayer { Name = "UILayer" };
        sceneRoot.AddChild(uiLayer);
    }

    var timeControl = timeControlScene.Instantiate();
    timeControl.Name = "TimeControl";
    uiLayer.AddChild(timeControl);
}
```

Update the call in `LoadingPipeline.cs` to pass mode:

```csharp
BuiltScene = RuntimeSceneBuilder.Build(layout, endpoint, mode);
```

- [ ] **Step 2: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/RuntimeSceneBuilder.cs \
        src/pmview-app/scripts/LoadingPipeline.cs
git commit -m "Add TimeControl to UILayer in archive mode

RuntimeSceneBuilder instantiates time_control.tscn as a child
of the existing UILayer CanvasLayer when mode is archive."
```

---

### Task 12: HostViewController — Keyboard Shortcuts and Signal Wiring

**Files:**
- Modify: `src/pmview-app/scripts/HostViewController.gd`

- [ ] **Step 1: Add archive keyboard shortcuts and TimeControl signal wiring**

Update `HostViewController.gd` to wire TimeControl signals and handle keyboard input:

```gdscript
extends Node3D

## Host view controller. Receives the runtime-built scene from SceneManager,
## wires up MetricPoller and SceneBinder, handles ESC overlay and archive controls.

@onready var esc_label: Label = %EscLabel

var _esc_pending := false
var _esc_timer: SceneTreeTimer = null
var _poller: Node = null
var _time_control: Control = null
var _is_archive_mode := false
var _poll_interval_seconds := 60.0  # default, updated from archive metadata


func _ready() -> void:
	print("[HostView] _ready called")
	var scene := SceneManager.built_scene
	if scene == null:
		push_error("[HostView] No built scene — returning to menu")
		SceneManager.go_to_main_menu()
		return

	SceneManager.built_scene = null
	print("[HostView] Built scene name: %s, script: %s, child count: %d" % [
		scene.name, scene.get_script(), scene.get_child_count()])
	for child in scene.get_children():
		print("[HostView]   child: %s (type: %s, script: %s)" % [
			child.name, child.get_class(), child.get_script()])

	print("[HostView] Adding built scene to tree...")
	add_child(scene)
	print("[HostView] Built scene added to tree")

	var config := SceneManager.connection_config
	_is_archive_mode = config.get("mode", "live") == "archive"

	if _is_archive_mode:
		_poller = scene.find_child("MetricPoller", true, false)
		_time_control = scene.find_child("TimeControl", true, false)

		if _poller:
			var start_time: String = config.get("start_time", "")
			print("[HostView] Starting archive playback at: %s" % start_time)
			_poller.StartPlayback(start_time)

			# Wire MetricPoller feedback to TimeControl
			if _time_control:
				_poller.PlaybackPositionChanged.connect(
					_time_control.update_playhead)
				_time_control.playhead_jumped.connect(_on_playhead_jumped)
				_time_control.range_set.connect(_on_range_set)
				_time_control.range_cleared.connect(_on_range_cleared)
				_time_control.panel_opened.connect(_on_panel_opened)
				_time_control.panel_closed.connect(_on_panel_closed)

				# Set archive bounds on TimeControl
				# Parse start_time to get epoch, use archive range from config
				# For now, the TimeControl will get bounds from the playback position signals
		else:
			push_error("[HostView] MetricPoller not found in built scene")


func _on_playhead_jumped(timestamp: String) -> void:
	if _poller:
		_poller.JumpToTimestamp(timestamp)


func _on_range_set(in_time: String, out_time: String) -> void:
	if _poller:
		_poller.SetInOutRange(in_time, out_time)


func _on_range_cleared() -> void:
	if _poller:
		_poller.ClearRange()


func _on_panel_opened() -> void:
	if _poller:
		_poller.PausePlayback()


func _on_panel_closed() -> void:
	if _poller:
		_poller.ResumePlayback()


func _unhandled_input(event: InputEvent) -> void:
	# ESC — double-press to return to menu
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if _esc_pending:
			SceneManager.go_to_main_menu()
		else:
			_esc_pending = true
			esc_label.visible = true
			_esc_timer = get_tree().create_timer(2.0)
			_esc_timer.timeout.connect(_dismiss_esc)
		return

	if not _is_archive_mode:
		return

	# Archive mode keyboard shortcuts
	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_SPACE:
				get_viewport().set_input_as_handled()
				if _poller:
					if _poller.TimeCursor.Mode == 1:  # CursorMode.Playback
						_poller.PausePlayback()
					elif _poller.TimeCursor.Mode == 2:  # CursorMode.Paused
						_poller.ResumePlayback()
			KEY_LEFT:
				get_viewport().set_input_as_handled()
				if _poller:
					var step := 5.0 if event.shift_pressed else _poll_interval_seconds
					_poller.StepPlayback(step, -1)
			KEY_RIGHT:
				get_viewport().set_input_as_handled()
				if _poller:
					var step := 5.0 if event.shift_pressed else _poll_interval_seconds
					_poller.StepPlayback(step, 1)
			KEY_R:
				get_viewport().set_input_as_handled()
				if _poller and _time_control:
					_time_control.reset_range()
					_poller.ClearRange()
			KEY_F2:
				get_viewport().set_input_as_handled()
				if _time_control:
					_time_control.toggle_panel()


func _dismiss_esc() -> void:
	_esc_pending = false
	esc_label.visible = false


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		SceneManager.quit_app()
```

- [ ] **Step 2: Verify build compiles**

```bash
dotnet build pmview-nextgen.ci.slnf
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/HostViewController.gd
git commit -m "Wire archive keyboard shortcuts and TimeControl signals

Space play/pause, arrow key scrubbing, SHIFT+arrows for 5s jumps,
R to reset range, F2 to toggle Time Control panel."
```

---

### Task 13: Final Build + Test Validation

- [ ] **Step 1: Run full CI build and tests**

```bash
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration" -v n
```

Expected: all tests pass, no regressions.

- [ ] **Step 2: Verify all new files are tracked**

```bash
git status
```

- [ ] **Step 3: Final commit if any cleanup needed**

---

## Documentation Checklist

- [ ] Update `docs/ARCHITECTURE.md` to document archive mode flow and Time Control
- [ ] Update `README.md` with archive mode usage (selecting sources, keyboard shortcuts)
