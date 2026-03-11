using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Manages the pmproxy server-side context lifecycle.
/// Handles creation, tracking, timeout, and disposal of contexts.
/// </summary>
internal sealed class PcpContext
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUrl;

    public int? ContextId { get; private set; }
    public int PollTimeoutSeconds { get; private set; }
    public bool IsActive => ContextId.HasValue;

    public PcpContext(HttpClient httpClient, Uri baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl;
    }

    public async Task<int> CreateAsync(int pollTimeoutSeconds, CancellationToken cancellationToken)
    {
        PollTimeoutSeconds = pollTimeoutSeconds;
        var url = new Uri(_baseUrl, $"/pmapi/context?polltimeout={pollTimeoutSeconds}");

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var contextId = doc.RootElement.GetProperty("context").GetInt32();

        ContextId = contextId;
        return contextId;
    }

    /// <summary>
    /// Marks the local context as invalid. Does not notify the server —
    /// the server-side context will expire after its polltimeout elapses.
    /// pmproxy does not currently expose a context destruction endpoint.
    /// </summary>
    public void Invalidate()
    {
        ContextId = null;
    }

    public Uri BuildFetchUrl(IEnumerable<string> metricNames)
    {
        if (!IsActive)
            throw new InvalidOperationException("No active context. Call CreateAsync first.");

        var names = string.Join(",", metricNames);
        return new Uri(_baseUrl, $"/pmapi/fetch?names={Uri.EscapeDataString(names)}&context={ContextId}");
    }
}
