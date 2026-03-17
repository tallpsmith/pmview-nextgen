using System;
using System.Collections.Generic;
using Godot;
using PmviewProjectionCore.Models;

namespace PmviewApp;

/// <summary>
/// Runtime equivalent of TscnWriter. Instantiates live Godot nodes from a
/// SceneLayout, producing the exact same node hierarchy that TscnWriter emits
/// as .tscn text — so SceneBinder can find and validate bindings identically.
/// </summary>
public static class RuntimeSceneBuilder
{
    // -- script paths --
    private const string ControllerScriptPath = "res://addons/pmview-bridge/host_view_controller.gd";
    private const string MetricPollerScriptPath = "res://addons/pmview-bridge/MetricPoller.cs";
    private const string SceneBinderScriptPath = "res://addons/pmview-bridge/SceneBinder.cs";
    private const string MetricGroupScriptPath = "res://addons/pmview-bridge/building_blocks/metric_group_node.gd";
    private const string MetricGridScriptPath = "res://addons/pmview-bridge/building_blocks/metric_grid.gd";
    private const string GroundBezelScriptPath = "res://addons/pmview-bridge/building_blocks/ground_bezel.gd";
    private const string StackGroupScriptPath = "res://addons/pmview-bridge/building_blocks/stack_group_node.gd";
    private const string BindableScriptPath = "res://addons/pmview-bridge/PcpBindable.cs";
    private const string BindingResScriptPath = "res://addons/pmview-bridge/PcpBindingResource.cs";

    // -- packed scene paths --
    private const string BarScenePath = "res://addons/pmview-bridge/building_blocks/grounded_bar.tscn";
    private const string CylinderScenePath = "res://addons/pmview-bridge/building_blocks/grounded_cylinder.tscn";
    private const string RangeTuningPanelScenePath = "res://addons/pmview-bridge/ui/range_tuning_panel.tscn";

    // -- ambient label colours --
    private static readonly Color TimestampColour = new(0.976f, 0.451f, 0.086f, 1f);
    private static readonly Color BezelColour = new(0.3f, 0.3f, 0.3f, 1f);

    /// <summary>
    /// Builds a live node hierarchy from a <see cref="SceneLayout"/>,
    /// mirroring the structure TscnWriter emits to .tscn.
    /// </summary>
    public static Node3D Build(SceneLayout layout, string pmproxyEndpoint,
        IProgress<float>? progress = null)
    {
        GD.Print("[RuntimeSceneBuilder] Build starting...");
        var root = CreateHostViewRoot();
        AddMetricPoller(root, pmproxyEndpoint, layout.Hostname);
        AddSceneBinder(root);

        GD.Print($"[RuntimeSceneBuilder] Building {layout.Zones.Count} zones...");
        for (var i = 0; i < layout.Zones.Count; i++)
        {
            BuildZone(root, layout.Zones[i]);
            progress?.Report((float)(i + 1) / layout.Zones.Count);
        }

        BuildAmbientLabels(root);
        AddRangeTuningPanel(root);

        // Set Owner on all descendants so find_child(owned=true) works —
        // programmatic nodes don't get an owner automatically unlike .tscn scenes.
        SetOwnerRecursive(root, root);

        GD.Print($"[RuntimeSceneBuilder] Build complete. Root children: {root.GetChildCount()}");
        return root;
    }

    // ── root + infrastructure ──────────────────────────────────────────

    private static Node3D CreateHostViewRoot()
    {
        var root = new Node3D { Name = "HostView" };
        var script = GD.Load<Script>(ControllerScriptPath);
        if (script == null)
            GD.PrintErr($"[RuntimeSceneBuilder] FAILED to load controller script: {ControllerScriptPath}");
        else
            GD.Print($"[RuntimeSceneBuilder] Loaded controller script: {script.ResourcePath}");
        root.SetScript(script);
        GD.Print($"[RuntimeSceneBuilder] Root script after SetScript: {root.GetScript()}");
        return root;
    }

    private static void AddMetricPoller(Node3D root, string endpoint, string hostname)
    {
        var poller = new Node { Name = "MetricPoller" };
        var script = GD.Load<Script>(MetricPollerScriptPath);
        if (script == null)
            GD.PrintErr($"[RuntimeSceneBuilder] FAILED to load MetricPoller script: {MetricPollerScriptPath}");
        poller = SetCSharpScript<Node>(poller, MetricPollerScriptPath);
        GD.Print($"[RuntimeSceneBuilder] MetricPoller type after SetScript: {poller.GetClass()}, script: {poller.GetScript()}");
        poller.Set("Endpoint", endpoint);
        poller.Set("Hostname", hostname);
        root.AddChild(poller);
    }

    private static void AddSceneBinder(Node3D root)
    {
        var binder = new Node { Name = "SceneBinder" };
        var script = GD.Load<Script>(SceneBinderScriptPath);
        if (script == null)
            GD.PrintErr($"[RuntimeSceneBuilder] FAILED to load SceneBinder script: {SceneBinderScriptPath}");
        binder = SetCSharpScript<Node>(binder, SceneBinderScriptPath);
        GD.Print($"[RuntimeSceneBuilder] SceneBinder type after SetScript: {binder.GetClass()}, script: {binder.GetScript()}");
        root.AddChild(binder);
    }

    // ── zones ──────────────────────────────────────────────────────────

    private static void BuildZone(Node3D root, PlacedZone zone)
    {
        var groupNode = CreateMetricGroupNode(zone);
        root.AddChild(groupNode);

        var bezel = CreateGroundBezel(zone);
        groupNode.AddChild(bezel);

        var grid = CreateMetricGrid(zone);
        groupNode.AddChild(grid);

        foreach (var item in zone.Items)
        {
            switch (item)
            {
                case PlacedStack stack:
                    BuildStack(grid, stack, zone.Name);
                    break;
                case PlacedShape shape:
                    BuildShape(grid, shape, zone.Name);
                    break;
            }
        }
    }

    private static Node3D CreateMetricGroupNode(PlacedZone zone)
    {
        var node = new Node3D { Name = zone.Name };
        node.SetScript(GD.Load<Script>(MetricGroupScriptPath));

        var pos = zone.Position;
        if (zone.RotateYNinetyDeg)
        {
            // 90° Y rotation: Basis columns are (0,0,-1), (0,1,0), (1,0,0)
            var basis = new Basis(
                new Vector3(0, 0, 1),
                new Vector3(0, 1, 0),
                new Vector3(-1, 0, 0));
            node.Transform = new Transform3D(basis, new Vector3(pos.X, pos.Y, pos.Z));
        }
        else if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
        {
            node.Position = new Vector3(pos.X, pos.Y, pos.Z);
        }

        node.Set("title_text", zone.ZoneLabel);
        return node;
    }

    private static MeshInstance3D CreateGroundBezel(PlacedZone zone)
    {
        var bezel = new MeshInstance3D { Name = $"{zone.Name}Bezel" };
        bezel.SetScript(GD.Load<Script>(GroundBezelScriptPath));
        bezel.Set("bezel_colour", BezelColour);
        return bezel;
    }

    private static Node3D CreateMetricGrid(PlacedZone zone)
    {
        var grid = new Node3D { Name = $"{zone.Name}Grid" };
        grid.SetScript(GD.Load<Script>(MetricGridScriptPath));

        if (zone.ColumnSpacing.HasValue)
            grid.Set("column_spacing", zone.ColumnSpacing.Value);

        if (zone.RowSpacing.HasValue)
            grid.Set("row_spacing", zone.RowSpacing.Value);

        if (zone.MetricLabels is { Count: > 0 })
            grid.Set("metric_labels", ToPackedStringArray(zone.MetricLabels));

        if (zone.InstanceLabels is { Count: > 0 })
            grid.Set("instance_labels", ToPackedStringArray(zone.InstanceLabels));

        return grid;
    }

    // ── shapes ─────────────────────────────────────────────────────────

    private static void BuildShape(Node parent, PlacedShape shape, string zoneName)
    {
        var scenePath = shape.Shape == ShapeType.Cylinder ? CylinderScenePath : BarScenePath;
        var packedScene = GD.Load<PackedScene>(scenePath);
        var instance = packedScene.Instantiate<Node3D>();
        instance.Name = shape.NodeName;

        var colour = new Color(shape.Colour.R, shape.Colour.G, shape.Colour.B, 1f);
        instance.Set("colour", colour);

        if (shape.IsPlaceholder)
        {
            instance.Set("ghost", true);
            parent.AddChild(instance);
            return;
        }

        parent.AddChild(instance);
        AddBindable(instance, BuildBinding(shape, zoneName));
    }

    // ── stacks ─────────────────────────────────────────────────────────

    private static void BuildStack(Node3D grid, PlacedStack stack, string zoneName)
    {
        var stackNode = new Node3D { Name = stack.GroupName };
        stackNode.SetScript(GD.Load<Script>(StackGroupScriptPath));

        var modeValue = stack.Mode == StackMode.Proportional ? 0 : 1;
        stackNode.Set("stack_mode", modeValue);

        var pos = stack.LocalPosition;
        if (pos.X != 0f || pos.Y != 0f || pos.Z != 0f)
            stackNode.Position = new Vector3(pos.X, pos.Y, pos.Z);

        grid.AddChild(stackNode);

        foreach (var member in stack.Members)
            BuildShape(stackNode, member, zoneName);
    }

    // ── bindings ───────────────────────────────────────────────────────

    private static Resource BuildBinding(PlacedShape shape, string zoneName)
    {
        return CreateBindingResource(
            shape.MetricName,
            "height",
            shape.SourceRangeMin,
            shape.SourceRangeMax,
            shape.TargetRangeMin,
            shape.TargetRangeMax,
            shape.InstanceName,
            shape.TargetRangeMin,
            zoneName);
    }

    private static Resource BuildAmbientBinding(string metricName)
    {
        return CreateBindingResource(
            metricName,
            targetProperty: "text",
            sourceRangeMin: 0f,
            sourceRangeMax: 1f,
            targetRangeMin: 0f,
            targetRangeMax: 1f,
            instanceName: null,
            initialValue: 0f,
            zoneName: "");
    }

    private static Resource CreateBindingResource(
        string metricName, string targetProperty,
        float sourceRangeMin, float sourceRangeMax,
        float targetRangeMin, float targetRangeMax,
        string? instanceName, float initialValue,
        string zoneName)
    {
        var res = new Resource();
        // C# script — re-fetch after SetScript
        res = SetCSharpScript<Resource>(res, BindingResScriptPath);
        res.ResourceLocalToScene = true;
        res.Set("MetricName", metricName);
        res.Set("TargetProperty", targetProperty);
        res.Set("SourceRangeMin", sourceRangeMin);
        res.Set("SourceRangeMax", sourceRangeMax);
        res.Set("TargetRangeMin", targetRangeMin);
        res.Set("TargetRangeMax", targetRangeMax);

        if (instanceName is not null)
            res.Set("InstanceName", instanceName);

        res.Set("InstanceId", -1);
        res.Set("InitialValue", initialValue);
        res.Set("ZoneName", zoneName);
        return res;
    }

    private static void AddBindable(Node parent, Resource binding)
    {
        var bindable = new Node { Name = "PcpBindable" };
        // C# script — re-fetch after SetScript
        bindable = SetCSharpScript<Node>(bindable, BindableScriptPath);

        // PcpBindings is a typed Array[PcpBindingResource]
        var bindings = new Godot.Collections.Array<Resource> { binding };
        bindable.Set("PcpBindings", bindings);

        parent.AddChild(bindable);
    }

    // ── ambient labels ─────────────────────────────────────────────────

    private static void BuildAmbientLabels(Node3D root)
    {
        BuildTimestampLabel(root);
        BuildHostnameLabel(root);
    }

    private static void BuildTimestampLabel(Node3D root)
    {
        var label = new Label3D { Name = "TimestampLabel" };

        // Flat on floor: rotated -90° around X, positioned at Y=0.02, Z=-4
        // .tscn columns: (1,0,0), (0,0,-1), (0,1,0) → transposed to rows
        var basis = new Basis(
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 1),
            new Vector3(0, -1, 0));
        label.Transform = new Transform3D(basis, new Vector3(0, 0.02f, -4));

        label.PixelSize = 0.02f;
        label.FontSize = 96;
        label.OutlineSize = 8;
        label.OutlineModulate = new Color(0, 0, 0, 1);
        label.Modulate = TimestampColour;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Text = "";

        root.AddChild(label);
        AddBindable(label, BuildAmbientBinding("pmview.meta.timestamp"));
    }

    private static void BuildHostnameLabel(Node3D root)
    {
        var label = new Label3D { Name = "HostnameLabel" };
        label.Position = new Vector3(0, 10, 0);
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.PixelSize = 0.015f;
        label.FontSize = 128;
        label.OutlineSize = 12;
        label.OutlineModulate = new Color(0, 0, 0, 1);
        label.Uppercase = true;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Text = "";

        root.AddChild(label);
        AddBindable(label, BuildAmbientBinding("pmview.meta.hostname"));
    }

    // ── range tuning panel ──────────────────────────────────────────────

    private static void AddRangeTuningPanel(Node3D sceneRoot)
    {
        var panelScene = GD.Load<PackedScene>(RangeTuningPanelScenePath);
        if (panelScene == null)
        {
            GD.PushWarning("[RuntimeSceneBuilder] RangeTuningPanel scene not found");
            return;
        }

        var canvas = new CanvasLayer();
        canvas.Name = "UILayer";

        var panel = panelScene.Instantiate();
        panel.Name = "RangeTuningPanel";

        canvas.AddChild(panel);
        sceneRoot.AddChild(canvas);
        // Owner is set by SetOwnerRecursive() in Build() — don't set manually here.
    }

    // ── script assignment ─────────────────────────────────────────────

    /// <summary>
    /// Assigns a C# script to a GodotObject and returns the new managed wrapper.
    /// In Godot 4's C# binding, SetScript() with a C# script disposes the
    /// original managed wrapper. We must re-fetch via InstanceFromId.
    /// </summary>
    private static T SetCSharpScript<T>(T obj, string scriptPath) where T : GodotObject
    {
        var id = obj.GetInstanceId();
        obj.SetScript(GD.Load<Script>(scriptPath));
        return (T)GodotObject.InstanceFromId(id);
    }

    // ── ownership ───────────────────────────────────────────────────────

    /// <summary>
    /// Recursively sets Owner on all descendants so that GDScript's
    /// find_child(owned=true) can discover them. Programmatically-added
    /// nodes don't get an owner automatically — unlike .tscn scene loading.
    /// </summary>
    private static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    // ── utilities ──────────────────────────────────────────────────────

    private static string[] ToPackedStringArray(IReadOnlyList<string> items)
    {
        var array = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
            array[i] = items[i];
        return array;
    }
}
