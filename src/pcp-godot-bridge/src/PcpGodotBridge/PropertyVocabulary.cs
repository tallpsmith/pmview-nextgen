namespace PcpGodotBridge;

public enum PropertyKind { BuiltIn, Custom }

/// <summary>
/// Resolved binding with property classification and Godot property name.
/// </summary>
public record ResolvedBinding(
    MetricBinding Binding,
    PropertyKind Kind,
    string GodotPropertyName);

/// <summary>
/// Maps binding config property names to Godot node properties.
/// Built-in properties map to specific Godot properties (e.g. "height" -> "scale:y").
/// Custom properties pass through as-is for @export vars on scene nodes.
/// </summary>
public static class PropertyVocabulary
{
    private static readonly Dictionary<string, string> BuiltInMappings = new()
    {
        ["height"] = "scale:y",
        ["width"] = "scale:x",
        ["depth"] = "scale:z",
        ["scale"] = "scale",
        ["rotation_speed"] = "rotation:y",
        ["position_y"] = "position:y",
        ["color_temperature"] = "albedo_color",
        ["opacity"] = "albedo_color:a",
    };

    public static bool IsBuiltIn(string property) =>
        BuiltInMappings.ContainsKey(property);

    public static PropertyKind Classify(string property) =>
        IsBuiltIn(property) ? PropertyKind.BuiltIn : PropertyKind.Custom;

    public static string ResolveGodotProperty(string property) =>
        BuiltInMappings.TryGetValue(property, out var godotProp)
            ? godotProp
            : property;

    public static ResolvedBinding Resolve(MetricBinding binding) =>
        new(binding, Classify(binding.Property),
            ResolveGodotProperty(binding.Property));
}
