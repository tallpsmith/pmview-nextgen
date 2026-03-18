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
        SharedZones.NetworkInAggregateZone(),
        SharedZones.NetworkOutAggregateZone(),
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
}
