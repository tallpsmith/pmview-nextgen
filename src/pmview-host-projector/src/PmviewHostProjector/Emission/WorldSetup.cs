using System.Globalization;
using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Emission;

public record SceneBounds(float MinX, float MaxX, float MinZ, float MaxZ)
{
    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public float CentreX => (MinX + MaxX) / 2f;
    public float CentreZ => (MinZ + MaxZ) / 2f;

    public static SceneBounds FromLayout(SceneLayout layout)
    {
        if (layout.Zones.Count == 0) return new SceneBounds(-5, 5, -10, 0);
        var minX = layout.Zones.Min(z => z.Position.X);
        var maxX = layout.Zones.Max(z => z.Position.X + ZoneWidth(z));
        var allZ = layout.Zones.Select(z => z.Position.Z);
        return new SceneBounds(minX - 2, maxX + 2, allZ.Min() - 5, 0);
    }

    private static float ZoneWidth(PlacedZone zone)
    {
        if (zone.GroundWidth > 0f) return zone.GroundWidth;
        if (zone.Shapes.Count == 0) return 0f;
        return zone.Shapes.Max(s => s.LocalPosition.X);
    }
}

public record CameraSetup(Vec3 Position, Vec3 LookAtTarget);

public static class WorldSetup
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static CameraSetup ComputeCamera(SceneBounds bounds)
    {
        var extent = Math.Max(bounds.Width, Math.Abs(bounds.Depth));
        var height = extent * 0.35f;
        var distance = extent * 0.35f;
        var target = new Vec3(bounds.CentreX, 1.5f, bounds.CentreZ);
        var position = new Vec3(bounds.CentreX, height, bounds.MaxZ + distance);
        return new CameraSetup(position, target);
    }

    public static string BuildLookAtTransform(Vec3 eye, Vec3 target)
    {
        var fwd = NormaliseVec(eye.X - target.X, eye.Y - target.Y, eye.Z - target.Z);
        var right = NormaliseVec(fwd.Z, 0f, -fwd.X);
        var up = (
            X: fwd.Y * right.Z - fwd.Z * right.Y,
            Y: fwd.Z * right.X - fwd.X * right.Z,
            Z: fwd.X * right.Y - fwd.Y * right.X);
        return $"Transform3D({F(right.X)}, {F(up.X)}, {F(fwd.X)}, {F(right.Y)}, {F(up.Y)}, {F(fwd.Y)}, {F(right.Z)}, {F(up.Z)}, {F(fwd.Z)}, {F(eye.X)}, {F(eye.Y)}, {F(eye.Z)})";
    }

    private static (float X, float Y, float Z) NormaliseVec(float x, float y, float z)
    {
        var len = MathF.Sqrt(x * x + y * y + z * z);
        return len > 0f ? (x / len, y / len, z / len) : (0f, 0f, 1f);
    }

    private static string F(float value) => (value == 0f ? 0f : value).ToString(Inv);
}
