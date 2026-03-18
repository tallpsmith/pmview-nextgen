using System.Linq;

namespace PmviewProjectionCore.Models;

public record Vec3(float X, float Y, float Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

public enum LabelPlacement { Front, Left, Right }
public enum StackMode { Proportional, Normalised }

// Common parent — position is the only shared structural concern.
public abstract record PlacedItem(Vec3 LocalPosition);

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
    float TargetRangeMax,
    LabelPlacement LabelPlacement = LabelPlacement.Front,
    bool IsPlaceholder = false) : PlacedItem(LocalPosition);

// Members are ordered bottom → top. All member LocalPositions are Vec3.Zero;
// StackGroupNode owns their Y positions at runtime.
public record PlacedStack(
    string GroupName,
    Vec3 LocalPosition,
    IReadOnlyList<PlacedShape> Members,
    StackMode Mode) : PlacedItem(LocalPosition);

public record PlacedZone(
    string Name,
    string ZoneLabel,
    Vec3 Position,
    float? ColumnSpacing,
    float? RowSpacing,
    IReadOnlyList<PlacedItem> Items,
    float GroundWidth = 0f,
    float GroundDepth = 0f,
    IReadOnlyList<string>? MetricLabels = null,
    IReadOnlyList<string>? InstanceLabels = null,
    bool RotateYNinetyDeg = false)
{
    private IReadOnlyList<PlacedShape>? _shapes;

    /// <summary>Returns only standalone PlacedShape items (not members of stacks).</summary>
    public IReadOnlyList<PlacedShape> Shapes => _shapes ??= Items.OfType<PlacedShape>().ToList();
    public bool HasGrid => ColumnSpacing.HasValue;
}

public record SceneLayout(
    string Hostname,
    IReadOnlyList<PlacedZone> Zones);
