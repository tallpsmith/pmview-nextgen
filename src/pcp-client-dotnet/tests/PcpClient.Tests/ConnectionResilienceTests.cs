using System.Net;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for connection resilience: unreachable endpoints, context expiry,
/// auto-reconnection, and state transitions per data-model.md state diagram.
/// </summary>
public class ConnectionResilienceTests
{
    // ── Unreachable Endpoint ──

    [Fact]
    public async Task ConnectAsync_UnreachableEndpoint_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://unreachable:44322"), httpClient);

        var ex = await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
        Assert.Contains("unreachable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAsync_UnreachableEndpoint_WrapsInnerException()
    {
        var handler = new MockHttpHandler(_ =>
            throw new HttpRequestException("DNS failed"));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://unreachable:44322"), httpClient);

        var ex = await Assert.ThrowsAsync<PcpConnectionException>(() => client.ConnectAsync());
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ── State Transitions ──

    [Fact]
    public async Task State_Disconnected_AfterConstruction()
    {
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"));
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task State_Connected_AfterSuccessfulConnect()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"context\":1}", System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();

        Assert.Equal(ConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task State_Failed_AfterConnectionError()
    {
        var handler = new MockHttpHandler(_ =>
            throw new HttpRequestException("nope"));
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        try { await client.ConnectAsync(); } catch { }

        Assert.Equal(ConnectionState.Failed, client.State);
    }

    [Fact]
    public async Task State_Disconnected_AfterDisconnect()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"context\":1}", System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();
        await client.DisconnectAsync();

        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    // ── Context Expiry and Auto-Reconnect ──

    [Fact]
    public async Task FetchAsync_ContextExpired_AutoReconnects()
    {
        var contextCallCount = 0;
        var fetchCallCount = 0;

        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
            {
                contextCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"context\":{contextCallCount}}}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            fetchCallCount++;
            if (fetchCallCount == 1)
            {
                // First fetch: context expired
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"message\":\"unknown context identifier\"}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            // Second fetch: success after reconnect
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "timestamp": 1709654400.0,
                      "values": [
                        {
                          "pmid": "60.2.0",
                          "name": "kernel.all.load",
                          "instances": [
                            { "instance": -1, "value": 1.5 }
                          ]
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();

        var values = await client.FetchAsync(new[] { "kernel.all.load" });

        Assert.Single(values);
        Assert.Equal(2, contextCallCount); // original + reconnect
        Assert.Equal(ConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task FetchAsync_ContextExpiredAndReconnectFails_ThrowsPcpConnectionException()
    {
        var contextCallCount = 0;
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
            {
                contextCallCount++;
                if (contextCallCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"context\":1}",
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                }
                // Reconnect attempt fails
                throw new HttpRequestException("pmproxy down");
            }

            // Fetch returns context expired
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"message\":\"unknown context identifier\"}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.FetchAsync(new[] { "kernel.all.load" }));
    }

    // ── Reconnect State Transition ──

    [Fact]
    public async Task FetchAsync_AfterReconnect_StateIsConnected()
    {
        var contextCallCount = 0;
        var fetchCallCount = 0;

        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
            {
                contextCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"context\":{contextCallCount}}}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            fetchCallCount++;
            if (fetchCallCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"message\":\"unknown context identifier\"}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "timestamp": 1709654400.0,
                      "values": [
                        {
                          "pmid": "60.2.0",
                          "name": "kernel.all.load",
                          "instances": [{ "instance": -1, "value": 1.0 }]
                        }
                      ]
                    }
                    """)
            };
        });
        var httpClient = new HttpClient(handler);
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);

        await client.ConnectAsync();
        await client.FetchAsync(new[] { "kernel.all.load" });

        Assert.Equal(ConnectionState.Connected, client.State);
    }
}
