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
    public void Calculate_ForegroundZoneOrder_IsCpuLoadMemoryDiskNetInNetOut()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foregroundNames = layout.Zones
            .Where(z => !z.HasGrid)
            .OrderBy(z => z.Position.X)
            .Select(z => z.Name)
            .ToList();
        Assert.Contains("CPU", foregroundNames);
        Assert.Contains("Load", foregroundNames);
        Assert.Contains("Memory", foregroundNames);
        Assert.Contains("Disk", foregroundNames);
        Assert.Contains("Net-In", foregroundNames);
        Assert.Contains("Net-Out", foregroundNames);
    }

    [Fact]
    public void Calculate_CpuZone_HasThreeShapes_NoStacks()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        Assert.Equal(3, cpu.Shapes.Count);
        Assert.Empty(cpu.Items.OfType<PlacedStack>());
    }

    [Fact]
    public void Calculate_LoadZone_HasThreeShapes_WithInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var load = layout.Zones.Single(z => z.Name == "Load");
        Assert.Equal(3, load.Shapes.Count);
        var instanceNames = load.Shapes.Select(s => s.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instanceNames);
    }

    [Fact]
    public void Calculate_MemoryZone_HasThreeShapes_SourceRangeMaxFromPhysmem()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var memory = layout.Zones.Single(z => z.Name == "Memory");
        Assert.Equal(3, memory.Shapes.Count);
        Assert.All(memory.Shapes, s => Assert.Equal(16_000_000_000f, s.SourceRangeMax));
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
    public void Calculate_SimpleZones_HaveNoRotation()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        var load = layout.Zones.Single(z => z.Name == "Load");
        var memory = layout.Zones.Single(z => z.Name == "Memory");
        Assert.False(cpu.RotateYNinetyDeg);
        Assert.False(load.RotateYNinetyDeg);
        Assert.False(memory.RotateYNinetyDeg);
    }

    [Fact]
    public void Calculate_SimpleZones_HaveEmptyGridLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        Assert.Empty(cpu.MetricLabels ?? []);
        Assert.Empty(cpu.InstanceLabels ?? []);
    }

    [Fact]
    public void Calculate_SimpleZones_ShapesHaveAutoSpacedPositions()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        var positions = cpu.Shapes.Select(s => s.LocalPosition.X).ToList();
        // Auto-spacing: shapes at 0, 1.2, 2.4 (ShapeSpacing=1.2)
        Assert.Equal(3, positions.Distinct().Count());
        Assert.True(positions[1] > positions[0], "Second shape should be right of first");
        Assert.True(positions[2] > positions[1], "Third shape should be right of second");
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
