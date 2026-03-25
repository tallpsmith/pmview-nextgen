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
        var request = PcpSeriesQuery.BuildSeriesRequest(_baseUrl, "/series/metrics", seriesIds);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseMetricsResponse(json);
    }

    public async Task<IReadOnlyList<SeriesDescriptor>> GetDescriptorsAsync(
        IEnumerable<string> seriesIds, CancellationToken cancellationToken = default)
    {
        var request = PcpSeriesQuery.BuildSeriesRequest(_baseUrl, "/series/descs", seriesIds);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseDescsResponse(json);
    }

    public async Task<Dictionary<string, SeriesInstanceInfo>> GetInstancesAsync(
        IEnumerable<string> seriesIds, CancellationToken cancellationToken = default)
    {
        var request = PcpSeriesQuery.BuildSeriesRequest(_baseUrl, "/series/instances", seriesIds);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseInstancesResponse(json);
    }

    public async Task<IReadOnlyList<SeriesValue>> GetValuesAsync(
        IEnumerable<string> seriesIds, DateTime position, double windowSeconds = 2.0,
        CancellationToken cancellationToken = default)
    {
        var startEpoch = PcpSeriesQuery.ToEpochSeconds(position.AddSeconds(-windowSeconds));
        var finishEpoch = PcpSeriesQuery.ToEpochSeconds(position);
        var extraParams = new Dictionary<string, string>
        {
            ["start"] = $"{startEpoch:F3}",
            ["finish"] = $"{finishEpoch:F3}"
        };
        var request = PcpSeriesQuery.BuildSeriesRequest(
            _baseUrl, "/series/values", seriesIds, extraParams);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseValuesResponse(json);
    }
}
