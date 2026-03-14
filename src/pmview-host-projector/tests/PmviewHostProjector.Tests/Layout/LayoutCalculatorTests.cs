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
        // Row visual centre (based on GroundWidth footprint) should land at X=0.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => z.GridColumns == null).ToList();
        var leftEdge  = foreground.Min(z => z.Position.X);
        var rightEdge = foreground.Max(z => z.Position.X + z.GroundWidth);
        var centre    = (leftEdge + rightEdge) / 2f;
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
    public void Calculate_ForegroundShapes_HaveDistinctLocalPositions()
    {
        // Shapes in multi-column zones share X (one per column) or Z (one per row),
        // but no two shapes in the same zone should occupy the same (X, Z) grid cell.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        foreach (var zone in layout.Zones.Where(z => z.GridColumns == null))
        {
            var positions = zone.Shapes.Select(s => (s.LocalPosition.X, s.LocalPosition.Z)).Distinct().ToList();
            Assert.Equal(zone.Shapes.Count, positions.Count);
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
    public void Calculate_SystemZone_HasSevenItems_OneStackSixShapes()
    {
        // CPU migrated to PlacedStack: 1 stack + Load(3) + Memory(3) = 7 Items total.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Equal(7, system.Items.Count);
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
        // Two-column layout: width spans CPU(X=0) + Memory(X=3) + bar width + padding
        Assert.True(system.GroundWidth > 4f,
            $"GroundWidth {system.GroundWidth} should be > 4 for a 2-column zone");
        // Load row below columns: depth spans 3 column items + gap + load row + padding
        Assert.True(system.GroundDepth > 4f,
            $"GroundDepth {system.GroundDepth} should be > 4 for a zone with a bottom load row");
    }

    [Fact]
    public void Calculate_SystemZone_CpuShapes_AreInsideStack_NotDirectZoneShapes()
    {
        // After migration CPU metrics live inside the PlacedStack's Members, not in zone.Shapes.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var directCpuShapes = system.Shapes.Where(s => s.MetricName.StartsWith("kernel.all.cpu.")).ToList();
        Assert.Empty(directCpuShapes);

        var stackMembers = system.Items.OfType<PlacedStack>().Single().Members;
        Assert.Equal(3, stackMembers.Count);
    }

    [Fact]
    public void Calculate_SystemZone_MemoryShapes_InSingleColumn_RightOfCpuStack()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var cpuStack = system.Items.OfType<PlacedStack>().Single();
        var memShapes = system.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();

        Assert.Equal(3, memShapes.Count);
        Assert.Single(memShapes.Select(s => s.LocalPosition.X).Distinct()); // same column
        Assert.True(memShapes.First().LocalPosition.X > cpuStack.LocalPosition.X); // right of CPU stack
    }

    [Fact]
    public void Calculate_SystemZone_LoadShapes_InRowAtDeepestZ_BeyondMemoryColumn()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var memShapes  = system.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();
        var loadShapes = system.Shapes.Where(s => s.MetricName == "kernel.all.load").ToList();

        Assert.Equal(3, loadShapes.Count);
        Assert.Single(loadShapes.Select(s => s.LocalPosition.Z).Distinct()); // all at same Z (a row)
        Assert.Equal(3, loadShapes.Select(s => s.LocalPosition.X).Distinct().Count()); // distinct X
        var maxMemZ = memShapes.Max(s => s.LocalPosition.Z);
        Assert.All(loadShapes, s => Assert.True(s.LocalPosition.Z > maxMemZ)); // deeper than column items
    }

    [Fact]
    public void Calculate_SystemZone_GroundDepth_SpansLoadRow()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var maxShapeZ = system.Shapes.Max(s => s.LocalPosition.Z);
        Assert.True(system.GroundDepth > maxShapeZ, // must reach past the deepest shape
            $"GroundDepth {system.GroundDepth} should exceed deepest shape Z {maxShapeZ}");
    }

    [Fact]
    public void Calculate_SystemZone_CpuStackMembers_HaveLeftLabelPlacement()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var stackMembers = system.Items.OfType<PlacedStack>().Single().Members;
        Assert.All(stackMembers, m => Assert.Equal(LabelPlacement.Left, m.LabelPlacement));
    }

    [Fact]
    public void Calculate_SystemZone_MemoryShapes_HaveRightLabelPlacement()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var memShapes = system.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();
        Assert.All(memShapes, s => Assert.Equal(LabelPlacement.Right, s.LabelPlacement));
    }

    [Fact]
    public void Calculate_SystemZone_LoadShapes_HaveFrontLabelPlacement()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var loadShapes = system.Shapes.Where(s => s.MetricName == "kernel.all.load").ToList();
        Assert.All(loadShapes, s => Assert.Equal(LabelPlacement.Front, s.LabelPlacement));
    }

    [Fact]
    public void Calculate_ForegroundZoneOrder_IncludesStackedAndSplitCpu()
    {
        // Foreground zones include Cpu-Split comparison zone alongside the stacked System.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foregroundNames = layout.Zones
            .Where(z => z.GridColumns == null)
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
    public void Calculate_NetInAggregateZone_IsForeground_HasTwoShapes()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        // Net-In foreground zone (not the per-instance background zone "Network In")
        var netIn = layout.Zones.Single(z => z.Name == "Net-In" && z.GridColumns == null);
        Assert.Equal(0f, netIn.Position.Z);
        Assert.Equal(2, netIn.Shapes.Count);
    }

    [Fact]
    public void Calculate_ForegroundRow_CenteredOnGroundWidthFootprint()
    {
        // ZoneWidth must use GroundWidth (visual footprint including shape width + padding),
        // not just the max shape X-origin. The row's visual centre should land at X=0.
        // We verify by computing centre as midpoint of [leftmost zone start, rightmost zone end],
        // where zone end = zone.Position.X + zone.GroundWidth.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones.Where(z => z.GridColumns == null).ToList();

        var leftEdge  = foreground.Min(z => z.Position.X);
        var rightEdge = foreground.Max(z => z.Position.X + z.GroundWidth);
        var centre    = (leftEdge + rightEdge) / 2f;

        Assert.True(Math.Abs(centre) < 0.1f,
            $"Foreground visual centre {centre:F4} should be ~0 when using GroundWidth footprint");
    }

    [Fact]
    public void Calculate_ForegroundZones_InterZoneGapIsAtMostTwoPointFive()
    {
        // ZoneGap reduced from 3.0 to 2.0. The gap (empty space) between adjacent
        // foreground zones should now be <= 2.5. With old ZoneGap=3.0 it was ~3.0.
        // Gap is measured using the zone's visual footprint (GroundDepth for Ry(90°) zones,
        // GroundWidth otherwise).
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var foreground = layout.Zones
            .Where(z => z.GridColumns == null)
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
    public void Calculate_SystemZone_HasRotateYNinetyDegEnabled()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.True(system.RotateYNinetyDeg);
    }

    // --- PlacedStack / stacked CPU tests ---

    [Fact]
    public void Calculate_SystemZone_CpuBars_AreASinglePlacedStack()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var stacks = system.Items.OfType<PlacedStack>().ToList();
        Assert.Single(stacks);
    }

    [Fact]
    public void Calculate_SystemZone_CpuStack_HasProportionalMode()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var stack = system.Items.OfType<PlacedStack>().Single();
        Assert.Equal(StackMode.Proportional, stack.Mode);
    }

    [Fact]
    public void Calculate_SystemZone_CpuStack_MembersAreUserSysNiceInOrder()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var stack = system.Items.OfType<PlacedStack>().Single();
        Assert.Equal(
            new[] { "kernel.all.cpu.user", "kernel.all.cpu.sys", "kernel.all.cpu.nice" },
            stack.Members.Select(m => m.MetricName));
    }

    [Fact]
    public void Calculate_SystemZone_CpuStack_AllMembersAtLocalPositionZero()
    {
        // TscnWriter emits all at Y=0; StackGroupNode owns Y at runtime.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        var stack = system.Items.OfType<PlacedStack>().Single();
        Assert.All(stack.Members, m => Assert.Equal(Vec3.Zero, m.LocalPosition));
    }

    [Fact]
    public void Calculate_SystemZone_HasSixShapesAndOneStack_AfterMigration()
    {
        // CPU group → 1 PlacedStack; remaining = 3 load + 3 memory = 6 PlacedShapes.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var system = layout.Zones.Single(z => z.Name == "System");
        Assert.Equal(6, system.Shapes.Count);
        Assert.Single(system.Items.OfType<PlacedStack>());
    }

    [Fact]
    public void Calculate_CpuSplitComparisonZone_HasThreeSeparateShapes()
    {
        // The comparison zone (side-by-side CPU for visual validation) should have 3 PlacedShapes.
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var splitZone = layout.Zones.Single(z => z.Name == "Cpu-Split");
        Assert.Equal(3, splitZone.Shapes.Count);
        Assert.Empty(splitZone.Items.OfType<PlacedStack>());
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
        // Without RowHeaderReservation, stride = bezelWidth + ZoneGap(2.0).
        // With it, stride = bezelWidth + RowHeaderReservation(2.0) + ZoneGap(2.0).
        // The extra 2.0 is the distinguishing assertion.
        const float ZoneGap = 2.0f;   // was 3.0f — matches LayoutCalculator constant
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
