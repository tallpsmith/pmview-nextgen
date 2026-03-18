using PcpClient;

namespace PmviewProjectionCore.Tests.TestHelpers;

/// <summary>
/// Hand-rolled stub for IPcpClient. Configure fetch/instance domain results
/// before each test — keeps the test surface crisp and dependency-free.
/// </summary>
public class StubPcpClient : IPcpClient
{
    public ConnectionState State => ConnectionState.Connected;
    public Uri BaseUrl => new("http://localhost:44322");

    private readonly Dictionary<string, string> _stringFetches = new();
    private readonly Dictionary<string, double> _doubleFetches = new();
    private readonly Dictionary<string, InstanceDomain> _instanceDomains = new();

    public void SetFetchStringResult(string metricName, string value)
        => _stringFetches[metricName] = value;

    public void SetFetchDoubleResult(string metricName, double value)
        => _doubleFetches[metricName] = value;

    public void SetInstanceDomain(string metricName, params string[] instanceNames)
    {
        var instances = instanceNames
            .Select((name, idx) => new Instance(idx, name))
            .ToList();
        _instanceDomains[metricName] = new InstanceDomain(metricName, instances);
    }

    public Task<int> ConnectAsync(int pollTimeoutSeconds = 60, CancellationToken cancellationToken = default)
        => Task.FromResult(1);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<MetricValue>> FetchAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MetricValue>();
        foreach (var name in metricNames)
        {
            if (_stringFetches.TryGetValue(name, out var strVal))
            {
                results.Add(new MetricValue(name, name, 0,
                    [new InstanceValue(null, strVal)]));
            }
            else if (_doubleFetches.TryGetValue(name, out var dblVal))
            {
                results.Add(new MetricValue(name, name, 0,
                    [new InstanceValue(null, dblVal)]));
            }
        }
        return Task.FromResult<IReadOnlyList<MetricValue>>(results);
    }

    public Task<InstanceDomain> GetInstanceDomainAsync(
        string metricName,
        CancellationToken cancellationToken = default)
    {
        if (_instanceDomains.TryGetValue(metricName, out var domain))
            return Task.FromResult(domain);
        return Task.FromResult(new InstanceDomain(metricName, []));
    }

    public Task<MetricNamespace> GetChildrenAsync(string prefix = "", CancellationToken cancellationToken = default)
        => Task.FromResult(new MetricNamespace(prefix, [], []));

    public Task<IReadOnlyList<MetricDescriptor>> DescribeMetricsAsync(
        IEnumerable<string> metricNames,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MetricDescriptor>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}
