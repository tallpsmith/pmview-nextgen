using Xunit;
using PmviewProjectionCore.Models;

namespace PmviewProjectionCore.Tests.Models;

public class PlacedStackTests
{
    private static PlacedShape MakeShape(string nodeName, string metric) =>
        new(nodeName, ShapeType.Bar, Vec3.Zero, metric, null, null,
            new RgbColour(1f, 0f, 0f), 0f, 100f, 0.2f, 5.0f);

    [Fact]
    public void PlacedStack_HoldsGroupNameMembersAndMode()
    {
        var user = MakeShape("Cpu_User", "kernel.all.cpu.user");
        var sys  = MakeShape("Cpu_Sys",  "kernel.all.cpu.sys");
        var stack = new PlacedStack("CpuStack", Vec3.Zero, [user, sys], StackMode.Proportional);

        Assert.Equal("CpuStack", stack.GroupName);
        Assert.Equal(2, stack.Members.Count);
        Assert.Equal(StackMode.Proportional, stack.Mode);
    }

    [Fact]
    public void PlacedStack_LocalPosition_MatchesConstructorArg()
    {
        var pos = new Vec3(1.5f, 0f, 2.4f);
        var stack = new PlacedStack("G", pos, [MakeShape("S", "m")], StackMode.Normalised);
        Assert.Equal(pos, stack.LocalPosition);
    }

    [Fact]
    public void PlacedZone_Items_CanHoldMixedPlacedItems()
    {
        var shape = MakeShape("S1", "metric.a");
        var stack = new PlacedStack("G1", Vec3.Zero, [shape], StackMode.Proportional);
        var zone = new PlacedZone(Name: "Z", ZoneLabel: "Z", Position: Vec3.Zero,
            ColumnSpacing: null, RowSpacing: null, Items: [shape, stack]);

        Assert.Equal(2, zone.Items.Count);
        Assert.Contains(zone.Items, i => i is PlacedShape);
        Assert.Contains(zone.Items, i => i is PlacedStack);
    }

    [Fact]
    public void PlacedZone_Shapes_ReturnsOnlyPlacedShapeItems()
    {
        var shape = MakeShape("S1", "metric.a");
        var stack = new PlacedStack("G1", Vec3.Zero, [shape], StackMode.Proportional);
        var zone = new PlacedZone(Name: "Z", ZoneLabel: "Z", Position: Vec3.Zero,
            ColumnSpacing: null, RowSpacing: null, Items: [shape, stack]);

        Assert.Single(zone.Shapes);
        Assert.IsType<PlacedShape>(zone.Shapes[0]);
    }

    [Fact]
    public void PlacedStack_IsA_PlacedItem()
    {
        var stack = new PlacedStack("G", Vec3.Zero, [MakeShape("S", "m")], StackMode.Proportional);
        Assert.IsAssignableFrom<PlacedItem>(stack);
    }

    [Fact]
    public void PlacedShape_IsA_PlacedItem()
    {
        var shape = MakeShape("S", "m");
        Assert.IsAssignableFrom<PlacedItem>(shape);
    }
}
