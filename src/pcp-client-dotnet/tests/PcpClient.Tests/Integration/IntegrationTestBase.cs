using Xunit;

namespace PcpClient.Tests.Integration;

/// <summary>
/// Base class for integration tests that require a live pmproxy instance.
/// Skips gracefully when pmproxy is unavailable (e.g. CI without dev stack).
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected const string PmproxyEndpoint = "http://localhost:44322";
    protected static readonly Uri PmproxyUri = new(PmproxyEndpoint);

    private static readonly Lazy<bool> PmproxyAvailable = new(() =>
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = probe.GetAsync($"{PmproxyEndpoint}/pmapi/context")
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    });

    protected PcpClientConnection Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        SkipIfPmproxyUnavailable();
        Client = new PcpClientConnection(PmproxyUri);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
    }

    protected static void SkipIfPmproxyUnavailable()
    {
        Skip.If(!PmproxyAvailable.Value,
            $"pmproxy not available at {PmproxyEndpoint} — start dev-environment stack first");
    }

    protected PcpClientConnection CreateClient() => new(PmproxyUri);
}
