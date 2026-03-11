namespace PcpClient;

/// <summary>
/// Primary entry point for interacting with a pmproxy endpoint.
/// Manages context lifecycle and provides metric operations.
/// </summary>
public interface IPcpClient : IAsyncDisposable, IDisposable
{
    ConnectionState State { get; }
    Uri BaseUrl { get; }

    Task<int> ConnectAsync(int pollTimeoutSeconds = 60,
                           CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<MetricNamespace> GetChildrenAsync(string prefix = "",
                                           CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetricDescriptor>> DescribeMetricsAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default);

    Task<InstanceDomain> GetInstanceDomainAsync(string metricName,
                                                 CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetricValue>> FetchAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of traversing the PCP metric namespace tree.
/// </summary>
public record MetricNamespace(
    string Prefix,
    IReadOnlyList<string> LeafNames,
    IReadOnlyList<string> NonLeafNames);
