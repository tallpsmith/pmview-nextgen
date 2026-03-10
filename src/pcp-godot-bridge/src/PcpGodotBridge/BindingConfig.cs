namespace PcpGodotBridge;

/// <summary>
/// Top-level binding configuration loaded from a TOML file.
/// Maps a Godot scene to PCP metrics via bindings.
/// </summary>
public record BindingConfig(
    string ScenePath,
    string? Endpoint,
    int PollIntervalMs,
    string? Description,
    IReadOnlyList<MetricBinding> Bindings);
