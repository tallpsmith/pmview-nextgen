namespace PcpGodotBridge;

/// <summary>
/// Validates MetricBinding records independently of any config format (TOML, scene properties, etc).
/// Pure semantic validation: ranges, required fields, instance exclusivity, duplicate detection.
/// </summary>
public static class BindingValidator
{
    /// <summary>
    /// Validates a single binding and tracks seen node+property pairs for duplicate detection.
    /// The seenNodeProperties set is mutated — pass the same set across all bindings in a config.
    /// </summary>
    public static List<ValidationMessage> ValidateBinding(
        MetricBinding binding, HashSet<string> seenNodeProperties)
    {
        var messages = new List<ValidationMessage>();
        var context = $"{binding.SceneNode}+{binding.Property}";

        if (string.IsNullOrWhiteSpace(binding.Metric))
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required field: metric", context));

        if (string.IsNullOrWhiteSpace(binding.Property))
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required field: property", context));

        if (binding.SourceRangeMin >= binding.SourceRangeMax)
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"source_range[0] must be less than source_range[1]: got [{binding.SourceRangeMin}, {binding.SourceRangeMax}]",
                context));

        if (binding.TargetRangeMin >= binding.TargetRangeMax)
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"target_range[0] must be less than target_range[1]: got [{binding.TargetRangeMin}, {binding.TargetRangeMax}]",
                context));

        if (binding.InstanceName != null && binding.InstanceId != null)
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "instance_name and instance_id are mutually exclusive", context));

        var nodePropertyKey = $"{binding.SceneNode}+{binding.Property}";
        if (!seenNodeProperties.Add(nodePropertyKey))
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Duplicate binding for {nodePropertyKey}", context));

        var propertyMessage = ValidateProperty(binding.Property);
        if (propertyMessage != null)
            messages.Add(propertyMessage);

        return messages;
    }

    /// <summary>
    /// Classifies a single property name: built-in (no message), custom (info), or empty (null — handled elsewhere).
    /// </summary>
    public static ValidationMessage? ValidateProperty(string property)
    {
        if (string.IsNullOrWhiteSpace(property))
            return null;

        if (PropertyVocabulary.IsBuiltIn(property))
            return null;

        return new ValidationMessage(ValidationSeverity.Info,
            $"Property '{property}' is a custom pass-through — will validate against scene node at load time",
            null);
    }
}
