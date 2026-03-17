using System.Globalization;
using System.Text;
using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Emission;

/// <summary>
/// Emits a Godot .tscn file from a SceneLayout.
/// Each zone becomes a MetricGroupNode with GroundBezel and MetricGrid children.
/// Shapes/stacks are children of the MetricGrid; MetricGrid handles layout at runtime.
/// </summary>
public static class TscnWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write(SceneLayout layout,
        string pmproxyEndpoint = "http://localhost:44322")
    {
        var sb = new StringBuilder();
        var registry = new ExtResourceRegistry();
        RegisterControllerResources(registry);
        var subResources = CollectSubResources(layout, registry);
        var ambientLabels = BuildAmbientLabels();

        WriteHeader(sb, registry, subResources, ambientLabels);
        WriteExtResources(sb, registry);
        WriteSubResources(sb, subResources);
        WriteAmbientSubResources(sb, ambientLabels);
        WriteNodes(sb, layout, registry, subResources, ambientLabels, pmproxyEndpoint);

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
        registry.Require("metric_group_script", "Script",
            "res://addons/pmview-bridge/building_blocks/metric_group_node.gd");
        registry.Require("metric_grid_script", "Script",
            "res://addons/pmview-bridge/building_blocks/metric_grid.gd");
        registry.Require("ground_bezel_script", "Script",
            "res://addons/pmview-bridge/building_blocks/ground_bezel.gd");
        registry.Require("bindable_script", "Script",
            "res://addons/pmview-bridge/PcpBindable.cs");
        registry.Require("binding_res_script", "Script",
            "res://addons/pmview-bridge/PcpBindingResource.cs");
        registry.Require("range_tuning_panel_scene", "PackedScene",
            "res://addons/pmview-bridge/ui/range_tuning_panel.tscn");
    }

    // --- resource collection ---

    private static List<SubResourceEntry> CollectSubResources(SceneLayout layout, ExtResourceRegistry registry)
    {
        var list = new List<SubResourceEntry>();

        foreach (var zone in layout.Zones)
        {
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

                    if (shape.IsPlaceholder)
                        continue;

                    registry.Require("bindable_script", "Script", "res://addons/pmview-bridge/PcpBindable.cs");
                    registry.Require("binding_res_script", "Script", "res://addons/pmview-bridge/PcpBindingResource.cs");

                    list.Add(new SubResourceEntry(
                        Id: SubResourceId(shape.NodeName),
                        MetricName: shape.MetricName,
                        InstanceName: shape.InstanceName,
                        SourceRangeMin: shape.SourceRangeMin,
                        SourceRangeMax: shape.SourceRangeMax,
                        TargetRangeMin: shape.TargetRangeMin,
                        TargetRangeMax: shape.TargetRangeMax,
                        ZoneName: zone.Name));
                }
            }
        }

        return list;
    }

    // --- header ---

    private static void WriteHeader(StringBuilder sb, ExtResourceRegistry registry,
        List<SubResourceEntry> subResources,
        IReadOnlyList<AmbientLabelSpec> ambientLabels)
    {
        var loadSteps = registry.Count + subResources.Count + ambientLabels.Count;
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
            sb.AppendLine($"ZoneName = \"{entry.ZoneName}\"");
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

    // --- nodes ---

    private static void WriteNodes(StringBuilder sb, SceneLayout layout, ExtResourceRegistry registry,
        List<SubResourceEntry> subResources,
        IReadOnlyList<AmbientLabelSpec> ambientLabels,
        string pmproxyEndpoint)
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

        foreach (var zone in layout.Zones)
            WriteZone(sb, zone, registry, subResources);

        WriteAmbientLabels(sb, ambientLabels);
        WriteRangeTuningPanel(sb, registry);
    }

    private static void WriteAmbientLabels(StringBuilder sb,
        IReadOnlyList<AmbientLabelSpec> labels)
    {
        foreach (var label in labels)
        {
            sb.AppendLine($"[node name=\"{label.NodeName}\" type=\"Label3D\" parent=\".\"]");

            if (label.IsFlatOnFloor)
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

    private static void WriteRangeTuningPanel(StringBuilder sb, ExtResourceRegistry registry)
    {
        sb.AppendLine("[node name=\"UILayer\" type=\"CanvasLayer\" parent=\".\"]");
        sb.AppendLine();

        sb.AppendLine("[node name=\"RangeTuningPanel\" parent=\"UILayer\" instance=ExtResource(\"range_tuning_panel_scene\")]");
        sb.AppendLine();
    }

    private static void WriteZone(StringBuilder sb, PlacedZone zone,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        WriteMetricGroupNode(sb, zone);
        WriteGroundBezelNode(sb, zone);
        WriteMetricGridNode(sb, zone);

        var gridPath = $"{zone.Name}/{zone.Name}Grid";
        foreach (var item in zone.Items)
        {
            switch (item)
            {
                case PlacedStack stack:
                    WriteStack(sb, stack, zone, registry, subResources, gridPath);
                    break;
                case PlacedShape shape:
                    WriteShape(sb, shape, registry, subResources, parentOverride: gridPath);
                    break;
            }
        }
    }

    private static void WriteMetricGroupNode(StringBuilder sb, PlacedZone zone)
    {
        var pos = zone.Position;
        sb.AppendLine($"[node name=\"{zone.Name}\" type=\"Node3D\" parent=\".\"]");
        sb.AppendLine("script = ExtResource(\"metric_group_script\")");

        if (zone.RotateYNinetyDeg)
            sb.AppendLine($"transform = Transform3D(0, 0, -1, 0, 1, 0, 1, 0, 0, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");
        else if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");

        sb.AppendLine($"title_text = \"{zone.ZoneLabel}\"");
        sb.AppendLine();
    }

    private static void WriteGroundBezelNode(StringBuilder sb, PlacedZone zone)
    {
        sb.AppendLine($"[node name=\"{zone.Name}Bezel\" type=\"MeshInstance3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine("script = ExtResource(\"ground_bezel_script\")");
        sb.AppendLine("bezel_colour = Color(0.3, 0.3, 0.3, 1)");
        sb.AppendLine();
    }

    private static void WriteMetricGridNode(StringBuilder sb, PlacedZone zone)
    {
        sb.AppendLine($"[node name=\"{zone.Name}Grid\" type=\"Node3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine("script = ExtResource(\"metric_grid_script\")");

        if (zone.ColumnSpacing.HasValue)
            sb.AppendLine($"column_spacing = {F(zone.ColumnSpacing.Value)}");

        if (zone.RowSpacing.HasValue)
            sb.AppendLine($"row_spacing = {F(zone.RowSpacing.Value)}");

        if (zone.MetricLabels is { Count: > 0 })
            sb.AppendLine($"metric_labels = {FormatPackedStringArray(zone.MetricLabels)}");

        if (zone.InstanceLabels is { Count: > 0 })
            sb.AppendLine($"instance_labels = {FormatPackedStringArray(zone.InstanceLabels)}");

        sb.AppendLine();
    }

    private static void WriteShape(StringBuilder sb, PlacedShape shape,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources,
        string parentOverride)
    {
        var sceneId = SceneExtResourceId(shape.Shape);
        var shapePath = $"{parentOverride}/{shape.NodeName}";

        sb.AppendLine($"[node name=\"{shape.NodeName}\" parent=\"{parentOverride}\" instance=ExtResource(\"{sceneId}\")]");
        sb.AppendLine($"colour = Color({F(shape.Colour.R)}, {F(shape.Colour.G)}, {F(shape.Colour.B)}, 1)");

        if (shape.IsPlaceholder)
        {
            sb.AppendLine("ghost = true");
            sb.AppendLine();
            return;
        }

        sb.AppendLine();

        var subResId = SubResourceId(shape.NodeName);
        sb.AppendLine($"[node name=\"PcpBindable\" type=\"Node\" parent=\"{shapePath}\"]");
        sb.AppendLine("script = ExtResource(\"bindable_script\")");
        sb.AppendLine($"PcpBindings = Array[ExtResource(\"binding_res_script\")]([SubResource(\"{subResId}\")])");

        sb.AppendLine();
    }

    private static void WriteStack(StringBuilder sb, PlacedStack stack, PlacedZone zone,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources, string gridPath)
    {
        var pos = stack.LocalPosition;
        var stackPath = $"{gridPath}/{stack.GroupName}";

        sb.AppendLine($"[node name=\"{stack.GroupName}\" type=\"Node3D\" parent=\"{gridPath}\"]");
        sb.AppendLine("script = ExtResource(\"stack_group_script\")");

        var modeValue = stack.Mode == StackMode.Proportional ? 0 : 1;
        sb.AppendLine($"stack_mode = {modeValue}");

        if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            sb.AppendLine($"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");

        sb.AppendLine();

        foreach (var member in stack.Members)
            WriteShape(sb, member, registry, subResources, parentOverride: stackPath);
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

    private static string F(float value) => (value == 0f ? 0f : value).ToString(Inv);

    private static string FormatPackedStringArray(IReadOnlyList<string> items)
    {
        var quoted = string.Join(", ", items.Select(s => $"\"{s}\""));
        return $"PackedStringArray({quoted})";
    }

    // --- private supporting types ---

    private record SubResourceEntry(
        string Id,
        string MetricName,
        string? InstanceName,
        float SourceRangeMin,
        float SourceRangeMax,
        float TargetRangeMin,
        float TargetRangeMax,
        string ZoneName);

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
