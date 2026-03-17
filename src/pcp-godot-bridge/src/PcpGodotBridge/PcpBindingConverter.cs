namespace PcpGodotBridge;

/// <summary>
/// Converts primitive editor property values into MetricBinding records.
/// Pure .NET — no Godot dependency. The Godot Resource (PcpBindingResource)
/// passes primitive values in; this class handles the mapping conventions.
/// </summary>
public static class PcpBindingConverter
{
    /// <summary>
    /// Converts editor property values to a MetricBinding.
    /// Convention: instanceId of -1 maps to null (no instance filter).
    /// Convention: empty/whitespace instanceName maps to null.
    /// </summary>
    public static MetricBinding ToMetricBinding(
        string sceneNode,
        string metricName,
        string targetProperty,
        double sourceRangeMin,
        double sourceRangeMax,
        double targetRangeMin,
        double targetRangeMax,
        int instanceId,
        string? instanceName,
        double initialValue,
        string? zoneName = null)
    {
        return new MetricBinding(
            SceneNode: sceneNode,
            Metric: metricName,
            Property: targetProperty,
            SourceRangeMin: sourceRangeMin,
            SourceRangeMax: sourceRangeMax,
            TargetRangeMin: targetRangeMin,
            TargetRangeMax: targetRangeMax,
            InstanceId: instanceId < 0 ? null : instanceId,
            InstanceName: string.IsNullOrWhiteSpace(instanceName) ? null : instanceName,
            InitialValue: initialValue,
            ZoneName: zoneName);
    }
}
