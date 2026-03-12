# Archive-Mode Metric Browsing Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable the editor MetricBrowserDialog to discover and browse metrics from archived host data via pmproxy's `/series/` API, and remove superseded runtime prototype code.

**Architecture:** New `PcpSeriesClient` (stateless HTTP for `/series/*`) + `ArchiveMetricDiscoverer` (orchestration with caching) + `NamespaceTreeBuilder` (flat names → tree) in PcpClient library. `MetricBrowserDialog` becomes mode-aware. Runtime `MetricBrowser` and `PlaybackControls` prototypes deleted.

**Tech Stack:** C# (.NET 8.0), xUnit, System.Net.Http, System.Text.Json, Godot 4.4+ editor API

**Spec:** `docs/superpowers/specs/2026-03-12-archive-metric-browsing-design.md`

---

## Chunk 1: Series Response Parsers

New parse methods in `PcpSeriesQuery` for `/series/labels`, `/series/metrics`, and `/series/descs` responses.

### Task 1: Parse `/series/labels` response

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests for `ParseLabelsResponse`**

Add to `SeriesQueryTests.cs`:

```csharp
// ── PcpSeriesQuery.ParseLabelsResponse — label value extraction ──

[Fact]
public void ParseLabelsResponse_ReturnsLabelValues()
{
    var json = """{"hostname": ["app", "nas", "webserver01"]}""";

    var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");

    Assert.Equal(3, result.Count);
    Assert.Contains("app", result);
    Assert.Contains("nas", result);
    Assert.Contains("webserver01", result);
}

[Fact]
public void ParseLabelsResponse_EmptyValues_ReturnsEmpty()
{
    var json = """{"hostname": []}""";

    var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");

    Assert.Empty(result);
}

[Fact]
public void ParseLabelsResponse_MissingLabel_ReturnsEmpty()
{
    var json = """{"otherlabel": ["value1"]}""";

    var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");

    Assert.Empty(result);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseLabelsResponse"`
Expected: FAIL — method does not exist

- [ ] **Step 3: Implement `ParseLabelsResponse`**

Add to `PcpSeriesQuery.cs`:

```csharp
/// <summary>
/// Parses a /series/labels?names=X response to extract values for a specific label.
/// Response format: {"labelname": ["value1", "value2"]}
/// </summary>
public static IReadOnlyList<string> ParseLabelsResponse(string json, string labelName)
{
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty(labelName, out var values))
        return Array.Empty<string>();

    var results = new List<string>();
    foreach (var item in values.EnumerateArray())
    {
        var val = item.GetString();
        if (val != null)
            results.Add(val);
    }
    return results;
}

public static Uri BuildLabelsUrl(Uri baseUrl, string labelName)
{
    return new Uri(baseUrl, $"/series/labels?names={Uri.EscapeDataString(labelName)}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseLabelsResponse"`
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "add /series/labels response parsing for host discovery"
```

### Task 2: Parse `/series/metrics` response

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests for `ParseMetricsResponse`**

Add to `SeriesQueryTests.cs`:

```csharp
// ── PcpSeriesQuery.ParseMetricsResponse — series-to-metric-name mapping ──

[Fact]
public void ParseMetricsResponse_ReturnsSeriesMetricNames()
{
    var json = """
    [
        {"series": "abc123", "name": "disk.dev.read"},
        {"series": "def456", "name": "disk.dev.write"}
    ]
    """;

    var result = PcpSeriesQuery.ParseMetricsResponse(json);

    Assert.Equal(2, result.Count);
    Assert.Equal("abc123", result[0].SeriesId);
    Assert.Equal("disk.dev.read", result[0].Name);
    Assert.Equal("def456", result[1].SeriesId);
    Assert.Equal("disk.dev.write", result[1].Name);
}

[Fact]
public void ParseMetricsResponse_EmptyArray_ReturnsEmpty()
{
    var json = "[]";

    var result = PcpSeriesQuery.ParseMetricsResponse(json);

    Assert.Empty(result);
}

[Fact]
public void BuildMetricsUrl_FormatsSeriesIds()
{
    var url = PcpSeriesQuery.BuildMetricsUrl(
        new Uri("http://localhost:44322"),
        new[] { "abc123", "def456" });

    var urlStr = url.ToString();
    Assert.Contains("/series/metrics", urlStr);
    Assert.Contains("abc123", urlStr);
    Assert.Contains("def456", urlStr);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseMetricsResponse|BuildMetricsUrl"`
Expected: FAIL — types and methods do not exist

- [ ] **Step 3: Add `SeriesMetricName` record and implement parser**

Add record (can go at the end of `PcpSeriesQuery.cs` with the other records):

```csharp
/// <summary>
/// Metric name associated with a series identifier, from /series/metrics response.
/// </summary>
public record SeriesMetricName(string SeriesId, string Name);
```

Add methods to `PcpSeriesQuery`:

```csharp
/// <summary>
/// Parses a /series/metrics response into series-to-metric-name mappings.
/// Response format: [{"series": "<hash>", "name": "metric.name"}, ...]
/// </summary>
public static IReadOnlyList<SeriesMetricName> ParseMetricsResponse(string json)
{
    using var doc = JsonDocument.Parse(json);
    var results = new List<SeriesMetricName>();

    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var seriesId = item.GetProperty("series").GetString()!;
        var name = item.GetProperty("name").GetString()!;
        results.Add(new SeriesMetricName(seriesId, name));
    }

    return results;
}

public static Uri BuildMetricsUrl(Uri baseUrl, IEnumerable<string> seriesIds)
{
    var ids = string.Join(",", seriesIds);
    return new Uri(baseUrl, $"/series/metrics?series={Uri.EscapeDataString(ids)}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseMetricsResponse|BuildMetricsUrl"`
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "add /series/metrics response parsing for metric name discovery"
```

### Task 3: Parse `/series/descs` response

**Files:**
- Modify: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs`
- Test: `src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs`

- [ ] **Step 1: Write failing tests for `ParseDescsResponse`**

Add to `SeriesQueryTests.cs`:

```csharp
// ── PcpSeriesQuery.ParseDescsResponse — metric descriptor extraction ──

[Fact]
public void ParseDescsResponse_ReturnsDescriptors()
{
    var json = """
    [
        {
            "series": "abc123",
            "pmid": "60.0.32",
            "indom": "60.1",
            "semantics": "counter",
            "type": "u64",
            "units": "Kbyte / sec"
        }
    ]
    """;

    var result = PcpSeriesQuery.ParseDescsResponse(json);

    Assert.Single(result);
    Assert.Equal("abc123", result[0].SeriesId);
    Assert.Equal("60.0.32", result[0].Pmid);
    Assert.Equal("60.1", result[0].Indom);
    Assert.Equal("counter", result[0].Semantics);
    Assert.Equal("u64", result[0].Type);
    Assert.Equal("Kbyte / sec", result[0].Units);
}

[Fact]
public void ParseDescsResponse_MissingOptionalFields_ReturnsNulls()
{
    var json = """
    [
        {
            "series": "abc123",
            "semantics": "instant",
            "type": "float"
        }
    ]
    """;

    var result = PcpSeriesQuery.ParseDescsResponse(json);

    Assert.Single(result);
    Assert.Null(result[0].Pmid);
    Assert.Null(result[0].Indom);
    Assert.Null(result[0].Units);
}

[Fact]
public void ParseDescsResponse_EmptyArray_ReturnsEmpty()
{
    var json = "[]";

    var result = PcpSeriesQuery.ParseDescsResponse(json);

    Assert.Empty(result);
}

[Fact]
public void BuildDescsUrl_FormatsSeriesIds()
{
    var url = PcpSeriesQuery.BuildDescsUrl(
        new Uri("http://localhost:44322"),
        new[] { "abc123", "def456" });

    var urlStr = url.ToString();
    Assert.Contains("/series/descs", urlStr);
    Assert.Contains("abc123", urlStr);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseDescsResponse|BuildDescsUrl"`
Expected: FAIL — types and methods do not exist

- [ ] **Step 3: Add `SeriesDescriptor` record and implement parser**

Add record:

```csharp
/// <summary>
/// Metric descriptor from a /series/descs response: type, semantics, units.
/// </summary>
public record SeriesDescriptor(
    string SeriesId,
    string? Pmid,
    string? Indom,
    string? Semantics,
    string? Type,
    string? Units);
```

Add methods to `PcpSeriesQuery`:

```csharp
/// <summary>
/// Parses a /series/descs response into metric descriptors.
/// Response format: [{"series": "<hash>", "pmid": "...", "semantics": "...", ...}, ...]
/// </summary>
public static IReadOnlyList<SeriesDescriptor> ParseDescsResponse(string json)
{
    using var doc = JsonDocument.Parse(json);
    var results = new List<SeriesDescriptor>();

    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var seriesId = item.GetProperty("series").GetString()!;
        var pmid = item.TryGetProperty("pmid", out var p) ? p.GetString() : null;
        var indom = item.TryGetProperty("indom", out var i) ? i.GetString() : null;
        var semantics = item.TryGetProperty("semantics", out var s) ? s.GetString() : null;
        var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
        var units = item.TryGetProperty("units", out var u) ? u.GetString() : null;

        results.Add(new SeriesDescriptor(seriesId, pmid, indom, semantics, type, units));
    }

    return results;
}

public static Uri BuildDescsUrl(Uri baseUrl, IEnumerable<string> seriesIds)
{
    var ids = string.Join(",", seriesIds);
    return new Uri(baseUrl, $"/series/descs?series={Uri.EscapeDataString(ids)}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ParseDescsResponse|BuildDescsUrl"`
Expected: 4 PASS

- [ ] **Step 5: Run full test suite to verify no regressions**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln`
Expected: All tests pass (existing + new)

- [ ] **Step 6: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesQuery.cs src/pcp-client-dotnet/tests/PcpClient.Tests/SeriesQueryTests.cs
git commit -m "add /series/descs response parsing for metric descriptors"
```

## Chunk 2: PcpSeriesClient

### Task 4: `PcpSeriesClient` — stateless HTTP layer

**Files:**
- Create: `src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs`
- Create: `src/pcp-client-dotnet/tests/PcpClient.Tests/PcpSeriesClientTests.cs`

- [ ] **Step 1: Write failing tests for `GetHostnamesAsync`**

Create `PcpSeriesClientTests.cs`:

```csharp
using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for PcpSeriesClient: stateless HTTP layer for /series/* endpoints.
/// </summary>
public class PcpSeriesClientTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    // ── GetHostnamesAsync ──

    [Fact]
    public async Task GetHostnamesAsync_ReturnsHostnames()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/labels", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"hostname": ["app", "nas"]}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);

        var result = await client.GetHostnamesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("app", result);
        Assert.Contains("nas", result);
    }

    [Fact]
    public async Task GetHostnamesAsync_EmptyResponse_ReturnsEmpty()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"hostname": []}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);

        var result = await client.GetHostnamesAsync();

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "PcpSeriesClientTests"`
Expected: FAIL — class does not exist

- [ ] **Step 3: Implement `PcpSeriesClient` with `GetHostnamesAsync`**

Create `PcpSeriesClient.cs`:

```csharp
namespace PcpClient;

/// <summary>
/// Stateless HTTP client for pmproxy /series/* endpoints.
/// No context management — just request/parse/return.
/// Caller owns the HttpClient.
/// </summary>
public sealed class PcpSeriesClient
{
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    public PcpSeriesClient(Uri baseUrl, HttpClient httpClient)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<string>> GetHostnamesAsync(
        CancellationToken cancellationToken = default)
    {
        var url = PcpSeriesQuery.BuildLabelsUrl(_baseUrl, "hostname");
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseLabelsResponse(json, "hostname");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "PcpSeriesClientTests"`
Expected: 2 PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs src/pcp-client-dotnet/tests/PcpClient.Tests/PcpSeriesClientTests.cs
git commit -m "add PcpSeriesClient with hostname discovery via /series/labels"
```

- [ ] **Step 6: Write failing tests for remaining methods**

Add to `PcpSeriesClientTests.cs`:

```csharp
// ── QuerySeriesAsync ──

[Fact]
public async Task QuerySeriesAsync_ReturnsSeriesIds()
{
    var handler = new MockHttpHandler(req =>
    {
        Assert.Contains("/series/query", req.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """["abc123", "def456"]""",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);

    var result = await client.QuerySeriesAsync("disk.*");

    Assert.Equal(2, result.Count);
}

[Fact]
public async Task QuerySeriesAsync_EncodesSpecialCharacters()
{
    string? capturedUrl = null;
    var handler = new MockHttpHandler(req =>
    {
        capturedUrl = req.RequestUri!.ToString();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);

    await client.QuerySeriesAsync("""*{hostname=="web server.local"}""");

    Assert.NotNull(capturedUrl);
    Assert.Contains("/series/query", capturedUrl);
    var encodedPart = capturedUrl.Split("expr=")[1];
    // Spaces, braces, and quotes must all be URL-encoded
    Assert.DoesNotContain(" ", encodedPart);
    Assert.Contains("%7B", encodedPart); // { encoded
    Assert.Contains("%22", encodedPart); // " encoded
}

// ── GetMetricNamesAsync ──

[Fact]
public async Task GetMetricNamesAsync_ReturnsNames()
{
    var handler = new MockHttpHandler(req =>
    {
        Assert.Contains("/series/metrics", req.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"series": "abc123", "name": "disk.dev.read"}]""",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);

    var result = await client.GetMetricNamesAsync(new[] { "abc123" });

    Assert.Single(result);
    Assert.Equal("disk.dev.read", result[0].Name);
}

// ── GetDescriptorsAsync ──

[Fact]
public async Task GetDescriptorsAsync_ReturnsDescriptors()
{
    var handler = new MockHttpHandler(req =>
    {
        Assert.Contains("/series/descs", req.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"series": "abc123", "semantics": "counter", "type": "u64", "units": "count"}]""",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);

    var result = await client.GetDescriptorsAsync(new[] { "abc123" });

    Assert.Single(result);
    Assert.Equal("counter", result[0].Semantics);
}

// ── GetInstancesAsync — delegates to PcpSeriesQuery ──

[Fact]
public async Task GetInstancesAsync_ReturnsInstanceMapping()
{
    var handler = new MockHttpHandler(req =>
    {
        Assert.Contains("/series/instances", req.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """[{"series": "abc123", "instance": "inst1", "id": 0, "name": "sda"}]""",
                System.Text.Encoding.UTF8, "application/json")
        };
    });
    var httpClient = new HttpClient(handler);
    var client = new PcpSeriesClient(BaseUrl, httpClient);

    var result = await client.GetInstancesAsync(new[] { "abc123" });

    Assert.Single(result);
    Assert.Equal("sda", result["inst1"].Name);
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "PcpSeriesClientTests"`
Expected: new tests FAIL — methods do not exist

- [ ] **Step 8: Implement remaining methods**

Add to `PcpSeriesClient.cs`:

```csharp
public async Task<IReadOnlyList<string>> QuerySeriesAsync(
    string expression, CancellationToken cancellationToken = default)
{
    var url = PcpSeriesQuery.BuildQueryUrl(_baseUrl, expression);
    var response = await _httpClient.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return PcpSeriesQuery.ParseQueryResponse(json);
}

public async Task<IReadOnlyList<SeriesMetricName>> GetMetricNamesAsync(
    IEnumerable<string> seriesIds, CancellationToken cancellationToken = default)
{
    var url = PcpSeriesQuery.BuildMetricsUrl(_baseUrl, seriesIds);
    var response = await _httpClient.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return PcpSeriesQuery.ParseMetricsResponse(json);
}

public async Task<IReadOnlyList<SeriesDescriptor>> GetDescriptorsAsync(
    IEnumerable<string> seriesIds, CancellationToken cancellationToken = default)
{
    var url = PcpSeriesQuery.BuildDescsUrl(_baseUrl, seriesIds);
    var response = await _httpClient.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return PcpSeriesQuery.ParseDescsResponse(json);
}

public async Task<Dictionary<string, SeriesInstanceInfo>> GetInstancesAsync(
    IEnumerable<string> seriesIds, CancellationToken cancellationToken = default)
{
    var url = PcpSeriesQuery.BuildInstancesUrl(_baseUrl, seriesIds);
    var response = await _httpClient.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return PcpSeriesQuery.ParseInstancesResponse(json);
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "PcpSeriesClientTests"`
Expected: All PASS

- [ ] **Step 10: Run full test suite**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln`
Expected: All tests pass

- [ ] **Step 11: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/PcpSeriesClient.cs src/pcp-client-dotnet/tests/PcpClient.Tests/PcpSeriesClientTests.cs
git commit -m "complete PcpSeriesClient with query, metrics, descs, instances methods"
```

## Chunk 3: NamespaceTreeBuilder

### Task 5: `NamespaceTreeBuilder` — flat metric names to tree

**Files:**
- Create: `src/pcp-client-dotnet/src/PcpClient/NamespaceTreeBuilder.cs`
- Create: `src/pcp-client-dotnet/tests/PcpClient.Tests/NamespaceTreeBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `NamespaceTreeBuilderTests.cs`:

```csharp
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for NamespaceTreeBuilder: flat metric names to hierarchical tree.
/// </summary>
public class NamespaceTreeBuilderTests
{
    [Fact]
    public void BuildTree_EmptyList_ReturnsEmptyRoot()
    {
        var root = NamespaceTreeBuilder.BuildTree(Array.Empty<string>());

        Assert.Empty(root);
    }

    [Fact]
    public void BuildTree_SingleMetric_CreatesPathToLeaf()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[] { "disk.dev.read" });

        Assert.Single(root);
        Assert.Equal("disk", root[0].Name);
        Assert.False(root[0].IsLeaf);
        Assert.Single(root[0].Children);
        Assert.Equal("dev", root[0].Children[0].Name);
        Assert.Single(root[0].Children[0].Children);
        Assert.Equal("read", root[0].Children[0].Children[0].Name);
        Assert.True(root[0].Children[0].Children[0].IsLeaf);
        Assert.Equal("disk.dev.read", root[0].Children[0].Children[0].FullPath);
    }

    [Fact]
    public void BuildTree_SharedPrefix_MergesIntermediateNodes()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "disk.dev.read",
            "disk.dev.write",
            "disk.all.total"
        });

        Assert.Single(root); // single "disk" root
        var disk = root[0];
        Assert.Equal(2, disk.Children.Count); // "dev" and "all"

        var dev = disk.Children.First(c => c.Name == "dev");
        Assert.Equal(2, dev.Children.Count); // "read" and "write"
        Assert.All(dev.Children, c => Assert.True(c.IsLeaf));
    }

    [Fact]
    public void BuildTree_MultipleTopLevel_CreatesSiblings()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "disk.dev.read",
            "kernel.all.load"
        });

        Assert.Equal(2, root.Count);
        Assert.Contains(root, n => n.Name == "disk");
        Assert.Contains(root, n => n.Name == "kernel");
    }

    [Fact]
    public void BuildTree_TopLevelLeaf_HandledCorrectly()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[] { "uptime" });

        Assert.Single(root);
        Assert.Equal("uptime", root[0].Name);
        Assert.True(root[0].IsLeaf);
        Assert.Equal("uptime", root[0].FullPath);
    }

    [Fact]
    public void BuildTree_SortedAlphabetically()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "zebra.metric",
            "alpha.metric",
            "middle.metric"
        });

        Assert.Equal("alpha", root[0].Name);
        Assert.Equal("middle", root[1].Name);
        Assert.Equal("zebra", root[2].Name);
    }

    [Fact]
    public void BuildTree_DeepNesting_PreservesFullPath()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "a.b.c.d.e.f"
        });

        var node = root[0];
        for (int i = 0; i < 5; i++)
            node = node.Children[0];

        Assert.True(node.IsLeaf);
        Assert.Equal("a.b.c.d.e.f", node.FullPath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "NamespaceTreeBuilderTests"`
Expected: FAIL — class does not exist

- [ ] **Step 3: Implement `NamespaceTreeBuilder`**

Create `NamespaceTreeBuilder.cs`:

```csharp
namespace PcpClient;

/// <summary>
/// Node in a metric namespace tree. Intermediate nodes have children;
/// leaf nodes represent actual metrics.
/// </summary>
public record NamespaceNode(
    string Name,
    string FullPath,
    IReadOnlyList<NamespaceNode> Children,
    bool IsLeaf);

/// <summary>
/// Converts a flat list of dotted metric names into a hierarchical tree.
/// Used by archive-mode metric browsing to build a navigable namespace
/// from /series/ query results.
/// </summary>
public static class NamespaceTreeBuilder
{
    public static IReadOnlyList<NamespaceNode> BuildTree(IReadOnlyList<string> metricNames)
    {
        if (metricNames.Count == 0)
            return Array.Empty<NamespaceNode>();

        var root = new Dictionary<string, object>();

        foreach (var name in metricNames)
        {
            var parts = name.Split('.');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == parts.Length - 1)
                {
                    // Leaf — only store if not already an intermediate node
                    if (!current.ContainsKey(part))
                        current[part] = null!;
                    // If it's already a dict (intermediate), leave it — the metric
                    // name exists at both leaf and intermediate positions (rare in PCP
                    // but possible). The intermediate node wins; the leaf is still
                    // reachable via its full path in the tree.
                }
                else
                {
                    if (!current.TryGetValue(part, out var existing))
                    {
                        var next = new Dictionary<string, object>();
                        current[part] = next;
                        current = next;
                    }
                    else if (existing is Dictionary<string, object> existingDict)
                    {
                        current = existingDict;
                    }
                    else
                    {
                        // Was a leaf, now also needed as intermediate — promote to dict
                        var next = new Dictionary<string, object>();
                        current[part] = next;
                        current = next;
                    }
                }
            }
        }

        return BuildNodes(root, "");
    }

    private static IReadOnlyList<NamespaceNode> BuildNodes(
        Dictionary<string, object> dict, string prefix)
    {
        var nodes = new List<NamespaceNode>();

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            var fullPath = string.IsNullOrEmpty(prefix)
                ? kvp.Key
                : $"{prefix}.{kvp.Key}";

            if (kvp.Value is Dictionary<string, object> children)
            {
                nodes.Add(new NamespaceNode(
                    kvp.Key, fullPath,
                    BuildNodes(children, fullPath),
                    IsLeaf: false));
            }
            else
            {
                nodes.Add(new NamespaceNode(
                    kvp.Key, fullPath,
                    Array.Empty<NamespaceNode>(),
                    IsLeaf: true));
            }
        }

        return nodes;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "NamespaceTreeBuilderTests"`
Expected: 7 PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/NamespaceTreeBuilder.cs src/pcp-client-dotnet/tests/PcpClient.Tests/NamespaceTreeBuilderTests.cs
git commit -m "add NamespaceTreeBuilder to convert flat metric names to browseable tree"
```

## Chunk 4: ArchiveMetricDiscoverer

### Task 6: `ArchiveMetricDiscoverer` — orchestration layer

**Files:**
- Create: `src/pcp-client-dotnet/src/PcpClient/ArchiveMetricDiscoverer.cs`
- Create: `src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveMetricDiscovererTests.cs`

- [ ] **Step 1: Write failing tests for `GetHostnamesAsync` and `DiscoverMetricsForHostAsync`**

Create `ArchiveMetricDiscovererTests.cs`:

```csharp
using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for ArchiveMetricDiscoverer: orchestrates series queries
/// into high-level host and metric discovery operations.
/// </summary>
public class ArchiveMetricDiscovererTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    private static MockHttpHandler CreateHandler(
        Dictionary<string, string> urlToResponse)
    {
        return new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            foreach (var kvp in urlToResponse)
            {
                if (url.Contains(kvp.Key))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(kvp.Value,
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    // ── GetHostnamesAsync ──

    [Fact]
    public async Task GetHostnamesAsync_DelegatesToSeriesClient()
    {
        var handler = CreateHandler(new()
        {
            ["/series/labels"] = """{"hostname": ["host1", "host2"]}"""
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.GetHostnamesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("host1", result);
    }

    // ── DiscoverMetricsForHostAsync ──

    [Fact]
    public async Task DiscoverMetricsForHostAsync_ReturnsSortedDeduplicatedNames()
    {
        var handler = CreateHandler(new()
        {
            ["/series/query"] = """["series1", "series2", "series3"]""",
            ["/series/metrics"] = """
                [
                    {"series": "series1", "name": "disk.dev.write"},
                    {"series": "series2", "name": "disk.dev.read"},
                    {"series": "series3", "name": "disk.dev.read"}
                ]
                """
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.DiscoverMetricsForHostAsync("host1");

        Assert.Equal(2, result.Count); // deduplicated
        Assert.Equal("disk.dev.read", result[0]); // sorted
        Assert.Equal("disk.dev.write", result[1]);
    }

    [Fact]
    public async Task DiscoverMetricsForHostAsync_NoSeries_ReturnsEmpty()
    {
        var handler = CreateHandler(new()
        {
            ["/series/query"] = "[]"
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.DiscoverMetricsForHostAsync("host1");

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ArchiveMetricDiscovererTests"`
Expected: FAIL — class does not exist

- [ ] **Step 3: Implement `ArchiveMetricDiscoverer` (hostname + metric discovery only, no DescribeMetricAsync yet)**

Create `ArchiveMetricDiscoverer.cs`:

```csharp
namespace PcpClient;

/// <summary>
/// Metric detail resolved from archive series data.
/// </summary>
public record MetricDetail(
    string Name,
    string? Semantics,
    string? Type,
    string? Units,
    IReadOnlyList<SeriesInstanceInfo> Instances);

/// <summary>
/// Orchestrates multi-step metric discovery from pmproxy /series/ endpoints.
/// Caches series-ID-to-metric-name mappings from the discovery step
/// so DescribeMetricAsync can reuse them.
/// </summary>
public sealed class ArchiveMetricDiscoverer
{
    private readonly PcpSeriesClient _client;

    // Cache: hostname -> (metricName -> list of series IDs)
    private readonly Dictionary<string, Dictionary<string, List<string>>> _seriesCache = new();

    public ArchiveMetricDiscoverer(Uri baseUrl, HttpClient httpClient)
    {
        _client = new PcpSeriesClient(baseUrl, httpClient);
    }

    public async Task<IReadOnlyList<string>> GetHostnamesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _client.GetHostnamesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> DiscoverMetricsForHostAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var expression = $"""*{{hostname=="{hostname}"}}""";
        var seriesIds = await _client.QuerySeriesAsync(expression, cancellationToken);

        if (seriesIds.Count == 0)
            return Array.Empty<string>();

        var metricNames = await _client.GetMetricNamesAsync(seriesIds, cancellationToken);

        // Build cache: metric name -> series IDs
        var metricToSeries = new Dictionary<string, List<string>>();
        foreach (var entry in metricNames)
        {
            if (!metricToSeries.TryGetValue(entry.Name, out var ids))
            {
                ids = new List<string>();
                metricToSeries[entry.Name] = ids;
            }
            ids.Add(entry.SeriesId);
        }
        _seriesCache[hostname] = metricToSeries;

        return metricToSeries.Keys.Order().ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ArchiveMetricDiscovererTests"`
Expected: 3 PASS

- [ ] **Step 5: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/ArchiveMetricDiscoverer.cs src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveMetricDiscovererTests.cs
git commit -m "add ArchiveMetricDiscoverer with host and metric discovery"
```

- [ ] **Step 6: Write failing tests for `DescribeMetricAsync`**

Add to `ArchiveMetricDiscovererTests.cs`:

```csharp
// ── DescribeMetricAsync ──

[Fact]
public async Task DescribeMetricAsync_UsesCache_NoExtraQueryCall()
{
    int queryCallCount = 0;
    var handler = new MockHttpHandler(req =>
    {
        var url = req.RequestUri!.PathAndQuery;
        if (url.Contains("/series/query"))
        {
            queryCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """["series1"]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
        if (url.Contains("/series/metrics"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "series1", "name": "disk.dev.read"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        if (url.Contains("/series/descs"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "series1", "semantics": "counter", "type": "u64", "units": "count"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        if (url.Contains("/series/instances"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    var discoverer = new ArchiveMetricDiscoverer(
        BaseUrl, new HttpClient(handler));

    // First call populates cache
    await discoverer.DiscoverMetricsForHostAsync("host1");
    Assert.Equal(1, queryCallCount);

    // Describe uses cached series IDs — no extra /series/query call
    var detail = await discoverer.DescribeMetricAsync("disk.dev.read", "host1");
    Assert.Equal(1, queryCallCount); // still 1 — cache hit

    Assert.Equal("disk.dev.read", detail.Name);
    Assert.Equal("counter", detail.Semantics);
    Assert.Equal("u64", detail.Type);
    Assert.Equal("count", detail.Units);
}

[Fact]
public async Task DescribeMetricAsync_CacheMiss_QueriesDirectly()
{
    var handler = new MockHttpHandler(req =>
    {
        var url = req.RequestUri!.PathAndQuery;
        if (url.Contains("/series/query"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """["series1"]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        if (url.Contains("/series/descs"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "series1", "semantics": "instant", "type": "float"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        if (url.Contains("/series/instances"))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    var discoverer = new ArchiveMetricDiscoverer(
        BaseUrl, new HttpClient(handler));

    // No prior DiscoverMetricsForHostAsync — cache is empty
    var detail = await discoverer.DescribeMetricAsync(
        "kernel.all.load", "host1");

    Assert.Equal("instant", detail.Semantics);
    Assert.Equal("float", detail.Type);
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "DescribeMetricAsync"`
Expected: FAIL — method does not exist

- [ ] **Step 8: Implement `DescribeMetricAsync` and `ResolveSeriesIds`**

Add to `ArchiveMetricDiscoverer.cs`:

```csharp
public async Task<MetricDetail> DescribeMetricAsync(
    string metricName, string hostname,
    CancellationToken cancellationToken = default)
{
    var seriesIds = await ResolveSeriesIds(metricName, hostname, cancellationToken);

    if (seriesIds.Count == 0)
        return new MetricDetail(metricName, null, null, null,
            Array.Empty<SeriesInstanceInfo>());

    var descs = await _client.GetDescriptorsAsync(seriesIds, cancellationToken);
    var instances = await _client.GetInstancesAsync(seriesIds, cancellationToken);

    var desc = descs.Count > 0 ? descs[0] : null;

    return new MetricDetail(
        metricName,
        desc?.Semantics,
        desc?.Type,
        desc?.Units,
        instances.Values.ToList());
}

private async Task<IReadOnlyList<string>> ResolveSeriesIds(
    string metricName, string hostname,
    CancellationToken cancellationToken)
{
    // Try cache first
    if (_seriesCache.TryGetValue(hostname, out var metricToSeries)
        && metricToSeries.TryGetValue(metricName, out var cached))
    {
        return cached;
    }

    // Cache miss — targeted query
    var expression = $"""{metricName}{{hostname=="{hostname}"}}""";
    var seriesIds = await _client.QuerySeriesAsync(expression, cancellationToken);
    return seriesIds;
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln --filter "ArchiveMetricDiscovererTests"`
Expected: 5 PASS

- [ ] **Step 10: Run full test suite**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln`
Expected: All tests pass

- [ ] **Step 11: Commit**

```bash
git add src/pcp-client-dotnet/src/PcpClient/ArchiveMetricDiscoverer.cs src/pcp-client-dotnet/tests/PcpClient.Tests/ArchiveMetricDiscovererTests.cs
git commit -m "add DescribeMetricAsync with series ID caching"
```

## Chunk 5: Prototype Cleanup

### Task 7: Remove runtime MetricBrowser and PlaybackControls

**Files:**
- Delete: `godot-project/addons/pmview-bridge/MetricBrowser.cs`
- Delete: `godot-project/scripts/scenes/metric_browser.gd`
- Delete: `godot-project/scenes/metric_browser.tscn`
- Delete: `godot-project/scripts/scenes/playback_controls.gd`
- Delete: `godot-project/scenes/playback_controls.tscn`
- Modify: `godot-project/scripts/scenes/metric_scene_controller.gd`
- Modify: `godot-project/scenes/main.tscn`

- [ ] **Step 1: Delete prototype files**

```bash
cd "/Volumes/My Shared Files/pmview-nextgen"
rm godot-project/addons/pmview-bridge/MetricBrowser.cs
rm godot-project/scripts/scenes/metric_browser.gd
rm godot-project/scenes/metric_browser.tscn
rm godot-project/scripts/scenes/playback_controls.gd
rm godot-project/scenes/playback_controls.tscn
```

- [ ] **Step 2: Update `main.tscn` — remove MetricBrowser and PlaybackControls nodes**

The scene file should become:

```
[gd_scene load_steps=4 format=3 uid="uid://main_scene"]

[ext_resource type="Script" path="res://scripts/scenes/metric_scene_controller.gd" id="1_controller"]
[ext_resource type="Script" path="res://addons/pmview-bridge/MetricPoller.cs" id="2_poller"]
[ext_resource type="Script" path="res://addons/pmview-bridge/SceneBinder.cs" id="3_binder"]

[node name="Main" type="Node"]
script = ExtResource("1_controller")

[node name="MetricPoller" type="Node" parent="."]
script = ExtResource("2_poller")

[node name="SceneBinder" type="Node" parent="."]
script = ExtResource("3_binder")

[node name="UIOverlay" type="CanvasLayer" parent="."]

[node name="StatusLabel" type="Label" parent="UIOverlay"]
offset_left = 10.0
offset_top = 10.0
offset_right = 400.0
offset_bottom = 40.0
text = "Connection: Disconnected"
```

Removed: `MetricBrowser.cs` ext_resource, `metric_browser.tscn` ext_resource, `playback_controls.tscn` ext_resource, `MetricBrowser` node, `MetricBrowser` UI instance, `PlaybackControls` UI instance. `load_steps` changed from 7 to 4.

- [ ] **Step 3: Update `metric_scene_controller.gd`**

Strip all MetricBrowser and PlaybackControls references. The cleaned-up controller should be:

```gdscript
extends Node

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Loads scenes with editor-integrated bindings.

@export var default_scene: String = "res://scenes/test_bars.tscn"

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var status_label: Label = $UIOverlay/StatusLabel

var _connection_state: String = "Disconnected"

# World configuration from ProjectSettings (set by pmview-bridge plugin)
var _launch_endpoint: String = "http://localhost:44322"
var _launch_mode: int = 0  # 0=Archive, 1=Live
var _launch_timestamp: String = ""
var _launch_speed: float = 10.0
var _launch_loop: bool = false

func _ready() -> void:
	_read_launch_settings()
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	scene_binder.connect("BindingError", _on_binding_error)

	_update_status_display()

	# Load scene with editor-integrated bindings
	if default_scene != "":
		print("[MetricSceneController] Loading scene: %s" % default_scene)
		var metric_names = _load_scene_with_properties(default_scene)
		if metric_names.size() > 0:
			print("[MetricSceneController] Found %d metrics from scene properties" % metric_names.size())
			_start_polling_metrics(metric_names)
		else:
			print("[MetricSceneController] No bindings found in scene: %s" % default_scene)

	_apply_launch_settings()

func _load_scene_with_properties(scene_path: String) -> PackedStringArray:
	var packed = load(scene_path) as PackedScene
	if packed == null:
		print("[MetricSceneController] Cannot load scene: %s" % scene_path)
		return PackedStringArray()
	var scene_instance = packed.instantiate()
	scene_binder.add_child(scene_instance)
	var metric_names = scene_binder.call("BindFromSceneProperties", scene_instance)
	return metric_names

func _start_polling_metrics(metric_names: PackedStringArray) -> void:
	if metric_names.size() > 0:
		metric_poller.call("UpdateMetricNames", metric_names)
		metric_poller.call("StartPolling")

func _on_metrics_updated(metrics: Dictionary) -> void:
	scene_binder.call("ApplyMetrics", metrics)

func _on_connection_state_changed(state: String) -> void:
	_connection_state = state
	print("[MetricSceneController] Connection state: %s" % state)
	_update_status_display()

func _on_error_occurred(message: String) -> void:
	print("[MetricSceneController] Error: %s" % message)
	_update_status_display()

func _on_binding_error(message: String) -> void:
	print("[MetricSceneController] Binding error: %s" % message)

func _read_launch_settings() -> void:
	_launch_endpoint = ProjectSettings.get_setting("pmview/endpoint", "http://localhost:44322")
	_launch_mode = ProjectSettings.get_setting("pmview/mode", 0)
	_launch_timestamp = ProjectSettings.get_setting("pmview/archive_start_timestamp", "")
	_launch_speed = ProjectSettings.get_setting("pmview/archive_speed", 10.0)
	_launch_loop = ProjectSettings.get_setting("pmview/archive_loop", false)
	print("[MetricSceneController] Launch settings: mode=%d endpoint=%s speed=%.1f loop=%s" % [
		_launch_mode, _launch_endpoint, _launch_speed, _launch_loop])

func _apply_launch_settings() -> void:
	# Override endpoint if it differs from the default
	if _launch_endpoint != "http://localhost:44322":
		print("[MetricSceneController] Overriding endpoint from ProjectSettings: %s" % _launch_endpoint)
		metric_poller.set("Endpoint", _launch_endpoint)

	if _launch_mode == 0:  # Archive
		var timestamp = _launch_timestamp
		if timestamp == "":
			# Empty timestamp -> 24 hours before now
			var now = Time.get_unix_time_from_system()
			var day_ago = now - 86400.0
			timestamp = Time.get_datetime_string_from_unix_time(int(day_ago)) + "Z"
			print("[MetricSceneController] Empty timestamp, using 24h ago: %s" % timestamp)

		# Set speed/loop before StartPlayback — StartPlayback is async and discovers
		# EndBound from the server. Loop + EndBound are checked together at AdvanceBy() time.
		metric_poller.call("SetPlaybackSpeed", _launch_speed)
		metric_poller.call("SetLoop", _launch_loop)
		metric_poller.call("StartPlayback", timestamp)
		print("[MetricSceneController] Archive mode: timestamp=%s speed=%.1f loop=%s" % [
			timestamp, _launch_speed, _launch_loop])
	elif _launch_mode == 1:  # Live
		metric_poller.call("ResetToLive")
		print("[MetricSceneController] Live mode: archive settings ignored")

func _update_status_display() -> void:
	if status_label:
		status_label.text = "Connection: %s" % _connection_state
		match _connection_state:
			"Connected":
				status_label.add_theme_color_override("font_color", Color.GREEN)
			"Reconnecting", "Connecting":
				status_label.add_theme_color_override("font_color", Color.YELLOW)
			_:
				status_label.add_theme_color_override("font_color", Color.RED)
```

- [ ] **Step 4: Build Godot project to verify no C# compilation errors**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet build godot-project/pmview-nextgen.sln`
Expected: Build succeeded

- [ ] **Step 5: Run PcpClient tests to verify no regressions**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add -A  # safe here — only deletions and known modifications
git commit -m "remove runtime MetricBrowser and PlaybackControls prototypes

Superseded by editor-integrated bindings and ProjectSettings-driven
archive mode. Simplifies metric_scene_controller to core wiring."
```

## Chunk 6: MetricBrowserDialog Archive Mode

### Task 8: Add archive mode to `MetricBrowserDialog`

**Files:**
- Modify: `godot-project/addons/pmview-bridge/MetricBrowserDialog.cs`

Note: This is `#if TOOLS` editor code with Godot dependencies — not unit-testable. Verified manually.

- [ ] **Step 1: Add host dropdown and mode-aware fields**

Add new fields to `MetricBrowserDialog`:

```csharp
private OptionButton? _hostDropdown;
private ArchiveMetricDiscoverer? _discoverer;
private bool _isArchiveMode;
```

- [ ] **Step 2: Update `_Ready()` — add host dropdown (hidden by default)**

Insert after `_statusLabel` creation, before the `_retryButton`:

```csharp
_hostDropdown = new OptionButton();
_hostDropdown.Visible = false;
_hostDropdown.ItemSelected += OnHostSelected;
vbox.AddChild(_hostDropdown);
```

- [ ] **Step 3: Update `OpenForBinding()` — detect mode and branch**

Replace the `OpenForBinding` method to check `pmview/mode` and branch:

```csharp
public async void OpenForBinding(PcpBindingResource binding)
{
    _targetBinding = binding;
    _selectedMetric = "";
    _confirmButton!.Disabled = true;
    _instanceList!.Clear();
    _tree!.Clear();
    _descriptionLabel!.Text = "Select a metric";

    var endpoint = ProjectSettings.GetSetting(
        "pmview/endpoint", "http://localhost:44322").AsString();

    if (string.IsNullOrEmpty(endpoint))
    {
        ShowError("Configure pmproxy endpoint in Project Settings > PCP");
        PopupCentered();
        return;
    }

    _isArchiveMode = ProjectSettings.GetSetting("pmview/mode", 0).AsInt32() == 0;

    PopupCentered();

    if (_isArchiveMode)
        await ConnectAndLoadHosts(endpoint);
    else
        await ConnectAndLoadRoot(endpoint);
}
```

- [ ] **Step 4: Add archive-mode methods**

```csharp
private async Task ConnectAndLoadHosts(string endpoint)
{
    try
    {
        _statusLabel!.Text = "Discovering hosts...";
        _retryButton!.Visible = false;
        _hostDropdown!.Visible = true;
        _hostDropdown.Clear();

        _httpClient?.Dispose();
        _client?.Dispose();
        _httpClient = new System.Net.Http.HttpClient();
        _discoverer = new ArchiveMetricDiscoverer(new Uri(endpoint), _httpClient);

        var hosts = await _discoverer.GetHostnamesAsync();

        if (hosts.Count == 0)
        {
            ShowError("No archived hosts found");
            return;
        }

        foreach (var host in hosts)
            _hostDropdown.AddItem(host);

        _statusLabel.Text = $"Archive: select a host ({hosts.Count} available)";
    }
    catch (Exception ex)
    {
        ShowError($"Host discovery failed: {ex.Message}");
    }
}

private async void OnHostSelected(long index)
{
    var hostname = _hostDropdown!.GetItemText((int)index);
    _statusLabel!.Text = $"Archive: {hostname} — loading metrics...";
    _tree!.Clear();

    try
    {
        var metricNames = await _discoverer!.DiscoverMetricsForHostAsync(hostname);

        if (metricNames.Count == 0)
        {
            _statusLabel.Text = $"Archive: {hostname} — no metrics found";
            return;
        }

        var tree = NamespaceTreeBuilder.BuildTree(metricNames);
        var root = _tree.CreateItem();
        root.SetText(0, hostname);

        PopulateTreeFromNamespace(root, tree);

        _statusLabel.Text = $"Archive: {hostname} — {metricNames.Count} metrics";
    }
    catch (Exception ex)
    {
        ShowError($"Metric discovery failed: {ex.Message}");
    }
}

private void PopulateTreeFromNamespace(TreeItem parent, IReadOnlyList<NamespaceNode> nodes)
{
    foreach (var node in nodes)
    {
        var item = _tree!.CreateItem(parent);
        item.SetText(0, node.Name);
        item.SetMetadata(0, node.FullPath);

        if (node.IsLeaf)
        {
            item.SetCustomColor(0, new Color(0.4f, 1.0f, 0.4f));
        }
        else
        {
            PopulateTreeFromNamespace(item, node.Children);
        }
    }
}
```

- [ ] **Step 5: Update `OnTreeItemSelected` for archive mode**

Replace the body of `OnTreeItemSelected` with mode-aware logic:

```csharp
private async void OnTreeItemSelected()
{
    var selected = _tree?.GetSelected();
    if (selected == null) return;

    var path = selected.GetMetadata(0).AsString();
    if (string.IsNullOrEmpty(path)) return;

    // Only describe leaf nodes (no children in either mode)
    if (selected.GetFirstChild() != null) return;

    _selectedMetric = path;
    _confirmButton!.Disabled = false;

    if (_isArchiveMode)
    {
        await DescribeMetricFromArchive(path);
    }
    else
    {
        await DescribeMetricFromLive(path);
    }
}

private async Task DescribeMetricFromArchive(string metricName)
{
    if (_discoverer == null) return;

    var hostname = _hostDropdown!.GetItemText(_hostDropdown.Selected);

    try
    {
        var detail = await _discoverer.DescribeMetricAsync(metricName, hostname);

        _descriptionLabel!.Text = $"{detail.Name}";
        if (detail.Semantics != null)
            _descriptionLabel.Text += $"\n\nSemantics: {detail.Semantics}";
        if (detail.Type != null)
            _descriptionLabel.Text += $"\nType: {detail.Type}";
        if (detail.Units != null)
            _descriptionLabel.Text += $"\nUnits: {detail.Units}";

        _instanceList!.Clear();
        if (detail.Instances.Count > 0)
        {
            foreach (var inst in detail.Instances)
                _instanceList.AddItem($"{inst.Name} (id: {inst.PcpInstanceId})");
        }
    }
    catch (Exception ex)
    {
        _descriptionLabel!.Text = $"Error: {ex.Message}";
    }
}

private async Task DescribeMetricFromLive(string metricName)
{
    if (_client == null) return;

    try
    {
        var descriptors = await _client.DescribeMetricsAsync(new[] { metricName });
        if (descriptors.Count > 0)
        {
            var desc = descriptors[0];
            _descriptionLabel!.Text = $"{desc.Name}\n\n{desc.OneLineHelp}";
            if (!string.IsNullOrEmpty(desc.LongHelp))
                _descriptionLabel.Text += $"\n\n{desc.LongHelp}";

            _instanceList!.Clear();
            try
            {
                var indom = await _client.GetInstanceDomainAsync(metricName);
                if (indom?.Instances != null)
                {
                    foreach (var inst in indom.Instances)
                        _instanceList.AddItem($"{inst.Name} (id: {inst.Id})");
                }
            }
            catch (PcpException)
            {
                // No instance domain — singular metric
            }
        }
    }
    catch (PcpMetricNotFoundException)
    {
        _descriptionLabel!.Text = $"Metric not found: {metricName}";
    }
    catch (PcpException ex)
    {
        _descriptionLabel!.Text = $"Error: {ex.Message}";
    }
}
```

- [ ] **Step 6: Update `OnTreeItemActivated` — skip lazy loading in archive mode**

```csharp
private async void OnTreeItemActivated()
{
    if (_isArchiveMode) return; // archive tree is fully populated

    var selected = _tree?.GetSelected();
    if (selected == null) return;

    var path = selected.GetMetadata(0).AsString();

    var firstChild = selected.GetFirstChild();
    if (firstChild != null && firstChild.GetText(0) == "Loading...")
    {
        firstChild.Free();
        await LoadChildren(path);
    }
}
```

- [ ] **Step 7: Update `CleanupAndClose` — dispose archive resources**

```csharp
private void CleanupAndClose()
{
    _client?.Dispose();
    _client = null;
    _discoverer = null; // doesn't own HttpClient
    _httpClient?.Dispose();
    _httpClient = null;
    if (_hostDropdown != null)
        _hostDropdown.Visible = false;
    Hide();
}
```

Update `_ExitTree` similarly:

```csharp
public override void _ExitTree()
{
    _client?.Dispose();
    _client = null;
    _discoverer = null;
    _httpClient?.Dispose();
    _httpClient = null;
    if (_hostDropdown != null)
        _hostDropdown.Visible = false;
}
```

- [ ] **Step 8: Build Godot project**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet build godot-project/pmview-nextgen.sln`
Expected: Build succeeded

- [ ] **Step 9: Run full PcpClient test suite**

Run: `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH" && export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" && dotnet test src/pcp-client-dotnet/PcpClient.sln`
Expected: All tests pass

- [ ] **Step 10: Commit**

```bash
git add godot-project/addons/pmview-bridge/MetricBrowserDialog.cs
git commit -m "add archive-mode metric browsing to MetricBrowserDialog

Host dropdown for selecting archived host, /series/ discovery for
metric namespace, NamespaceTreeBuilder for full tree population.
Live mode unchanged."
```
