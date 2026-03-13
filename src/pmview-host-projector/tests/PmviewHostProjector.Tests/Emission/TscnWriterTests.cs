using Xunit;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Tests.Emission;

public class TscnWriterTests
{
    private static SceneLayout MinimalLayout() =>
        new("testhost", [
            new PlacedZone("CPU", "CPU", new Vec3(0, 0, 0),
                null, null, null,
                [
                    new PlacedShape("CPU_User", ShapeType.Bar, new Vec3(0, 0, 0),
                        "kernel.all.cpu.user", null, null,
                        new RgbColour(0.976f, 0.451f, 0.086f),
                        0f, 100f, 0.2f, 5.0f)
                ])
        ]);

    [Fact]
    public void Write_StartsWithGdSceneHeader()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.StartsWith("[gd_scene", tscn);
    }

    [Fact]
    public void Write_HasExtResourceForPcpBindable()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("PcpBindable.cs", tscn);
        Assert.Contains("bindable_script", tscn);
    }

    [Fact]
    public void Write_HasExtResourceForPcpBindingResource()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("PcpBindingResource.cs", tscn);
        Assert.Contains("binding_res_script", tscn);
    }

    [Fact]
    public void Write_HasExtResourceForGroundedBar_WithBuildingBlocksPath()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("res://addons/pmview-bridge/building_blocks/grounded_bar.tscn", tscn);
    }

    [Fact]
    public void Write_SubResourceHasScriptAndBindingFields()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("script = ExtResource(\"binding_res_script\")", tscn);
        Assert.Contains("resource_local_to_scene = true", tscn);
        Assert.Contains("MetricName = \"kernel.all.cpu.user\"", tscn);
        Assert.Contains("TargetProperty = \"height\"", tscn);
        Assert.Contains("SourceRangeMax = 100", tscn);
        Assert.Contains("InstanceId = -1", tscn);
    }

    [Fact]
    public void Write_ContainsRootNode3D()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"HostView\" type=\"Node3D\"]", tscn);
    }

    [Fact]
    public void Write_ContainsZoneNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"CPU\"", tscn);
    }

    [Fact]
    public void Write_ShapeInstancesParentScene()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("instance=ExtResource(\"bar_scene\")", tscn);
    }

    [Fact]
    public void Write_ShapeHasPcpBindableChild()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"CPU/CPU_User\"]", tscn);
        Assert.Contains("script = ExtResource(\"bindable_script\")", tscn);
        Assert.Contains("PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(", tscn);
    }

    [Fact]
    public void Write_CylinderShape_UsesGroundedCylinder_WithBuildingBlocksPath()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Disk", "Disk", Vec3.Zero, null, null, null,
                [new PlacedShape("Disk_Read", ShapeType.Cylinder, Vec3.Zero,
                    "disk.all.read_bytes", null, null,
                    new RgbColour(0.961f, 0.620f, 0.043f),
                    0f, 500_000_000f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("res://addons/pmview-bridge/building_blocks/grounded_cylinder.tscn", tscn);
    }

    [Fact]
    public void Write_InstanceBinding_HasInstanceName()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
                [new PlacedShape("Load_1min", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.load", "1 minute", null,
                    new RgbColour(0.388f, 0.400f, 0.945f),
                    0f, 10f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("InstanceName = \"1 minute\"", tscn);
    }

    [Fact]
    public void Write_GridZone_HasGridLayout3DScript()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
                3, 1.5f, 2.0f,
                [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("res://addons/pmview-bridge/building_blocks/grid_layout_3d.gd", tscn);
        Assert.Contains("columns = 3", tscn);
    }

    [Fact]
    public void Write_EmitsZoneLabelNode_WithGroundPlaneProperties()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("Label3D", tscn);
        Assert.Contains("text = \"CPU\"", tscn);
        Assert.Contains("font_size = 32", tscn);
        Assert.Contains("pixel_size = 0.01", tscn);
        Assert.Contains("horizontal_alignment = 1", tscn);
    }

    [Fact]
    public void Write_ZoneTransformHasPosition()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("CPU", "CPU", new Vec3(2.5f, 0, 0), null, null, null,
                [new PlacedShape("CPU_User", ShapeType.Bar, new Vec3(1.0f, 0, 0),
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 2.5, 0, 0)", tscn);
    }

    [Fact]
    public void Write_ShapeNode_EmitsColourProperty()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
                [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("colour = Color(0.976, 0.451, 0.086, 1)", tscn);
    }

    [Fact]
    public void Write_ZoneLabel_CentredOnShapeSpan()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
                [
                    new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                        "kernel.all.load", "1 minute", "1m",
                        new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
                    new PlacedShape("Load_5m", ShapeType.Bar, new Vec3(1.5f, 0, 0),
                        "kernel.all.load", "5 minute", "5m",
                        new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
                    new PlacedShape("Load_15m", ShapeType.Bar, new Vec3(3.0f, 0, 0),
                        "kernel.all.load", "15 minute", "15m",
                        new RgbColour(0.388f, 0.400f, 0.945f), 0f, 10f, 0.2f, 5.0f),
                ],
                GroundWidth: 4.6f, GroundDepth: 2.0f)
        ]);
        var tscn = TscnWriter.Write(layout);
        // Label should be at X = 1.5 (centre of 0..3.0 span), Z = 1.5
        Assert.Contains("text = \"Load\"", tscn);
        Assert.Contains("1.5, 0.01, 1.5", tscn);
    }

    [Fact]
    public void Write_EmitsGroundBezelMeshPerZone()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
                [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 2.0f, GroundDepth: 2.0f)
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("[node name=\"CPUGround\" type=\"MeshInstance3D\" parent=\"CPU\"]", tscn);
        Assert.Contains("BoxMesh", tscn);
    }

    [Fact]
    public void Write_GroundBezel_HasDarkGreyMaterial()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("CPU", "CPU", Vec3.Zero, null, null, null,
                [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 2.0f, GroundDepth: 2.0f)
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("albedo_color = Color(0.15, 0.15, 0.15, 1)", tscn);
    }

    [Fact]
    public void Write_ZeroGroundExtent_NoBezelEmitted()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Empty", "Empty", Vec3.Zero, null, null, null, [],
                GroundWidth: 0f, GroundDepth: 0f)
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.DoesNotContain("Ground", tscn);
    }

    [Fact]
    public void Write_ForegroundShape_EmitsMetricLabel()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
                [new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                    "kernel.all.load", "1 minute", "1m",
                    new RgbColour(0.388f, 0.400f, 0.945f),
                    0f, 10f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("[node name=\"Load_1mLabel\" type=\"Label3D\" parent=\"Load\"]", tscn);
        Assert.Contains("text = \"1m\"", tscn);
    }
}
