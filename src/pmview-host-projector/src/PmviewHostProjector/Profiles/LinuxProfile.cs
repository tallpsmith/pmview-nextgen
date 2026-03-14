using PmviewHostProjector.Models;

namespace PmviewHostProjector.Profiles;

public static class LinuxProfile
{
    private static readonly RgbColour Orange    = RgbColour.FromHex("#f97316");
    private static readonly RgbColour Indigo    = RgbColour.FromHex("#6366f1");
    private static readonly RgbColour Green     = RgbColour.FromHex("#22c55e");
    private static readonly RgbColour Amber     = RgbColour.FromHex("#f59e0b");
    private static readonly RgbColour DarkGreen = RgbColour.FromHex("#16a34a");
    private static readonly RgbColour Blue      = RgbColour.FromHex("#3b82f6");
    private static readonly RgbColour Rose      = RgbColour.FromHex("#f43f5e");
    private static readonly RgbColour Red       = RgbColour.FromHex("#ef4444");

    public static IReadOnlyList<ZoneDefinition> GetZones() =>
    [
        SystemZone(),
        CpuSplitComparisonZone(),
        DiskTotalsZone(),
        NetworkInAggregateZone(),
        NetworkOutAggregateZone(),
        PerCpuZone(),
        PerDiskZone(),
        NetworkInZone(),
        NetworkOutZone(),
    ];

    // System zone: two column panels (CPU left, Memory right) with Load row below.
    //
    //   [USR ]        [USED   ]
    //   [SYS ]        [CACHED ]
    //   [NICE]        [BUFFERS]
    //   [1m  ][5m    ][15m    ]
    //
    //  X=0 (CPU col)  X=3.0 (Mem col)   Z=0,1.2,2.4 (col rows),  Z=4.0 (load row)
    private const float CpuX    = 0f;
    private const float MemX    = 3.0f;
    private const float LoadZ   = 4.0f;
    private const float ZStep   = 1.2f;
    private const float LoadMid = (CpuX + MemX) / 2f;

    private static ZoneDefinition SystemZone() => new(
        Name: "System",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        RotateYNinetyDeg: true,
        Metrics:
        [
            // CPU group — will be emitted as a PlacedStack via StackGroups below.
            new("kernel.all.cpu.user", ShapeType.Bar, "User",    Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 0), LabelPlacement: LabelPlacement.Left),
            new("kernel.all.cpu.sys",  ShapeType.Bar, "Sys",     Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 0), LabelPlacement: LabelPlacement.Left),
            new("kernel.all.cpu.nice", ShapeType.Bar, "Nice",    Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 0), LabelPlacement: LabelPlacement.Left),
            new("kernel.all.load", ShapeType.Bar, "1m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "1 minute",  Position: new(CpuX,    0, LoadZ)),
            new("kernel.all.load", ShapeType.Bar, "5m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "5 minute",  Position: new(LoadMid, 0, LoadZ)),
            new("kernel.all.load", ShapeType.Bar, "15m", Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "15 minute", Position: new(MemX,    0, LoadZ)),
            new("mem.util.used",   ShapeType.Bar, "Used",    Green, 0f, 0f, 0.2f, 5.0f,
                Position: new(MemX, 0, 0 * ZStep), LabelPlacement: LabelPlacement.Right),
            new("mem.util.cached", ShapeType.Bar, "Cached",  Green, 0f, 0f, 0.2f, 5.0f,
                Position: new(MemX, 0, 1 * ZStep), LabelPlacement: LabelPlacement.Right),
            new("mem.util.bufmem", ShapeType.Bar, "Buffers", Green, 0f, 0f, 0.2f, 5.0f,
                Position: new(MemX, 0, 2 * ZStep), LabelPlacement: LabelPlacement.Right),
        ],
        InstanceMetricSource: null,
        StackGroups:
        [
            new MetricStackGroupDefinition("CpuStack", StackMode.Proportional, ["User", "Sys", "Nice"]),
        ]);

    // Side-by-side CPU bars — kept alongside SystemZone for visual comparison in Godot.
    private static ZoneDefinition CpuSplitComparisonZone() => new(
        Name: "Cpu-Split",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        RotateYNinetyDeg: true,
        Metrics:
        [
            new("kernel.all.cpu.user", ShapeType.Bar, "User", Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 0 * ZStep), LabelPlacement: LabelPlacement.Left),
            new("kernel.all.cpu.sys",  ShapeType.Bar, "Sys",  Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 1 * ZStep), LabelPlacement: LabelPlacement.Left),
            new("kernel.all.cpu.nice", ShapeType.Bar, "Nice", Orange, 0f, 100f, 0.2f, 5.0f,
                Position: new(CpuX, 0, 2 * ZStep), LabelPlacement: LabelPlacement.Left),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition DiskTotalsZone() => new(
        Name: "Disk",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("disk.all.read_bytes",  ShapeType.Cylinder, "Read",  Amber, 0f, 500_000_000f, 0.2f, 5.0f),
            new("disk.all.write_bytes", ShapeType.Cylinder, "Write", Amber, 0f, 500_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkInAggregateZone() => new(
        Name: "Net-In",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.in.bytes",   ShapeType.Bar, "Bytes", Blue, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.in.packets", ShapeType.Bar, "Pkts",  Blue, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkOutAggregateZone() => new(
        Name: "Net-Out",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.out.bytes",   ShapeType.Bar, "Bytes", Rose, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.out.packets", ShapeType.Bar, "Pkts",  Rose, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition PerCpuZone() => new(
        Name: "Per-CPU",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("kernel.percpu.cpu.user", ShapeType.Bar, "User", Orange, 0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.sys",  ShapeType.Bar, "Sys",  Orange, 0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.nice", ShapeType.Bar, "Nice", Orange, 0f, 100f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "kernel.percpu.cpu.user");

    private static ZoneDefinition PerDiskZone() => new(
        Name: "Per-Disk",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("disk.dev.read_bytes",  ShapeType.Cylinder, "Read",  DarkGreen, 0f, 500_000_000f, 0.2f, 5.0f),
            new("disk.dev.write_bytes", ShapeType.Cylinder, "Write", DarkGreen, 0f, 500_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "disk.dev.read_bytes");

    private static ZoneDefinition NetworkInZone() => new(
        Name: "Network In",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("network.interface.in.bytes",   ShapeType.Bar, "Bytes",  Blue, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.interface.in.packets", ShapeType.Bar, "Pkts",   Blue, 0f, 100_000f,     0.2f, 5.0f),
            new("network.interface.in.errors",  ShapeType.Bar, "Errors", Red,  0f, 100f,         0.2f, 5.0f),
        ],
        InstanceMetricSource: "network.interface.in.bytes");

    private static ZoneDefinition NetworkOutZone() => new(
        Name: "Network Out",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("network.interface.out.bytes",   ShapeType.Bar, "Bytes",  Rose, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.interface.out.packets", ShapeType.Bar, "Pkts",   Rose, 0f, 100_000f,     0.2f, 5.0f),
            new("network.interface.out.errors",  ShapeType.Bar, "Errors", Red,  0f, 100f,         0.2f, 5.0f),
        ],
        InstanceMetricSource: "network.interface.out.bytes");
}
