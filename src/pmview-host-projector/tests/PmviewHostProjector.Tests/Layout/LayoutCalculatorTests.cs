using Xunit;
using PmviewHostProjector.Layout;
using PmviewHostProjector.Models;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector.Tests.Layout;

public class LayoutCalculatorForegroundTests
{
    private static HostTopology MakeTopology(int cpus = 4, int disks = 2, int nics = 3) =>
        new(HostOs.Linux, "testhost",
            Enumerable.Range(0, cpus).Select(i => $"cpu{i}").ToList(),
            Enumerable.Range(0, disks).Select(i => $"sda{i}").ToList(),
            Enumerable.Range(0, nics).Select(i => $"eth{i}").ToList(),
            PhysicalMemoryBytes: 16_000_000_000L);

    private static IReadOnlyList<ZoneDefinition> LinuxZones => LinuxProfile.GetZones();

    [Fact]
    public void Calculate_SetsHostname()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        Assert.Equal("testhost", layout.Hostname);
    }

    [Fact]
    public void Calculate_ForegroundRow_CenteredOnXZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => z.GridColumns == null).ToList();
        var allX = foreground.SelectMany(z => z.Shapes.Select(s => z.Position.X + s.LocalPosition.X));
        var centre = (allX.Min() + allX.Max()) / 2f;
        Assert.True(Math.Abs(centre) < 0.01f, $"Foreground centre {centre} should be ~0");
    }

    [Fact]
    public void Calculate_ForegroundZones_AtZEqualsZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => z.GridColumns == null).ToList();
        Assert.All(foreground, z => Assert.Equal(0f, z.Position.Z));
    }

    [Fact]
    public void Calculate_CpuForegroundZone_HasThreeShapes()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        Assert.Equal(3, cpu.Shapes.Count);
    }

    [Fact]
    public void Calculate_LoadZone_ShapesCarryInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var load = layout.Zones.Single(z => z.Name == "Load");
        var instances = load.Shapes.Select(s => s.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instances);
    }

    [Fact]
    public void Calculate_MemoryZone_SourceRangeMaxSetFromPhysmem()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var mem = layout.Zones.Single(z => z.Name == "Memory");
        Assert.All(mem.Shapes, s => Assert.Equal(16_000_000_000f, s.SourceRangeMax));
    }

    [Fact]
    public void Calculate_ForegroundShapes_HaveUniqueNodeNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var allNames = layout.Zones.SelectMany(z => z.Shapes).Select(s => s.NodeName).ToList();
        Assert.Equal(allNames.Count, allNames.Distinct().Count());
    }

    [Fact]
    public void Calculate_ForegroundShapes_HaveDistinctLocalXPositions()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        foreach (var zone in layout.Zones.Where(z => z.GridColumns == null))
        {
            var xPositions = zone.Shapes.Select(s => s.LocalPosition.X).Distinct().ToList();
            Assert.Equal(zone.Shapes.Count, xPositions.Count);
        }
    }

    [Fact]
    public void Calculate_ForegroundZone_HasGroundExtent()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var load = layout.Zones.Single(z => z.Name == "Load");
        // Load zone has 3 shapes at X=0, 1.5, 3.0. Ground should span them + padding.
        Assert.True(load.GroundWidth > 3.0f, $"GroundWidth {load.GroundWidth} should be > 3.0");
        Assert.True(load.GroundDepth > 0f, $"GroundDepth {load.GroundDepth} should be > 0");
    }
}

public class LayoutCalculatorBackgroundTests
{
    private static HostTopology MakeTopology(int cpus = 4, int disks = 2, int nics = 3) =>
        new(HostOs.Linux, "testhost",
            Enumerable.Range(0, cpus).Select(i => $"cpu{i}").ToList(),
            Enumerable.Range(0, disks).Select(i => $"sda{i}").ToList(),
            Enumerable.Range(0, nics).Select(i => $"eth{i}").ToList(),
            PhysicalMemoryBytes: 16_000_000_000L);

    private static IReadOnlyList<ZoneDefinition> LinuxZones => LinuxProfile.GetZones();

    [Fact]
    public void Calculate_BackgroundZones_AtNegativeZOffset()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var background = layout.Zones.Where(z => z.GridColumns != null).ToList();
        Assert.All(background, z => Assert.True(z.Position.Z < 0));
    }

    [Fact]
    public void Calculate_PerCpuZone_GridColumnsEqualsMetricCount()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(3, perCpu.GridColumns);
    }

    [Fact]
    public void Calculate_PerCpuZone_ShapeCountEqualsInstancesTimesMetrics()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(12, perCpu.Shapes.Count);
    }

    [Fact]
    public void Calculate_BackgroundShapes_HaveInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        var names = perCpu.Shapes.Select(s => s.InstanceName).Distinct().ToList();
        Assert.Contains("cpu0", names);
        Assert.Contains("cpu1", names);
    }

    [Fact]
    public void Calculate_BackgroundShapes_LocalPositionsAreZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.All(perCpu.Shapes, s => Assert.Equal(Vec3.Zero, s.LocalPosition));
    }

    [Fact]
    public void Calculate_InstanceNameShortening_StripsPrefixPath()
    {
        var topology = new HostTopology(HostOs.Linux, "testhost",
            ["cpu0"], ["/dev/sda1", "/dev/nvme0n1"], ["eth0"],
            PhysicalMemoryBytes: 16_000_000_000L);
        var layout = LayoutCalculator.Calculate(LinuxZones, topology);
        var perDisk = layout.Zones.Single(z => z.Name == "Per-Disk");
        var labels = perDisk.Shapes.Select(s => s.DisplayLabel).Distinct().ToList();
        Assert.Contains("sda1", labels);
        Assert.Contains("nvme0n1", labels);
    }

    [Fact]
    public void Calculate_EmptyInstances_ProducesZeroShapes()
    {
        var topology = new HostTopology(HostOs.Linux, "testhost",
            ["cpu0"], [], [], PhysicalMemoryBytes: 16_000_000_000L);
        var layout = LayoutCalculator.Calculate(LinuxZones, topology);
        var perDisk = layout.Zones.Single(z => z.Name == "Per-Disk");
        Assert.Empty(perDisk.Shapes);
    }

    [Fact]
    public void Calculate_BackgroundZone_HasGroundExtent()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // 4 instances x 3 metrics, grid 3 cols => 4 rows x 3 cols
        Assert.True(perCpu.GroundWidth > 0f);
        Assert.True(perCpu.GroundDepth > 0f);
    }
}
