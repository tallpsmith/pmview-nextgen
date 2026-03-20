using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Layout;

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
        var fgDefs = zones.Where(z => z.Row == ZoneRow.Foreground).ToList();
        var bgDefs = zones.Where(z => z.Row == ZoneRow.Background).ToList();

        var foreground = BuildGroupedRow(fgDefs, topology, zOffset: 0f);
        var background = AlignBackgroundToForeground(bgDefs, topology, foreground);
        return new SceneLayout(topology.Hostname, [.. foreground, .. background]);
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
        var shapes = zone.Metrics.Select(m =>
            (Shape: BuildShape(zone.Name, m, m.Position ?? Vec3.Zero, topology),
             Metric: m,
             GroupPosition: m.Position ?? Vec3.Zero));

        return AccumulateStackedItems(shapes, zone, namePrefix: zone.Name);
    }

    /// <summary>
    /// Converts a sequence of shapes into PlacedItems, grouping stacked metrics into PlacedStacks.
    /// Uses the first member's GroupPosition as the stack's local position.
    /// </summary>
    private static IReadOnlyList<PlacedItem> AccumulateStackedItems(
        IEnumerable<(PlacedShape Shape, MetricShapeMapping Metric, Vec3 GroupPosition)> shapes,
        ZoneDefinition zone,
        string namePrefix)
    {
        var stackedLabels = StackedMetricLabels(zone);
        var items = new List<PlacedItem>();
        var activeStacks = new Dictionary<string, (MetricStackGroupDefinition Def, List<PlacedShape> Members, Vec3 Position)>();

        foreach (var (shape, metric, groupPosition) in shapes)
        {
            if (!stackedLabels.TryGetValue(metric.Label, out var groupDef))
            {
                items.Add(shape);
                continue;
            }

            if (!activeStacks.TryGetValue(groupDef.GroupName, out var entry))
            {
                entry = (groupDef, [], groupPosition);
                activeStacks[groupDef.GroupName] = entry;
            }

            // Stack members use Vec3.Zero inside the StackGroupNode's local space
            var stackMember = shape with { LocalPosition = Vec3.Zero };
            entry.Members.Add(stackMember);
            activeStacks[groupDef.GroupName] = entry;

            if (metric.Label == groupDef.MetricLabels[^1])
            {
                items.Add(new PlacedStack(
                    GroupName:     SanitiseNodeName($"{namePrefix}_{groupDef.GroupName}"),
                    LocalPosition: entry.Position,
                    Members:       entry.Members,
                    Mode:          groupDef.Mode));
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
        var items = new List<PlacedItem>();

        foreach (var instance in instances)
        {
            var shortName = ShortenInstanceName(instance);
            var safeName  = SanitiseNodeName(shortName);
            var namePrefix = $"{zone.Name}_{safeName}";

            var shapes = zone.Metrics.Select(m =>
                (Shape: new PlacedShape(
                    NodeName:       SanitiseNodeName($"{namePrefix}_{m.Label}"),
                    Shape:          m.Shape,
                    LocalPosition:  Vec3.Zero,
                    MetricName:     m.MetricName,
                    InstanceName:   instance,
                    DisplayLabel:   shortName,
                    Colour:         m.DefaultColour,
                    SourceRangeMin: m.SourceRangeMin,
                    SourceRangeMax: ResolveSourceRangeMax(m, topology),
                    TargetRangeMin: m.TargetRangeMin,
                    TargetRangeMax: m.TargetRangeMax),
                 Metric: m,
                 GroupPosition: Vec3.Zero));

            items.AddRange(AccumulateStackedItems(shapes, zone, namePrefix));
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

    /// <summary>
    /// Builds a foreground row, handling grouped zones as single layout units.
    /// Within a group, the anchor (non-rotated) zone is centred, with rotated
    /// wing zones placed on either side.
    /// </summary>
    private static IReadOnlyList<PlacedZone> BuildGroupedRow(
        IList<ZoneDefinition> rowZones,
        HostTopology topology,
        float zOffset)
    {
        var placedZones = rowZones.Select(z => PlaceZone(z, topology)).ToList();

        // Identify groups: map group name → list of (index, placed zone, definition)
        var groups = new Dictionary<string, List<(int Index, PlacedZone Zone, ZoneDefinition Def)>>();
        for (int i = 0; i < placedZones.Count; i++)
        {
            var def = rowZones[i];
            if (def.GroupName != null)
            {
                if (!groups.ContainsKey(def.GroupName))
                    groups[def.GroupName] = [];
                groups[def.GroupName].Add((i, placedZones[i], def));
            }
        }

        if (groups.Count == 0)
            return CenterRowOnXZero(placedZones, zOffset);

        // Build layout units: each unit is either a standalone zone or a group.
        // A layout unit contributes a single width to the centering calculation.
        var units = new List<LayoutUnit>();
        var consumed = new HashSet<int>();

        for (int i = 0; i < placedZones.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var def = rowZones[i];
            if (def.GroupName != null && groups.TryGetValue(def.GroupName, out var members))
            {
                foreach (var m in members) consumed.Add(m.Index);
                units.Add(BuildGroupUnit(members));
            }
            else
            {
                consumed.Add(i);
                units.Add(new LayoutUnit([placedZones[i]], ZoneWidth(placedZones[i]),
                    AnchorIndex: 0, AnchorOffset: 0f));
            }
        }

        // Center the units on X=0
        var totalWidth = units.Sum(u => u.TotalWidth) + ZoneGap * (units.Count - 1);
        var cursor = -totalWidth / 2f;
        var result = new List<PlacedZone>();

        foreach (var unit in units)
        {
            for (int i = 0; i < unit.Zones.Count; i++)
            {
                var zone = unit.Zones[i];
                var relativeX = unit.ZoneOffsets[i];
                result.Add(zone with { Position = new Vec3(cursor + relativeX, 0f, zOffset) });
            }
            cursor += unit.TotalWidth + ZoneGap;
        }

        return result;
    }

    private static LayoutUnit BuildGroupUnit(List<(int Index, PlacedZone Zone, ZoneDefinition Def)> members)
    {
        const float IntraGroupGap = 0.5f;

        // Anchor = the non-rotated zone in the group
        var anchor = members.FirstOrDefault(m => !m.Def.RotateYNinetyDeg);
        var wings = members.Where(m => m.Def.RotateYNinetyDeg).ToList();

        // First wing goes left, second goes right (by declaration order within group)
        var leftWings = wings.Take(1).ToList();
        var rightWings = wings.Skip(1).ToList();

        // If no anchor found, treat first zone as anchor and compute offsets
        if (anchor.Zone == null)
        {
            var zones = members.Select(m => m.Zone).ToList();
            var fallbackOffsets = new List<float>();
            float fallbackCursor = 0f;
            foreach (var z in zones)
            {
                fallbackOffsets.Add(fallbackCursor);
                fallbackCursor += ZoneWidth(z) + IntraGroupGap;
            }
            var width = fallbackCursor - IntraGroupGap;
            return new LayoutUnit(zones, width, 0, 0f, fallbackOffsets);
        }

        // Compute layout: left wings | anchor | right wings
        var allZones = new List<PlacedZone>();
        var offsets = new List<float>();
        float cursorOffset = 0f;

        foreach (var w in leftWings)
        {
            allZones.Add(w.Zone);
            offsets.Add(cursorOffset);
            cursorOffset += ZoneWidth(w.Zone) + IntraGroupGap;
        }

        float anchorOffset = cursorOffset;
        allZones.Add(anchor.Zone);
        offsets.Add(cursorOffset);
        cursorOffset += ZoneWidth(anchor.Zone) + IntraGroupGap;

        foreach (var w in rightWings)
        {
            allZones.Add(w.Zone);
            offsets.Add(cursorOffset);
            cursorOffset += ZoneWidth(w.Zone) + IntraGroupGap;
        }

        // Total width (remove trailing gap)
        float totalWidth = cursorOffset - IntraGroupGap;

        int anchorIdx = leftWings.Count;
        return new LayoutUnit(allZones, totalWidth, anchorIdx, anchorOffset, offsets);
    }

    /// <summary>
    /// Positions background zones aligned to their foreground partner's X-centre.
    /// Falls back to independent centering for zones without a partner.
    /// </summary>
    private static IReadOnlyList<PlacedZone> AlignBackgroundToForeground(
        IList<ZoneDefinition> bgDefs,
        HostTopology topology,
        IReadOnlyList<PlacedZone> foreground)
    {
        var placed = bgDefs.Select(z => PlaceZone(z, topology)).ToList();

        // Build foreground X-centre lookup
        var fgCentres = new Dictionary<string, float>();
        foreach (var fz in foreground)
        {
            var w = ZoneWidth(fz);
            fgCentres[fz.Name] = fz.Position.X + w / 2f;
        }

        // Initial placement: centre each background zone on its foreground partner
        var result = new List<PlacedZone>();
        for (int i = 0; i < placed.Count; i++)
        {
            var zone = placed[i];
            var def = bgDefs[i];

            if (def.AlignWithForegroundZone != null && fgCentres.TryGetValue(def.AlignWithForegroundZone, out var fgCenter))
            {
                var bgWidth = ZoneWidth(zone);
                var bgX = fgCenter - bgWidth / 2f;
                result.Add(zone with { Position = new Vec3(bgX, 0f, BackgroundZOffset) });
            }
            else
            {
                result.Add(zone with { Position = new Vec3(0f, 0f, BackgroundZOffset) });
            }
        }

        // Resolve overlaps: push zones apart if they collide
        var sorted = result
            .Select((z, i) => (Zone: z, Index: i))
            .OrderBy(x => x.Zone.Position.X)
            .ToList();

        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var curr = sorted[i];
            var prevEnd = prev.Zone.Position.X + ZoneWidth(prev.Zone);
            var minStart = prevEnd + ZoneGap;

            if (curr.Zone.Position.X < minStart)
            {
                var shifted = curr.Zone with { Position = curr.Zone.Position with { X = minStart } };
                sorted[i] = (shifted, curr.Index);
                result[curr.Index] = shifted;
            }
        }

        return result;
    }

    private record LayoutUnit(
        IReadOnlyList<PlacedZone> Zones,
        float TotalWidth,
        int AnchorIndex,
        float AnchorOffset,
        IReadOnlyList<float>? ZoneOffsets = null)
    {
        public IReadOnlyList<float> ZoneOffsets { get; } = ZoneOffsets
            ?? (Zones.Count == 1 ? new[] { 0f } : []);
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

        // SourceRangeMax of 0 is a sentinel meaning "derive from topology" — only valid for memory metrics
        if (metric.MetricName.StartsWith("mem.", StringComparison.Ordinal))
            return topology.PhysicalMemoryBytes ?? FallbackMemoryBytes;

        throw new InvalidOperationException(
            $"Metric '{metric.MetricName}' has SourceRangeMax=0 but is not a memory metric. " +
            "Non-memory metrics must specify an explicit SourceRangeMax in the profile.");
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
