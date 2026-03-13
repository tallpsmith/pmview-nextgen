namespace PmviewHostProjector.Models;

public record Vec3(float X, float Y, float Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

public record PlacedShape(
    string NodeName,
    ShapeType Shape,
    Vec3 LocalPosition,
    string MetricName,
    string? InstanceName,
    string? DisplayLabel,
    RgbColour Colour,
    float SourceRangeMin,
    float SourceRangeMax,
    float TargetRangeMin,
    float TargetRangeMax);

public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,
    int? GridColumns,
    float? GridColumnSpacing,
    float? GridRowSpacing,
    IReadOnlyList<PlacedShape> Shapes,
    float GroundWidth = 0f,
    float GroundDepth = 0f,
    IReadOnlyList<string>? MetricLabels = null,
    IReadOnlyList<string>? InstanceLabels = null);

public record SceneLayout(
    string Hostname,
    IReadOnlyList<PlacedZone> Zones);
