namespace PcpClient;

/// <summary>
/// A fetched value for a specific metric at a point in time.
/// </summary>
public record MetricValue(
    string Name,
    string Pmid,
    double Timestamp,
    IReadOnlyList<InstanceValue> InstanceValues);

/// <summary>
/// A single value for a specific instance of a metric.
/// Singular metrics have InstanceId = null.
/// </summary>
public record InstanceValue(int? InstanceId, object Value);
