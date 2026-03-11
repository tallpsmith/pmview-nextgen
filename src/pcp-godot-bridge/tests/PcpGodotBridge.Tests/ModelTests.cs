using Xunit;

namespace PcpGodotBridge.Tests;

/// <summary>
/// Tests for binding config model records: BindingConfig, MetricBinding,
/// ValidationMessage, BindingConfigResult, PropertyVocabulary.
/// Per design spec 2026-03-10.
/// </summary>
public class ModelTests
{
    // ── MetricBinding Record ──

    [Fact]
    public void MetricBinding_StoresAllFields()
    {
        var binding = new MetricBinding(
            SceneNode: "CpuLoadBar",
            Metric: "kernel.all.load",
            Property: "height",
            SourceRangeMin: 0.0,
            SourceRangeMax: 10.0,
            TargetRangeMin: 0.0,
            TargetRangeMax: 5.0,
            InstanceId: null,
            InstanceName: "1 minute");

        Assert.Equal("CpuLoadBar", binding.SceneNode);
        Assert.Equal("kernel.all.load", binding.Metric);
        Assert.Equal("height", binding.Property);
        Assert.Equal(0.0, binding.SourceRangeMin);
        Assert.Equal(10.0, binding.SourceRangeMax);
        Assert.Equal(0.0, binding.TargetRangeMin);
        Assert.Equal(5.0, binding.TargetRangeMax);
        Assert.Null(binding.InstanceId);
        Assert.Equal("1 minute", binding.InstanceName);
    }

    [Fact]
    public void MetricBinding_WithInstanceName()
    {
        var binding = new MetricBinding("Node", "metric", "height",
            0, 10, 0, 5, InstanceId: null, InstanceName: "5 minute");

        Assert.Equal("5 minute", binding.InstanceName);
        Assert.Null(binding.InstanceId);
    }

    [Fact]
    public void MetricBinding_WithInstanceId()
    {
        var binding = new MetricBinding("Node", "metric", "height",
            0, 10, 0, 5, InstanceId: 42, InstanceName: null);

        Assert.Equal(42, binding.InstanceId);
        Assert.Null(binding.InstanceName);
    }

    [Fact]
    public void MetricBinding_ValueEquality()
    {
        var a = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);
        var b = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);

        Assert.Equal(a, b);
    }

    // ── BindingConfig Record ──

    [Fact]
    public void BindingConfig_StoresAllFields()
    {
        var bindings = new[] {
            new MetricBinding("Bar", "kernel.all.load", "height",
                0, 10, 0, 5, null, null)
        };

        var config = new BindingConfig(
            ScenePath: "res://scenes/test.tscn",
            Endpoint: "http://localhost:44322",
            PollIntervalMs: 1000,
            Description: "Test config",
            Bindings: bindings);

        Assert.Equal("res://scenes/test.tscn", config.ScenePath);
        Assert.Equal("http://localhost:44322", config.Endpoint);
        Assert.Equal(1000, config.PollIntervalMs);
        Assert.Equal("Test config", config.Description);
        Assert.Single(config.Bindings);
    }

    [Fact]
    public void BindingConfig_OptionalFieldsNullable()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            Array.Empty<MetricBinding>());

        Assert.Null(config.Endpoint);
        Assert.Null(config.Description);
    }

    // ── ValidationMessage ──

    [Fact]
    public void ValidationMessage_StoresSeverityAndMessage()
    {
        var msg = new ValidationMessage(
            ValidationSeverity.Error,
            "Missing required field: metric",
            "bindings[2]");

        Assert.Equal(ValidationSeverity.Error, msg.Severity);
        Assert.Equal("Missing required field: metric", msg.Message);
        Assert.Equal("bindings[2]", msg.BindingContext);
    }

    [Fact]
    public void ValidationMessage_NullContextAllowed()
    {
        var msg = new ValidationMessage(ValidationSeverity.Info,
            "Config loaded", null);

        Assert.Null(msg.BindingContext);
    }

    // ── ValidationSeverity Enum ──

    [Theory]
    [InlineData(ValidationSeverity.Info)]
    [InlineData(ValidationSeverity.Warning)]
    [InlineData(ValidationSeverity.Error)]
    public void ValidationSeverity_HasExpectedValues(ValidationSeverity severity)
    {
        Assert.True(Enum.IsDefined(severity));
    }

    // ── BindingConfigResult ──

    [Fact]
    public void BindingConfigResult_ValidConfig_IsValid()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            new[] { new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null) });
        var result = new BindingConfigResult(config,
            new[] { new ValidationMessage(ValidationSeverity.Info, "ok", null) });

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.NotNull(result.Config);
    }

    [Fact]
    public void BindingConfigResult_WithErrors_IsNotValid()
    {
        var result = new BindingConfigResult(null,
            new[] { new ValidationMessage(ValidationSeverity.Error, "bad", null) });

        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Null(result.Config);
    }

    [Fact]
    public void BindingConfigResult_WarningsOnly_IsStillValid()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            Array.Empty<MetricBinding>());
        var result = new BindingConfigResult(config,
            new[] { new ValidationMessage(ValidationSeverity.Warning, "eh", null) });

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
    }

    // ── PropertyVocabulary ──

    [Theory]
    [InlineData("height")]
    [InlineData("width")]
    [InlineData("depth")]
    [InlineData("scale")]
    [InlineData("rotation_speed")]
    [InlineData("position_y")]
    [InlineData("color_temperature")]
    [InlineData("opacity")]
    public void PropertyVocabulary_RecognisesBuiltInProperties(string property)
    {
        Assert.True(PropertyVocabulary.IsBuiltIn(property));
    }

    [Theory]
    [InlineData("river_flow_speed")]
    [InlineData("wind_intensity")]
    [InlineData("fire_brightness")]
    [InlineData("nonexistent")]
    public void PropertyVocabulary_DoesNotRecogniseCustomProperties(string property)
    {
        Assert.False(PropertyVocabulary.IsBuiltIn(property));
    }

    [Fact]
    public void PropertyVocabulary_ClassifiesBuiltInAsBuiltIn()
    {
        var kind = PropertyVocabulary.Classify("height");

        Assert.Equal(PropertyKind.BuiltIn, kind);
    }

    [Fact]
    public void PropertyVocabulary_ClassifiesUnknownAsCustom()
    {
        var kind = PropertyVocabulary.Classify("river_flow_speed");

        Assert.Equal(PropertyKind.Custom, kind);
    }

    [Fact]
    public void PropertyVocabulary_ResolvesBuiltInToGodotProperty()
    {
        Assert.Equal("scale:y", PropertyVocabulary.ResolveGodotProperty("height"));
        Assert.Equal("scale:x", PropertyVocabulary.ResolveGodotProperty("width"));
        Assert.Equal("scale:z", PropertyVocabulary.ResolveGodotProperty("depth"));
        Assert.Equal("scale", PropertyVocabulary.ResolveGodotProperty("scale"));
        Assert.Equal("rotation:y", PropertyVocabulary.ResolveGodotProperty("rotation_speed"));
        Assert.Equal("position:y", PropertyVocabulary.ResolveGodotProperty("position_y"));
    }

    [Fact]
    public void PropertyVocabulary_ResolvesCustomToSameName()
    {
        Assert.Equal("river_flow_speed",
            PropertyVocabulary.ResolveGodotProperty("river_flow_speed"));
    }

    // ── ResolvedBinding ──

    [Fact]
    public void ResolvedBinding_StoresBindingAndResolution()
    {
        var binding = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);
        var resolved = new ResolvedBinding(binding, PropertyKind.BuiltIn, "scale:y");

        Assert.Equal(binding, resolved.Binding);
        Assert.Equal(PropertyKind.BuiltIn, resolved.Kind);
        Assert.Equal("scale:y", resolved.GodotPropertyName);
    }
}
