namespace PcpGodotBridge;

/// <summary>
/// Maps a single scene node property to a PCP metric value.
/// Parsed from a [[bindings]] entry in the TOML config or from editor scene properties.
/// </summary>
public record MetricBinding(
    string SceneNode,
    string Metric,
    string Property,
    double SourceRangeMin,
    double SourceRangeMax,
    double TargetRangeMin,
    double TargetRangeMax,
    int? InstanceId,
    string? InstanceName,
    double InitialValue = 0.0);
