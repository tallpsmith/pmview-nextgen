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

        var metricLabels = VisualColumnLabels(zone);
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

        var visualColumns = VisualColumnCount(zone);

        if (zone.Row == ZoneRow.Foreground)
        {
            var width = (visualColumns - 1) * ShapeSpacing + 0.8f + GroundPadding * 2;
            var depth = 0.8f + GroundPadding * 2;
            return (width, depth);
        }
        else
        {
            var rows  = ResolveInstances(zone, topology).Count;
            var width = (visualColumns - 1) * ColumnSpacing + 0.8f + GroundPadding * 2;
            var depth = (rows - 1) * RowSpacing    + 0.8f + GroundPadding * 2;
            return (width, depth);
        }
    }

    /// <summary>
    /// Counts the number of visual columns: ungrouped metrics + one per stack group.
    /// A stack group of 3 metrics occupies 1 column, not 3.
    /// </summary>
    private static int VisualColumnCount(ZoneDefinition zone)
    {
        if (zone.StackGroups is null or { Count: 0 })
            return zone.Metrics.Count;

        var stackedLabels = StackedMetricLabels(zone);
        var ungrouped = zone.Metrics.Count(m => !stackedLabels.ContainsKey(m.Label));
        return ungrouped + zone.StackGroups.Count;
    }

    /// <summary>
    /// Returns column header labels matching visual columns: ungrouped metric labels
    /// plus stack group names for grouped metrics, preserving declaration order.
    /// </summary>
    private static IReadOnlyList<string> VisualColumnLabels(ZoneDefinition zone)
    {
        if (zone.StackGroups is null or { Count: 0 })
            return zone.Metrics.Select(m => m.Label).ToList();

        var stackedLabels = StackedMetricLabels(zone);
        var emittedGroups = new HashSet<string>();
        var labels = new List<string>();

        foreach (var metric in zone.Metrics)
        {
            if (!stackedLabels.TryGetValue(metric.Label, out var groupDef))
            {
                labels.Add(metric.Label);
                continue;
            }

            if (emittedGroups.Add(groupDef.GroupName))
                labels.Add(groupDef.GroupName);
        }

        return labels;
    }

    private static IReadOnlyList<PlacedItem> BuildForegroundItems(ZoneDefinition zone, HostTopology topology)
    {
        var items = new List<PlacedItem>();
        var stackedLabels = StackedMetricLabels(zone);

        // Track active stacks: label → PlacedStack being built.
        var activeStacks = new Dictionary<string, (MetricStackGroupDefinition Def, List<PlacedShape> Members, Vec3 Position)>();

        for (int i = 0; i < zone.Metrics.Count; i++)
        {
            var metric  = zone.Metrics[i];
            var localPos = metric.Position ?? Vec3.Zero;

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
            LabelPlacement: metric.LabelPlacement,
            IsPlaceholder:  metric.IsPlaceholder);

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
        var stackedLabels = StackedMetricLabels(zone);
        var items = new List<PlacedItem>();

        foreach (var instance in instances)
        {
            var shortName = ShortenInstanceName(instance);
            var safeName  = SanitiseNodeName(shortName);

            // Track active stacks per instance, keyed by group name.
            var activeStacks = new Dictionary<string, (MetricStackGroupDefinition Def, List<PlacedShape> Members)>();

            foreach (var metric in zone.Metrics)
            {
                var shape = new PlacedShape(
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
                    TargetRangeMax: metric.TargetRangeMax);

                if (!stackedLabels.TryGetValue(metric.Label, out var groupDef))
                {
                    items.Add(shape);
                    continue;
                }

                if (!activeStacks.TryGetValue(groupDef.GroupName, out var entry))
                {
                    entry = (groupDef, []);
                    activeStacks[groupDef.GroupName] = entry;
                }

                entry.Members.Add(shape);
                activeStacks[groupDef.GroupName] = entry;

                if (metric.Label == groupDef.MetricLabels[^1])
                {
                    items.Add(new PlacedStack(
                        GroupName:     SanitiseNodeName($"{zone.Name}_{safeName}_{groupDef.GroupName}"),
                        LocalPosition: Vec3.Zero,
                        Members:       entry.Members,
                        Mode:          groupDef.Mode));
                }
            }
        }
        return items;
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
        // Grid zones: shapes are at Vec3.Zero (positioned by MetricGrid at runtime).
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
