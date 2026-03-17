using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Profiles;

/// <summary>
/// macOS host profile. Shares CPU, Load, Disk, and per-instance zones with
/// Linux. Memory zone uses Darwin-specific metrics. Network aggregate zones
/// are ghost placeholders (network.all.* absent on Darwin PMDA).
/// See: https://github.com/performancecopilot/pcp/issues/2532
/// </summary>
public static class MacOsProfile
{
    public static IReadOnlyList<ZoneDefinition> GetZones() =>
    [
        SharedZones.CpuZone(),
        SharedZones.LoadZone(),
        MemoryZone(),
        SharedZones.DiskTotalsZone(),
        NetworkInAggregateGhostZone(),
        NetworkOutAggregateGhostZone(),
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

    private static ZoneDefinition NetworkInAggregateGhostZone() => new(
        Name: "Net-In",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.in.bytes",   ShapeType.Bar, "Bytes", SharedZones.Blue, 0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true),
            new("network.all.in.packets", ShapeType.Bar, "Pkts",  SharedZones.Blue, 0f, 100_000f,     0.2f, 5.0f,
                IsPlaceholder: true),
        ],
        InstanceMetricSource: null);

    private static ZoneDefinition NetworkOutAggregateGhostZone() => new(
        Name: "Net-Out",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("network.all.out.bytes",   ShapeType.Bar, "Bytes", SharedZones.Rose, 0f, 125_000_000f, 0.2f, 5.0f,
                IsPlaceholder: true),
            new("network.all.out.packets", ShapeType.Bar, "Pkts",  SharedZones.Rose, 0f, 100_000f,     0.2f, 5.0f,
                IsPlaceholder: true),
        ],
        InstanceMetricSource: null);
}
