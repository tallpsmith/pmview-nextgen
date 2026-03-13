using PmviewHostProjector.Models;

namespace PmviewHostProjector.Layout;

/// <summary>
/// Pure geometry — no I/O, no Godot dependencies.
/// Takes zone definitions and host topology, returns a fully positioned SceneLayout.
/// </summary>
public static class LayoutCalculator
{
    private const float ShapeSpacing       = 1.5f;
    private const float ZoneGap            = 3.0f;
    private const float BackgroundZOffset  = -8.0f;
    private const float GridColumnSpacing  = 1.5f;
    private const float GridRowSpacing     = 2.0f;
    private const long  FallbackMemoryBytes = 16_000_000_000L;
    private const float GroundPadding      = 0.6f;

    public static SceneLayout Calculate(IReadOnlyList<ZoneDefinition> zones, HostTopology topology)
    {
        var foreground = BuildRow(zones.Where(z => z.Row == ZoneRow.Foreground).ToList(), topology, zOffset: 0f);
        var background = BuildRow(zones.Where(z => z.Row == ZoneRow.Background).ToList(), topology, zOffset: BackgroundZOffset);
        return new SceneLayout(topology.Hostname, [.. foreground, .. background]);
    }

    private static IReadOnlyList<PlacedZone> BuildRow(
        IList<ZoneDefinition> rowZones,
        HostTopology topology,
        float zOffset)
    {
        var placedZones = rowZones.Select(z => PlaceZone(z, topology)).ToList();
        return CenterRowOnXZero(placedZones, zOffset);
    }

    private static PlacedZone PlaceZone(ZoneDefinition zone, HostTopology topology)
    {
        var shapes = zone.Row == ZoneRow.Foreground
            ? BuildForegroundShapes(zone, topology)
            : BuildBackgroundShapes(zone, topology);

        var (groundWidth, groundDepth) = ComputeGroundExtent(zone, topology, shapes);

        // Position will be finalised by CenterRowOnXZero; use Zero for now.
        return new PlacedZone(
            Name:              zone.Name,
            ZoneLabel:         zone.Name,
            Position:          Vec3.Zero,
            GridColumns:       zone.Row == ZoneRow.Background ? zone.Metrics.Count : null,
            GridColumnSpacing: zone.Row == ZoneRow.Background ? GridColumnSpacing : null,
            GridRowSpacing:    zone.Row == ZoneRow.Background ? GridRowSpacing : null,
            Shapes:            shapes,
            GroundWidth:       groundWidth,
            GroundDepth:       groundDepth);
    }

    private static (float Width, float Depth) ComputeGroundExtent(
        ZoneDefinition zone,
        HostTopology topology,
        IReadOnlyList<PlacedShape> shapes)
    {
        if (shapes.Count == 0) return (0f, 0f);

        if (zone.Row == ZoneRow.Foreground)
        {
            var maxX  = shapes.Max(s => s.LocalPosition.X);
            var width = maxX + 0.8f + GroundPadding * 2;
            var depth = 0.8f + GroundPadding * 2;
            return (width, depth);
        }
        else
        {
            var cols  = zone.Metrics.Count;
            var rows  = ResolveInstances(zone, topology).Count;
            var width = (cols - 1) * GridColumnSpacing + 0.8f + GroundPadding * 2;
            var depth = (rows - 1) * GridRowSpacing    + 0.8f + GroundPadding * 2;
            return (width, depth);
        }
    }

    private static IReadOnlyList<PlacedShape> BuildForegroundShapes(ZoneDefinition zone, HostTopology topology)
    {
        var shapes = new List<PlacedShape>();
        for (int i = 0; i < zone.Metrics.Count; i++)
        {
            var metric = zone.Metrics[i];
            var localX = i * ShapeSpacing;
            shapes.Add(new PlacedShape(
                NodeName:       SanitiseNodeName($"{zone.Name}_{metric.Label}"),
                Shape:          metric.Shape,
                LocalPosition:  new Vec3(localX, 0f, 0f),
                MetricName:     metric.MetricName,
                InstanceName:   metric.InstanceName,
                DisplayLabel:   metric.Label,
                Colour:         metric.DefaultColour,
                SourceRangeMin: metric.SourceRangeMin,
                SourceRangeMax: ResolveSourceRangeMax(metric, topology),
                TargetRangeMin: metric.TargetRangeMin,
                TargetRangeMax: metric.TargetRangeMax));
        }
        return shapes;
    }

    private static IReadOnlyList<PlacedShape> BuildBackgroundShapes(ZoneDefinition zone, HostTopology topology)
    {
        var instances = ResolveInstances(zone, topology);
        var shapes = new List<PlacedShape>();

        foreach (var instance in instances)
        {
            var shortName  = ShortenInstanceName(instance);
            var safeName   = SanitiseNodeName(shortName);

            foreach (var metric in zone.Metrics)
            {
                shapes.Add(new PlacedShape(
                    NodeName:       SanitiseNodeName($"{zone.Name}_{safeName}_{metric.Label}"),
                    Shape:          metric.Shape,
                    LocalPosition:  Vec3.Zero,
                    MetricName:     metric.MetricName,
                    InstanceName:   instance,
                    DisplayLabel:   shortName,
                    Colour:         metric.DefaultColour,
                    SourceRangeMin: metric.SourceRangeMin,
                    SourceRangeMax: ResolveSourceRangeMax(metric, topology),
                    TargetRangeMin: metric.TargetRangeMin,
                    TargetRangeMax: metric.TargetRangeMax));
            }
        }
        return shapes;
    }

    private static IReadOnlyList<PlacedZone> CenterRowOnXZero(
        IList<PlacedZone> zones,
        float zOffset)
    {
        // Compute width of each zone (span of shape local X positions, or 0 for empty).
        var zoneWidths = zones.Select(ZoneWidth).ToList();

        // Total span: sum of zone widths + gaps between zones.
        var totalWidth = zoneWidths.Sum() + ZoneGap * (zones.Count - 1);
        var startX = -totalWidth / 2f;

        var result = new List<PlacedZone>(zones.Count);
        var cursor = startX;

        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            result.Add(zone with { Position = new Vec3(cursor, 0f, zOffset) });
            cursor += zoneWidths[i] + ZoneGap;
        }

        return result;
    }

    private static float ZoneWidth(PlacedZone zone)
    {
        if (zone.Shapes.Count == 0) return 0f;
        var maxLocalX = zone.Shapes.Max(s => s.LocalPosition.X);
        return maxLocalX; // min is always 0; width from 0 to max
    }

    private static float ResolveSourceRangeMax(MetricShapeMapping metric, HostTopology topology)
    {
        if (metric.SourceRangeMax != 0f) return metric.SourceRangeMax;
        if (metric.MetricName.StartsWith("mem.", StringComparison.Ordinal))
            return topology.PhysicalMemoryBytes ?? FallbackMemoryBytes;
        return metric.SourceRangeMax;
    }

    private static IReadOnlyList<string> ResolveInstances(ZoneDefinition zone, HostTopology topology)
    {
        if (zone.InstanceMetricSource is null) return [];
        var src = zone.InstanceMetricSource;

        if (src.StartsWith("kernel.percpu.cpu.", StringComparison.Ordinal))
            return topology.CpuInstances;
        if (src.StartsWith("disk.dev.", StringComparison.Ordinal))
            return topology.DiskDevices;
        if (src.StartsWith("network.interface.", StringComparison.Ordinal))
            return topology.NetworkInterfaces;

        return [];
    }

    private static string ShortenInstanceName(string name)
    {
        // Strip path prefix: "/dev/sda1" → "sda1"
        var lastSlash = name.LastIndexOf('/');
        return lastSlash >= 0 ? name[(lastSlash + 1)..] : name;
    }

    private static string SanitiseNodeName(string name) =>
        name.Replace(' ', '_').Replace('-', '_').Replace('/', '_')
            .Replace(':', '_').Replace('@', '_').Replace('%', '_').Replace('.', '_');
}
