using System.Globalization;
using System.Text;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Emission;

/// <summary>
/// Orchestrates TscnWriter to emit a complete .tscn scene, then appends
/// Camera3D and DirectionalLight3D nodes computed from the scene bounds.
/// </summary>
public static class SceneEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Emit(SceneLayout layout,
        string pmproxyEndpoint = "http://localhost:44322")
    {
        var tscn = TscnWriter.Write(layout, pmproxyEndpoint);
        var camera = ComputeCamera(layout);
        return tscn + camera + BuildDirectionalLight();
    }

    private static string ComputeCamera(SceneLayout layout)
    {
        var bounds = SceneBounds.FromLayout(layout);
        var setup = WorldSetup.ComputeCamera(bounds);
        var transform = BuildLookAtTransform(setup.Position, setup.LookAtTarget);

        var sb = new StringBuilder();
        sb.AppendLine("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        sb.AppendLine($"transform = {transform}");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Computes a Godot Transform3D string that positions the camera at <paramref name="eye"/>
    /// looking at <paramref name="target"/> with Y-up. Matches Godot's look_at convention
    /// where camera forward is -Z in local space.
    /// </summary>
    private static string BuildLookAtTransform(Vec3 eye, Vec3 target)
    {
        // Forward vector (Godot camera looks along -Z, so forward = normalise(eye - target))
        var fwd = Normalise(eye.X - target.X, eye.Y - target.Y, eye.Z - target.Z);
        // Right vector = cross(up, forward)
        var right = Normalise(
            /* up.Y * fwd.Z - up.Z * fwd.Y */ fwd.Z,
            /* up.Z * fwd.X - up.X * fwd.Z */ 0f,
            /* up.X * fwd.Y - up.Y * fwd.X */ -fwd.X);
        // True up = cross(forward, right)
        var up = (
            X: fwd.Y * right.Z - fwd.Z * right.Y,
            Y: fwd.Z * right.X - fwd.X * right.Z,
            Z: fwd.X * right.Y - fwd.Y * right.X);

        // Transform3D column-major: (right.x, up.x, fwd.x, right.y, up.y, fwd.y, right.z, up.z, fwd.z, pos.x, pos.y, pos.z)
        return $"Transform3D({F(right.X)}, {F(up.X)}, {F(fwd.X)}, {F(right.Y)}, {F(up.Y)}, {F(fwd.Y)}, {F(right.Z)}, {F(up.Z)}, {F(fwd.Z)}, {F(eye.X)}, {F(eye.Y)}, {F(eye.Z)})";
    }

    private static (float X, float Y, float Z) Normalise(float x, float y, float z)
    {
        var len = MathF.Sqrt(x * x + y * y + z * z);
        return len > 0f ? (x / len, y / len, z / len) : (0f, 0f, 1f);
    }

    private static string BuildDirectionalLight()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[node name=\"DirectionalLight3D\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 5, 5)");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string F(float value) => value.ToString(Inv);
}
