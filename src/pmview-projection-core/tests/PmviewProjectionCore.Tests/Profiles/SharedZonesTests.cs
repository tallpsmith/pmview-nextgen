using Xunit;
using System.Linq;
using PmviewProjectionCore.Profiles;

namespace PmviewProjectionCore.Tests.Profiles;

public class SharedZonesTests
{
    [Theory]
    [InlineData("disk.all.read_bytes", "Disk")]
    [InlineData("disk.all.write_bytes", "Disk")]
    [InlineData("disk.dev.read_bytes", "Per-Disk")]
    [InlineData("disk.dev.write_bytes", "Per-Disk")]
    [InlineData("network.interface.in.bytes", "Network In")]
    [InlineData("network.interface.in.packets", "Network In")]
    [InlineData("network.interface.out.bytes", "Network Out")]
    [InlineData("network.interface.out.packets", "Network Out")]
    [InlineData("kernel.all.cpu.sys", "CPU")]
    public void ResolveZone_ReturnsCorrectZoneName(string metricName, string expectedZone)
    {
        var zone = SharedZones.ResolveZone(metricName);
        Assert.Equal(expectedZone, zone);
    }

    [Fact]
    public void ResolveZone_ReturnsNull_ForUnknownMetric()
    {
        var zone = SharedZones.ResolveZone("bogus.metric.name");
        Assert.Null(zone);
    }

    [Theory]
    [InlineData("Disk", new[] { "disk.all.read_bytes", "disk.all.write_bytes" })]
    [InlineData("Per-Disk", new[] { "disk.dev.read_bytes", "disk.dev.write_bytes" })]
    [InlineData("Network In", new[] { "network.interface.in.bytes", "network.interface.in.packets", "network.interface.in.errors" })]
    public void GetMetricNames_ReturnsAllMetricsInZone(string zoneName, string[] expectedMetrics)
    {
        var metrics = SharedZones.GetMetricNames(zoneName);
        Assert.Equal(expectedMetrics.OrderBy(m => m), metrics.OrderBy(m => m));
    }

    [Fact]
    public void GetMetricNames_ReturnsEmpty_ForUnknownZone()
    {
        var metrics = SharedZones.GetMetricNames("Nonexistent");
        Assert.Empty(metrics);
    }

    [Theory]
    [InlineData("Disk", 550_000_000f)]
    [InlineData("Per-Disk", 550_000_000f)]
    [InlineData("Network In", 125_000_000f)]
    [InlineData("Network Out", 125_000_000f)]
    public void Zone_SourceRangeMax_MatchesPreset(string zoneName, float expectedMax)
    {
        var metrics = SharedZones.GetMetricNames(zoneName);
        Assert.NotEmpty(metrics);

        var allZones = typeof(SharedZones)
            .GetField("AllZones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as PmviewProjectionCore.Models.ZoneDefinition[];

        var zone = allZones!.First(z => z.Name == zoneName);
        var bytesMetric = zone.Metrics.FirstOrDefault(m => m.MetricName.Contains("bytes"));
        if (bytesMetric != null)
            Assert.Equal(expectedMax, bytesMetric.SourceRangeMax);
    }
}
