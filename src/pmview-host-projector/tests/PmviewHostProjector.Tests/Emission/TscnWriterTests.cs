using Xunit;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Tests.Emission;

public class TscnWriterTests
{
    private static SceneLayout MinimalLayout() =>
        new("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: new Vec3(0, 0, 0),
                ColumnSpacing: null, RowSpacing: null,
                Items: [
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
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"CPU/CPUGrid/CPU_User\"]", tscn);
        Assert.Contains("script = ExtResource(\"bindable_script\")", tscn);
        Assert.Contains("PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(", tscn);
    }

    [Fact]
    public void Write_CylinderShape_UsesGroundedCylinder_WithBuildingBlocksPath()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "Disk", ZoneLabel: "Disk", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("Disk_Read", ShapeType.Cylinder, Vec3.Zero,
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
            new PlacedZone(
                Name: "Load", ZoneLabel: "Load", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("Load_1min", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.load", "1 minute", null,
                    new RgbColour(0.388f, 0.400f, 0.945f),
                    0f, 10f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("InstanceName = \"1 minute\"", tscn);
    }

    [Fact]
    public void Write_ZoneTransformHasPosition()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: new Vec3(2.5f, 0, 0),
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, new Vec3(1.0f, 0, 0),
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
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f),
                    0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("colour = Color(0.976, 0.451, 0.086, 1)", tscn);
    }

    [Fact]
    public void Write_RotatedZone_EmitsYRotationTransform()
    {
        // Ry(90°) rotation: Transform3D(0, 0, -1, 0, 1, 0, 1, 0, 0, tx, 0, tz)
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "System", ZoneLabel: "System", Position: new Vec3(2f, 0, 0),
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("System_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                RotateYNinetyDeg: true)
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Transform3D(0, 0, -1, 0, 1, 0, 1, 0, 0, 2, 0, 0)", tscn);
    }

    [Fact]
    public void Write_RotatedZoneAtOrigin_StillEmitsYRotationTransform()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "System", ZoneLabel: "System", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("System_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, null,
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                RotateYNinetyDeg: true)
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("Transform3D(0, 0, -1, 0, 1, 0, 1, 0, 0, 0, 0, 0)", tscn);
    }

    // --- New MetricGroupNode / GroundBezel / MetricGrid tests ---

    [Fact]
    public void Write_Zone_EmitsMetricGroupNode_WithTitleText()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"CPU\" type=\"Node3D\" parent=\".\"]", tscn);
        Assert.Contains("metric_group_node.gd", tscn);
        Assert.Contains("title_text = \"CPU\"", tscn);
    }

    [Fact]
    public void Write_Zone_EmitsGroundBezelChild()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"CPUBezel\" type=\"MeshInstance3D\" parent=\"CPU\"]", tscn);
        Assert.Contains("ground_bezel.gd", tscn);
    }

    [Fact]
    public void Write_Zone_EmitsMetricGridChild()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"CPUGrid\" type=\"Node3D\" parent=\"CPU\"]", tscn);
        Assert.Contains("metric_grid.gd", tscn);
    }

    [Fact]
    public void Write_Zone_MetricGrid_HasMetricLabels()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.all.cpu.user", null, "User",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                MetricLabels: ["User", "Sys", "Nice"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("metric_labels = PackedStringArray(\"User\", \"Sys\", \"Nice\")", tscn);
    }

    [Fact]
    public void Write_Zone_MetricGrid_HasInstanceLabels()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(Name: "PerCPU", ZoneLabel: "Per-CPU", Position: Vec3.Zero,
                ColumnSpacing: 2.0f, RowSpacing: 2.5f,
                Items: [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("instance_labels = PackedStringArray(\"cpu0\", \"cpu1\")", tscn);
    }

    [Fact]
    public void Write_Zone_MetricGrid_HasColumnSpacing()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(Name: "PerCPU", ZoneLabel: "Per-CPU", Position: Vec3.Zero,
                ColumnSpacing: 2.0f, RowSpacing: 2.5f,
                Items: [new PlacedShape("PerCPU_cpu0_User", ShapeType.Bar, Vec3.Zero,
                    "kernel.percpu.cpu.user", "cpu0", "cpu0",
                    new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f)],
                MetricLabels: ["User", "Sys", "Nice"],
                InstanceLabels: ["cpu0", "cpu1"])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("column_spacing = 2", tscn);
        Assert.Contains("row_spacing = 2.5", tscn);
    }

    [Fact]
    public void Write_Shapes_AreChildrenOfMetricGrid()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("parent=\"CPU/CPUGrid\"", tscn);
    }

    [Fact]
    public void Write_Stack_IsChildOfMetricGrid()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("[node name=\"CpuStack\" type=\"Node3D\" parent=\"CPU/CPUGrid\"]", tscn);
    }

    // --- Camera tests ---

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

    // --- Controller / poller / binder tests ---

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

    // --- Ambient labels ---

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
        Assert.Contains("Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0.02, -4)", tscn);
        Assert.Contains("font_size = 96", tscn);
        Assert.Contains("pixel_size = 0.02", tscn);
        Assert.Contains("outline_size = 8", tscn);
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

    // --- load_steps ---

    [Fact]
    public void Write_LoadSteps_EqualsExtResourcesPlusSubResourcesPlusWorldEnv()
    {
        // MinimalLayout: 1 Bar shape, no camera.
        // ext_resources (9): controller_script, metric_poller_script, scene_binder_script,
        //                     metric_group_script, metric_grid_script, ground_bezel_script,
        //                     bar_scene, bindable_script, binding_res_script
        // sub_resources (1): binding for CPU_User
        // ambient labels (2): TimestampLabel, HostnameLabel
        // WorldEnvironment (1)
        // = 9 + 1 + 2 + 1 = 13
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("load_steps=13 ", tscn);
    }

    // --- PlacedStack emission tests ---

    private static SceneLayout LayoutWithCpuStack() =>
        new("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [
                    new PlacedStack("CpuStack", Vec3.Zero,
                    [
                        new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                            "kernel.all.cpu.user", null, "User",
                            new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f),
                        new PlacedShape("CPU_Sys", ShapeType.Bar, Vec3.Zero,
                            "kernel.all.cpu.sys", null, "Sys",
                            new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f),
                        new PlacedShape("CPU_Nice", ShapeType.Bar, Vec3.Zero,
                            "kernel.all.cpu.nice", null, "Nice",
                            new RgbColour(0.976f, 0.451f, 0.086f), 0f, 100f, 0.2f, 5.0f),
                    ], StackMode.Proportional)
                ])
        ]);

    [Fact]
    public void Write_PlacedStack_EmitsStackGroupNode_WithScript()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("[node name=\"CpuStack\" type=\"Node3D\" parent=\"CPU/CPUGrid\"]", tscn);
        Assert.Contains("stack_group_node.gd", tscn);
    }

    [Fact]
    public void Write_PlacedStack_StackModeProportional_EmitsZero()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("stack_mode = 0", tscn);
    }

    [Fact]
    public void Write_PlacedStack_StackModeNormalised_EmitsOne()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "CPU", ZoneLabel: "CPU", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [
                    new PlacedStack("CpuStack", Vec3.Zero,
                    [
                        new PlacedShape("CPU_User", ShapeType.Bar, Vec3.Zero,
                            "kernel.all.cpu.user", null, null,
                            new RgbColour(1f, 0f, 0f), 0f, 100f, 0.2f, 5.0f),
                    ], StackMode.Normalised)
                ])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("stack_mode = 1", tscn);
    }

    [Fact]
    public void Write_PlacedStack_EmitsEachMemberBarWithPcpBindable()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("parent=\"CPU/CPUGrid/CpuStack\"", tscn);
        Assert.Contains("[node name=\"CPU_User\"", tscn);
        Assert.Contains("[node name=\"CPU_Sys\"", tscn);
        Assert.Contains("[node name=\"CPU_Nice\"", tscn);
        Assert.Contains("MetricName = \"kernel.all.cpu.user\"", tscn);
        Assert.Contains("MetricName = \"kernel.all.cpu.sys\"", tscn);
        Assert.Contains("MetricName = \"kernel.all.cpu.nice\"", tscn);
    }

    [Fact]
    public void Write_PlacedStack_MemberPcpBindables_AreChildrenOfStack()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"CPU/CPUGrid/CpuStack/CPU_User\"]", tscn);
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"CPU/CPUGrid/CpuStack/CPU_Sys\"]", tscn);
        Assert.Contains("[node name=\"PcpBindable\" type=\"Node\" parent=\"CPU/CPUGrid/CpuStack/CPU_Nice\"]", tscn);
    }

    [Fact]
    public void Write_PlacedStack_RegistersStackGroupScript_AsExtResource()
    {
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("res://addons/pmview-bridge/building_blocks/stack_group_node.gd", tscn);
    }

    [Fact]
    public void Write_LoadSteps_WithPlacedStack_CountsCorrectly()
    {
        // Stack with 3 members:
        // ext_resources (10): controller, metric_poller, scene_binder,
        //                      metric_group_script, metric_grid_script, ground_bezel_script,
        //                      bar_scene, bindable, binding_res, stack_group_script
        // sub_resources (3): one binding per member
        // ambient (2), WorldEnv (1)
        // = 10 + 3 + 2 + 1 = 16
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("load_steps=16 ", tscn);
    }
}
