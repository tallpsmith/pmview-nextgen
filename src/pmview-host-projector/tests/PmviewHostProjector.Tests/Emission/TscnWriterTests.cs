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
        Assert.Contains("font_size = 56", tscn);   // was font_size = 32
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
        Assert.Contains("albedo_color = Color(0.3, 0.3, 0.3, 1)", tscn);
    }

    [Fact]
    public void Write_ZoneNameWithInvalidIdChars_BezelSubResourceIdsAreSanitised()
    {
        // "Per-CPU" and "Network In" are real zone names — hyphens and spaces are invalid in Godot resource IDs.
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per-CPU", "Per-CPU", Vec3.Zero, null, null, null,
                [new PlacedShape("PerCPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 2f),
            new PlacedZone("Network In", "Network In", Vec3.Zero, null, null, null,
                [new PlacedShape("NetworkIn_Bytes", ShapeType.Bar, Vec3.Zero,
                    "network.interface.in.bytes", null, null,
                    new RgbColour(0.231f, 0.510f, 0.965f), 0f, 125_000_000f, 0.2f, 5.0f)],
                GroundWidth: 2f, GroundDepth: 2f),
        ]);
        var tscn = TscnWriter.Write(layout);

        Assert.DoesNotContain("bezel_mesh_Per-CPU",   tscn);
        Assert.DoesNotContain("bezel_mat_Per-CPU",    tscn);
        Assert.DoesNotContain("bezel_mesh_Network In", tscn);
        Assert.DoesNotContain("bezel_mat_Network In",  tscn);

        Assert.Contains("bezel_mesh_Per_CPU",    tscn);
        Assert.Contains("bezel_mat_Per_CPU",     tscn);
        Assert.Contains("bezel_mesh_Network_In", tscn);
        Assert.Contains("bezel_mat_Network_In",  tscn);
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

    [Fact]
    public void Write_GridZone_EmitsColumnHeaderLabels()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
                3, 1.5f, 2.0f,
                [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("text = \"User\"", tscn);
        Assert.Contains("text = \"Sys\"", tscn);
        Assert.Contains("text = \"Nice\"", tscn);
    }

    [Fact]
    public void Write_GridZone_EmitsRowHeaderLabels()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
                3, 1.5f, 2.0f,
                [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("text = \"cpu0\"", tscn);
        Assert.Contains("text = \"cpu1\"", tscn);
    }

    [Fact]
    public void Write_GridZone_ColumnHeaders_AreAtBackEdge_BeyondLastRow()
    {
        // 2 instances, rowSpacing=2.0 → back edge Z = -(2-1)*2.0 - 1.0 = -3.0
        // 3 metrics, colSpacing=1.5 → columns at X = 0, 1.5, 3.0
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", Vec3.Zero,
                3, 1.5f, 2.0f,
                [new PlacedShape("s1", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);

        // X=0, Z=-3 for User; X=1.5, Z=-3 for Sys; X=3, Z=-3 for Nice
        Assert.Contains("0, 0.01, -3)", tscn);
        Assert.Contains("1.5, 0.01, -3)", tscn);
        Assert.Contains("3, 0.01, -3)", tscn);
        // Confirm old inside-bezel position is gone
        Assert.DoesNotContain("0.01, -0.8)", tscn);
    }

    [Fact]
    public void Write_WithCamera_EmitsCameraNode()
    {
        var camera = new CameraSetup(new Vec3(5, 8, 12), new Vec3(2, 1.5f, -4));
        var tscn = TscnWriter.Write(MinimalLayout(), camera: camera);
        Assert.Contains("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]", tscn);
        Assert.Contains("camera_orbit.gd", tscn);
    }

    [Fact]
    public void Write_WithCamera_BakesOrbitCenterIntoNode()
    {
        var camera = new CameraSetup(new Vec3(5, 8, 12), new Vec3(2, 1.5f, -4));
        var tscn = TscnWriter.Write(MinimalLayout(), camera: camera);
        Assert.Contains("orbit_center = Vector3(2, 1.5, -4)", tscn);
    }

    [Fact]
    public void Write_WithoutCamera_NoCameraNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.DoesNotContain("Camera3D", tscn);
    }

    [Fact]
    public void Write_RootNode_HasControllerScript()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("host_view_controller.gd", tscn);
        Assert.Contains("controller_script", tscn);
    }

    [Fact]
    public void Write_HasMetricPollerChildNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"MetricPoller\" type=\"Node\" parent=\".\"]", tscn);
        Assert.Contains("MetricPoller.cs", tscn);
    }

    [Fact]
    public void Write_HasSceneBinderChildNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"SceneBinder\" type=\"Node\" parent=\".\"]", tscn);
        Assert.Contains("SceneBinder.cs", tscn);
    }

    [Fact]
    public void Write_MetricPoller_HasEndpointProperty()
    {
        var tscn = TscnWriter.Write(MinimalLayout(), "http://my-pcp:44322");
        Assert.Contains("Endpoint = \"http://my-pcp:44322\"", tscn);
    }

    [Fact]
    public void Write_MetricPoller_DefaultEndpoint_IsLocalhost()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("Endpoint = \"http://localhost:44322\"", tscn);
    }

    [Fact]
    public void Write_ShapeLabel_HasIncreasedFontSize()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Load", "Load", Vec3.Zero, null, null, null,
                [new PlacedShape("Load_1m", ShapeType.Bar, new Vec3(0, 0, 0),
                    "kernel.all.load", "1 minute", "1m",
                    new RgbColour(0.388f, 0.400f, 0.945f),
                    0f, 10f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("[node name=\"Load_1mLabel\" type=\"Label3D\"", tscn);
        Assert.Contains("font_size = 40", tscn);
        Assert.Contains("pixel_size = 0.01", tscn);
    }

    [Fact]
    public void Write_GridColumnHeader_HasIncreasedFontSize()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
                3, 1.5f, 2.0f,
                [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Per_CPUColLabel0", tscn);
        Assert.DoesNotContain("font_size = 24", tscn);
        Assert.Contains("font_size = 40", tscn);
    }

    [Fact]
    public void Write_GridRowHeader_HasIncreasedFontSize()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", new Vec3(0, 0, -8),
                3, 1.5f, 2.0f,
                [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Per_CPURowLabel0", tscn);
        Assert.DoesNotContain("font_size = 24", tscn);
        Assert.Contains("font_size = 40", tscn);
    }

    [Fact]
    public void Write_GridZone_RowHeaders_AreOnRightSide_BeyondLastColumn()
    {
        // 3 metrics, colSpacing=1.5, shapeWidth=0.8, rightOffset=0.5
        // → X = (3-1)*1.5 + 0.8 + 0.5 = 4.3
        // 2 instances, rowSpacing=2.0 → Z = 0 for cpu0, Z = -2 for cpu1
        var layout = new SceneLayout("testhost", [
            new PlacedZone("Per_CPU", "Per-CPU", Vec3.Zero,
                3, 1.5f, 2.0f,
                [new PlacedShape("s1", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                GroundWidth: 5f, GroundDepth: 8f,
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);

        Assert.Contains("4.3, 0.01, 0)", tscn);   // cpu0 at Z=0
        Assert.Contains("4.3, 0.01, -2)", tscn);  // cpu1 at Z=-2
        // Confirm old left-side position is gone
        Assert.DoesNotContain("-0.8, 0.01,", tscn);
    }

    [Fact]
    public void Write_HasTimestampLabelNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"TimestampLabel\" type=\"Label3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_TimestampLabel_IsFlat_WithNeonOrangeAndLargeFont()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        // Flat on floor: rotated -90° around X, centred at scene X=0, between rows at Z=-4
        Assert.Contains("Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0.02, -4)", tscn);
        Assert.Contains("font_size = 96", tscn);
        Assert.Contains("pixel_size = 0.02", tscn);
        Assert.Contains("outline_size = 8", tscn);
        // Orange: f97316 = (0.976, 0.451, 0.086)
        Assert.Contains("modulate = Color(0.976, 0.451, 0.086, 1)", tscn);
    }

    [Fact]
    public void Write_TimestampLabel_HasPcpBindableForTimestampMetric()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("MetricName = \"pmview.meta.timestamp\"", tscn);
        Assert.Contains("TargetProperty = \"text\"", tscn);
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"TimestampLabel\"]", tscn);
    }

    [Fact]
    public void Write_HasHostnameLabelNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"HostnameLabel\" type=\"Label3D\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HostnameLabel_IsBillboard_FloatingAtYTen()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("billboard = 1", tscn);
        Assert.Contains("font_size = 128", tscn);
        Assert.Contains("outline_size = 12", tscn);
        Assert.Contains("uppercase = true", tscn);
        // The HostnameLabel's transform places it at Y=10, directly above scene centre.
        Assert.Contains("[node name=\"HostnameLabel\" type=\"Label3D\" parent=\".\"]", tscn);
        Assert.Contains("Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 10, 0)", tscn);
    }

    [Fact]
    public void Write_HostnameLabel_HasPcpBindableForHostnameMetric()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("MetricName = \"pmview.meta.hostname\"", tscn);
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"HostnameLabel\"]", tscn);
    }

    [Fact]
    public void Write_MetricPoller_HasHostnameProperty()
    {
        var layout = new SceneLayout("my-server", MinimalLayout().Zones);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Hostname = \"my-server\"", tscn);
    }
}
