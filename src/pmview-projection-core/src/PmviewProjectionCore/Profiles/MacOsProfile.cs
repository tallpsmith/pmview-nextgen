using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

/// <summary>
/// macOS host profile. Shares CPU, Load, Disk, Network, and per-instance
/// zones with Linux. Memory zone uses Darwin-specific metrics.
/// </summary>
public static class MacOsProfile
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
            new("mem.util.wired",      ShapeType.Bar, "Wired",      SharedZones.Red,   0f, 0f, 0.2f, 5.0f),
            new("mem.util.active",     ShapeType.Bar, "Active",     SharedZones.Green, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.inactive",   ShapeType.Bar, "Inactive",   SharedZones.Amber, 0f, 0f, 0.2f, 5.0f),
            new("mem.util.compressed", ShapeType.Bar, "Compressed", SharedZones.Blue,  0f, 0f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);
}
