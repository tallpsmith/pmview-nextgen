using System.Globalization;
using System.Text;
using PmviewHostProjector.Models;

namespace PmviewHostProjector.Emission;

/// <summary>
/// Emits a Godot .tscn file from a SceneLayout.
/// Each shape gets a PcpBindable child node with PcpBindingResource sub_resources.
/// </summary>
public static class TscnWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write(SceneLayout layout)
    {
        var sb = new StringBuilder();
        var registry = new ExtResourceRegistry();
        var subResources = CollectSubResources(layout, registry);

        WriteHeader(sb, registry, subResources);
        WriteExtResources(sb, registry);
        WriteSubResources(sb, subResources);
        WriteNodes(sb, layout, registry, subResources);

        return sb.ToString();
    }

    // --- resource collection ---

    private static List<SubResourceEntry> CollectSubResources(SceneLayout layout, ExtResourceRegistry registry)
    {
        var list = new List<SubResourceEntry>();

        foreach (var zone in layout.Zones)
        {
            foreach (var shape in zone.Shapes)
            {
                var sceneId = SceneExtResourceId(shape.Shape);
                registry.Require(sceneId, "PackedScene", SceneExtResourcePath(shape.Shape));
                registry.Require("bindable_script", "Script", "res://addons/pmview-bridge/PcpBindable.cs");
                registry.Require("binding_res_script", "Script", "res://addons/pmview-bridge/PcpBindingResource.cs");

                if (zone.GridColumns.HasValue)
                    registry.Require("grid_script", "Script", "res://addons/pmview-bridge/building_blocks/grid_layout_3d.gd");

                list.Add(new SubResourceEntry(
                    Id: SubResourceId(shape.NodeName),
                    MetricName: shape.MetricName,
                    InstanceName: shape.InstanceName,
                    SourceRangeMin: shape.SourceRangeMin,
                    SourceRangeMax: shape.SourceRangeMax,
                    TargetRangeMin: shape.TargetRangeMin,
                    TargetRangeMax: shape.TargetRangeMax
                ));
            }
        }

        return list;
    }

    // --- header ---

    private static void WriteHeader(StringBuilder sb, ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        var loadSteps = registry.Count + subResources.Count + 1;
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

    // --- nodes ---

    private static void WriteNodes(StringBuilder sb, SceneLayout layout, ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        sb.AppendLine("[node name=\"HostView\" type=\"Node3D\"]");
        sb.AppendLine();

        foreach (var zone in layout.Zones)
            WriteZone(sb, zone, registry, subResources);
    }

    private static void WriteZone(StringBuilder sb, PlacedZone zone, ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        WriteZoneContainerNode(sb, zone, registry);
        WriteZoneLabelNode(sb, zone);

        foreach (var shape in zone.Shapes)
            WriteShape(sb, shape, zone, registry, subResources);
    }

    private static void WriteZoneContainerNode(StringBuilder sb, PlacedZone zone, ExtResourceRegistry registry)
    {
        var pos = zone.Position;
        sb.AppendLine($"[node name=\"{zone.Name}\" type=\"Node3D\" parent=\".\"]");

        if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
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
        sb.AppendLine($"[node name=\"{zone.Name}Label\" type=\"Label3D\" parent=\"{zone.Name}\"]");
        sb.AppendLine("transform = Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0.01, 1)");
        sb.AppendLine("pixel_size = 0.01");
        sb.AppendLine("font_size = 32");
        sb.AppendLine($"text = \"{zone.ZoneLabel}\"");
        sb.AppendLine("horizontal_alignment = 1");
        sb.AppendLine();
    }

    private static void WriteShape(StringBuilder sb, PlacedShape shape, PlacedZone zone,
        ExtResourceRegistry registry, List<SubResourceEntry> subResources)
    {
        var sceneId = SceneExtResourceId(shape.Shape);
        var pos = shape.LocalPosition;
        var zonePath = zone.Name;
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

    private static string F(float value) => value.ToString(Inv);

    // --- private supporting types ---

    private record SubResourceEntry(
        string Id,
        string MetricName,
        string? InstanceName,
        float SourceRangeMin,
        float SourceRangeMax,
        float TargetRangeMin,
        float TargetRangeMax);

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
