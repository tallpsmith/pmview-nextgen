using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Parses pmproxy /pmapi/indom JSON responses into InstanceDomain records.
/// Handles both instanced and singular metrics gracefully.
/// </summary>
internal static class PcpInstanceDomainFetcher
{
    public static InstanceDomain ParseIndomResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var indomId = root.GetProperty("indom").GetString()!;
        var instances = new List<Instance>();

        if (root.TryGetProperty("instances", out var instancesArray))
        {
            foreach (var inst in instancesArray.EnumerateArray())
            {
                var id = inst.GetProperty("instance").GetInt32();
                var name = inst.GetProperty("name").GetString()!;
                instances.Add(new Instance(id, name));
            }
        }

        return new InstanceDomain(indomId, instances);
    }

    public static Uri BuildIndomUrl(Uri baseUrl, string metricName)
    {
        return new Uri(baseUrl, $"/pmapi/indom?name={Uri.EscapeDataString(metricName)}");
    }

    public static bool IsNoIndomResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var msg))
            {
                var message = msg.GetString() ?? "";
                return message.Contains("no InDom", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("null indom", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}
