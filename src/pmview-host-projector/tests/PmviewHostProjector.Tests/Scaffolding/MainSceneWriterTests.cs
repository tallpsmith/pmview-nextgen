using Xunit;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector.Tests.Scaffolding;

public class MainSceneWriterTests
{
    [Fact]
    public void Write_StartsWithGdSceneHeader()
    {
        var tscn = MainSceneWriter.Write();
        Assert.StartsWith("[gd_scene", tscn);
    }

    [Fact]
    public void Write_HasCameraWithFlyOrbitScript()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]", tscn);
        Assert.Contains("fly_orbit_camera.gd", tscn);
    }

    [Fact]
    public void Write_HasWorldEnvironment()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"WorldEnvironment\" type=\"WorldEnvironment\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HasKeyLightAndFillLight()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"KeyLight\" type=\"DirectionalLight3D\" parent=\".\"]", tscn);
        Assert.Contains("[node name=\"FillLight\" type=\"DirectionalLight3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HasSceneRootNode()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"SceneRoot\" type=\"Node3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_RootNodeIsNamedMain()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("[node name=\"Main\" type=\"Node3D\"]", tscn);
    }

    [Fact]
    public void Write_RootNodeHasMainControllerScript()
    {
        var tscn = MainSceneWriter.Write();
        Assert.Contains("main_controller_script", tscn);
        Assert.Contains("main_scene_controller.gd", tscn);
    }
}
