using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class ErrorHandlingIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ConnectAsync_UnreachableEndpoint_ThrowsPcpConnectionException()
    {
        await using var badClient = new PcpClientConnection(new Uri("http://localhost:19999"));

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => badClient.ConnectAsync(pollTimeoutSeconds: 2));
    }

    [Fact]
    public async Task ConnectAsync_UnreachableEndpoint_StateIsFailed()
    {
        await using var badClient = new PcpClientConnection(new Uri("http://localhost:19999"));

        try { await badClient.ConnectAsync(pollTimeoutSeconds: 2); }
        catch (PcpConnectionException) { }

        Assert.Equal(ConnectionState.Failed, badClient.State);
    }

    [Fact]
    public async Task DescribeMetricsAsync_UnknownMetric_ThrowsPcpMetricNotFoundException()
    {
        await Client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<PcpMetricNotFoundException>(
            () => Client.DescribeMetricsAsync(["totally.bogus.metric.xyz"]));

        Assert.Equal("totally.bogus.metric.xyz", ex.MetricName);
    }

    [Fact]
    public async Task FetchAsync_UnknownMetric_ThrowsPcpMetricNotFoundException()
    {
        await Client.ConnectAsync();

        await Assert.ThrowsAsync<PcpMetricNotFoundException>(
            () => Client.FetchAsync(["no.such.metric.ever"]));
    }
}
