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
        var foreground = layout.Zones.Where(z => !z.HasGrid).ToList();
        var leftEdge  = foreground.Min(z => z.Position.X);
        var rightEdge = foreground.Max(z => z.Position.X + z.GroundWidth);
        var centre    = (leftEdge + rightEdge) / 2f;
        Assert.True(Math.Abs(centre) < 0.01f, $"Foreground centre {centre} should be ~0");
    }

    [Fact]
    public void Calculate_ForegroundZones_AtZEqualsZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => !z.HasGrid).ToList();
        Assert.All(foreground, z => Assert.Equal(0f, z.Position.Z));
    }

    [Fact]
    public void Calculate_ForegroundShapes_HaveUniqueNodeNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var allNames = layout.Zones.SelectMany(z => z.Shapes).Select(s => s.NodeName).ToList();
        Assert.Equal(allNames.Count, allNames.Distinct().Count());
    }

    [Fact]
    public void Calculate_ForegroundZoneOrder_ContainsExpectedZones()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foregroundNames = layout.Zones
            .Where(z => !z.HasGrid)
            .OrderBy(z => z.Position.X)
            .Select(z => z.Name)
            .ToList();
        Assert.Contains("System", foregroundNames);
        Assert.Contains("Cpu-Split", foregroundNames);
        Assert.Contains("Disk", foregroundNames);
        Assert.Contains("Net-In", foregroundNames);
        Assert.Contains("Net-Out", foregroundNames);
    }

    [Fact]
    public void Calculate_CpuSplitZone_HasThreeShapes_NoStacks()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpuSplit = layout.Zones.Single(z => z.Name == "Cpu-Split");
        Assert.Equal(3, cpuSplit.Shapes.Count);
        Assert.Empty(cpuSplit.Items.OfType<PlacedStack>());
    }

    [Fact]
    public void Calculate_SystemZone_HasLoadShapes_WithInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var loadShapes = system.Shapes.Where(s => s.InstanceName != null).ToList();
        Assert.Equal(3, loadShapes.Count);
        var instanceNames = loadShapes.Select(s => s.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instanceNames);
    }

    [Fact]
    public void Calculate_SystemZone_MemoryShapes_SourceRangeMaxFromPhysmem()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var memShapes = system.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();
        Assert.Equal(3, memShapes.Count);
        Assert.All(memShapes, s => Assert.Equal(16_000_000_000f, s.SourceRangeMax));
    }

    [Fact]
    public void Calculate_NetInAggregateZone_IsForeground_HasTwoShapes()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var netIn = layout.Zones.Single(z => z.Name == "Net-In" && !z.HasGrid);
        Assert.Equal(0f, netIn.Position.Z);
        Assert.Equal(2, netIn.Shapes.Count);
    }

    [Fact]
    public void Calculate_ForegroundRow_CenteredOnGroundWidthFootprint()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => !z.HasGrid).ToList();

        var leftEdge  = foreground.Min(z => z.Position.X);
        var rightEdge = foreground.Max(z => z.Position.X + z.GroundWidth);
        var centre    = (leftEdge + rightEdge) / 2f;

        Assert.True(Math.Abs(centre) < 0.1f,
            $"Foreground visual centre {centre:F4} should be ~0 when using GroundWidth footprint");
    }

    [Fact]
    public void Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones
            .Where(z => !z.HasGrid)
            .OrderBy(z => z.Position.X)
            .ToList();

        for (int i = 0; i < foreground.Count - 1; i++)
        {
            var left  = foreground[i];
            var right = foreground[i + 1];
            var leftVisualWidth = left.RotateYNinetyDeg ? left.GroundDepth : left.GroundWidth;
            var leftFootprintEnd = left.Position.X + leftVisualWidth;
            var gap = right.Position.X - leftFootprintEnd;
            Assert.True(gap <= 2.5f,
                $"Gap '{left.Name}'→'{right.Name}' is {gap:F2}, expected <= 2.5 (ZoneGap=2.0)");
        }
    }

    [Fact]
    public void Calculate_NonRotatedZones_HaveNoRotation()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var disk = layout.Zones.Single(z => z.Name == "Disk");
        var netIn = layout.Zones.Single(z => z.Name == "Net-In" && !z.HasGrid);
        var netOut = layout.Zones.Single(z => z.Name == "Net-Out" && !z.HasGrid);
        Assert.False(disk.RotateYNinetyDeg);
        Assert.False(netIn.RotateYNinetyDeg);
        Assert.False(netOut.RotateYNinetyDeg);
    }

    [Fact]
    public void Calculate_ForegroundZones_HaveEmptyInstanceLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Empty(system.InstanceLabels ?? []);
    }

    [Fact]
    public void Calculate_CpuSplitZone_HasMetricLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpuSplit = layout.Zones.Single(z => z.Name == "Cpu-Split");
        Assert.Equal(new[] { "User", "Sys", "Nice" }, cpuSplit.MetricLabels);
    }

    [Fact]
    public void Calculate_CpuSplitZone_GroundWidthIsNominalFromMetricCount()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpuSplit = layout.Zones.Single(z => z.Name == "Cpu-Split");
        // Cpu-Split has 3 metrics → nominal width = (3-1)*1.2 + 0.8 + 1.2 = 4.4
        Assert.True(cpuSplit.GroundWidth > 2f, $"Cpu-Split GroundWidth {cpuSplit.GroundWidth} should reflect 3-metric zone");
    }

    [Fact]
    public void Calculate_DiskZone_ShapesAllAtVec3Zero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var disk = layout.Zones.Single(z => z.Name == "Disk");
        Assert.All(disk.Shapes, s => Assert.Equal(Vec3.Zero, s.LocalPosition));
    }

    [Fact]
    public void Calculate_NetInZone_ShapesAllAtOrigin()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var netIn = layout.Zones.Single(z => z.Name == "Net-In" && !z.HasGrid);
        Assert.All(netIn.Shapes, s => Assert.Equal(Vec3.Zero, s.LocalPosition));
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
        var background = layout.Zones.Where(z => z.HasGrid).ToList();
        Assert.All(background, z => Assert.True(z.Position.Z < 0));
    }

    [Fact]
    public void Calculate_PerCpuZone_MetricLabelsCountEqualsMetricCount()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(3, perCpu.MetricLabels?.Count);
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
        Assert.True(perCpu.GroundWidth > 0f);
        Assert.True(perCpu.GroundDepth > 0f);
    }

    [Fact]
    public void Calculate_GridSpacing_WiderThanShapeWidth()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.True(perCpu.ColumnSpacing >= 2.0f,
            $"Column spacing {perCpu.ColumnSpacing} should be >= 2.0 for label clearance");
        Assert.True(perCpu.RowSpacing >= 2.5f,
            $"Row spacing {perCpu.RowSpacing} should be >= 2.5 for label clearance");
    }

    [Fact]
    public void Calculate_PerCpuZone_HasMetricLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(new[] { "User", "Sys", "Nice" }, perCpu.MetricLabels);
    }

    [Fact]
    public void Calculate_PerCpuZone_HasInstanceLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(new[] { "cpu0", "cpu1" }, perCpu.InstanceLabels);
    }

    [Fact]
    public void Calculate_AdjacentBackgroundZones_StrideIncludesRowHeaderReservation()
    {
        const float ZoneGap = 2.0f;
        const float RowHeaderReservation = 2.0f;
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2, nics: 2));
        var background = layout.Zones
            .Where(z => z.HasGrid)
            .OrderBy(z => z.Position.X)
            .ToList();

        for (var i = 0; i < background.Count - 1; i++)
        {
            var left = background[i];
            var right = background[i + 1];
            var stride = right.Position.X - left.Position.X;
            Assert.True(stride >= left.GroundWidth + RowHeaderReservation + ZoneGap,
                $"Zone '{left.Name}' → '{right.Name}': stride {stride:F2} < {left.GroundWidth + RowHeaderReservation + ZoneGap:F2} (bezel + reservation + gap)");
        }
    }
}
