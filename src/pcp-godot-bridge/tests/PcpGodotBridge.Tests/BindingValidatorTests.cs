using Xunit;

namespace PcpGodotBridge.Tests;

/// <summary>
/// Tests for BindingValidator.ValidateBinding() — semantic validation
/// of individual MetricBinding records independent of TOML parsing.
/// </summary>
public class BindingValidatorValidateBindingTests
{
    private static MetricBinding MakeValid(
        string sceneNode = "Bar",
        string metric = "kernel.all.load",
        string property = "height",
        double srcMin = 0.0, double srcMax = 10.0,
        double tgtMin = 0.0, double tgtMax = 5.0,
        int? instanceId = null,
        string? instanceName = null,
        double initialValue = 0.0)
    {
        return new MetricBinding(sceneNode, metric, property,
            srcMin, srcMax, tgtMin, tgtMax,
            instanceId, instanceName, initialValue);
    }

    [Fact]
    public void ValidBinding_ReturnsNoErrors()
    {
        var binding = MakeValid();
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void MissingMetricName_ReturnsError()
    {
        var binding = MakeValid(metric: "");
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("metric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingTargetProperty_ReturnsError()
    {
        var binding = MakeValid(property: "");
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("property", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceRangeMinEqualsMax_ReturnsError()
    {
        var binding = MakeValid(srcMin: 5.0, srcMax: 5.0);
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("source_range"));
    }

    [Fact]
    public void SourceRangeReversed_ReturnsError()
    {
        var binding = MakeValid(srcMin: 10.0, srcMax: 0.0);
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("source_range"));
    }

    [Fact]
    public void TargetRangeMinEqualsMax_ReturnsError()
    {
        var binding = MakeValid(tgtMin: 3.0, tgtMax: 3.0);
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("target_range"));
    }

    [Fact]
    public void TargetRangeReversed_ReturnsError()
    {
        var binding = MakeValid(tgtMin: 5.0, tgtMax: 0.0);
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("target_range"));
    }

    [Fact]
    public void BothInstanceNameAndId_ReturnsError()
    {
        var binding = MakeValid(instanceId: 1, instanceName: "1 minute");
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("mutually exclusive"));
    }

    [Fact]
    public void DuplicateNodeProperty_ReturnsError()
    {
        var binding1 = MakeValid(sceneNode: "Bar", property: "height");
        var binding2 = MakeValid(sceneNode: "Bar", property: "height", metric: "disk.dev.read");
        var seen = new HashSet<string>();

        BindingValidator.ValidateBinding(binding1, seen);
        var messages = BindingValidator.ValidateBinding(binding2, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("Duplicate"));
    }

    [Fact]
    public void SameNodeDifferentProperty_NoError()
    {
        var binding1 = MakeValid(sceneNode: "Bar", property: "height");
        var binding2 = MakeValid(sceneNode: "Bar", property: "width", metric: "disk.dev.read");
        var seen = new HashSet<string>();

        BindingValidator.ValidateBinding(binding1, seen);
        var messages = BindingValidator.ValidateBinding(binding2, seen);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void CustomProperty_EmitsInfoMessage()
    {
        var binding = MakeValid(property: "river_flow_speed");
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Info &&
            m.Message.Contains("custom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltInProperty_NoInfoMessage()
    {
        var binding = MakeValid(property: "height");
        var seen = new HashSet<string>();

        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.DoesNotContain(messages, m =>
            m.Severity == ValidationSeverity.Info &&
            m.Message.Contains("custom", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Tests for BindingValidator.ValidateProperty() — property classification
/// as standalone validation independent of a full binding.
/// </summary>
public class BindingValidatorValidatePropertyTests
{
    [Theory]
    [InlineData("height")]
    [InlineData("width")]
    [InlineData("rotation_speed")]
    [InlineData("opacity")]
    public void BuiltInProperty_ReturnsNull(string property)
    {
        var result = BindingValidator.ValidateProperty(property);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("river_flow_speed")]
    [InlineData("wind_intensity")]
    [InlineData("fire_brightness")]
    public void CustomProperty_ReturnsInfoMessage(string property)
    {
        var result = BindingValidator.ValidateProperty(property);

        Assert.NotNull(result);
        Assert.Equal(ValidationSeverity.Info, result!.Severity);
        Assert.Contains("custom", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyOrWhitespace_ReturnsNull(string property)
    {
        // Empty property is handled by the required field check in ValidateBinding
        var result = BindingValidator.ValidateProperty(property);

        Assert.Null(result);
    }
}

/// <summary>
/// Tests for BindingValidator.ValidateBindingSet() — validates a complete
/// set of bindings for cross-binding issues like duplicate node+property targets.
/// </summary>
public class BindingValidatorValidateBindingSetTests
{
    private static MetricBinding MakeValid(
        string sceneNode = "Bar",
        string metric = "kernel.all.load",
        string property = "height",
        double srcMin = 0.0, double srcMax = 10.0,
        double tgtMin = 0.0, double tgtMax = 5.0)
    {
        return new MetricBinding(sceneNode, metric, property,
            srcMin, srcMax, tgtMin, tgtMax, null, null, 0.0);
    }

    [Fact]
    public void EmptySet_ReturnsNoErrors()
    {
        var messages = BindingValidator.ValidateBindingSet(Array.Empty<MetricBinding>());

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void SingleValidBinding_ReturnsNoErrors()
    {
        var bindings = new[] { MakeValid() };

        var messages = BindingValidator.ValidateBindingSet(bindings);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void TwoBindingsSamePropertySameNode_ReturnsError()
    {
        var bindings = new[]
        {
            MakeValid(sceneNode: "Bar", property: "height", metric: "kernel.all.load"),
            MakeValid(sceneNode: "Bar", property: "height", metric: "disk.dev.read"),
        };

        var messages = BindingValidator.ValidateBindingSet(bindings);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("Duplicate"));
    }

    [Fact]
    public void MultipleBindingsDifferentProperties_ReturnsNoErrors()
    {
        var bindings = new[]
        {
            MakeValid(sceneNode: "Bar", property: "height"),
            MakeValid(sceneNode: "Bar", property: "width", metric: "disk.dev.read"),
            MakeValid(sceneNode: "Spinner", property: "rotation_speed", metric: "disk.dev.write"),
        };

        var messages = BindingValidator.ValidateBindingSet(bindings);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void InvalidBinding_InSet_ReturnsError()
    {
        var bindings = new[]
        {
            MakeValid(),
            new MetricBinding("Bar2", "", "height", 0, 10, 0, 5, null, null, 0.0),
        };

        var messages = BindingValidator.ValidateBindingSet(bindings);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("metric", StringComparison.OrdinalIgnoreCase));
    }
}
