using System.Globalization;
using System.Text;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Emission;

/// <summary>
/// Emits a Godot .tscn file from a SceneLayout.
/// Each shape gets a PcpBindable child node with PcpBindingResource sub_resources.
/// Each zone with non-zero ground extent gets a dark-grey BoxMesh ground bezel.
/// </summary>
public static class TscnWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write(SceneLayout layout,
        string pmproxyEndpoint = "http://localhost:44322",
        CameraSetup? camera = null)
    {
        var sb = new StringBuilder();
        var registry = new ExtResourceRegistry();
        RegisterControllerResources(registry);
        if (camera != null)
            registry.Require("camera_orbit_script", "Script",
                "res://addons/pmview-bridge/camera_orbit.gd");
        var subResources = CollectSubResources(layout, registry);
        var bezelResources = CollectBezelSubResources(layout);
        var ambientLabels = BuildAmbientLabels();

        WriteHeader(sb, registry, subResources, bezelResources, ambientLabels);
        WriteExtResources(sb, registry);
        WriteSubResources(sb, subResources);
        WriteBezelSubResources(sb, bezelResources);
        WriteAmbientSubResources(sb, ambientLabels);
        WriteWorldEnvironmentSubResource(sb);
        WriteNodes(sb, layout, registry, subResources, bezelResources, ambientLabels, pmproxyEndpoint, camera);

        return sb.ToString();
    }

    private static void RegisterControllerResources(ExtResourceRegistry registry)
    {
        registry.Require("controller_script", "Script",
            "res://addons/pmview-bridge/host_view_controller.gd");
        registry.Require("metric_poller_script", "Script",
            "res://addons/pmview-bridge/MetricPoller.cs");
        registry.Require("scene_binder_script", "Script",
            "res://addons/pmview-bridge/SceneBinder.cs");
    }

    // --- resource collection ---

    private static List<SubResourceEntry> CollectSubResources(SceneLayout layout, ExtResourceRegistry registry)
    {
        var list = new List<SubResourceEntry>();

        foreach (var zone in layout.Zones)
        {
            if (zone.GridColumns.HasValue)
                registry.Require("grid_script", "Script", "res://addons/pmview-bridge/building_blocks/grid_layout_3d.gd");

            foreach (var item in zone.Items)
            {
                var shapes = item switch
                {
                    PlacedStack stack => stack.Members,
                    PlacedShape shape => (IReadOnlyList<PlacedShape>)[shape],
                    _                 => []
                };

                if (item is PlacedStack)
                    registry.Require("stack_group_script", "Script",
                        "res://addons/pmview-bridge/building_blocks/stack_group_node.gd");

                foreach (var shape in shapes)
                {
                    var sceneId = SceneExtResourceId(shape.Shape);
                    registry.Require(sceneId, "PackedScene", SceneExtResourcePath(shape.Shape));
                    registry.Require("bindable_script", "Script", "res://addons/pmview-bridge/PcpBindable.cs");
                    registry.Require("binding_res_script", "Script", "res://addons/pmview-bridge/PcpBindingResource.cs");

                    list.Add(new SubResourceEntry(
                        Id: SubResourceId(shape.NodeName),
                        MetricName: shape.MetricName,
                        InstanceName: shape.InstanceName,
                        SourceRangeMin: shape.SourceRangeMin,
                        SourceRangeMax: shape.SourceRangeMax,
                        TargetRangeMin: shape.TargetRangeMin,
                        TargetRangeMax: shape.TargetRangeMax));
                }
            }
        }

        return list;
    }

    private static List<BezelSubResources> CollectBezelSubResources(SceneLayout layout)
        => layout.Zones
            .Where(z => z.GroundWidth > 0f && z.GroundDepth > 0f)
            .Select(z => new BezelSubResources(
                z.Name,
                $"bezel_mesh_{ToResourceId(z.Name)}",
                $"bezel_mat_{ToResourceId(z.Name)}",
                z.GroundWidth,
                z.GroundDepth))
            .ToList();

    // --- header ---

    private static void WriteHeader(StringBuilder sb, ExtResourceRegistry registry,
        List<SubResourceEntry> subResources, List<BezelSubResources> bezelResources,
        IReadOnlyList<AmbientLabelSpec> ambientLabels)
    {
        // +1 for WorldEnvironment Environment sub_resource only.
        // The root scene node is NOT a resource — Godot format 3 does not count it.
        var loadSteps = registry.Count + subResources.Count + bezelResources.Count * 2 + ambientLabels.Count + 1;
        sb.AppendLine($"[gd_scene load_steps={loadSteps} format=3]");
        sb.AppendLine();
    }

    // --- ext_resources ---

    private static void WriteExtResources(StringBuilder sb, ExtResourceRegistry registry)
    {
        foreach (var (id, type, path) in registry.All())
            sb.AppendLine($"[ext_resource type=\"{type}\" path=\"{path}\" id=\"{id}\"]");

        sb.AppendLine();
    }

    // --- sub_resources ---

    private static void WriteSubResources(StringBuilder sb, List<SubResourceEntry> entries)
    {
        foreach (var entry in entries)
        {
            sb.AppendLine($"[sub_resource type=\"Resource\" id=\"{entry.Id}\"]");
            sb.AppendLine("script = ExtResource(\"binding_res_script\")");
            sb.AppendLine("resource_local_to_scene = true");
            sb.AppendLine($"MetricName = \"{entry.MetricName}\"");
            sb.AppendLine("TargetProperty = \"height\"");
            sb.AppendLine($"SourceRangeMin = {F(entry.SourceRangeMin)}");
            sb.AppendLine($"SourceRangeMax = {F(entry.SourceRangeMax)}");
            sb.AppendLine($"TargetRangeMin = {F(entry.TargetRangeMin)}");
            sb.AppendLine($"TargetRangeMax = {F(entry.TargetRangeMax)}");

            if (entry.InstanceName is not null)
                sb.AppendLine($"InstanceName = \"{entry.InstanceName}\"");

            sb.AppendLine("InstanceId = -1");
            sb.AppendLine($"InitialValue = {F(entry.TargetRangeMin)}");
            sb.AppendLine();
        }
    }

    private static void WriteBezelSubResources(StringBuilder sb, List<BezelSubResources> bezels)
    {
        foreach (var bezel in bezels)
        {
            sb.AppendLine($"[sub_resource type=\"BoxMesh\" id=\"{bezel.MeshId}\"]");
            sb.AppendLine($"size = Vector3({F(bezel.Width)}, 0.02, {F(bezel.Depth)})");
            sb.AppendLine();

            sb.AppendLine($"[sub_resource type=\"StandardMaterial3D\" id=\"{bezel.MaterialId}\"]");
            sb.AppendLine("albedo_color = Color(0.3, 0.3, 0.3, 1)");
            sb.AppendLine();
        }
    }

    private static void WriteAmbientSubResources(StringBuilder sb,
        IReadOnlyList<AmbientLabelSpec> labels)
    {
        foreach (var label in labels)
        {
            sb.AppendLine($"[sub_resource type=\"Resource\" id=\"{label.SubResourceId}\"]");
            sb.AppendLine("script = ExtResource(\"binding_res_script\")");
            sb.AppendLine("resource_local_to_scene = true");
            sb.AppendLine($"MetricName = \"{label.MetricName}\"");
            sb.AppendLine("TargetProperty = \"text\"");
            sb.AppendLine("SourceRangeMin = 0");
            sb.AppendLine("SourceRangeMax = 1");
            sb.AppendLine("TargetRangeMin = 0");
            sb.AppendLine("TargetRangeMax = 1");
            sb.AppendLine("InstanceId = -1");
            sb.AppendLine("InitialValue = 0");
            sb.AppendLine();
        }
    }

    private static void WriteWorldEnvironmentSubResource(StringBuilder sb)
    {
        sb.AppendLine("[sub_resource type=\"Environment\" id=\"world_env\"]");
        sb.AppendLine("background_mode = 1");
        sb.AppendLine("background_color = Color(0.02, 0.02, 0.06, 1)");
        sb.AppendLine("ambient_light_source = 1");
        sb.AppendLine("ambient_light_color = Color(0.4, 0.4, 0.5, 1)");
        sb.AppendLine("ambient_light_energy = 0.5");
        sb.AppendLine();
    }

    // --- nodes ---

    private static void WriteNodes(StringBuilder sb, SceneLayout layout, ExtResourceRegistry registry,
        List<SubResourceEntry> subResources, List<BezelSubResources> bezelResources,
        IReadOnlyList<AmbientLabelSpec> ambientLabels,
        string pmproxyEndpoint, CameraSetup? camera)
    {
        sb.AppendLine("[node name=\"HostView\" type=\"Node3D\"]");
        sb.AppendLine("script = ExtResource(\"controller_script\")");
        sb.AppendLine();

        sb.AppendLine("[node name=\"MetricPoller\" type=\"Node\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"metric_poller_script\")");
        sb.AppendLine($"Endpoint = \"{pmproxyEndpoint}\"");
        sb.AppendLine($"Hostname = \"{layout.Hostname}\"");
        sb.AppendLine();

        sb.AppendLine("[node name=\"SceneBinder\" type=\"Node\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"scene_binder_script\")");
        sb.AppendLine();

        sb.AppendLine("[node name=\"WorldEnvironment\" type=\"WorldEnvironment\" parent=\".\"]");
        sb.AppendLine("environment = SubResource(\"world_env\")");
        sb.AppendLine();

        // Key light: front-above at ~45° pitch, illuminates camera-facing surfaces
        sb.AppendLine("[node name=\"KeyLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 0, 0)");
        sb.AppendLine("light_energy = 1.2");
        sb.AppendLine("shadow_enabled = true");
        sb.AppendLine();

        // Fill light: from rear-above at ~30° pitch, lifts the back and side faces out of shadow
        sb.AppendLine("[node name=\"FillLight\" type=\"DirectionalLight3D\" parent=\".\"]");
        sb.AppendLine("transform = Transform3D(-1, 0, 0, 0, 0.866, 0.5, 0, 0.5, -0.866, 0, 0, 0)");
        sb.AppendLine("light_energy = 0.5");
        sb.AppendLine();

        foreach (var zone in layout.Zones)
            WriteZone(sb, zone, registry, subResources, bezelResources);

        WriteAmbientLabels(sb, ambientLabels);

        if (camera != null)
            WriteCameraNode(sb, camera);
    }

    private static void WriteAmbientLabels(StringBuilder sb,
        IReadOnlyList<AmbientLabelSpec> labels)
    {
        foreach (var label in labels)
        {
            sb.AppendLine($"[node name=\"{label.NodeName}\" type=\"Label3D\" parent=\".\"]");

            if (label.IsFlatOnFloor)
                // Rotated -90° around X (lies flat), centred at X=0, between rows at Z=-4
                sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, {F(label.YPosition)}, -4)");
            else
                sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, {F(label.YPosition)}, 0)");

            if (!label.IsFlatOnFloor)
                sb.AppendLine("billboard = 1");

            sb.AppendLine($"pixel_size = {F(label.PixelSize)}");
            sb.AppendLine($"font_size = {label.FontSize}");
            sb.AppendLine($"outline_size = {label.OutlineSize}");
            sb.AppendLine("outline_modulate = Color(0, 0, 0, 1)");

            if (label.Modulate != null)
                sb.AppendLine($"modulate = {label.Modulate}");

            if (label.Uppercase)
                sb.AppendLine("uppercase = true");

            sb.AppendLine("horizontal_alignment = 1");
            sb.AppendLine("text = \"\"");
            sb.AppendLine();

            sb.AppendLine($"[node name=\"PcpBindable\" type=\"Node\" parent=\"{label.NodeName}\"]");
            sb.AppendLine("script = ExtResource(\"bindable_script\")");
            sb.AppendLine($"PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(\"{label.SubResourceId}\")])");
            sb.AppendLine();
        }
    }

    private static void WriteCameraNode(StringBuilder sb, CameraSetup camera)
    {
        var transform = BuildLookAtTransform(camera.Position, camera.LookAtTarget);
        var c = camera.LookAtTarget;
        sb.AppendLine("[node name=\"Camera3D\" type=\"Camera3D\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"camera_orbit_script\")");
        sb.AppendLine($"transform = {transform}");
        sb.AppendLine($"orbit_center = Vector3({F(c.X)}, {F(c.Y)}, {F(c.Z)})");
        sb.AppendLine();
    }

    private static string BuildLookAtTransform(Vec3 eye, Vec3 target)
    {
        var fwd = Normalise(eye.X - target.X, eye.Y - target.Y, eye.Z - target.Z);
        var right = Normalise(fwd.Z, 0f, -fwd.X);
        var up = (
            X: fwd.Y * right.Z - fwd.Z * right.Y,
            Y: fwd.Z * right.X - fwd.X * right.Z,
            Z: fwd.X * right.Y - fwd.Y * right.X);
        return $"Transform3D({F(right.X)}, {F(up.X)}, {F(fwd.X)}, {F(right.Y)}, {F(up.Y)}, {F(fwd.Y)}, {F(right.Z)}, {F(up.Z)}, {F(fwd.Z)}, {F(eye.X)}, {F(eye.Y)}, {F(eye.Z)})";
    }

    private static (float X, float Y, float Z) Normalise(float x, float y, float z)
    {
        var len = MathF.Sqrt(x * x + y * y + z * z);
        return len > 0f ? (x / len, y / len, z / len) : (0f, 0f, 1f);
    }

    private static void WriteZone(StringBuilder sb, PlacedZone zone, ExtResourceRegistry registry,
        List<SubResourceEntry> subResources, List<BezelSubResources> bezelResources)
    {
        WriteZoneContainerNode(sb, zone, registry);
        WriteZoneLabelNode(sb, zone);
        WriteGroundBezel(sb, zone, bezelResources);

        if (zone.GridColumns.HasValue)
        {
            WriteGridColumnHeaders(sb, zone);
            WriteGridRowHeaders(sb, zone);
        }

        foreach (var item in zone.Items)
        {
            switch (item)
            {
                case PlacedStack stack:
                    WriteStack(sb, stack, zone, registry, subResources);
                    break;
                case PlacedShape shape:
                    WriteShape(sb, shape, zone, registry, subResources);
                    if (!zone.GridColumns.HasValue && shape.DisplayLabel is not null)
                        WriteShapeLabel(sb, shape, zone.Name, zone.RotateYNinetyDeg);
                    break;
            }
        }
    }

    private static void WriteZoneContainerNode(StringBuilder sb, PlacedZone zone, ExtResourceRegistry registry)
    {
        var pos = zone.Position;
        sb.AppendLine($"[node name=\"{zone.Name}\" type=\"Node3D\" parent=\".\"]");

        if (zone.RotateYNinetyDeg)
            // Ry(90°): local +Z → world +X, local +X → world -Z
            sb.AppendLine($"transform = Transform3D(0, 0, -1, 0, 1, 0, 1, 0, 0, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");
        else if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");

        if (zone.GridColumns.HasValue)
        {
            sb.AppendLine("script = ExtResource(\"grid_script\")");
            sb.AppendLine($"columns = {zone.GridColumns.Value}");

            if (zone.GridColumnSpacing.HasValue)
                sb.AppendLine($"column_spacing = {F(zone.GridColumnSpacing.Value)}");

            if (zone.GridRowSpacing.HasValue)
                sb.AppendLine($"row_spacing = {F(zone.GridRowSpacing.Value)}");
        }

        sb.AppendLine();
    }

    private static void WriteZoneLabelNode(StringBuilder sb, PlacedZone zone)
    {
        float labelLocalX, labelLocalZ;
        string basisStr;

        if (zone.RotateYNinetyDeg)
        {
            // Zone Ry(-90°) basis rows: world_x = -local_z, world_z = local_x.
            // "In front" (world +Z) = local +X; centre label over world-X spread (= local Z span).
            labelLocalZ = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.Z) / 2f : 0f;
            labelLocalX = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.X) + 1.0f : 1.0f;
            basisStr = "0, -1, 0, 0, 0, 1, -1, 0, 0";
        }
        else
        {
            labelLocalX = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.X) / 2f : 0f;
            labelLocalZ = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.Z) + 1.0f : 1.0f;
            basisStr = "1, 0, 0, 0, 0, 1, 0, -1, 0";
        }

        sb.AppendLine($"[node name=\"{zone.Name}Label\" type=\"Label3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine($"transform = Transform3D({basisStr}, {F(labelLocalX)}, 0.01, {F(labelLocalZ)})");
        sb.AppendLine("pixel_size = 0.01");
        sb.AppendLine("font_size = 56");
        sb.AppendLine($"text = \"{zone.ZoneLabel}\"");
        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine();
    }

    private static void WriteGroundBezel(StringBuilder sb, PlacedZone zone, List<BezelSubResources> bezelResources)
    {
        var bezel = bezelResources.FirstOrDefault(b => b.ZoneName == zone.Name);
        if (bezel is null) return;

        float centreX, centreZ;
        if (zone.GridColumns.HasValue)
        {
            // Grid shapes are positioned by GridLayout3D at runtime — centre on the grid's visual extent
            var cols = zone.GridColumns.Value;
            var colSpacing = zone.GridColumnSpacing ?? 2.0f;
            var rows = zone.InstanceLabels?.Count ?? 1;
            var rowSpacing = zone.GridRowSpacing ?? 2.5f;
            centreX = (cols - 1) * colSpacing / 2f;
            centreZ = -(rows - 1) * rowSpacing / 2f;
        }
        else
        {
            centreX = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.X) / 2f : 0f;
            centreZ = zone.Items.Count > 0 ? zone.Items.Max(s => s.LocalPosition.Z) / 2f : 0f;
        }

        sb.AppendLine($"[node name=\"{zone.Name}Ground\" type=\"MeshInstance3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(centreX)}, -0.01, {F(centreZ)})");
        sb.AppendLine($"mesh = SubResource(\"{bezel.MeshId}\")");
        sb.AppendLine($"surface_material_override/0 = SubResource(\"{bezel.MaterialId}\")");
        sb.AppendLine();
    }

    private static void WriteShape(StringBuilder sb, PlacedShape shape, PlacedZone zone,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources,
        string? parentOverride = null)
    {
        var sceneId = SceneExtResourceId(shape.Shape);
        var pos = shape.LocalPosition;
        var zonePath = parentOverride ?? zone.Name;
        var shapePath = $"{zonePath}/{shape.NodeName}";

        sb.AppendLine($"[node name=\"{shape.NodeName}\" parent=\"{zonePath}\" instance=ExtResource(\"{sceneId}\")]");

        if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");

        sb.AppendLine($"colour = Color({F(shape.Colour.R)}, {F(shape.Colour.G)}, {F(shape.Colour.B)}, 1)");
        sb.AppendLine();

        var subResId = SubResourceId(shape.NodeName);
        sb.AppendLine($"[node name=\"PcpBindable\" type=\"Node\" parent=\"{shapePath}\"]");
        sb.AppendLine("script = ExtResource(\"bindable_script\")");
        sb.AppendLine($"PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(\"{subResId}\")])");

        sb.AppendLine();
    }

    private static void WriteStack(StringBuilder sb, PlacedStack stack, PlacedZone zone,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        var pos = stack.LocalPosition;
        var stackPath = $"{zone.Name}/{stack.GroupName}";

        sb.AppendLine($"[node name=\"{stack.GroupName}\" type=\"Node3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine("script = ExtResource(\"stack_group_script\")");

        // StackMode enum value: 0 = PROPORTIONAL, 1 = NORMALISED (mirrors GDScript enum order).
        var modeValue = stack.Mode == StackMode.Proportional ? 0 : 1;
        sb.AppendLine($"stack_mode = {modeValue}");

        if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");

        sb.AppendLine();

        foreach (var member in stack.Members)
            WriteShape(sb, member, zone, registry, subResources, parentOverride: stackPath);
    }

    private static void WriteGridColumnHeaders(StringBuilder sb, PlacedZone zone)
    {
        if (zone.MetricLabels is null || zone.MetricLabels.Count == 0) return;
        var colSpacing = zone.GridColumnSpacing ?? 2.0f;
        var rowCount = zone.InstanceLabels?.Count ?? 1;
        var rowSpacing = zone.GridRowSpacing ?? 2.5f;
        var z = -(rowCount - 1) * rowSpacing - 1.0f;

        for (var i = 0; i < zone.MetricLabels.Count; i++)
        {
            var x = i * colSpacing;
            sb.AppendLine($"[node name=\"{zone.Name}ColLabel{i}\" type=\"Label3D\" parent=\"{zone.Name}\"]");
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(x)}, 0.01, {F(z)})");
            sb.AppendLine("pixel_size = 0.01");
            sb.AppendLine("font_size = 40");
            sb.AppendLine($"text = \"{zone.MetricLabels[i]}\"");
            sb.AppendLine("horizontal_alignment = 1");
            sb.AppendLine();
        }
    }

    private static void WriteGridRowHeaders(StringBuilder sb, PlacedZone zone)
    {
        if (zone.InstanceLabels is null || zone.InstanceLabels.Count == 0) return;
        var rowSpacing = zone.GridRowSpacing ?? 2.5f;
        var colCount = zone.MetricLabels?.Count ?? 1;
        var colSpacing = zone.GridColumnSpacing ?? 2.0f;
        // ShapeWidth matches the grounded_bar default X scale (see building_blocks/grounded_bar.tscn)
        const float ShapeWidth = 0.8f;
        const float RightEdgeOffset = 0.5f;
        var x = (colCount - 1) * colSpacing + ShapeWidth + RightEdgeOffset;

        for (var i = 0; i < zone.InstanceLabels.Count; i++)
        {
            // Negate after computing; -(0 * rowSpacing) is -0f but F() formats it as "0" on InvariantCulture
            var z = -(i * rowSpacing);
            sb.AppendLine($"[node name=\"{zone.Name}RowLabel{i}\" type=\"Label3D\" parent=\"{zone.Name}\"]");
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, {F(x)}, 0.01, {F(z)})");
            sb.AppendLine("pixel_size = 0.01");
            sb.AppendLine("font_size = 40");
            sb.AppendLine($"text = \"{zone.InstanceLabels[i]}\"");
            sb.AppendLine("horizontal_alignment = 1");
            sb.AppendLine();
        }
    }

    private static void WriteShapeLabel(StringBuilder sb, PlacedShape shape, string zoneName,
        bool rotateYNinetyDeg = false)
    {
        var pos = shape.LocalPosition;
        float labelX, labelZ;
        string transform;

        if (rotateYNinetyDeg)
        {
            // Zone Ry(-90°) basis rows: world_x = -local_z, world_z = local_x.
            // Left (world -X) = local +Z; Right (world +X) = local -Z; Front (world +Z) = local +X.
            (labelX, labelZ) = shape.LabelPlacement switch
            {
                LabelPlacement.Left  => (pos.X,        pos.Z + 0.9f),
                LabelPlacement.Right => (pos.X,        pos.Z - 0.9f),
                _                    => (pos.X + 0.6f, pos.Z),
            };
            transform = shape.LabelPlacement switch
            {
                // Left: vertical label facing camera, text reads up world-Y axis
                LabelPlacement.Left  => "0, 1, 0, 0, 0, 1, 1, 0, 0",
                // Right: flat on floor, reads along world +X (same as upright Front)
                LabelPlacement.Right => "1, 0, 0, 0, 0, 1, 0, -1, 0",
                // Front/default: vertical label facing camera, text reads down world-Y axis
                _                    => "0, -1, 0, 0, 0, 1, -1, 0, 0",
            };
        }
        else
        {
            (labelX, labelZ, transform) = shape.LabelPlacement switch
            {
                // Left/Right labels align with the Z axis — Ry(-90°) flat: text reads along local +Z
                LabelPlacement.Left  => (pos.X - 0.9f, pos.Z,        "0, 0, 1, -1, 0, 0, 0, -1, 0"),
                LabelPlacement.Right => (pos.X + 0.9f, pos.Z,        "0, 0, 1, -1, 0, 0, 0, -1, 0"),
                // Front labels align with the X axis — standard flat: text reads along local +X
                _                    => (pos.X,         pos.Z + 0.6f, "1, 0, 0, 0, 0, 1, 0, -1, 0"),
            };
        }
        sb.AppendLine($"[node name=\"{shape.NodeName}Label\" type=\"Label3D\" parent=\"{zoneName}\"]");
        sb.AppendLine($"transform = Transform3D({transform}, {F(labelX)}, 0.01, {F(labelZ)})");
        sb.AppendLine("pixel_size = 0.01");
        sb.AppendLine("font_size = 40");
        sb.AppendLine($"text = \"{shape.DisplayLabel}\"");
        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine();
    }

    // --- helpers ---

    private static string SceneExtResourceId(ShapeType shape) => shape switch
    {
        ShapeType.Cylinder => "cylinder_scene",
        _ => "bar_scene"
    };

    private static string SceneExtResourcePath(ShapeType shape) => shape switch
    {
        ShapeType.Cylinder => "res://addons/pmview-bridge/building_blocks/grounded_cylinder.tscn",
        _ => "res://addons/pmview-bridge/building_blocks/grounded_bar.tscn"
    };

    private static string SubResourceId(string nodeName) => $"binding_{nodeName}";

    // Normalise -0f → 0f so the tscn output never contains the ugly "-0" literal.
    private static string F(float value) => (value == 0f ? 0f : value).ToString(Inv);

    private static string ToResourceId(string name) =>
        string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_'));

    // --- private supporting types ---

    private record SubResourceEntry(
        string Id,
        string MetricName,
        string? InstanceName,
        float SourceRangeMin,
        float SourceRangeMax,
        float TargetRangeMin,
        float TargetRangeMax);

    private record BezelSubResources(
        string ZoneName,
        string MeshId,
        string MaterialId,
        float Width,
        float Depth);

    private record AmbientLabelSpec(
        string NodeName,
        string MetricName,
        string SubResourceId,
        bool IsFlatOnFloor,
        int FontSize,
        float PixelSize,
        int OutlineSize,
        string? Modulate,
        bool Uppercase,
        float YPosition);

    private static IReadOnlyList<AmbientLabelSpec> BuildAmbientLabels() =>
    [
        new("TimestampLabel", "pmview.meta.timestamp", "binding_TimestampLabel",
            IsFlatOnFloor: true,
            FontSize: 96, PixelSize: 0.02f, OutlineSize: 8,
            Modulate: "Color(0.976, 0.451, 0.086, 1)",
            Uppercase: false, YPosition: 0.02f),
        new("HostnameLabel", "pmview.meta.hostname", "binding_HostnameLabel",
            IsFlatOnFloor: false,
            FontSize: 128, PixelSize: 0.015f, OutlineSize: 12,
            Modulate: null,
            Uppercase: true, YPosition: 10f),
    ];

    /// <summary>
    /// Tracks ext_resources, ensuring each id is registered only once (first-write wins).
    /// </summary>
    private class ExtResourceRegistry
    {
        private readonly List<(string Id, string Type, string Path)> _resources = new();
        private readonly HashSet<string> _seen = new();

        public int Count => _resources.Count;

        public void Require(string id, string type, string path)
        {
            if (_seen.Add(id))
                _resources.Add((id, type, path));
        }

        public IEnumerable<(string Id, string Type, string Path)> All() => _resources;
    }
}
