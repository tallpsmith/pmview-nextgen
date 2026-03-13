using PmviewHostProjector.Models;

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
        var allX = layout.Zones.Select(z => z.Position.X);
        var allZ = layout.Zones.Select(z => z.Position.Z);
        return new SceneBounds(allX.Min() - 5, allX.Max() + 5, allZ.Min() - 5, 0);
    }
}

public record CameraSetup(Vec3 Position, Vec3 LookAtTarget);

public static class WorldSetup
{
    public static CameraSetup ComputeCamera(SceneBounds bounds)
    {
        var extent = Math.Max(bounds.Width, Math.Abs(bounds.Depth));
        var height = extent * 0.6f;
        var distance = extent * 0.8f;
        var target = new Vec3(bounds.CentreX, 0, bounds.CentreZ);
        var position = new Vec3(bounds.CentreX, height, bounds.MaxZ + distance);
        return new CameraSetup(position, target);
    }
}
