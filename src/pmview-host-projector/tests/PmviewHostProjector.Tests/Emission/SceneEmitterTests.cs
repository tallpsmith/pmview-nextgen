using Xunit;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Tests.Emission;

public class SceneEmitterTests
{
    [Fact]
    public void Emit_ProducesSceneWithCameraAndLight()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = SceneEmitter.Emit(layout);
        Assert.Contains("[gd_scene", tscn);
        Assert.Contains("Camera3D", tscn);
        Assert.Contains("DirectionalLight3D", tscn);
    }

    [Fact]
    public void Emit_EmptyLayout_StillProducesValidScene()
    {
        var tscn = SceneEmitter.Emit(new SceneLayout("emptyhost", []));
        Assert.Contains("[gd_scene", tscn);
        Assert.Contains("HostView", tscn);
    }

    [Fact]
    public void Emit_CameraTransform_IsNotIdentityRotation()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = SceneEmitter.Emit(layout);
        // Should NOT have identity rotation (1, 0, 0, 0, 1, 0, 0, 0, 1)
        Assert.DoesNotContain("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1,", tscn.Split("Camera3D")[1]);
    }
}
