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
        Assert.Contains("CPU", foregroundNames);
        Assert.Contains("Load", foregroundNames);
        Assert.Contains("Memory", foregroundNames);
        Assert.Contains("Disk", foregroundNames);
        Assert.Contains("Net-In", foregroundNames);
        Assert.Contains("Net-Out", foregroundNames);
    }

    [Fact]
    public void Calculate_CpuZone_HasOneStack_NoStandaloneShapes()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        Assert.Empty(cpu.Shapes);
        Assert.Single(cpu.Items.OfType<PlacedStack>());
    }

    [Fact]
    public void Calculate_CpuZone_HasOneStack_WithThreeMembers()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        var stacks = cpu.Items.OfType<PlacedStack>().ToList();
        Assert.Single(stacks);
        Assert.Equal(3, stacks[0].Members.Count);
    }

    [Fact]
    public void Calculate_CpuZone_StackMembers_HaveCorrectColours()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        var stack = cpu.Items.OfType<PlacedStack>().Single();
        var members = stack.Members;

        // Bottom-to-top: Sys (red), User (green), Nice (cyan)
        Assert.True(members[0].Colour.R > 0.9f, "Sys should be red");
        Assert.True(members[1].Colour.G > 0.5f, "User should be green");
        Assert.True(members[2].Colour.G > 0.5f && members[2].Colour.B > 0.5f, "Nice should be cyan");
    }

    [Fact]
    public void Calculate_LoadZone_HasThreeShapes_WithInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var load = layout.Zones.Single(z => z.Name == "Load");
        var loadShapes = load.Shapes.Where(s => s.InstanceName != null).ToList();
        Assert.Equal(3, loadShapes.Count);
        var instanceNames = loadShapes.Select(s => s.InstanceName).ToList();
        Assert.Equal(new[] { "1 minute", "5 minute", "15 minute" }, instanceNames);
    }

    [Fact]
    public void Calculate_MemoryZone_SourceRangeMaxFromPhysmem()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var memory = layout.Zones.Single(z => z.Name == "Memory");
        var memShapes = memory.Shapes.Where(s => s.MetricName.StartsWith("mem.")).ToList();
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
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        Assert.Empty(cpu.InstanceLabels ?? []);
    }

    [Fact]
    public void Calculate_CpuZone_MetricLabels_CollapsedToStackGroupName()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        // All 3 metrics are in one stack group "CPU" → 1 visual column label
        Assert.Equal(new[] { "CPU" }, cpu.MetricLabels);
    }

    [Fact]
    public void Calculate_CpuZone_GroundWidthReflectsOneStackColumn()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var cpu = layout.Zones.Single(z => z.Name == "CPU");
        // CPU has 3 metrics in 1 stack group = 1 visual column
        // width = (1-1)*1.2 + 0.8 + 1.2 = 2.0
        Assert.Equal(2.0f, cpu.GroundWidth, 0.01f);
    }

    [Fact]
    public void Calculate_StackedForegroundZone_GroundWidthReflectsVisualColumns()
    {
        // 3 metrics in 1 stack group = 1 visual column, not 3.
        var zone = new ZoneDefinition(
            Name: "Test", Row: ZoneRow.Foreground, Type: ZoneType.Aggregate,
            Metrics:
            [
                new("m.a", ShapeType.Bar, "A", new RgbColour(1, 0, 0), 0f, 100f, 0.2f, 5.0f),
                new("m.b", ShapeType.Bar, "B", new RgbColour(0, 1, 0), 0f, 100f, 0.2f, 5.0f),
                new("m.c", ShapeType.Bar, "C", new RgbColour(0, 0, 1), 0f, 100f, 0.2f, 5.0f),
            ],
            StackGroups: [new MetricStackGroupDefinition("Stack", StackMode.Proportional, ["A", "B", "C"])]);

        var layout = LayoutCalculator.Calculate([zone], MakeTopology());
        var placed = layout.Zones.Single(z => z.Name == "Test");

        // 1 visual column: width = (1-1)*spacing + 0.8 + padding*2 = 2.0
        Assert.Equal(2.0f, placed.GroundWidth, 0.01f);
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
    public void Calculate_PerCpuZone_MetricLabelsCountEqualsVisualColumns()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // 3 metrics in 1 stack group = 1 visual column label
        Assert.Equal(1, perCpu.MetricLabels?.Count);
    }

    [Fact]
    public void Calculate_PerCpuZone_StackCountEqualsInstances()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // With stacking: 4 stacks (1 per CPU), 0 standalone shapes
        Assert.Empty(perCpu.Shapes);
        Assert.Equal(4, perCpu.Items.OfType<PlacedStack>().Count());
    }

    [Fact]
    public void Calculate_BackgroundShapes_HaveInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // Per-CPU is now stacked — instance names are on the stack members
        var names = perCpu.Items.OfType<PlacedStack>()
            .SelectMany(s => s.Members)
            .Select(m => m.InstanceName)
            .Distinct().ToList();
        Assert.Contains("cpu0", names);
        Assert.Contains("cpu1", names);
    }

    [Fact]
    public void Calculate_BackgroundItems_LocalPositionsAreZero()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology());
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.All(perCpu.Items, item => Assert.Equal(Vec3.Zero, item.LocalPosition));
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
    public void Calculate_PerCpuZone_MetricLabels_CollapsedToStackGroupName()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        // All 3 metrics are in one stack group "CPU" → 1 visual column label
        Assert.Equal(new[] { "CPU" }, perCpu.MetricLabels);
    }

    [Fact]
    public void Calculate_PerCpuZone_HasInstanceLabels()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        Assert.Equal(new[] { "cpu0", "cpu1" }, perCpu.InstanceLabels);
    }

    [Fact]
    public void Calculate_PerCpuZone_WithStacking_EmitsOneStackPerInstance()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 4));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        var stacks = perCpu.Items.OfType<PlacedStack>().ToList();
        Assert.Equal(4, stacks.Count);
        Assert.All(stacks, s => Assert.Equal(3, s.Members.Count));
    }

    [Fact]
    public void Calculate_PerCpuZone_StackMembers_HaveInstanceNames()
    {
        var layout = LayoutCalculator.Calculate(LinuxZones, MakeTopology(cpus: 2));
        var perCpu = layout.Zones.Single(z => z.Name == "Per-CPU");
        var stacks = perCpu.Items.OfType<PlacedStack>().ToList();
        Assert.Equal(2, stacks.Count);

        // Each stack's members should all have the same instance name
        foreach (var stack in stacks)
        {
            var instanceNames = stack.Members.Select(m => m.InstanceName).Distinct().ToList();
            Assert.Single(instanceNames);
        }

        var allInstances = stacks.SelectMany(s => s.Members).Select(m => m.InstanceName).Distinct().ToList();
        Assert.Contains("cpu0", allInstances);
        Assert.Contains("cpu1", allInstances);
    }

    [Fact]
    public void Calculate_StackedBackgroundZone_GroundWidthReflectsVisualColumns()
    {
        // 3 metrics in 1 stack group per instance = 1 visual column, not 3.
        var zone = new ZoneDefinition(
            Name: "TestBg", Row: ZoneRow.Background, Type: ZoneType.PerInstance,
            Metrics:
            [
                new("kernel.percpu.cpu.sys",  ShapeType.Bar, "Sys",  new RgbColour(1, 0, 0), 0f, 100f, 0.2f, 5.0f),
                new("kernel.percpu.cpu.user", ShapeType.Bar, "User", new RgbColour(0, 1, 0), 0f, 100f, 0.2f, 5.0f),
                new("kernel.percpu.cpu.nice", ShapeType.Bar, "Nice", new RgbColour(0, 0, 1), 0f, 100f, 0.2f, 5.0f),
            ],
            InstanceMetricSource: "kernel.percpu.cpu.user",
            StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]);

        var layout = LayoutCalculator.Calculate([zone], MakeTopology(cpus: 2));
        var placed = layout.Zones.Single(z => z.Name == "TestBg");

        // 1 visual column: width = (1-1)*ColumnSpacing + 0.8 + padding*2 = 2.0
        Assert.Equal(2.0f, placed.GroundWidth, 0.01f);
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
