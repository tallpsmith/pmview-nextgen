using System.Linq;
using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Profiles;

/// <summary>
/// Zone definitions and colour palette shared across platform profiles.
/// Both LinuxProfile and MacOsProfile delegate here for zones with
/// identical PCP metric names on both platforms.
/// </summary>
internal static class SharedZones
{
    internal static readonly RgbColour Orange    = RgbColour.FromHex("#f97316");
    internal static readonly RgbColour Indigo    = RgbColour.FromHex("#6366f1");
    internal static readonly RgbColour Green     = RgbColour.FromHex("#22c55e");
    internal static readonly RgbColour Amber     = RgbColour.FromHex("#f59e0b");
    internal static readonly RgbColour DarkGreen = RgbColour.FromHex("#16a34a");
    internal static readonly RgbColour Blue      = RgbColour.FromHex("#3b82f6");
    internal static readonly RgbColour Rose      = RgbColour.FromHex("#f43f5e");
    internal static readonly RgbColour Red       = RgbColour.FromHex("#ef4444");
    internal static readonly RgbColour Cyan      = RgbColour.FromHex("#22d3ee");

    internal static ZoneDefinition CpuZone() => new(
        Name: "CPU",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("kernel.all.cpu.sys",  ShapeType.Bar, "Sys",  Red,   0f, 100f, 0.2f, 5.0f),
            new("kernel.all.cpu.user", ShapeType.Bar, "User", Green, 0f, 100f, 0.2f, 5.0f),
            new("kernel.all.cpu.nice", ShapeType.Bar, "Nice", Cyan,  0f, 100f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null,
        StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]);

    internal static ZoneDefinition LoadZone() => new(
        Name: "Load",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("kernel.all.load", ShapeType.Bar, "1m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "1 minute"),
            new("kernel.all.load", ShapeType.Bar, "5m",  Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "5 minute"),
            new("kernel.all.load", ShapeType.Bar, "15m", Indigo, 0f, 10f, 0.2f, 5.0f,
                InstanceName: "15 minute"),
        ],
        InstanceMetricSource: null);

    internal static ZoneDefinition DiskTotalsZone() => new(
        Name: "Disk",
        Row: ZoneRow.Foreground,
        Type: ZoneType.Aggregate,
        Metrics:
        [
            new("disk.all.read_bytes",  ShapeType.Cylinder, "Read",  Amber, 0f, 550_000_000f, 0.2f, 5.0f),
            new("disk.all.write_bytes", ShapeType.Cylinder, "Write", Amber, 0f, 550_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: null);

    internal static ZoneDefinition PerCpuZone() => new(
        Name: "Per-CPU",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("kernel.percpu.cpu.sys",  ShapeType.Bar, "Sys",  Red,   0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.user", ShapeType.Bar, "User", Green, 0f, 100f, 0.2f, 5.0f),
            new("kernel.percpu.cpu.nice", ShapeType.Bar, "Nice", Cyan,  0f, 100f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "kernel.percpu.cpu.user",
        StackGroups: [new MetricStackGroupDefinition("CPU", StackMode.Proportional, ["Sys", "User", "Nice"])]);

    internal static ZoneDefinition PerDiskZone() => new(
        Name: "Per-Disk",
        Row: ZoneRow.Background,
        Type: ZoneType.PerInstance,
        Metrics:
        [
            new("disk.dev.read_bytes",  ShapeType.Cylinder, "Read",  DarkGreen, 0f, 550_000_000f, 0.2f, 5.0f),
            new("disk.dev.write_bytes", ShapeType.Cylinder, "Write", DarkGreen, 0f, 550_000_000f, 0.2f, 5.0f),
        ],
        InstanceMetricSource: "disk.dev.read_bytes");

    internal static ZoneDefinition NetworkInZone() => new(
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

    internal static ZoneDefinition NetworkOutZone() => new(
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

    private static readonly ZoneDefinition[] AllZones =
    [
        CpuZone(), LoadZone(), DiskTotalsZone(),
        PerCpuZone(), PerDiskZone(),
        NetworkInZone(), NetworkOutZone(),
    ];

    internal static string? ResolveZone(string metricName)
    {
        foreach (var zone in AllZones)
            foreach (var metric in zone.Metrics)
                if (metric.MetricName == metricName)
                    return zone.Name;
        return null;
    }

    internal static string[] GetMetricNames(string zoneName)
    {
        foreach (var zone in AllZones)
            if (zone.Name == zoneName)
                return zone.Metrics.Select(m => m.MetricName).ToArray();
        return [];
    }
}
