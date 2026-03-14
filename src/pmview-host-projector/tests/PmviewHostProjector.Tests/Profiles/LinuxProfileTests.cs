using Xunit;
using PmviewHostProjector.Models;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector.Tests.Profiles;

public class LinuxProfileTests
{
    private readonly IReadOnlyList<ZoneDefinition> _zones = LinuxProfile.GetZones();

    [Fact]
    public void GetZones_ReturnsFiveForegroundZones()
    {
        // System, Cpu-Split (comparison), Disk, Net-In, Net-Out = 5
        var foreground = _zones.Where(z => z.Row == ZoneRow.Foreground).ToList();
        Assert.Equal(5, foreground.Count);
    }

    [Fact]
    public void GetZones_ReturnsFourBackgroundZones()
    {
        var background = _zones.Where(z => z.Row == ZoneRow.Background).ToList();
        Assert.Equal(4, background.Count);
    }

    [Fact]
    public void GetZones_ForegroundOrder_IsSystemCpuSplitDiskNetInNetOut()
    {
        var names = _zones.Where(z => z.Row == ZoneRow.Foreground).Select(z => z.Name).ToList();
        Assert.Equal(new[] { "System", "Cpu-Split", "Disk", "Net-In", "Net-Out" }, names);
    }

    [Fact]
    public void GetZones_BackgroundOrder_IsPerCpuPerDiskNetInNetOut()
    {
        var names = _zones.Where(z => z.Row == ZoneRow.Background).Select(z => z.Name).ToList();
        Assert.Equal(new[] { "Per-CPU", "Per-Disk", "Network In", "Network Out" }, names);
    }

    [Fact]
    public void SystemZone_HasNineMetrics_CpuLoadMemory()
    {
        var system = _zones.Single(z => z.Name == "System");
        Assert.Equal(9, system.Metrics.Count);
        var names = system.Metrics.Select(m => m.MetricName).ToList();
        Assert.Contains("kernel.all.cpu.user", names);
        Assert.Contains("kernel.all.cpu.sys", names);
        Assert.Contains("kernel.all.cpu.nice", names);
        Assert.Contains("kernel.all.load", names);
        Assert.Contains("mem.util.used", names);
    }

    [Fact]
    public void SystemZone_CpuMetrics_SourceRange_IsZeroToHundred()
    {
        var system = _zones.Single(z => z.Name == "System");
        var cpuMetrics = system.Metrics.Where(m => m.MetricName.StartsWith("kernel.all.cpu.")).ToList();
        Assert.Equal(3, cpuMetrics.Count);
        Assert.All(cpuMetrics, m => { Assert.Equal(0f, m.SourceRangeMin); Assert.Equal(100f, m.SourceRangeMax); });
    }

    [Fact]
    public void SystemZone_LoadMetrics_HaveInstanceNames()
    {
        var system = _zones.Single(z => z.Name == "System");
        var loadMetrics = system.Metrics.Where(m => m.MetricName == "kernel.all.load").ToList();
        Assert.Equal(3, loadMetrics.Count);
        var instanceNames = loadMetrics.Select(m => m.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instanceNames);
    }

    [Fact]
    public void SystemZone_MemoryMetrics_HaveZeroSourceRangeMax_AutoDetectedAtRuntime()
    {
        var system = _zones.Single(z => z.Name == "System");
        var memMetrics = system.Metrics.Where(m => m.MetricName.StartsWith("mem.")).ToList();
        Assert.Equal(3, memMetrics.Count);
        Assert.All(memMetrics, m => Assert.Equal(0f, m.SourceRangeMax));
    }

    [Fact]
    public void SystemZone_AllMetrics_TargetRange_IsPointTwoToFive()
    {
        var system = _zones.Single(z => z.Name == "System");
        Assert.All(system.Metrics, m =>
        {
            Assert.Equal(0.2f, m.TargetRangeMin);
            Assert.Equal(5.0f, m.TargetRangeMax);
        });
    }

    [Fact]
    public void DiskTotalsZone_HasTwoCylinders()
    {
        var disk = _zones.Single(z => z.Name == "Disk");
        Assert.Equal(2, disk.Metrics.Count);
        Assert.All(disk.Metrics, m => Assert.Equal(ShapeType.Cylinder, m.Shape));
    }

    [Fact]
    public void BackgroundZones_HaveInstanceMetricSource()
    {
        var background = _zones.Where(z => z.Row == ZoneRow.Background);
        Assert.All(background, z => Assert.NotNull(z.InstanceMetricSource));
    }

    [Fact]
    public void ForegroundZones_HaveNoInstanceMetricSource()
    {
        var foreground = _zones.Where(z => z.Row == ZoneRow.Foreground);
        Assert.All(foreground, z => Assert.Null(z.InstanceMetricSource));
    }

    [Fact]
    public void PerCpuZone_InstanceSource_IsKernelPercpuCpuUser()
    {
        var perCpu = _zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal("kernel.percpu.cpu.user", perCpu.InstanceMetricSource);
    }

    [Fact]
    public void NetworkInZone_HasThreeMetrics_BytesPktsErrors()
    {
        var netIn = _zones.Single(z => z.Name == "Network In");
        Assert.Equal(3, netIn.Metrics.Count);
        Assert.Equal("network.interface.in.bytes", netIn.InstanceMetricSource);
    }

    [Fact]
    public void NetworkOutZone_ErrorMetric_IsRed()
    {
        var netOut = _zones.Single(z => z.Name == "Network Out");
        var errors = netOut.Metrics.Single(m => m.MetricName.Contains("errors"));
        var red = RgbColour.FromHex("#ef4444");
        Assert.Equal(red.R, errors.DefaultColour.R, 0.01f);
    }

    [Fact]
    public void NetworkZones_HaveNonZeroSourceRangeMax()
    {
        var netIn = _zones.Single(z => z.Name == "Network In");
        var netOut = _zones.Single(z => z.Name == "Network Out");
        Assert.All(netIn.Metrics, m => Assert.True(m.SourceRangeMax > 0f, $"{m.MetricName} has SourceRangeMax=0"));
        Assert.All(netOut.Metrics, m => Assert.True(m.SourceRangeMax > 0f, $"{m.MetricName} has SourceRangeMax=0"));
    }

    [Fact]
    public void NetworkBytesMetrics_SourceRangeMax_Is125MB()
    {
        var netIn = _zones.Single(z => z.Name == "Network In");
        var bytes = netIn.Metrics.Single(m => m.MetricName.Contains("bytes"));
        Assert.Equal(125_000_000f, bytes.SourceRangeMax);
    }
}
