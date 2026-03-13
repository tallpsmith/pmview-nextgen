using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class MetricDiscoveryIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task GetChildrenAsync_RootPrefix_ReturnsNonEmptyNamespace()
    {
        await Client.ConnectAsync();

        var root = await Client.GetChildrenAsync();

        Assert.NotEmpty(root.NonLeafNames);
    }

    [Fact]
    public async Task GetChildrenAsync_KernelPrefix_ContainsAllChild()
    {
        await Client.ConnectAsync();

        var ns = await Client.GetChildrenAsync("kernel");

        Assert.Contains("all", ns.NonLeafNames);
    }

    [Fact]
    public async Task GetChildrenAsync_NonExistentPrefix_ReturnsEmptyNamespace()
    {
        await Client.ConnectAsync();

        var ns = await Client.GetChildrenAsync("totally.bogus.metric.path");

        Assert.Empty(ns.LeafNames);
        Assert.Empty(ns.NonLeafNames);
    }

    [Fact]
    public async Task DescribeMetricsAsync_KnownMetric_ReturnsDescriptor()
    {
        await Client.ConnectAsync();

        var descriptors = await Client.DescribeMetricsAsync(["kernel.all.load"]);

        Assert.Single(descriptors);
        var desc = descriptors[0];
        Assert.Equal("kernel.all.load", desc.Name);
        Assert.NotEmpty(desc.Pmid);
        Assert.NotEmpty(desc.OneLineHelp);
    }

    [Fact]
    public async Task DescribeMetricsAsync_MultipleMetrics_ReturnsAll()
    {
        await Client.ConnectAsync();

        var names = new[] { "kernel.all.load", "kernel.all.cpu.user" };
        var descriptors = await Client.DescribeMetricsAsync(names);

        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, d => d.Name == "kernel.all.load");
        Assert.Contains(descriptors, d => d.Name == "kernel.all.cpu.user");
    }

    [Fact]
    public async Task GetInstanceDomainAsync_InstancedMetric_ReturnsInstances()
    {
        await Client.ConnectAsync();

        // kernel.all.load is instanced (1min, 5min, 15min)
        var indom = await Client.GetInstanceDomainAsync("kernel.all.load");

        Assert.NotEmpty(indom.Instances);
        Assert.Contains(indom.Instances, i => i.Name == "1 minute");
    }

    [Fact]
    public async Task GetInstanceDomainAsync_SingularMetric_ReturnsEmptyDomain()
    {
        await Client.ConnectAsync();

        // hinv.ncpu is a singular metric (no instances)
        var indom = await Client.GetInstanceDomainAsync("hinv.ncpu");

        Assert.Empty(indom.Instances);
    }
}
