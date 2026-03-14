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
    public void Calculate_ForegroundZone_HasEmptyGridLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Empty(system.MetricLabels ?? []);
        Assert.Empty(system.InstanceLabels ?? []);
    }

    [Fact]
    public void Calculate_SystemZone_HasNineShapes()
    {
        // CPU(3) + Load(3) + Memory(3) = 9 shapes in the merged System zone.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Equal(9, system.Shapes.Count);
    }

    [Fact]
    public void Calculate_SystemZone_IsForeground_AtZZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Equal(0f, system.Position.Z);
        Assert.Null(system.GridColumns);
    }

    [Fact]
    public void Calculate_SystemZone_LoadShapes_CarryInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var loadInstances = system.Shapes
            .Where(s => s.MetricName == "kernel.all.load")
            .Select(s => s.InstanceName)
            .ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, loadInstances);
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
    public void Calculate_SystemZone_HasGroundExtent()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        // 9 shapes — ground must span them all + padding
        Assert.True(system.GroundWidth > 9f,
            $"GroundWidth {system.GroundWidth} should be > 9 for a 9-bar zone");
        Assert.True(system.GroundDepth > 0f);
    }

    [Fact]
    public void Calculate_ForegroundZoneOrder_IsSystemDiskNetInNetOut()
    {
        // Foreground zones ordered left-to-right: System, Disk, Net-In, Net-Out
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones
            .Where(z => z.GridColumns == null)
            .OrderBy(z => z.Position.X)
            .Select(z => z.Name)
            .ToList();
        Assert.Equal(new[] { "System", "Disk", "Net-In", "Net-Out" }, foreground);
    }

    [Fact]
    public void Calculate_NetInAggregateZone_IsForeground_HasTwoShapes()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        // Net-In foreground zone (not the per-instance background zone "Network In")
        var netIn = layout.Zones.Single(z => z.Name == "Net-In" && z.GridColumns == null);
        Assert.Equal(0f, netIn.Position.Z);
        Assert.Equal(2, netIn.Shapes.Count);
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

    [Fact]
    public void Calculate_GridSpacing_WiderThanShapeWidth()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // Column spacing should be at least 2.0 to fit label text between columns
        Assert.True(perCpu.GridColumnSpacing >= 2.0f,
            $"Column spacing {perCpu.GridColumnSpacing} should be >= 2.0 for label clearance");
        // Row spacing should be at least 2.5 to fit row header labels
        Assert.True(perCpu.GridRowSpacing >= 2.5f,
            $"Row spacing {perCpu.GridRowSpacing} should be >= 2.5 for label clearance");
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
        // Without RowHeaderReservation, stride = bezelWidth + ZoneGap(3.0).
        // With it, stride = bezelWidth + RowHeaderReservation(2.0) + ZoneGap(3.0).
        // The extra 2.0 is the distinguishing assertion.
        const float ZoneGap = 3.0f;
        const float RowHeaderReservation = 2.0f;
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2, nics: 2));
        var background = layout.Zones
            .Where(z => z.GridColumns.HasValue)
            .OrderBy(z => z.Position.X)
            .ToList();

        for (var i = 0; i < background.Count - 1; i++)
        {
            var left = background[i];
            var right = background[i + 1];
            var stride = right.Position.X - left.Position.X;
            // Stride must include the bezel, the row header reservation, and the inter-group gap.
            // Without RowHeaderReservation this would only be bezelWidth + ZoneGap.
            Assert.True(stride >= left.GroundWidth + RowHeaderReservation + ZoneGap,
                $"Zone '{left.Name}' → '{right.Name}': stride {stride:F2} < {left.GroundWidth + RowHeaderReservation + ZoneGap:F2} (bezel + reservation + gap)");
        }
    }
}
