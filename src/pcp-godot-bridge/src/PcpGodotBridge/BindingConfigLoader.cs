using Tomlyn;
using Tomlyn.Model;

namespace PcpGodotBridge;

/// <summary>
/// Parses TOML binding config files and validates structure.
/// Returns a BindingConfigResult with the parsed config and validation messages.
/// All validation that can be done without Godot happens here.
/// </summary>
public static class BindingConfigLoader
{
    private const int DefaultPollIntervalMs = 1000;
    private const int MinPollIntervalMs = 100;

    public static BindingConfigResult Load(string toml)
    {
        var messages = new List<ValidationMessage>();

        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (TomlException ex)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"TOML parse error: {ex.Message}", null));
            return new BindingConfigResult(null, messages);
        }

        if (!table.TryGetValue("meta", out var metaObj) || metaObj is not TomlTable meta)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required [meta] section", null));
            return new BindingConfigResult(null, messages);
        }

        var scenePath = ValidateScenePath(meta, messages);
        if (scenePath == null)
            return new BindingConfigResult(null, messages);

        var endpoint = meta.TryGetValue("endpoint", out var ep) ? ep?.ToString() : null;
        var pollIntervalMs = ValidatePollInterval(meta, messages);
        var description = meta.TryGetValue("description", out var desc) ? desc?.ToString() : null;

        if (!table.TryGetValue("bindings", out var bindingsObj) || bindingsObj is not TomlTableArray bindingsArray)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required [[bindings]] section", null));
            return new BindingConfigResult(null, messages);
        }

        var bindings = new List<MetricBinding>();
        var seenNodeProperties = new HashSet<string>();

        for (var i = 0; i < bindingsArray.Count; i++)
        {
            var bindingTable = bindingsArray[i];
            var context = FormatBindingContext(i, bindingTable);
            var binding = ValidateBinding(bindingTable, i, context, seenNodeProperties, messages);
            if (binding != null)
            {
                bindings.Add(binding);
                LogBindingInfo(binding, i, messages, context);
            }
        }

        var config = new BindingConfig(scenePath, endpoint, pollIntervalMs, description, bindings);
        var skipped = bindingsArray.Count - bindings.Count;

        messages.Add(new ValidationMessage(ValidationSeverity.Info,
            $"Config loaded: {bindings.Count} active binding{(bindings.Count == 1 ? "" : "s")}" +
            (skipped > 0 ? $", {skipped} skipped" : ""),
            null));

        return new BindingConfigResult(config, messages);
    }

    public static BindingConfigResult LoadFromFile(string filePath)
    {
        try
        {
            var toml = File.ReadAllText(filePath);
            return Load(toml);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BindingConfigResult(null, new[]
            {
                new ValidationMessage(ValidationSeverity.Error,
                    $"Cannot read file: {ex.Message}", null)
            });
        }
    }

    private static string? ValidateScenePath(TomlTable meta, List<ValidationMessage> messages)
    {
        if (!meta.TryGetValue("scene", out var sceneObj) || sceneObj is not string scene
            || string.IsNullOrWhiteSpace(scene))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required field: [meta].scene", null));
            return null;
        }

        if (!scene.StartsWith("res://") || !scene.EndsWith(".tscn"))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"[meta].scene must start with 'res://' and end with '.tscn', got: '{scene}'",
                null));
            return null;
        }

        return scene;
    }

    private static int ValidatePollInterval(TomlTable meta, List<ValidationMessage> messages)
    {
        if (!meta.TryGetValue("poll_interval_ms", out var pollObj))
            return DefaultPollIntervalMs;

        var pollValue = Convert.ToInt32(pollObj);
        if (pollValue < MinPollIntervalMs)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Warning,
                $"poll_interval_ms must be >= {MinPollIntervalMs}, got {pollValue}. Using default {DefaultPollIntervalMs}.",
                null));
            return DefaultPollIntervalMs;
        }

        return pollValue;
    }

    private static MetricBinding? ValidateBinding(TomlTable binding, int index, string context,
        HashSet<string> seenNodeProperties, List<ValidationMessage> messages)
    {
        // Phase 1: TOML field extraction (format-specific)
        var sceneNode = GetRequiredString(binding, "scene_node", context, messages);
        var metric = GetRequiredString(binding, "metric", context, messages);
        var property = GetRequiredString(binding, "property", context, messages);

        if (sceneNode == null || metric == null || property == null)
            return null;

        var sourceRange = ValidateRange(binding, "source_range", context, messages);
        var targetRange = ValidateRange(binding, "target_range", context, messages);

        if (sourceRange == null || targetRange == null)
            return null;

        string? instanceName = binding.TryGetValue("instance_name", out var nameObj)
            ? nameObj?.ToString() : null;
        int? instanceId = binding.TryGetValue("instance_id", out var idObj)
            ? Convert.ToInt32(idObj) : null;

        var metricBinding = new MetricBinding(sceneNode, metric, property,
            sourceRange.Value.min, sourceRange.Value.max,
            targetRange.Value.min, targetRange.Value.max,
            instanceId, instanceName);

        // Phase 2: Semantic validation (format-independent, delegates to BindingValidator)
        var validationMessages = BindingValidator.ValidateBinding(metricBinding, seenNodeProperties);

        // Rewrite contexts to use TOML-specific context string
        foreach (var msg in validationMessages)
        {
            messages.Add(new ValidationMessage(msg.Severity, msg.Message, context));
        }

        var hasErrors = validationMessages.Any(m => m.Severity == ValidationSeverity.Error);
        return hasErrors ? null : metricBinding;
    }

    private static string? GetRequiredString(TomlTable table, string key, string context,
        List<ValidationMessage> messages)
    {
        if (!table.TryGetValue(key, out var value) || value is not string str
            || string.IsNullOrWhiteSpace(str))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Missing required field: {key}", context));
            return null;
        }
        return str;
    }

    private static (double min, double max)? ValidateRange(TomlTable table, string key,
        string context, List<ValidationMessage> messages)
    {
        if (!table.TryGetValue(key, out var rangeObj) || rangeObj is not TomlArray rangeArray)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Missing required field: {key}", context));
            return null;
        }

        if (rangeArray.Count != 2)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"{key} must have exactly 2 elements, got {rangeArray.Count}", context));
            return null;
        }

        var min = Convert.ToDouble(rangeArray[0]);
        var max = Convert.ToDouble(rangeArray[1]);

        if (min >= max)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"{key}[0] must be less than {key}[1]: got [{min}, {max}]", context));
            return null;
        }

        return (min, max);
    }

    private static void LogBindingInfo(MetricBinding binding, int index,
        List<ValidationMessage> messages, string context)
    {
        var instanceInfo = binding.InstanceName != null
            ? $" (name: {binding.InstanceName})"
            : binding.InstanceId != null
                ? $" (instance: {binding.InstanceId})"
                : "";

        messages.Add(new ValidationMessage(ValidationSeverity.Info,
            $"{binding.SceneNode}.{binding.Property} <- {binding.Metric} " +
            $"[{binding.SourceRangeMin}-{binding.SourceRangeMax}] -> " +
            $"[{binding.TargetRangeMin}-{binding.TargetRangeMax}]{instanceInfo}",
            context));
    }

    private static string FormatBindingContext(int index, TomlTable binding)
    {
        var node = binding.TryGetValue("scene_node", out var n) ? n?.ToString() : "?";
        var metric = binding.TryGetValue("metric", out var m) ? m?.ToString() : "?";
        return $"bindings[{index}] (scene_node='{node}', metric='{metric}')";
    }
}
