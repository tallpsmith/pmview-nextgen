using Godot;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Spike 1: Prove we can fetch a real PCP metric from pmproxy via C# in Godot.
/// THROWAWAY CODE — do not promote to production.
/// </summary>
public partial class ConnectivitySpike : Node
{
    private static readonly HttpClient Http = new();
    private const string PmproxyUrl = "http://localhost:44322";

    public override async void _Ready()
    {
        GD.Print("[Spike-01] Starting connectivity proof...");
        try
        {
            var contextId = await CreateContext();
            GD.Print($"[Spike-01] Got context: {contextId}");

            var fetchResult = await FetchMetric("kernel.all.load", contextId);
            GD.Print($"[Spike-01] Fetch result: {fetchResult}");

            GD.Print("[Spike-01] SUCCESS — connectivity proof complete!");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Spike-01] FAILED: {ex.Message}");
        }
    }

    private async Task<int> CreateContext()
    {
        var response = await Http.GetStringAsync($"{PmproxyUrl}/pmapi/context?polltimeout=60");
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.GetProperty("context").GetInt32();
    }

    private async Task<string> FetchMetric(string metricName, int contextId)
    {
        var response = await Http.GetStringAsync(
            $"{PmproxyUrl}/pmapi/fetch?names={metricName}&context={contextId}");
        return response;
    }
}
