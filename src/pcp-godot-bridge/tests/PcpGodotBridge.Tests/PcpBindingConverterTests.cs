using Xunit;

namespace PcpGodotBridge.Tests;

/// <summary>
/// Tests for PcpBindingConverter.ToMetricBinding() — converting editor property
/// values (primitives from Godot Resource exports) into MetricBinding records.
/// </summary>
public class PcpBindingConverterTests
{
    [Fact]
    public void AllFieldsMappedCorrectly()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            sceneNode: "CpuBar",
            metricName: "kernel.all.load",
            targetProperty: "height",
            sourceRangeMin: 0.0, sourceRangeMax: 10.0,
            targetRangeMin: 0.0, targetRangeMax: 5.0,
            instanceId: 42,
            instanceName: "1 minute",
            initialValue: 1.5);

        Assert.Equal("CpuBar", binding.SceneNode);
        Assert.Equal("kernel.all.load", binding.Metric);
        Assert.Equal("height", binding.Property);
        Assert.Equal(0.0, binding.SourceRangeMin);
        Assert.Equal(10.0, binding.SourceRangeMax);
        Assert.Equal(0.0, binding.TargetRangeMin);
        Assert.Equal(5.0, binding.TargetRangeMax);
        Assert.Equal(42, binding.InstanceId);
        Assert.Equal("1 minute", binding.InstanceName);
        Assert.Equal(1.5, binding.InitialValue);
    }

    [Fact]
    public void InstanceIdNegativeOne_MapsToNull()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: -1, instanceName: null, initialValue: 0);

        Assert.Null(binding.InstanceId);
    }

    [Fact]
    public void InstanceIdPositive_PreservedAsIs()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: 7, instanceName: null, initialValue: 0);

        Assert.Equal(7, binding.InstanceId);
    }

    [Fact]
    public void InstanceIdZero_PreservedAsIs()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: 0, instanceName: null, initialValue: 0);

        Assert.Equal(0, binding.InstanceId);
    }

    [Fact]
    public void EmptyInstanceName_MapsToNull()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: -1, instanceName: "", initialValue: 0);

        Assert.Null(binding.InstanceName);
    }

    [Fact]
    public void WhitespaceInstanceName_MapsToNull()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: -1, instanceName: "  ", initialValue: 0);

        Assert.Null(binding.InstanceName);
    }

    [Fact]
    public void NonEmptyInstanceName_PreservedAsIs()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: -1, instanceName: "nvme0n1", initialValue: 0);

        Assert.Equal("nvme0n1", binding.InstanceName);
    }

    [Fact]
    public void InitialValue_Preserved()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Node", "metric", "height",
            0, 10, 0, 5,
            instanceId: -1, instanceName: null, initialValue: 3.14);

        Assert.Equal(3.14, binding.InitialValue);
    }
}

/// <summary>
/// Integration tests: PcpBindingConverter output feeds into BindingValidator correctly.
/// </summary>
public class PcpBindingConverterValidationIntegrationTests
{
    [Fact]
    public void ValidConvertedBinding_PassesValidation()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "CpuBar", "kernel.all.load", "height",
            0.0, 10.0, 0.0, 5.0,
            instanceId: -1, instanceName: null, initialValue: 0);

        var seen = new HashSet<string>();
        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void InvalidConvertedBinding_EmptyMetric_FailsValidation()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Bar", "", "height",
            0.0, 10.0, 0.0, 5.0,
            instanceId: -1, instanceName: null, initialValue: 0);

        var seen = new HashSet<string>();
        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("metric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidConvertedBinding_ReversedRange_FailsValidation()
    {
        var binding = PcpBindingConverter.ToMetricBinding(
            "Bar", "metric", "height",
            10.0, 0.0, 0.0, 5.0,
            instanceId: -1, instanceName: null, initialValue: 0);

        var seen = new HashSet<string>();
        var messages = BindingValidator.ValidateBinding(binding, seen);

        Assert.Contains(messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("source_range"));
    }
}
