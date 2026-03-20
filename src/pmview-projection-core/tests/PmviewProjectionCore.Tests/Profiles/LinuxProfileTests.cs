using Xunit;
using PmviewProjectionCore.Models;
using PmviewProjectionCore.Profiles;

namespace PmviewProjectionCore.Tests.Profiles;

public class LinuxProfileTests
{
    private readonly IReadOnlyList<ZoneDefinition> _zones = LinuxProfile.GetZones();

    [Fact]
    public void GetZones_ReturnsSixForegroundZones()
    {
        // CPU, Load, Memory, Disk, Net-In, Net-Out = 6
        var foreground = _zones.Where(z => z.Row == ZoneRow.Foreground).ToList();
        Assert.Equal(6, foreground.Count);
    }

    [Fact]
    public void GetZones_ReturnsFourBackgroundZones()
    {
        var background = _zones.Where(z => z.Row == ZoneRow.Background).ToList();
        Assert.Equal(4, background.Count);
    }

    [Fact]
    public void GetZones_ForegroundOrder_IsCpuLoadMemoryDiskNetInNetOut()
    {
        var names = _zones.Where(z => z.Row == ZoneRow.Foreground).Select(z => z.Name).ToList();
        Assert.Equal(new[] { "CPU", "Load", "Memory", "Disk", "Net-In", "Net-Out" }, names);
    }

    [Fact]
    public void GetZones_BackgroundOrder_IsPerCpuPerDiskNetInNetOut()
    {
        var names = _zones.Where(z => z.Row == ZoneRow.Background).Select(z => z.Name).ToList();
        Assert.Equal(new[] { "Per-CPU", "Per-Disk", "Network In", "Network Out" }, names);
    }

    [Fact]
    public void CpuZone_HasThreeMetrics()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.Equal(3, cpu.Metrics.Count);
        var names = cpu.Metrics.Select(m => m.MetricName).ToList();
        Assert.Contains("kernel.all.cpu.user", names);
        Assert.Contains("kernel.all.cpu.sys", names);
        Assert.Contains("kernel.all.cpu.nice", names);
    }

    [Fact]
    public void CpuZone_SourceRange_IsZeroToHundred()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.All(cpu.Metrics, m => { Assert.Equal(0f, m.SourceRangeMin); Assert.Equal(100f, m.SourceRangeMax); });
    }

    [Fact]
    public void CpuZone_HasStackGroups()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.NotNull(cpu.StackGroups);
    }

    [Fact]
    public void CpuZone_HasOneStackGroup_WithThreeMembers()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.NotNull(cpu.StackGroups);
        Assert.Single(cpu.StackGroups);
        Assert.Equal(3, cpu.StackGroups[0].MetricLabels.Count);
    }

    [Fact]
    public void CpuZone_StackGroup_OrderIsSysUserNice_ModeIsProportional()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        var group = cpu.StackGroups![0];
        Assert.Equal(new[] { "Sys", "User", "Nice" }, group.MetricLabels);
        Assert.Equal(StackMode.Proportional, group.Mode);
    }

    [Fact]
    public void CpuZone_MetricColours_SysIsRed_UserIsGreen_NiceIsCyan()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        var sys  = cpu.Metrics.Single(m => m.Label == "Sys");
        var user = cpu.Metrics.Single(m => m.Label == "User");
        var nice = cpu.Metrics.Single(m => m.Label == "Nice");

        // Sys = Red (#ef4444) — dominant red channel
        var expectedRed = RgbColour.FromHex("#ef4444");
        Assert.Equal(expectedRed, sys.DefaultColour);

        // User = Green (#22c55e) — dominant green channel
        var expectedGreen = RgbColour.FromHex("#22c55e");
        Assert.Equal(expectedGreen, user.DefaultColour);

        // Nice = Cyan (#22d3ee) — dominant green+blue channels
        var expectedCyan = RgbColour.FromHex("#22d3ee");
        Assert.Equal(expectedCyan, nice.DefaultColour);
    }

    [Fact]
    public void LoadZone_HasThreeMetrics_WithInstanceNames()
    {
        var load = _zones.Single(z => z.Name == "Load");
        Assert.Equal(3, load.Metrics.Count);
        var instanceNames = load.Metrics.Select(m => m.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instanceNames);
    }

    [Fact]
    public void MemoryZone_HasThreeMetrics_WithZeroSourceRangeMax()
    {
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.Equal(3, memory.Metrics.Count);
        Assert.All(memory.Metrics, m => Assert.Equal(0f, m.SourceRangeMax));
    }

    [Fact]
    public void AllForegroundZones_TargetRange_IsPointTwoToFive()
    {
        var foreground = _zones.Where(z => z.Row == ZoneRow.Foreground);
        foreach (var zone in foreground)
        {
            Assert.All(zone.Metrics, m =>
            {
                Assert.Equal(0.2f, m.TargetRangeMin);
                Assert.Equal(5.0f, m.TargetRangeMax);
            });
        }
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
    public void PerCpuZone_HasOneStackGroup_MatchingAggregateZone()
    {
        var perCpu = _zones.Single(z => z.Name == "Per-CPU");
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.NotNull(perCpu.StackGroups);
        Assert.Single(perCpu.StackGroups);
        Assert.Equal(cpu.StackGroups![0].MetricLabels, perCpu.StackGroups[0].MetricLabels);
        Assert.Equal(cpu.StackGroups[0].Mode, perCpu.StackGroups[0].Mode);
    }

    [Fact]
    public void PerCpuZone_MetricColours_MatchAggregateZone()
    {
        var perCpu = _zones.Single(z => z.Name == "Per-CPU");
        var sys  = perCpu.Metrics.Single(m => m.Label == "Sys");
        var user = perCpu.Metrics.Single(m => m.Label == "User");
        var nice = perCpu.Metrics.Single(m => m.Label == "Nice");

        Assert.Equal(RgbColour.FromHex("#ef4444"), sys.DefaultColour);
        Assert.Equal(RgbColour.FromHex("#22c55e"), user.DefaultColour);
        Assert.Equal(RgbColour.FromHex("#22d3ee"), nice.DefaultColour);
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

    [Fact]
    public void SystemGroup_CpuLoadMemory_ShareGroupName()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        var load = _zones.Single(z => z.Name == "Load");
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.Equal("System", cpu.GroupName);
        Assert.Equal("System", load.GroupName);
        Assert.Equal("System", memory.GroupName);
    }

    [Fact]
    public void SystemGroup_LoadAndMemory_AreRotated()
    {
        var load = _zones.Single(z => z.Name == "Load");
        var memory = _zones.Single(z => z.Name == "Memory");
        Assert.True(load.YRotationDegrees != 0f);
        Assert.True(memory.YRotationDegrees != 0f);
    }

    [Fact]
    public void SystemGroup_Cpu_IsNotRotated()
    {
        var cpu = _zones.Single(z => z.Name == "CPU");
        Assert.False(cpu.YRotationDegrees != 0f);
    }

    [Fact]
    public void NonSystemForegroundZones_HaveNoGroupName()
    {
        var disk = _zones.Single(z => z.Name == "Disk");
        var netIn = _zones.Single(z => z.Name == "Net-In");
        var netOut = _zones.Single(z => z.Name == "Net-Out");
        Assert.Null(disk.GroupName);
        Assert.Null(netIn.GroupName);
        Assert.Null(netOut.GroupName);
    }

    [Fact]
    public void BackgroundZones_AlignWithForegroundPartners()
    {
        var perCpu = _zones.Single(z => z.Name == "Per-CPU");
        var perDisk = _zones.Single(z => z.Name == "Per-Disk");
        var netIn = _zones.Single(z => z.Name == "Network In");
        var netOut = _zones.Single(z => z.Name == "Network Out");
        Assert.Equal("CPU", perCpu.AlignWithForegroundZone);
        Assert.Equal("Disk", perDisk.AlignWithForegroundZone);
        Assert.Equal("Net-In", netIn.AlignWithForegroundZone);
        Assert.Equal("Net-Out", netOut.AlignWithForegroundZone);
    }
}
