using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

public static class LinuxProfile
{
    public static IReadOnlyList<ZoneDefinition> GetZones() =>
    [
        SharedZones.CpuZone(),
        SharedZones.LoadZone(),
        MemoryZone(),
        SharedZones.DiskTotalsZone(),
        NetworkInAggregateZone(),
        NetworkOutAggregateZone(),
        SharedZones.PerCpuZone(),
        SharedZones.PerDiskZone(),
        SharedZones.NetworkInZone(),
        SharedZones.NetworkOutZone(),
    ];

    private static ZoneDefinition MemoryZone() => new(
        Name: "Memory",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("mem.util.used",   ShapeType.Bar, "Used",    SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.cached", ShapeType.Bar, "Cached",  SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.bufmem", ShapeType.Bar, "Buffers", SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkInAggregateZone() => new(
        Name: "Net-In",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.in.bytes",   ShapeType.Bar, "Bytes", SharedZones.Blue, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.in.packets", ShapeType.Bar, "Pkts",  SharedZones.Blue, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkOutAggregateZone() => new(
        Name: "Net-Out",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.out.bytes",   ShapeType.Bar, "Bytes", SharedZones.Rose, 0f, 125_000_000f, 0.2f, 5.0f),
            new("network.all.out.packets", ShapeType.Bar, "Pkts",  SharedZones.Rose, 0f, 100_000f,     0.2f, 5.0f),
        ],
        InstanceMetricSource: null);
}
