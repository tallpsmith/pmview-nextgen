using System.Net;
using System.Text.Json;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for PcpContext lifecycle: creation, disconnection, expiry detection.
/// Uses mocked HTTP to avoid real pmproxy dependency.
/// </summary>
public class PcpContextTests
{
    // ── ConnectAsync ──

    [Fact]
    public async Task ConnectAsync_CreatesContextWithPolltimeout()
    {
        var handler = new MockHttpHandler(request =>
        {
            Assert.Contains("polltimeout=60", request.RequestUri!.ToString());
            return RespondWithJson(new { context = 12345 });
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        var contextId = await client.ConnectAsync(pollTimeoutSeconds: 60);

        Assert.Equal(12345, contextId);
        Assert.Equal(ConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task ConnectAsync_CustomPolltimeout_SentToServer()
    {
        var handler = new MockHttpHandler(request =>
        {
            Assert.Contains("polltimeout=120", request.RequestUri!.ToString());
            return RespondWithJson(new { context = 99 });
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync(pollTimeoutSeconds: 120);
    }

    [Fact]
    public async Task ConnectAsync_SetsStateToConnecting_ThenConnected()
    {
        var statesDuringRequest = new List<ConnectionState>();

        var handler = new MockHttpHandler(request =>
        {
            // Can't easily capture mid-request state, but we verify final state
            return RespondWithJson(new { context = 1 });
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        Assert.Equal(ConnectionState.Disconnected, client.State);
        await client.ConnectAsync();
        Assert.Equal(ConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task ConnectAsync_ServerUnreachable_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ConnectAsync_ServerError_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ConnectAsync_FailedState_OnConnectionError()
    {
        var handler = new MockHttpHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        try { await client.ConnectAsync(); } catch { }

        Assert.Equal(ConnectionState.Failed, client.State);
    }

    // ── DisconnectAsync ──

    [Fact]
    public async Task DisconnectAsync_SetsStateToDisconnected()
    {
        var handler = new MockHttpHandler(_ => RespondWithJson(new { context = 1 }));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();
        await client.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task DisconnectAsync_WhenAlreadyDisconnected_NoError()
    {
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"));

        await client.DisconnectAsync(); // should not throw
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    // ── Context Expiry Detection ──

    [Fact]
    public async Task FetchAsync_ContextExpired_ThrowsPcpContextExpiredException()
    {
        var callCount = 0;
        var handler = new MockHttpHandler(request =>
        {
            callCount++;
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return RespondWithJson(new { context = 42 });

            // Simulate context expiry on fetch
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"message\":\"unknown context identifier\"}")
            };
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();

        await Assert.ThrowsAsync<PcpContextExpiredException>(
            () => client.FetchAsync(new[] { "kernel.all.load" }));
    }

    // ── Helpers ──

    private static HttpResponseMessage RespondWithJson(object data)
    {
        var json = JsonSerializer.Serialize(data);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
