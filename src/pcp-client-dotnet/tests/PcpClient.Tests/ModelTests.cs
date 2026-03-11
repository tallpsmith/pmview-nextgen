using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for PCP domain model records: MetricDescriptor, MetricValue, InstanceValue,
/// Instance, InstanceDomain, and related enums.
/// Per data-model.md specification.
/// </summary>
public class ModelTests
{
    // ── MetricType Enum ──

    [Theory]
    [InlineData(MetricType.Float)]
    [InlineData(MetricType.Double)]
    [InlineData(MetricType.U32)]
    [InlineData(MetricType.U64)]
    [InlineData(MetricType.I32)]
    [InlineData(MetricType.I64)]
    [InlineData(MetricType.String)]
    public void MetricType_HasExpectedValues(MetricType type)
    {
        Assert.True(Enum.IsDefined(type));
    }

    // ── MetricSemantics Enum ──

    [Theory]
    [InlineData(MetricSemantics.Instant)]
    [InlineData(MetricSemantics.Counter)]
    [InlineData(MetricSemantics.Discrete)]
    public void MetricSemantics_HasExpectedValues(MetricSemantics semantics)
    {
        Assert.True(Enum.IsDefined(semantics));
    }

    // ── ConnectionState Enum ──

    [Theory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Failed)]
    public void ConnectionState_HasExpectedValues(ConnectionState state)
    {
        Assert.True(Enum.IsDefined(state));
    }

    // ── Instance Record ──

    [Fact]
    public void Instance_StoresIdAndName()
    {
        var instance = new Instance(42, "cpu0");

        Assert.Equal(42, instance.Id);
        Assert.Equal("cpu0", instance.Name);
    }

    [Fact]
    public void Instance_ValueEquality()
    {
        var a = new Instance(1, "sda");
        var b = new Instance(1, "sda");

        Assert.Equal(a, b);
    }

    // ── InstanceDomain Record ──

    [Fact]
    public void InstanceDomain_StoresIndomIdAndInstances()
    {
        var instances = new[] { new Instance(0, "cpu0"), new Instance(1, "cpu1") };
        var domain = new InstanceDomain("60.2", instances);

        Assert.Equal("60.2", domain.IndomId);
        Assert.Equal(2, domain.Instances.Count);
        Assert.Equal("cpu0", domain.Instances[0].Name);
    }

    [Fact]
    public void InstanceDomain_EmptyInstancesAllowed()
    {
        var domain = new InstanceDomain("60.2", Array.Empty<Instance>());

        Assert.Empty(domain.Instances);
    }

    // ── InstanceValue Record ──

    [Fact]
    public void InstanceValue_SingularMetric_NullInstanceId()
    {
        var iv = new InstanceValue(null, 3.14);

        Assert.Null(iv.InstanceId);
        Assert.Equal(3.14, iv.Value);
    }

    [Fact]
    public void InstanceValue_InstancedMetric_HasInstanceId()
    {
        var iv = new InstanceValue(7, 42.0);

        Assert.Equal(7, iv.InstanceId);
        Assert.Equal(42.0, iv.Value);
    }

    // ── MetricDescriptor Record ──

    [Fact]
    public void MetricDescriptor_StoresAllFields()
    {
        var descriptor = new MetricDescriptor(
            Name: "kernel.all.load",
            Pmid: "60.2.0",
            Type: MetricType.Float,
            Semantics: MetricSemantics.Instant,
            Units: "load",
            IndomId: "60.2",
            OneLineHelp: "1, 5 and 15 minute load average",
            LongHelp: null);

        Assert.Equal("kernel.all.load", descriptor.Name);
        Assert.Equal("60.2.0", descriptor.Pmid);
        Assert.Equal(MetricType.Float, descriptor.Type);
        Assert.Equal(MetricSemantics.Instant, descriptor.Semantics);
        Assert.Equal("load", descriptor.Units);
        Assert.Equal("60.2", descriptor.IndomId);
        Assert.Equal("1, 5 and 15 minute load average", descriptor.OneLineHelp);
        Assert.Null(descriptor.LongHelp);
    }

    [Fact]
    public void MetricDescriptor_SingularMetric_NullIndomId()
    {
        var descriptor = new MetricDescriptor(
            Name: "hinv.ncpu",
            Pmid: "60.0.32",
            Type: MetricType.U32,
            Semantics: MetricSemantics.Discrete,
            Units: null,
            IndomId: null,
            OneLineHelp: "number of CPUs",
            LongHelp: null);

        Assert.Null(descriptor.IndomId);
    }

    // ── MetricValue Record ──

    [Fact]
    public void MetricValue_StoresTimestampAndInstances()
    {
        var instanceValues = new[]
        {
            new InstanceValue(null, 2.5)
        };

        var value = new MetricValue(
            Name: "kernel.all.load",
            Pmid: "60.2.0",
            Timestamp: 1709654400.123456,
            InstanceValues: instanceValues);

        Assert.Equal("kernel.all.load", value.Name);
        Assert.Equal("60.2.0", value.Pmid);
        Assert.Equal(1709654400.123456, value.Timestamp, precision: 6);
        Assert.Single(value.InstanceValues);
    }

    [Fact]
    public void MetricValue_InstancedMetric_MultipleValues()
    {
        var instanceValues = new[]
        {
            new InstanceValue(0, 100.0),
            new InstanceValue(1, 200.0),
            new InstanceValue(2, 150.0)
        };

        var value = new MetricValue(
            Name: "disk.dev.read",
            Pmid: "60.10.3",
            Timestamp: 1709654400.0,
            InstanceValues: instanceValues);

        Assert.Equal(3, value.InstanceValues.Count);
    }

    // ── Immutability ──

    [Fact]
    public void Records_AreImmutable()
    {
        // Records in C# are immutable by default — this test validates
        // that our domain types are indeed records (compile-time guarantee
        // via the test using positional syntax).
        var desc1 = new MetricDescriptor("a", "1.0", MetricType.Float,
            MetricSemantics.Instant, null, null, "help", null);
        var desc2 = desc1 with { Name = "b" };

        Assert.Equal("a", desc1.Name);
        Assert.Equal("b", desc2.Name);
    }
}
