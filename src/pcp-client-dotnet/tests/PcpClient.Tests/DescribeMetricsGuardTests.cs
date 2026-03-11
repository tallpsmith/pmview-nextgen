using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for DescribeMetricsAsync state guards and exception wrapping.
/// Covers I-2 (connection state guard) and C-3 (exception wrapping).
/// </summary>
public class DescribeMetricsGuardTests
{
    [Fact]
    public async Task DescribeMetricsAsync_BeforeConnect_ThrowsPcpConnectionException()
    {
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"));

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.DescribeMetricsAsync(new[] { "kernel.all.load" }));
    }

    [Fact]
    public async Task DescribeMetricsAsync_AfterDisconnect_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"context\":1}",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();
        await client.DisconnectAsync();

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.DescribeMetricsAsync(new[] { "kernel.all.load" }));
    }

    [Fact]
    public async Task DescribeMetricsAsync_HttpError_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"context\":1}",
                        System.Text.Encoding.UTF8, "application/json")
                };

            throw new HttpRequestException("Connection refused");
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.DescribeMetricsAsync(new[] { "kernel.all.load" }));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
