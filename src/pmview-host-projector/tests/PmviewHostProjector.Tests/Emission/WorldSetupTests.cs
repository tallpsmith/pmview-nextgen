using Xunit;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Tests.Emission;

public class WorldSetupTests
{
    [Fact]
    public void ComputeCamera_PositionedAboveAndInFront()
    {
        var bounds = new SceneBounds(-10, 10, -20, 0);
        var camera = WorldSetup.ComputeCamera(bounds);
        Assert.True(camera.Position.Y > 0);
        Assert.True(camera.Position.Z > bounds.MaxZ);
    }

    [Fact]
    public void ComputeCamera_LooksAtSceneCentre()
    {
        var bounds = new SceneBounds(-10, 10, -20, 0);
        var camera = WorldSetup.ComputeCamera(bounds);
        Assert.True(Math.Abs(camera.LookAtTarget.X) < 0.01f);
    }

    [Fact]
    public void ComputeCamera_ScalesWithSceneSize()
    {
        var small = WorldSetup.ComputeCamera(new SceneBounds(-5, 5, -10, 0));
        var large = WorldSetup.ComputeCamera(new SceneBounds(-20, 20, -40, 0));
        Assert.True(large.Position.Z > small.Position.Z);
    }

    [Fact]
    public void FromLayout_EmptyLayout_ReturnsDefaultBounds()
    {
        var bounds = SceneBounds.FromLayout(new SceneLayout("empty", []));
        Assert.True(bounds.Width > 0);
    }
}
