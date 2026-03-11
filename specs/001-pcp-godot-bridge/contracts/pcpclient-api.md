# Contract: PcpClient Public API

**Date:** 2026-03-05
**Library:** `src/pcp-client-dotnet/src/PcpClient/`
**Target:** net8.0+ | No Godot dependencies

## Design Principles

- PcpClient knows about PCP concepts, not about Godot, scenes, or visualisation
- All public methods are async (return `Task<T>`)
- All HTTP errors surface as typed exceptions, not raw HttpClient exceptions
- The client is stateful (holds a pmproxy context) but thread-safe for concurrent reads
- Immutable return types — callers cannot corrupt internal state

## Public Interface

```csharp
namespace PcpClient;

/// <summary>
/// Primary entry point for interacting with a pmproxy endpoint.
/// Manages context lifecycle and provides metric operations.
/// </summary>
public interface IPcpClient : IAsyncDisposable
{
    /// <summary>Current connection state.</summary>
    ConnectionState State { get; }

    /// <summary>The pmproxy base URL this client connects to.</summary>
    Uri BaseUrl { get; }

    // ── Connection Lifecycle ──

    /// <summary>
    /// Establishes a context with pmproxy. Must be called before any metric operations.
    /// </summary>
    /// <param name="pollTimeoutSeconds">
    /// Server-side context inactivity timeout. Default 60.
    /// Must exceed the expected polling interval.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The server-assigned context ID.</returns>
    /// <exception cref="PcpConnectionException">If the endpoint is unreachable.</exception>
    Task<int> ConnectAsync(int pollTimeoutSeconds = 60,
                           CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current context. Safe to call multiple times.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    // ── Metric Discovery ──

    /// <summary>
    /// Traverses the PCP metric namespace tree from a given prefix.
    /// Returns leaf (metric) and non-leaf (subtree) names.
    /// </summary>
    /// <param name="prefix">
    /// Namespace prefix to start from. Empty string for root.
    /// </param>
    /// <returns>Children with their leaf/non-leaf classification.</returns>
    Task<MetricNamespace> GetChildrenAsync(string prefix = "",
                                           CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata for one or more metrics by name.
    /// </summary>
    /// <param name="metricNames">One or more dotted metric names.</param>
    /// <returns>Descriptors with type, semantics, units, and help text.</returns>
    /// <exception cref="PcpMetricNotFoundException">
    /// If any requested metric does not exist on the endpoint.
    /// </exception>
    Task<IReadOnlyList<MetricDescriptor>> DescribeMetricsAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default);

    // ── Instance Domains ──

    /// <summary>
    /// Enumerates all instances in the instance domain for a given metric.
    /// Returns empty for singular (non-instanced) metrics.
    /// </summary>
    /// <param name="metricName">Dotted metric name.</param>
    /// <returns>Instance domain with instance IDs and names.</returns>
    Task<InstanceDomain> GetInstanceDomainAsync(string metricName,
                                                 CancellationToken cancellationToken = default);

    // ── Metric Fetching ──

    /// <summary>
    /// Fetches current values for one or more metrics.
    /// Refreshes the server-side context timeout.
    /// </summary>
    /// <param name="metricNames">One or more dotted metric names.</param>
    /// <returns>
    /// Values with timestamp, instance breakdown, and raw typed values.
    /// </returns>
    Task<IReadOnlyList<MetricValue>> FetchAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default);
}
```

## Exception Hierarchy

```csharp
/// <summary>Base exception for all PcpClient errors.</summary>
public class PcpException : Exception { }

/// <summary>Endpoint unreachable or context creation failed.</summary>
public class PcpConnectionException : PcpException { }

/// <summary>Server-side context expired. Client should reconnect.</summary>
public class PcpContextExpiredException : PcpException { }

/// <summary>Requested metric name does not exist on the endpoint.</summary>
public class PcpMetricNotFoundException : PcpException
{
    public string MetricName { get; }
}
```

## Construction

```csharp
/// <summary>
/// Creates a PcpClient targeting a pmproxy endpoint.
/// Does not connect — call ConnectAsync() to establish a context.
/// </summary>
/// <param name="baseUrl">pmproxy base URL, e.g., http://localhost:44322</param>
/// <param name="httpClient">
/// Optional HttpClient for testability. If null, a default singleton is used.
/// </param>
public class PcpClient : IPcpClient
{
    public PcpClient(Uri baseUrl, HttpClient? httpClient = null);
}
```

## Usage Pattern

```csharp
await using var client = new PcpClient(new Uri("http://localhost:44322"));
await client.ConnectAsync(pollTimeoutSeconds: 60);

// Discover what's available
var ns = await client.GetChildrenAsync("kernel");
var descriptors = await client.DescribeMetricsAsync(new[] { "kernel.all.load" });
var indom = await client.GetInstanceDomainAsync("kernel.all.load");

// Fetch in a polling loop
while (monitoring)
{
    var values = await client.FetchAsync(new[] { "kernel.all.load", "disk.dev.read" });
    // Process values...
    await Task.Delay(1000);
}
```

## Deferred (Not in Initial API)

These will be added when the corresponding user stories are implemented:

- **Historical queries** (`/series/*` endpoints) — needed for FR-013/FR-014 (time cursor playback). Requires Valkey backend. Will be a separate interface or extension methods on `IPcpClient`.
- **Instance filtering** (`/pmapi/profile`) — optimisation for large instance domains. Add when performance requires it.
- **Derived metrics** (`/pmapi/derive`) — out of scope per spec (no metric aggregation/math).
- **Store** (`/pmapi/store`) — write operations not needed for a visualisation tool.

## Testability Contract

- `IPcpClient` interface enables mock injection in bridge layer tests
- Constructor accepts `HttpClient` for HTTP-level mocking (e.g., via `MockHttpMessageHandler`)
- All return types are immutable records or read-only collections — safe to cache in tests
- No static state — multiple `PcpClient` instances can coexist (different endpoints)
