namespace PcpClient;

/// <summary>
/// Infers archive metadata (sampling interval, time bounds) from
/// series values returned by pmproxy. Assumes uniform sampling
/// across all metrics in the archive.
/// </summary>
public static class ArchiveDiscovery
{
    public const double DefaultSamplingIntervalSeconds = 60.0;

    /// <summary>
    /// Infers the archive sampling interval from a set of timestamps.
    /// Uses median of consecutive deltas to resist outliers (e.g. archive gaps).
    /// Deduplicates timestamps first (pmproxy returns one per instance).
    /// Timestamps are in milliseconds (pmproxy series/values format).
    /// </summary>
    public static double InferSamplingIntervalSeconds(double[] timestampsMs)
    {
        if (timestampsMs.Length < 2)
            return DefaultSamplingIntervalSeconds;

        var unique = timestampsMs
            .Distinct()
            .OrderBy(t => t)
            .ToArray();

        if (unique.Length < 2)
            return DefaultSamplingIntervalSeconds;

        var deltas = new double[unique.Length - 1];
        for (int i = 0; i < deltas.Length; i++)
            deltas[i] = (unique[i + 1] - unique[i]) / 1000.0;

        Array.Sort(deltas);
        return deltas[deltas.Length / 2]; // median
    }

    /// <summary>
    /// Detects the earliest and latest timestamps from a set of series values.
    /// Timestamps in SeriesValue are milliseconds from pmproxy.
    /// </summary>
    public static (DateTime Start, DateTime End)? DetectTimeBounds(
        IReadOnlyList<SeriesValue> values)
    {
        if (values.Count == 0)
            return null;

        var minMs = values.Min(v => v.Timestamp);
        var maxMs = values.Max(v => v.Timestamp);

        return (
            DateTimeOffset.FromUnixTimeMilliseconds((long)minMs).UtcDateTime,
            DateTimeOffset.FromUnixTimeMilliseconds((long)maxMs).UtcDateTime
        );
    }
}
