using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for MetricRateConverter: converts cumulative counter metrics
/// into per-second rates while passing through instant/discrete values unchanged.
/// Mirrors what PCP tools (pmval, pmrep) do client-side.
/// </summary>
public class MetricRateConverterTests
{
    // ── Helper Factories ──

    private static MetricDescriptor CounterMetric(string name) =>
        new(name, "1.0.0", MetricType.U64, MetricSemantics.Counter,
            null, null, "test counter", null);

    private static MetricDescriptor InstantMetric(string name) =>
        new(name, "2.0.0", MetricType.Double, MetricSemantics.Instant,
            null, null, "test instant", null);

    private static MetricDescriptor DiscreteMetric(string name) =>
        new(name, "3.0.0", MetricType.U32, MetricSemantics.Discrete,
            null, null, "test discrete", null);

    private static MetricValue MakeValue(string name, double timestamp,
        params (int? instanceId, double value)[] instances) =>
        new(name, "0.0.0", timestamp,
            instances.Select(i => new InstanceValue(i.instanceId, i.value))
                .ToList());

    // ── Instant metrics pass through unchanged ──

    [Fact]
    public void Convert_InstantMetric_PassesThroughUnchanged()
    {
        var converter = new MetricRateConverter(new[] { InstantMetric("cpu.load") });

        var values = new[] { MakeValue("cpu.load", 1000.0, (null, 3.5)) };
        var result = converter.Convert(values);

        Assert.Single(result);
        Assert.Equal(3.5, result[0].InstanceValues[0].Value);
    }

    [Fact]
    public void Convert_DiscreteMetric_PassesThroughUnchanged()
    {
        var converter = new MetricRateConverter(new[] { DiscreteMetric("hinv.ncpu") });

        var values = new[] { MakeValue("hinv.ncpu", 1000.0, (null, 8.0)) };
        var result = converter.Convert(values);

        Assert.Single(result);
        Assert.Equal(8.0, result[0].InstanceValues[0].Value);
    }

    // ── Counter: first fetch returns empty (no previous to diff against) ──

    [Fact]
    public void Convert_CounterFirstFetch_ReturnsEmptyForThatMetric()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        var values = new[] { MakeValue("disk.dev.read", 1000.0, (0, 50000.0)) };
        var result = converter.Convert(values);

        Assert.Empty(result);
    }

    // ── Counter: second fetch computes rate ──

    [Fact]
    public void Convert_CounterSecondFetch_ComputesPerSecondRate()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        // First fetch — baseline, returns nothing
        converter.Convert(new[] { MakeValue("disk.dev.read", 1000.0, (0, 50000.0)) });

        // Second fetch — 1 second later, counter increased by 200
        var result = converter.Convert(
            new[] { MakeValue("disk.dev.read", 1001.0, (0, 50200.0)) });

        Assert.Single(result);
        var rate = result[0].InstanceValues[0].Value;
        Assert.Equal(200.0, rate, precision: 1);
    }

    [Fact]
    public void Convert_CounterWithHalfSecondInterval_ScalesToPerSecond()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.write") });

        converter.Convert(new[] { MakeValue("disk.dev.write", 1000.0, (0, 10000.0)) });

        // 0.5 seconds later, counter increased by 100 → rate = 200/sec
        var result = converter.Convert(
            new[] { MakeValue("disk.dev.write", 1000.5, (0, 10100.0)) });

        var rate = result[0].InstanceValues[0].Value;
        Assert.Equal(200.0, rate, precision: 1);
    }

    // ── Counter: multiple instances tracked independently ──

    [Fact]
    public void Convert_CounterMultipleInstances_TracksEachIndependently()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        // First fetch — two instances
        converter.Convert(new[]
        {
            MakeValue("disk.dev.read", 1000.0, (0, 1000.0), (1, 5000.0))
        });

        // Second fetch — different deltas per instance
        var result = converter.Convert(new[]
        {
            MakeValue("disk.dev.read", 1001.0, (0, 1300.0), (1, 5100.0))
        });

        Assert.Single(result);
        var instances = result[0].InstanceValues;
        Assert.Equal(2, instances.Count);
        Assert.Equal(300.0, instances[0].Value, precision: 1); // inst 0: +300/1s
        Assert.Equal(100.0, instances[1].Value, precision: 1); // inst 1: +100/1s
    }

    // ── Mixed counter and instant metrics in same fetch ──

    [Fact]
    public void Convert_MixedMetrics_CounterRateConvertedInstantPassedThrough()
    {
        var converter = new MetricRateConverter(new[]
        {
            CounterMetric("disk.dev.read"),
            InstantMetric("kernel.all.load")
        });

        // First fetch
        converter.Convert(new[]
        {
            MakeValue("disk.dev.read", 1000.0, (0, 50000.0)),
            MakeValue("kernel.all.load", 1000.0, (null, 1.5))
        });

        // Second fetch
        var result = converter.Convert(new[]
        {
            MakeValue("disk.dev.read", 1001.0, (0, 50500.0)),
            MakeValue("kernel.all.load", 1001.0, (null, 2.0))
        });

        Assert.Equal(2, result.Count);

        // disk.dev.read: rate converted
        var diskResult = result.First(r => r.Name == "disk.dev.read");
        Assert.Equal(500.0, diskResult.InstanceValues[0].Value, precision: 1);

        // kernel.all.load: passed through
        var loadResult = result.First(r => r.Name == "kernel.all.load");
        Assert.Equal(2.0, loadResult.InstanceValues[0].Value);
    }

    // ── Counter wrap (value decreases) — treat as reset, skip that sample ──

    [Fact]
    public void Convert_CounterWrap_SkipsInstanceForThatSample()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("net.bytes") });

        converter.Convert(new[] { MakeValue("net.bytes", 1000.0, (0, 4000000000.0)) });

        // Counter wrapped — new value is less than previous
        var result = converter.Convert(
            new[] { MakeValue("net.bytes", 1001.0, (0, 1000.0)) });

        // Should skip this metric entirely (no valid rate)
        Assert.Empty(result);
    }

    // ── Unknown metric (not in descriptors) passes through unchanged ──

    [Fact]
    public void Convert_UnknownMetric_PassesThroughUnchanged()
    {
        var converter = new MetricRateConverter(new[] { InstantMetric("known.metric") });

        var values = new[] { MakeValue("mystery.metric", 1000.0, (null, 42.0)) };
        var result = converter.Convert(values);

        Assert.Single(result);
        Assert.Equal(42.0, result[0].InstanceValues[0].Value);
    }

    // ── New instance appearing mid-stream ──

    [Fact]
    public void Convert_NewInstanceAppears_SkipsItFirstTime()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        // First fetch — one instance
        converter.Convert(new[] { MakeValue("disk.dev.read", 1000.0, (0, 1000.0)) });

        // Second fetch — new instance 1 appears
        var result = converter.Convert(new[]
        {
            MakeValue("disk.dev.read", 1001.0, (0, 1500.0), (1, 3000.0))
        });

        // Instance 0 has rate, instance 1 is new so skipped
        Assert.Single(result);
        Assert.Single(result[0].InstanceValues);
        Assert.Equal(0, result[0].InstanceValues[0].InstanceId);
        Assert.Equal(500.0, result[0].InstanceValues[0].Value, precision: 1);
    }

    // ── Zero time delta (duplicate timestamp) — skip to avoid division by zero ──

    [Fact]
    public void Convert_ZeroTimeDelta_SkipsCounterMetric()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        converter.Convert(new[] { MakeValue("disk.dev.read", 1000.0, (0, 1000.0)) });

        // Same timestamp
        var result = converter.Convert(
            new[] { MakeValue("disk.dev.read", 1000.0, (0, 1500.0)) });

        Assert.Empty(result);
    }

    // ── IsCounter — exposes semantic knowledge for archive playback ──

    [Fact]
    public void IsCounter_CounterMetric_ReturnsTrue()
    {
        var converter = new MetricRateConverter(new[] { CounterMetric("disk.dev.read") });

        Assert.True(converter.IsCounter("disk.dev.read"));
    }

    [Fact]
    public void IsCounter_InstantMetric_ReturnsFalse()
    {
        var converter = new MetricRateConverter(new[] { InstantMetric("kernel.all.load") });

        Assert.False(converter.IsCounter("kernel.all.load"));
    }

    [Fact]
    public void IsCounter_UnknownMetric_ReturnsFalse()
    {
        var converter = new MetricRateConverter(new[] { InstantMetric("known") });

        Assert.False(converter.IsCounter("unknown.metric"));
    }
}
