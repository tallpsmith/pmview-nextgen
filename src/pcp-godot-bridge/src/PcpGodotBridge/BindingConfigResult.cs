namespace PcpGodotBridge;

public enum ValidationSeverity { Info, Warning, Error }

/// <summary>
/// A single validation message from config loading.
/// BindingContext identifies which binding entry (e.g. "bindings[2]") if applicable.
/// </summary>
public record ValidationMessage(
    ValidationSeverity Severity,
    string Message,
    string? BindingContext);

/// <summary>
/// Result of loading and validating a binding config.
/// Contains the parsed config (if valid) and all validation messages.
/// </summary>
public record BindingConfigResult(
    BindingConfig? Config,
    IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
    public bool IsValid => Config != null && !HasErrors;
}
