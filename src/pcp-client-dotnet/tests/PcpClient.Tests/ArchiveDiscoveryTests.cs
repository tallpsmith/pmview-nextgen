using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for archive metadata discovery: sampling interval inference
/// and time bounds detection from series values.
/// </summary>
public class ArchiveDiscoveryTests
{
    // ── Sampling interval inference ──

    [Fact]
    public void InferSamplingInterval_UniformSixtySecondData_ReturnsSixtySeconds()
    {
        var timestamps = new double[]
        {
            1773207912000.0,  // t+0s
            1773207972000.0,  // t+60s
            1773208032000.0,  // t+120s  (intentionally unsorted)
            1773208152000.0,  // t+240s  (intentionally unsorted)
            1773208092000.0,  // t+180s
        };

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        Assert.Equal(60.0, interval, precision: 1);
    }

    [Fact]
    public void InferSamplingInterval_TenSecondData_ReturnsTenSeconds()
    {
        var timestamps = new double[]
        {
            1773207912000.0,
            1773207922000.0,
            1773207932000.0,
            1773207942000.0,
        };

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        Assert.Equal(10.0, interval, precision: 1);
    }

    [Fact]
    public void InferSamplingInterval_SingleTimestamp_ReturnsDefaultFallback()
    {
        var timestamps = new double[] { 1773207912000.0 };

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        Assert.Equal(ArchiveDiscovery.DefaultSamplingIntervalSeconds, interval);
    }

    [Fact]
    public void InferSamplingInterval_EmptyArray_ReturnsDefaultFallback()
    {
        var timestamps = Array.Empty<double>();

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        Assert.Equal(ArchiveDiscovery.DefaultSamplingIntervalSeconds, interval);
    }

    [Fact]
    public void InferSamplingInterval_UsesMedian_NotMean_ToResistOutliers()
    {
        // 4 intervals of ~60s, plus one large gap (e.g. archive restart)
        var timestamps = new double[]
        {
            1773207912000.0,  // t+0
            1773207972000.0,  // t+60
            1773208032000.0,  // t+120
            1773208092000.0,  // t+180
            1773208152000.0,  // t+240
            1773209000000.0,  // t+1088 (big gap — outlier)
        };

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        // Median of [60, 60, 60, 60, 848] = 60, not the mean (~217)
        Assert.Equal(60.0, interval, precision: 1);
    }

    [Fact]
    public void InferSamplingInterval_DuplicateTimestamps_FromMultipleInstances_StillWorks()
    {
        // Real pmproxy data has duplicate timestamps (one per instance)
        // The inference should handle this by deduplicating first
        var timestamps = new double[]
        {
            1773207912000.0, 1773207912000.0,  // two instances at t+0
            1773207972000.0, 1773207972000.0,  // two instances at t+60
            1773208032000.0, 1773208032000.0,  // two instances at t+120
        };

        var interval = ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

        Assert.Equal(60.0, interval, precision: 1);
    }

    // ── Archive time bounds ──

    [Fact]
    public void DetectTimeBounds_ReturnsEarliestAndLatestFromMillisecondTimestamps()
    {
        var values = new List<SeriesValue>
        {
            new("series1", 1773208032000.0, 100.0),  // middle
            new("series1", 1773207912000.0, 50.0),    // earliest
            new("series1", 1773208152000.0, 200.0),   // latest
        };

        var bounds = ArchiveDiscovery.DetectTimeBounds(values);

        Assert.NotNull(bounds);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773207912000).UtcDateTime,
            bounds.Value.Start);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773208152000).UtcDateTime,
            bounds.Value.End);
    }

    [Fact]
    public void DetectTimeBounds_EmptyValues_ReturnsNull()
    {
        var values = new List<SeriesValue>();

        var bounds = ArchiveDiscovery.DetectTimeBounds(values);

        Assert.Null(bounds);
    }

    [Fact]
    public void DetectTimeBounds_SingleValue_StartEqualsEnd()
    {
        var values = new List<SeriesValue>
        {
            new("series1", 1773207912000.0, 42.0),
        };

        var bounds = ArchiveDiscovery.DetectTimeBounds(values);

        Assert.NotNull(bounds);
        Assert.Equal(bounds.Value.Start, bounds.Value.End);
    }
}
