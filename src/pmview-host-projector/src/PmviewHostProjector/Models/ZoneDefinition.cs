namespace PmviewHostProjector.Models;

public enum ZoneRow { Foreground, Background }
public enum ZoneType { Aggregate, PerInstance }
public enum ShapeType { Bar, Cylinder }

public record RgbColour(float R, float G, float B)
{
    public static RgbColour FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return new RgbColour(
            Convert.ToInt32(hex[..2], 16) / 255f,
            Convert.ToInt32(hex[2..4], 16) / 255f,
            Convert.ToInt32(hex[4..6], 16) / 255f);
    }
}

/// <summary>
/// InstanceName: for instanced aggregate metrics like kernel.all.load,
/// where each shape targets a specific instance ("1 minute", "5 minute", "15 minute").
/// Null for singular aggregate metrics and per-instance zones (where instances
/// come from HostTopology).
/// </summary>
public record MetricShapeMapping(
    string MetricName,
    ShapeType Shape,
    string Label,
    RgbColour DefaultColour,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax,
    string? InstanceName = null,
    Vec3? Position = null,
    LabelPlacement LabelPlacement = LabelPlacement.Front,
    bool IsPlaceholder = false);

/// <summary>
/// Declares that a named subset of this zone's metrics should be emitted as a
/// vertically-stacked group (StackGroupNode) rather than individual shapes.
/// MetricLabels must match MetricShapeMapping.Label values in the parent zone's Metrics list,
/// in the desired bottom-to-top render order.
/// </summary>
public record MetricStackGroupDefinition(
    string GroupName,
    StackMode Mode,
    IReadOnlyList<string> MetricLabels);

public record ZoneDefinition(
    string Name,
    ZoneRow Row,
    ZoneType Type,
    IReadOnlyList<MetricShapeMapping> Metrics,
    string? InstanceMetricSource = null,
    bool RotateYNinetyDeg = false,
    IReadOnlyList<MetricStackGroupDefinition>? StackGroups = null);
