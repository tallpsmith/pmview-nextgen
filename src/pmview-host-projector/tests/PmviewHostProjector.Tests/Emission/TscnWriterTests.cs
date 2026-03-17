using Xunit;
using PmviewHostProjector.Emission;
using PmviewProjectionCore.Layout;
using PmviewProjectionCore.Models;
using PmviewProjectionCore.Profiles;

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

    // --- Camera tests (camera now lives in project main.tscn, not per-scene) ---

    [Fact]
    public void Write_NeverEmitsCameraNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.DoesNotContain("Camera3D", tscn);
        Assert.DoesNotContain("camera_orbit", tscn);
    }

    [Fact]
    public void Write_DoesNotEmitLightsOrEnvironment()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.DoesNotContain("KeyLight", tscn);
        Assert.DoesNotContain("FillLight", tscn);
        Assert.DoesNotContain("WorldEnvironment", tscn);
        Assert.DoesNotContain("world_env", tscn);
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
    public void Write_LoadSteps_EqualsExtResourcesPlusSubResources()
    {
        // MinimalLayout: 1 Bar shape.
        // ext_resources (10): controller_script, metric_poller_script, scene_binder_script,
        //                      metric_group_script, metric_grid_script, ground_bezel_script,
        //                      bar_scene, bindable_script, binding_res_script,
        //                      range_tuning_panel_scene
        // sub_resources (1): binding for CPU_User
        // ambient labels (2): TimestampLabel, HostnameLabel
        // = 10 + 1 + 2 = 13
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
        // ext_resources (11): controller, metric_poller, scene_binder,
        //                      metric_group_script, metric_grid_script, ground_bezel_script,
        //                      bar_scene, bindable, binding_res, stack_group_script,
        //                      range_tuning_panel_scene
        // sub_resources (3): one binding per member
        // ambient (2)
        // = 11 + 3 + 2 = 16
        var tscn = TscnWriter.Write(LayoutWithCpuStack());
        Assert.Contains("load_steps=16 ", tscn);
    }

    // --- Placeholder / ghost shape tests ---

    [Fact]
    public void Write_PlaceholderShape_EmitsGhostProperty_NoBindingOrPcpBindable()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "Net", ZoneLabel: "Net-In", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("Net_Bytes", ShapeType.Bar, Vec3.Zero,
                    "network.all.in.bytes", null, "Bytes",
                    new RgbColour(0.5f, 0.5f, 0.5f),
                    0f, 125_000_000f, 0.2f, 5.0f,
                    IsPlaceholder: true)])
        ]);
        var tscn = TscnWriter.Write(layout);

        Assert.Contains("[node name=\"Net_Bytes\"", tscn);
        Assert.Contains("ghost = true", tscn);
        Assert.DoesNotContain("binding_Net_Bytes", tscn);
    }

    [Fact]
    public void Write_PlaceholderShape_DoesNotInflateLoadSteps()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "Net", ZoneLabel: "Net-In", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("Net_Bytes", ShapeType.Bar, Vec3.Zero,
                    "network.all.in.bytes", null, "Bytes",
                    new RgbColour(0.5f, 0.5f, 0.5f),
                    0f, 125_000_000f, 0.2f, 5.0f,
                    IsPlaceholder: true)])
        ]);
        var tscn = TscnWriter.Write(layout);

        // ext_resources (10): controller, metric_poller, scene_binder,
        //                      metric_group, metric_grid, ground_bezel,
        //                      bar_scene, bindable_script, binding_res_script,
        //                      range_tuning_panel_scene
        // sub_resources (0): placeholder has no binding
        // ambient (2): TimestampLabel, HostnameLabel
        // = 10 + 0 + 2 = 12
        Assert.Contains("load_steps=12 ", tscn);
    }

    [Fact]
    public void Write_MixedLiveAndPlaceholder_OnlyLiveGetsBinding()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "Net", ZoneLabel: "Net-In", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [
                    new PlacedShape("Net_Bytes", ShapeType.Bar, Vec3.Zero,
                        "network.all.in.bytes", null, "Bytes",
                        new RgbColour(0, 0, 1), 0f, 125_000_000f, 0.2f, 5.0f),
                    new PlacedShape("Net_Ghost", ShapeType.Bar, Vec3.Zero,
                        "network.all.in.packets", null, "Pkts",
                        new RgbColour(0.5f, 0.5f, 0.5f), 0f, 100_000f, 0.2f, 5.0f,
                        IsPlaceholder: true),
                ])
        ]);
        var tscn = TscnWriter.Write(layout);

        Assert.Contains("binding_Net_Bytes", tscn);
        Assert.DoesNotContain("binding_Net_Ghost", tscn);
        Assert.Contains("ghost = true", tscn);
        Assert.DoesNotContain("ghost = true", tscn.Split("Net_Bytes")[1].Split("Net_Ghost")[0]);
    }

    // --- ZoneName emission ---

    [Fact]
    public void Write_SubResource_ContainsZoneName()
    {
        var layout = new SceneLayout("testhost", [
            new PlacedZone(
                Name: "TestZone", ZoneLabel: "Test Zone", Position: Vec3.Zero,
                ColumnSpacing: null, RowSpacing: null,
                Items: [new PlacedShape("TZ_Metric", ShapeType.Bar, Vec3.Zero,
                    "test.metric.bytes", null, null,
                    new RgbColour(0.5f, 0.5f, 0.5f),
                    0f, 100f, 0.2f, 5.0f)])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("ZoneName = \"TestZone\"", tscn);
    }

    [Fact]
    public void Write_SubResource_StackMembers_ContainZoneName()
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
                    ], StackMode.Proportional)
                ])
        ]);
        var tscn = TscnWriter.Write(layout);
        Assert.Contains("ZoneName = \"CPU\"", tscn);
    }

    // --- Range tuning panel ---

    [Fact]
    public void Write_HasRangeTuningPanelExtResource()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("res://addons/pmview-bridge/ui/range_tuning_panel.tscn", tscn);
        Assert.Contains("range_tuning_panel_scene", tscn);
    }

    [Fact]
    public void Write_HasUILayerCanvasLayerNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"UILayer\" type=\"CanvasLayer\" parent=\".\"]", tscn);
    }

    [Fact]
    public void Write_HasRangeTuningPanelInstancedNode()
    {
        var tscn = TscnWriter.Write(MinimalLayout());
        Assert.Contains("[node name=\"RangeTuningPanel\" parent=\"UILayer\" instance=ExtResource(\"range_tuning_panel_scene\")]", tscn);
    }

    // --- macOS end-to-end integration test ---

    [Fact]
    public void Write_MacOsLayout_GhostNetworkShapes_AndDarwinMemoryMetrics()
    {
        var topology = new HostTopology(HostOs.MacOs, "macbook",
            ["cpu0", "cpu1"], ["disk0"], ["en0"],
            PhysicalMemoryBytes: 16_000_000_000L);
        var zones = MacOsProfile.GetZones();
        var layout = LayoutCalculator.Calculate(zones, topology);
        var tscn = TscnWriter.Write(layout);

        // Ghost shapes should have ghost = true
        Assert.Contains("ghost = true", tscn);

        // Memory zone should have Darwin-specific metrics
        Assert.Contains("mem.util.wired", tscn);
        Assert.Contains("mem.util.compressed", tscn);

        // No binding for ghost network metrics
        Assert.DoesNotContain("binding_Net_In_Bytes", tscn);
        Assert.DoesNotContain("binding_Net_Out_Bytes", tscn);

        // Real metrics should still have bindings
        Assert.Contains("kernel.all.cpu.sys", tscn);
    }
}
