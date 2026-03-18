using Xunit;
using PmviewProjectionCore.Models;
using PmviewProjectionCore.Profiles;

namespace PmviewProjectionCore.Tests.Profiles;

public class MacOsProfileTests
{
    private readonly IReadOnlyList<ZoneDefinition> _zones = MacOsProfile.GetZones();

    [Fact]
    public void GetZones_ReturnsTenZones()
    {
        Assert.Equal(10, _zones.Count);
    }

    [Fact]
    public void GetZones_HasSameZoneNamesAsLinux()
    {
        var linux = LinuxProfile.GetZones().Select(z => z.Name);
        var macOs = _zones.Select(z => z.Name);
        Assert.Equal(linux, macOs);
    }

    [Fact]
    public void MemoryZone_HasFourMetrics_WiredActiveInactiveCompressed()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.Equal(4, memory.Metrics.Count);
        var labels = memory.Metrics.Select(m => m.Label).ToList();
        Assert.Equal(new[] { "Wired", "Active", "Inactive", "Compressed" }, labels);
    }

    [Fact]
    public void MemoryZone_MetricNames_AreDarwinSpecific()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        var names = memory.Metrics.Select(m => m.MetricName).ToList();
        Assert.Contains("mem.util.wired", names);
        Assert.Contains("mem.util.active", names);
        Assert.Contains("mem.util.inactive", names);
        Assert.Contains("mem.util.compressed", names);
    }

    [Fact]
    public void MemoryZone_SourceRangeMaxIsZero_ForPhysmemResolution()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.All(memory.Metrics, m => Assert.Equal(0f, m.SourceRangeMax));
    }

    [Fact]
    public void NetInAggregate_UsesRealMetrics_NotPlaceholders()
    {
        var netIn = _zones.Single(z => z.Name == "Net-In");
        Assert.All(netIn.Metrics, m => Assert.False(m.IsPlaceholder,
            $"{m.MetricName} should be a real metric now that PCP ships network.all.* on macOS"));
    }

    [Fact]
    public void NetOutAggregate_UsesRealMetrics_NotPlaceholders()
    {
        var netOut = _zones.Single(z => z.Name == "Net-Out");
        Assert.All(netOut.Metrics, m => Assert.False(m.IsPlaceholder,
            $"{m.MetricName} should be a real metric now that PCP ships network.all.* on macOS"));
    }

    [Fact]
    public void NetworkAggregateZones_HaveSameMetricsAsLinux()
    {
        var macNetIn = _zones.Single(z => z.Name == "Net-In");
        var macNetOut = _zones.Single(z => z.Name == "Net-Out");
        var linuxNetIn = LinuxProfile.GetZones().Single(z => z.Name == "Net-In");
        var linuxNetOut = LinuxProfile.GetZones().Single(z => z.Name == "Net-Out");
        Assert.Equal(linuxNetIn.Metrics, macNetIn.Metrics);
        Assert.Equal(linuxNetOut.Metrics, macNetOut.Metrics);
    }

    [Fact]
    public void CpuZone_IsIdenticalToLinux()
    {
        var macCpu = _zones.Single(z => z.Name == "CPU");
        var linuxCpu = LinuxProfile.GetZones().Single(z => z.Name == "CPU");
        Assert.Equal(linuxCpu.Metrics.Count, macCpu.Metrics.Count);
        Assert.NotNull(macCpu.StackGroups);
    }

    [Fact]
    public void AllZones_HaveNoPlaceholders()
    {
        foreach (var zone in _zones)
            Assert.All(zone.Metrics, m => Assert.False(m.IsPlaceholder,
                $"{zone.Name}/{m.MetricName} should not be a placeholder"));
    }

    [Fact]
    public void LinuxProfile_HasNoPlaceholders()
    {
        var linux = LinuxProfile.GetZones();
        foreach (var zone in linux)
            Assert.All(zone.Metrics, m => Assert.False(m.IsPlaceholder,
                $"Linux {zone.Name}/{m.MetricName} should never be a placeholder"));
    }
}
