using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests that ConnectAsync wraps non-HTTP exceptions (e.g., malformed JSON)
/// in PcpConnectionException rather than leaking implementation details.
/// </summary>
public class ConnectJsonParseTests
{
    [Fact]
    public async Task ConnectAsync_MalformedJson_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json at all",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        var ex = await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
        Assert.Contains("Unexpected response", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_MissingContextField_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"source\":\"local\"}",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        var ex = await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
        Assert.Contains("Unexpected response", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_MalformedJson_SetsStateToFailed()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("garbage",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        try { await client.ConnectAsync(); } catch { }

        Assert.Equal(ConnectionState.Failed, client.State);
    }
}
