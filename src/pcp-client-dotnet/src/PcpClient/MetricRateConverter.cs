namespace PcpClient;

/// <summary>
/// Converts cumulative PCP counter metrics into per-second rates.
/// Instant and discrete metrics pass through unchanged.
/// Mirrors the rate conversion that PCP tools (pmval, pmrep) do client-side.
/// </summary>
public class MetricRateConverter
{
    private readonly Dictionary<string, MetricSemantics> _semantics = new();
    private readonly Dictionary<string, CachedFetch> _previousValues = new();

    private record CachedFetch(double Timestamp, Dictionary<int, double> InstanceValues);

    private static int InstanceKey(int? instanceId) => instanceId ?? int.MinValue;

    public MetricRateConverter(IEnumerable<MetricDescriptor> descriptors)
    {
        foreach (var desc in descriptors)
            _semantics[desc.Name] = desc.Semantics;
    }

    /// <summary>
    /// Convert a batch of fetched metric values.
    /// Counter metrics are rate-converted (per second), others pass through.
    /// Returns only metrics that have valid values (counters need two samples).
    /// </summary>
    public IReadOnlyList<MetricValue> Convert(IReadOnlyList<MetricValue> values)
    {
        var results = new List<MetricValue>();

        foreach (var metric in values)
        {
            if (!_semantics.TryGetValue(metric.Name, out var semantics)
                || semantics != MetricSemantics.Counter)
            {
                // Unknown or non-counter: pass through unchanged
                results.Add(metric);
                continue;
            }

            var converted = ConvertCounter(metric);
            if (converted != null)
                results.Add(converted);
        }

        return results;
    }

    private MetricValue? ConvertCounter(MetricValue current)
    {
        // Build current instance map (using sentinel key for null InstanceId)
        var currentInstances = new Dictionary<int, double>();
        foreach (var iv in current.InstanceValues)
            currentInstances[InstanceKey(iv.InstanceId)] = iv.Value is double d ? d : System.Convert.ToDouble(iv.Value);

        if (!_previousValues.TryGetValue(current.Name, out var previous))
        {
            // First sample — cache and return nothing
            _previousValues[current.Name] = new CachedFetch(current.Timestamp, currentInstances);
            return null;
        }

        var timeDelta = current.Timestamp - previous.Timestamp;
        if (timeDelta <= 0)
        {
            // Zero or negative time delta — skip to avoid division by zero
            return null;
        }

        // Compute per-second rates for each instance
        var rateInstances = new List<InstanceValue>();

        foreach (var iv in current.InstanceValues)
        {
            var key = InstanceKey(iv.InstanceId);
            var currentValue = currentInstances[key];

            if (!previous.InstanceValues.TryGetValue(key, out var previousValue))
            {
                // New instance — no previous value, skip this sample
                continue;
            }

            if (currentValue < previousValue)
            {
                // Counter wrap/reset — skip this instance
                continue;
            }

            var rate = (currentValue - previousValue) / timeDelta;
            rateInstances.Add(new InstanceValue(iv.InstanceId, rate));
        }

        // Update cache for next call
        _previousValues[current.Name] = new CachedFetch(current.Timestamp, currentInstances);

        if (rateInstances.Count == 0)
            return null;

        return new MetricValue(current.Name, current.Pmid, current.Timestamp, rateInstances);
    }
}
