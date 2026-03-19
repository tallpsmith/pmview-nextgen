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
        var queryUrl = PcpSeriesQuery.BuildHostnameFilteredQueryUrl(
            _baseUrl, ProbeMetric, hostname);
        var queryResponse = await _httpClient.GetAsync(queryUrl, cancellationToken);
        queryResponse.EnsureSuccessStatusCode();
        var queryJson = await queryResponse.Content.ReadAsStringAsync(cancellationToken);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);

        if (seriesIds.Count == 0)
            return null;

        var now = DateTime.UtcNow;
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
