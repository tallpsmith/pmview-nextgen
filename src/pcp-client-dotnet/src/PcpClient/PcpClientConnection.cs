using System.Net;

namespace PcpClient;

/// <summary>
/// Concrete IPcpClient implementation targeting a pmproxy endpoint.
/// Does not connect on construction — call ConnectAsync() first.
/// Handles connection resilience: auto-reconnect on context expiry.
/// </summary>
public sealed class PcpClientConnection : IPcpClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private PcpContext? _context;
    private int _pollTimeoutSeconds;
    private bool _disposed;

    public Uri BaseUrl { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public PcpClientConnection(Uri baseUrl, HttpClient? httpClient = null)
    {
        BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<int> ConnectAsync(int pollTimeoutSeconds = 60,
                                         CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connecting;
        _pollTimeoutSeconds = pollTimeoutSeconds;

        try
        {
            _context = new PcpContext(_httpClient, BaseUrl);
            var contextId = await _context.CreateAsync(pollTimeoutSeconds, cancellationToken);
            State = ConnectionState.Connected;
            return contextId;
        }
        catch (HttpRequestException ex)
        {
            State = ConnectionState.Failed;
            throw new PcpConnectionException(
                $"Failed to connect to pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not PcpConnectionException)
        {
            State = ConnectionState.Failed;
            throw new PcpConnectionException(
                $"Unexpected response from pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _context?.Invalidate();
        _context = null;
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MetricValue>> FetchAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default)
    {
        var namesList = metricNames.ToList();
        if (namesList.Count == 0)
            throw new ArgumentException("At least one metric name is required.", nameof(metricNames));

        if (_context == null || !_context.IsActive)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        return await FetchWithResilience(namesList, cancellationToken);
    }

    private async Task<IReadOnlyList<MetricValue>> FetchWithResilience(
        List<string> namesList, CancellationToken cancellationToken)
    {
        var fetchUrl = _context!.BuildFetchUrl(namesList);
        var response = await _httpClient.GetAsync(fetchUrl, cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (PcpMetricFetcher.IsContextExpiredResponse(errorBody))
            {
                return await ReconnectAndRetryFetch(namesList, cancellationToken);
            }
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpMetricFetcher.ParseFetchResponse(json);
    }

    private async Task<IReadOnlyList<MetricValue>> ReconnectAndRetryFetch(
        List<string> namesList, CancellationToken cancellationToken)
    {
        State = ConnectionState.Reconnecting;

        try
        {
            _context = new PcpContext(_httpClient, BaseUrl);
            await _context.CreateAsync(_pollTimeoutSeconds, cancellationToken);
            State = ConnectionState.Connected;

            var fetchUrl = _context.BuildFetchUrl(namesList);
            var response = await _httpClient.GetAsync(fetchUrl, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var retryErrorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (PcpMetricFetcher.IsContextExpiredResponse(retryErrorBody))
                {
                    State = ConnectionState.Failed;
                    throw new PcpContextExpiredException(
                        $"Context expired again immediately after reconnect to {BaseUrl}");
                }
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return PcpMetricFetcher.ParseFetchResponse(json);
        }
        catch (PcpContextExpiredException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            State = ConnectionState.Failed;
            throw new PcpConnectionException(
                $"Failed to reconnect to pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
    }

    public async Task<MetricNamespace> GetChildrenAsync(string prefix = "",
                                                         CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected)
            throw new PcpConnectionException(
                $"Not connected to pmproxy at {BaseUrl}. Call ConnectAsync first.");

        try
        {
            var url = PcpNamespaceTraverser.BuildChildrenUrl(BaseUrl, prefix);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                // Non-existent prefix returns empty namespace rather than throwing
                return new MetricNamespace(prefix, Array.Empty<string>(), Array.Empty<string>());
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return PcpNamespaceTraverser.ParseChildrenResponse(json, prefix);
        }
        catch (HttpRequestException ex)
        {
            throw new PcpConnectionException(
                $"Failed to get children from pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<MetricDescriptor>> DescribeMetricsAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected)
            throw new PcpConnectionException(
                $"Not connected to pmproxy at {BaseUrl}. Call ConnectAsync first.");

        var namesList = metricNames.ToList();

        try
        {
            var url = PcpMetricDescriber.BuildDescribeUrl(BaseUrl, namesList);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (PcpMetricDescriber.IsUnknownMetricResponse(errorBody))
                {
                    var metricName = PcpMetricDescriber.ExtractUnknownMetricName(errorBody)
                                     ?? namesList.FirstOrDefault() ?? "unknown";
                    throw new PcpMetricNotFoundException(metricName);
                }
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return PcpMetricDescriber.ParseDescribeResponse(json);
        }
        catch (PcpMetricNotFoundException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new PcpConnectionException(
                $"Failed to describe metrics from pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
    }

    public async Task<InstanceDomain> GetInstanceDomainAsync(string metricName,
                                                              CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected)
            throw new PcpConnectionException(
                $"Not connected to pmproxy at {BaseUrl}. Call ConnectAsync first.");

        try
        {
            var url = PcpInstanceDomainFetcher.BuildIndomUrl(BaseUrl, metricName);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (PcpInstanceDomainFetcher.IsNoIndomResponse(errorBody))
                    return new InstanceDomain("", Array.Empty<Instance>());
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return PcpInstanceDomainFetcher.ParseIndomResponse(json);
        }
        catch (HttpRequestException ex)
        {
            throw new PcpConnectionException(
                $"Failed to get instance domain from pmproxy at {BaseUrl}: {ex.Message}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await DisconnectAsync();
            if (_ownsHttpClient)
                _httpClient.Dispose();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _context?.Invalidate();
            _context = null;
            State = ConnectionState.Disconnected;
            if (_ownsHttpClient)
                _httpClient.Dispose();
            _disposed = true;
        }
    }
}
