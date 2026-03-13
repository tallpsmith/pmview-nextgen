using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class ConnectionLifecycleTests : IntegrationTestBase
{
    [Fact]
    public async Task ConnectAsync_ReturnsValidContextId()
    {
        var contextId = await Client.ConnectAsync();

        Assert.True(contextId > 0, "Context ID should be a positive integer");
        Assert.Equal(ConnectionState.Connected, Client.State);
    }

    [Fact]
    public async Task ConnectAsync_ThenDisconnect_ReturnsToDisconnected()
    {
        await Client.ConnectAsync();
        Assert.Equal(ConnectionState.Connected, Client.State);

        await Client.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, Client.State);
    }

    [Fact]
    public async Task MultipleConnections_EachGetsUniqueContext()
    {
        await using var client2 = CreateClient();

        var ctx1 = await Client.ConnectAsync();
        var ctx2 = await client2.ConnectAsync();

        Assert.NotEqual(ctx1, ctx2);
    }

    [Fact]
    public async Task InitialState_IsDisconnected()
    {
        await using var fresh = CreateClient();

        Assert.Equal(ConnectionState.Disconnected, fresh.State);
    }
}
