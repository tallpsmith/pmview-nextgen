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

    public static string Emit(SceneLayout layout)
    {
        var tscn = TscnWriter.Write(layout);
        var camera = ComputeCamera(layout);
        return tscn + camera + BuildDirectionalLight();
    }

    private static string ComputeCamera(SceneLayout layout)
    {
        var bounds = SceneBounds.FromLayout(layout);
        var setup = WorldSetup.ComputeCamera(bounds);
        var p = setup.Position;
        var t = setup.LookAtTarget;

        var sb = new StringBuilder();
        sb.AppendLine("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(p.X)}, {F(p.Y)}, {F(p.Z)})");
        sb.AppendLine();
        return sb.ToString();
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
