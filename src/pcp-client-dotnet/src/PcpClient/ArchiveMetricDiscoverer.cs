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
/// so DescribeMetricAsync can reuse them without an extra query.
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
        var expression = $$"""*{hostname=="{{hostname}}"}""";
        var seriesIds = await _client.QuerySeriesAsync(expression, cancellationToken);

        if (seriesIds.Count == 0)
            return Array.Empty<string>();

        var metricNames = await _client.GetMetricNamesAsync(seriesIds, cancellationToken);

        var metricToSeries = BuildMetricToSeriesMap(metricNames);
        _seriesCache[hostname] = metricToSeries;

        return metricToSeries.Keys.Order().ToList();
    }

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

    private static Dictionary<string, List<string>> BuildMetricToSeriesMap(
        IReadOnlyList<SeriesMetricName> metricNames)
    {
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
        return metricToSeries;
    }

    private async Task<IReadOnlyList<string>> ResolveSeriesIds(
        string metricName, string hostname,
        CancellationToken cancellationToken)
    {
        if (_seriesCache.TryGetValue(hostname, out var metricToSeries)
            && metricToSeries.TryGetValue(metricName, out var cached))
        {
            return cached;
        }

        // Cache miss — targeted query for this specific metric + host
        var expression = $$"""{{metricName}}{hostname=="{{hostname}}"}""";
        return await _client.QuerySeriesAsync(expression, cancellationToken);
    }
}
