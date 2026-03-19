using PcpClient;
using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Discovery;

/// <summary>
/// Discovers host topology from archive data via pmproxy /series/* endpoints.
/// Uses hostname-filtered queries to isolate data for a specific archived host,
/// avoiding the live /pmapi/* endpoints that return the local host's topology.
/// </summary>
public class ArchiveMetricDiscovery
{
    private const string SysnameMetric  = "kernel.uname.sysname";
    private const string CpuMetric      = "kernel.percpu.cpu.user";
    private const string DiskMetric     = "disk.dev.read_bytes";
    private const string NetworkMetric  = "network.interface.in.bytes";

    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    public ArchiveMetricDiscovery(Uri baseUrl, HttpClient httpClient)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Builds a HostTopology from archive series data for the given hostname.
    /// Queries instance domains for CPU, disk, and network metrics, and
    /// fetches kernel.uname.sysname to determine the OS.
    /// </summary>
    public async Task<HostTopology> DiscoverAsync(
        string hostname, CancellationToken cancellationToken = default)
    {
        var os = await DiscoverOsAsync(hostname, cancellationToken);
        var cpuInstances = await DiscoverInstanceNamesAsync(
            CpuMetric, hostname, cancellationToken);
        var diskDevices = MetricDiscovery.FilterDiskDevices(
            await DiscoverInstanceNamesAsync(DiskMetric, hostname, cancellationToken));
        var networkInterfaces = MetricDiscovery.FilterNetworkInterfaces(
            await DiscoverInstanceNamesAsync(NetworkMetric, hostname, cancellationToken));

        return new HostTopology(
            Os: os,
            Hostname: hostname,
            CpuInstances: cpuInstances,
            DiskDevices: diskDevices,
            NetworkInterfaces: networkInterfaces);
    }

    private async Task<HostOs> DiscoverOsAsync(
        string hostname, CancellationToken cancellationToken)
    {
        var seriesIds = await QuerySeriesForHostAsync(
            SysnameMetric, hostname, cancellationToken);
        if (seriesIds.Count == 0)
            return HostOs.Unknown;

        // Fetch the latest value to determine OS
        var now = DateTime.UtcNow;
        var valuesUrl = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
            _baseUrl, seriesIds, now, windowSeconds: 30 * 86400);
        var response = await _httpClient.GetAsync(valuesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var values = PcpSeriesQuery.ParseValuesResponse(json);

        if (values.Count == 0)
            return HostOs.Unknown;

        // sysname is a string value
        var latest = values.OrderByDescending(v => v.Timestamp).First();
        if (latest.IsString)
        {
            return latest.StringValue switch
            {
                "Linux" => HostOs.Linux,
                "Darwin" => HostOs.MacOs,
                _ => HostOs.Unknown
            };
        }

        return HostOs.Unknown;
    }

    private async Task<IReadOnlyList<string>> DiscoverInstanceNamesAsync(
        string metricName, string hostname, CancellationToken cancellationToken)
    {
        var seriesIds = await QuerySeriesForHostAsync(
            metricName, hostname, cancellationToken);
        if (seriesIds.Count == 0)
            return [];

        var instancesUrl = PcpSeriesQuery.BuildInstancesUrl(_baseUrl, seriesIds);
        var response = await _httpClient.GetAsync(instancesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var instances = PcpSeriesQuery.ParseInstancesResponse(json);

        return instances.Values.Select(i => i.Name).Distinct().ToList();
    }

    private async Task<IReadOnlyList<string>> QuerySeriesForHostAsync(
        string metricName, string hostname, CancellationToken cancellationToken)
    {
        var queryUrl = PcpSeriesQuery.BuildHostnameFilteredQueryUrl(
            _baseUrl, metricName, hostname);
        var response = await _httpClient.GetAsync(queryUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return PcpSeriesQuery.ParseQueryResponse(json);
    }
}
