using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for IPcpClient interface contract and PcpClient construction.
/// Per contracts/pcpclient-api.md specification.
/// </summary>
public class PcpClientConstructionTests
{
    // ── Construction ──

    [Fact]
    public void Constructor_WithUri_SetsBaseUrl()
    {
        var url = new Uri("http://localhost:44322");
        using var client = new PcpClientConnection(url);

        Assert.Equal(url, client.BaseUrl);
    }

    [Fact]
    public void Constructor_InitialState_IsDisconnected()
    {
        var url = new Uri("http://localhost:44322");
        using var client = new PcpClientConnection(url);

        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public void Constructor_WithCustomHttpClient_AcceptsIt()
    {
        var url = new Uri("http://localhost:44322");
        var httpClient = new HttpClient();
        using var client = new PcpClientConnection(url, httpClient);

        Assert.Equal(url, client.BaseUrl);
    }

    [Fact]
    public void Constructor_NullUri_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PcpClientConnection(null!));
    }

    // ── IPcpClient Interface ──

    [Fact]
    public void PcpClient_ImplementsIPcpClient()
    {
        var url = new Uri("http://localhost:44322");
        using var client = new PcpClientConnection(url);

        Assert.IsAssignableFrom<IPcpClient>(client);
    }

    [Fact]
    public void PcpClient_ImplementsIAsyncDisposable()
    {
        var url = new Uri("http://localhost:44322");
        using var client = new PcpClientConnection(url);

        Assert.IsAssignableFrom<IAsyncDisposable>(client);
    }

    // ── Disposal ──

    [Fact]
    public async Task DisposeAsync_SetsStateToDisconnected()
    {
        var url = new Uri("http://localhost:44322");
        var client = new PcpClientConnection(url);

        await client.DisposeAsync();

        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task DisposeAsync_MultipleCallsSafe()
    {
        var url = new Uri("http://localhost:44322");
        var client = new PcpClientConnection(url);

        await client.DisposeAsync();
        await client.DisposeAsync(); // should not throw
    }

    // ── Synchronous IDisposable for using statement ──

    [Fact]
    public void Dispose_WorksSynchronously()
    {
        var url = new Uri("http://localhost:44322");
        var client = new PcpClientConnection(url);

        client.Dispose(); // should not throw

        Assert.Equal(ConnectionState.Disconnected, client.State);
    }
}
