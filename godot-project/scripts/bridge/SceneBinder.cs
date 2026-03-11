using Godot;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Loads scene+binding config pairs and applies metric values to scene nodes.
/// Validates properties against real nodes at scene load time.
/// Supports scene swapping at runtime (US03).
/// </summary>
public partial class SceneBinder : Node
{
    [Signal]
    public delegate void SceneLoadedEventHandler(string scenePath, string configPath);

    [Signal]
    public delegate void BindingErrorEventHandler(string message);

    private Node? _currentScene;
    private string? _currentConfigPath;
    private BindingConfig? _currentConfig;
    private readonly List<ActiveBinding> _activeBindings = new();
    private readonly Dictionary<Node3D, float> _rotationSpeeds = new();

    /// <summary>
    /// A validated, resolved binding with a cached node reference.
    /// Only created for bindings that passed both config and scene validation.
    /// </summary>
    private record ActiveBinding(
        ResolvedBinding Resolved,
        Node TargetNode);

    public BindingConfig? CurrentConfig => _currentConfig;
    public int ActiveBindingCount => _activeBindings.Count;

    public override void _Process(double delta)
    {
        foreach (var (node, degreesPerSecond) in _rotationSpeeds)
        {
            if (IsInstanceValid(node))
                node.RotateY(Mathf.DegToRad(degreesPerSecond) * (float)delta);
        }
    }

    /// <summary>
    /// Load a scene and its binding config. Replaces any currently loaded scene.
    /// Returns the list of metric names needed for polling.
    /// </summary>
    public string[] LoadSceneWithBindings(string configPath)
    {
        UnloadCurrentScene();

        // Phase 1: Config validation (pure .NET)
        // Resolve Godot res:// paths to filesystem paths for .NET File I/O
        var resolvedPath = configPath.StartsWith("res://")
            ? ProjectSettings.GlobalizePath(configPath)
            : configPath;
        var configResult = BindingConfigLoader.LoadFromFile(resolvedPath);
        LogConfigResult(configResult);

        if (!configResult.IsValid)
        {
            EmitSignal(SignalName.BindingError, "Config validation failed — see log");
            return [];
        }

        _currentConfig = configResult.Config!;
        _currentConfigPath = configPath;

        // Phase 2: Scene load + node/property validation (Godot runtime)
        var packedScene = GD.Load<PackedScene>(_currentConfig.ScenePath);
        if (packedScene == null)
        {
            EmitSignal(SignalName.BindingError,
                $"Cannot load scene: {_currentConfig.ScenePath}");
            return [];
        }

        _currentScene = packedScene.Instantiate();
        AddChild(_currentScene);

        ResolveAndValidateBindings();

        EmitSignal(SignalName.SceneLoaded, _currentConfig.ScenePath, configPath);

        return _currentConfig.Bindings
            .Select(b => b.Metric)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Apply metric values from a MetricPoller signal to the active bindings.
    /// Called every poll cycle. Hot path — no validation, just cached lookups.
    /// </summary>
    public void ApplyMetrics(Godot.Collections.Dictionary metrics)
    {
        foreach (var active in _activeBindings)
        {
            var binding = active.Resolved.Binding;

            if (!metrics.ContainsKey(binding.Metric))
                continue;

            var metricData = metrics[binding.Metric].AsGodotDictionary();
            var instances = metricData["instances"].AsGodotDictionary();

            double? rawValue = ExtractValue(binding, instances);
            if (rawValue == null)
                continue;

            var normalised = Normalise(rawValue.Value,
                binding.SourceRangeMin, binding.SourceRangeMax,
                binding.TargetRangeMin, binding.TargetRangeMax);

            ApplyProperty(active, (float)normalised);
        }
    }

    public void UnloadCurrentScene()
    {
        _activeBindings.Clear();
        _rotationSpeeds.Clear();

        if (_currentScene != null)
        {
            _currentScene.QueueFree();
            _currentScene = null;
        }

        _currentConfig = null;
        _currentConfigPath = null;
    }

    private void ResolveAndValidateBindings()
    {
        _activeBindings.Clear();

        foreach (var binding in _currentConfig!.Bindings)
        {
            var resolved = PropertyVocabulary.Resolve(binding);

            var node = _currentScene!.GetNodeOrNull(binding.SceneNode);
            if (node == null)
            {
                GD.PushWarning(
                    $"[SceneBinder] Node not found: '{binding.SceneNode}' — skipping binding");
                EmitSignal(SignalName.BindingError,
                    $"Node not found: '{binding.SceneNode}'");
                continue;
            }

            if (!ValidatePropertyExists(node, resolved))
                continue;

            _activeBindings.Add(new ActiveBinding(resolved, node));
            GD.Print($"[SceneBinder] Bound: {binding.SceneNode}.{binding.Property} " +
                     $"<- {binding.Metric}");
        }

        GD.Print($"[SceneBinder] {_activeBindings.Count} active bindings " +
                 $"({_currentConfig.Bindings.Count - _activeBindings.Count} skipped)");
    }

    private bool ValidatePropertyExists(Node node, ResolvedBinding resolved)
    {
        var godotProperty = resolved.GodotPropertyName;

        // For "scale:y" check "scale" exists; for "river_flow_speed" check as-is
        var propertyName = godotProperty.Contains(':')
            ? godotProperty.Split(':')[0]
            : godotProperty;

        var propertyList = node.GetPropertyList();
        foreach (var prop in propertyList)
        {
            if (prop["name"].AsString() == propertyName)
                return true;
        }

        // Build helpful error message listing available script properties
        var available = new List<string>();
        foreach (var prop in propertyList)
        {
            var name = prop["name"].AsString();
            var usage = prop["usage"].AsInt32();
            if ((usage & (int)PropertyUsageFlags.ScriptVariable) != 0)
                available.Add(name);
        }

        var availableStr = available.Count > 0
            ? $" Available script properties: {string.Join(", ", available)}"
            : " No script properties found on this node.";

        GD.PushWarning(
            $"[SceneBinder] Property '{resolved.GodotPropertyName}' not found on " +
            $"node '{resolved.Binding.SceneNode}'.{availableStr}");
        EmitSignal(SignalName.BindingError,
            $"Property '{resolved.GodotPropertyName}' not found on " +
            $"'{resolved.Binding.SceneNode}'");

        return false;
    }

    private static double? ExtractValue(MetricBinding binding,
        Godot.Collections.Dictionary instances)
    {
        if (binding.InstanceId != null)
        {
            return instances.ContainsKey(binding.InstanceId.Value)
                ? instances[binding.InstanceId.Value].AsDouble()
                : null;
        }

        if (binding.InstanceFilter != null)
        {
            // Take first matching instance (full glob matching is a future enhancement)
            foreach (var key in instances.Keys)
                return instances[key].AsDouble();
            return null;
        }

        // Singular metric (key -1) or first available instance
        if (instances.ContainsKey(-1))
            return instances[-1].AsDouble();

        foreach (var key in instances.Keys)
            return instances[key].AsDouble();

        return null;
    }

    private void ApplyProperty(ActiveBinding active, float value)
    {
        switch (active.Resolved.Kind)
        {
            case PropertyKind.BuiltIn:
                ApplyBuiltInProperty(active.TargetNode, active.Resolved.Binding.Property, value);
                break;
            case PropertyKind.Custom:
                active.TargetNode.Set(active.Resolved.GodotPropertyName, value);
                break;
        }
    }

    private void ApplyBuiltInProperty(Node node, string property, float value)
    {
        if (node is not Node3D node3D)
        {
            GD.PushWarning($"[SceneBinder] Built-in property '{property}' requires " +
                           $"Node3D but got {node.GetClass()}");
            return;
        }

        switch (property)
        {
            case "height":
                node3D.Scale = new Vector3(node3D.Scale.X, value, node3D.Scale.Z);
                break;
            case "width":
                node3D.Scale = new Vector3(value, node3D.Scale.Y, node3D.Scale.Z);
                break;
            case "depth":
                node3D.Scale = new Vector3(node3D.Scale.X, node3D.Scale.Y, value);
                break;
            case "scale":
                node3D.Scale = new Vector3(value, value, value);
                break;
            case "rotation_speed":
                _rotationSpeeds[node3D] = value;
                break;
            case "position_y":
                node3D.Position = new Vector3(node3D.Position.X, value, node3D.Position.Z);
                break;
            case "color_temperature":
                ApplyColorTemperature(node3D, value);
                break;
            case "opacity":
                ApplyOpacity(node3D, value);
                break;
        }
    }

    private static void ApplyColorTemperature(Node3D node, float value)
    {
        if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
        {
            // 0 = blue (cold), 1 = red (hot)
            var hue = Mathf.Lerp(0.66f, 0.0f, Mathf.Clamp(value, 0f, 1f));
            mat.AlbedoColor = Color.FromHsv(hue, 0.8f, 0.9f);
        }
    }

    private static void ApplyOpacity(Node3D node, float value)
    {
        if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
        {
            var color = mat.AlbedoColor;
            mat.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(value, 0f, 1f));
        }
    }

    private static double Normalise(double value,
        double srcMin, double srcMax, double tgtMin, double tgtMax)
    {
        var clamped = Math.Clamp(value, srcMin, srcMax);
        var ratio = (clamped - srcMin) / (srcMax - srcMin);
        return tgtMin + ratio * (tgtMax - tgtMin);
    }

    private void LogConfigResult(BindingConfigResult result)
    {
        foreach (var msg in result.Messages)
        {
            var prefix = msg.BindingContext != null ? $"[{msg.BindingContext}] " : "";
            var text = $"[SceneBinder] {prefix}{msg.Message}";

            switch (msg.Severity)
            {
                case ValidationSeverity.Error:
                    GD.PushError(text);
                    break;
                case ValidationSeverity.Warning:
                    GD.PushWarning(text);
                    break;
                case ValidationSeverity.Info:
                    GD.Print(text);
                    break;
            }
        }
    }
}
