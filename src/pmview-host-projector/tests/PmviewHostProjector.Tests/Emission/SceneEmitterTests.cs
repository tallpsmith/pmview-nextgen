using Xunit;
using PmviewHostProjector.Emission;
using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Tests.Emission;

public class SceneEmitterTests
{
    [Fact]
    public void Write_ProducesValidScene_WithoutCameraOrLights()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("[gd_scene", tscn);
        Assert.DoesNotContain("Camera3D", tscn);
    }

    [Fact]
    public void Write_EmptyLayout_StillProducesValidScene()
    {
        var tscn = TscnWriter.Write(new SceneLayout("emptyhost", []));
        Assert.Contains("[gd_scene", tscn);
        Assert.Contains("HostView", tscn);
    }
}
