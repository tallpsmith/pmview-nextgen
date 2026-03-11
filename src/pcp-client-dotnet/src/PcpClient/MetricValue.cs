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
/// Numeric values use Value directly; string metrics use the StringValue constructor.
/// </summary>
public record InstanceValue
{
    public int? InstanceId { get; }
    public double Value { get; }
    public string? StringValue { get; }
    public bool IsString => StringValue != null;

    public InstanceValue(int? instanceId, double value)
    {
        InstanceId = instanceId;
        Value = value;
    }

    public InstanceValue(int? instanceId, string stringValue)
    {
        InstanceId = instanceId;
        StringValue = stringValue;
    }

    public double AsDouble() => IsString
        ? throw new InvalidOperationException($"Cannot convert string value '{StringValue}' to double")
        : Value;
}
