using PmviewHostProjector.Models;

namespace PmviewHostProjector.Layout;

/// <summary>
/// Pure geometry — no I/O, no Godot dependencies.
/// Takes zone definitions and host topology, returns a fully positioned SceneLayout.
/// </summary>
public static class LayoutCalculator
{
    private const float ShapeSpacing       = 1.2f;   // was 1.5f — tighter bar grouping
    private const float ZoneGap            = 2.0f;   // was 3.0f — less dead air between zones
    private const float BackgroundZOffset  = -8.0f;
    private const float ColumnSpacing  = 2.0f;
    private const float RowSpacing     = 2.5f;
    private const long  FallbackMemoryBytes = 16_000_000_000L;
    private const float GroundPadding      = 0.6f;
    private const float RowHeaderReservation = 2.0f;

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
        var items = zone.Row == ZoneRow.Foreground
            ? BuildForegroundItems(zone, topology)
            : BuildBackgroundShapes(zone, topology);

        var (groundWidth, groundDepth) = ComputeGroundExtent(zone, topology, items);

        var metricLabels = zone.Row == ZoneRow.Background
            ? zone.Metrics.Select(m => m.Label).ToList()
            : (IReadOnlyList<string>)[];
        var instanceLabels = zone.Row == ZoneRow.Background
            ? ResolveInstances(zone, topology).Select(ShortenInstanceName).ToList()
            : (IReadOnlyList<string>)[];

        // Position will be finalised by CenterRowOnXZero; use Zero for now.
        return new PlacedZone(
            Name:             zone.Name,
            ZoneLabel:        zone.Name,
            Position:         Vec3.Zero,
            ColumnSpacing:    zone.Row == ZoneRow.Background ? ColumnSpacing : null,
            RowSpacing:       zone.Row == ZoneRow.Background ? RowSpacing : null,
            Items:            items,
            GroundWidth:      groundWidth,
            GroundDepth:      groundDepth,
            MetricLabels:     metricLabels,
            InstanceLabels:   instanceLabels,
            RotateYNinetyDeg: zone.RotateYNinetyDeg);
    }

    private static (float Width, float Depth) ComputeGroundExtent(
        ZoneDefinition zone,
        HostTopology topology,
        IReadOnlyList<PlacedItem> items)
    {
        if (items.Count == 0) return (0f, 0f);

        if (zone.Row == ZoneRow.Foreground)
        {
            var maxX  = items.Max(ItemFootprintMaxX);
            var maxZ  = items.Max(ItemFootprintMaxZ);
            var width = maxX + 0.8f + GroundPadding * 2;
            var depth = maxZ + 0.8f + GroundPadding * 2;
            return (width, depth);
        }
        else
        {
            var cols  = zone.Metrics.Count;
            var rows  = ResolveInstances(zone, topology).Count;
            var width = (cols - 1) * ColumnSpacing + 0.8f + GroundPadding * 2;
            var depth = (rows - 1) * RowSpacing    + 0.8f + GroundPadding * 2;
            return (width, depth);
        }
    }

    // Stacks occupy the X position of the group; shapes use their own X.
    private static float ItemFootprintMaxX(PlacedItem item) => item switch
    {
        PlacedStack s => s.LocalPosition.X,
        PlacedShape s => s.LocalPosition.X,
        _             => 0f
    };

    private static float ItemFootprintMaxZ(PlacedItem item) => item switch
    {
        PlacedStack s => s.LocalPosition.Z,
        PlacedShape s => s.LocalPosition.Z,
        _             => 0f
    };

    private static IReadOnlyList<PlacedItem> BuildForegroundItems(ZoneDefinition zone, HostTopology topology)
    {
        var items = new List<PlacedItem>();
        var stackedLabels = StackedMetricLabels(zone);

        // Track active stacks: label → PlacedStack being built.
        var activeStacks = new Dictionary<string, (MetricStackGroupDefinition Def, List<PlacedShape> Members, Vec3 Position)>();

        for (int i = 0; i < zone.Metrics.Count; i++)
        {
            var metric  = zone.Metrics[i];
            var localPos = metric.Position ?? new Vec3(i * ShapeSpacing, 0f, 0f);

            if (!stackedLabels.TryGetValue(metric.Label, out var groupDef))
            {
                // Ungrouped metric → standalone PlacedShape.
                items.Add(BuildShape(zone.Name, metric, localPos, topology));
                continue;
            }

            // Stacked metric — accumulate into the group.
            if (!activeStacks.TryGetValue(groupDef.GroupName, out var entry))
            {
                // First member encountered for this group — record the group's position.
                entry = (groupDef, [], localPos);
                activeStacks[groupDef.GroupName] = entry;
            }

            // All members sit at Y=0 inside the StackGroupNode's local space.
            entry.Members.Add(BuildShape(zone.Name, metric, Vec3.Zero, topology));
            activeStacks[groupDef.GroupName] = entry;

            // If this is the last member in the group, seal it and emit one PlacedStack.
            if (metric.Label == groupDef.MetricLabels[^1])
            {
                items.Add(new PlacedStack(
                    GroupName:    SanitiseNodeName($"{zone.Name}_{groupDef.GroupName}"),
                    LocalPosition: entry.Position,
                    Members:      entry.Members,
                    Mode:         groupDef.Mode));
            }
        }

        return items;
    }

    private static PlacedShape BuildShape(string zoneName, MetricShapeMapping metric, Vec3 localPos, HostTopology topology) =>
        new(
            NodeName:       SanitiseNodeName($"{zoneName}_{metric.Label}"),
            Shape:          metric.Shape,
            LocalPosition:  localPos,
            MetricName:     metric.MetricName,
            InstanceName:   metric.InstanceName,
            DisplayLabel:   metric.Label,
            Colour:         metric.DefaultColour,
            SourceRangeMin: metric.SourceRangeMin,
            SourceRangeMax: ResolveSourceRangeMax(metric, topology),
            TargetRangeMin: metric.TargetRangeMin,
            TargetRangeMax: metric.TargetRangeMax,
            LabelPlacement: metric.LabelPlacement);

    // Returns a lookup from metric label → the stack group it belongs to (empty if no StackGroups).
    private static Dictionary<string, MetricStackGroupDefinition> StackedMetricLabels(ZoneDefinition zone)
    {
        var result = new Dictionary<string, MetricStackGroupDefinition>();
        if (zone.StackGroups is null) return result;
        foreach (var group in zone.StackGroups)
            foreach (var label in group.MetricLabels)
                result[label] = group;
        return result;
    }

    private static IReadOnlyList<PlacedItem> BuildBackgroundShapes(ZoneDefinition zone, HostTopology topology)
    {
        var instances = ResolveInstances(zone, topology);
        var shapes = new List<PlacedItem>();

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
        // Grid zones: shapes are at Vec3.Zero (positioned by GridLayout3D at runtime).
        // Add RowHeaderReservation to account for right-side instance labels.
        if (zone.HasGrid && zone.GroundWidth > 0f)
            return zone.GroundWidth + RowHeaderReservation;
        if (zone.Items.Count == 0) return 0f;
        // Rotated zones (Ry 90°): local Z becomes world X, so visual width = GroundDepth.
        if (zone.RotateYNinetyDeg) return zone.GroundDepth;
        // Use GroundWidth (visual footprint = shape origins + shape width + padding),
        // not just the rightmost shape X-origin, so centering reflects the actual extent.
        return zone.GroundWidth;
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
