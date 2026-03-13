using Xunit;

namespace PcpClient.Tests.Integration;

/// <summary>
/// Base class for integration tests requiring a live pmproxy instance.
/// Tests fail hard if pmproxy is unavailable — that's intentional.
/// To skip integration tests (e.g. in a VM without network access to
/// the dev-environment stack), run with:
///   dotnet test --filter "FullyQualifiedName!~Integration"
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected const string PmproxyEndpoint = "http://localhost:44322";
    protected static readonly Uri PmproxyUri = new(PmproxyEndpoint);

    protected PcpClientConnection Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = new PcpClientConnection(PmproxyUri);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
    }

    protected PcpClientConnection CreateClient() => new(PmproxyUri);
}
