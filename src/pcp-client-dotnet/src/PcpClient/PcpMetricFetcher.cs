using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Parses pmproxy /pmapi/fetch JSON responses into typed MetricValue objects.
/// </summary>
internal static class PcpMetricFetcher
{
    public static IReadOnlyList<MetricValue> ParseFetchResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var timestamp = root.GetProperty("timestamp").GetDouble();
        var valuesArray = root.GetProperty("values");

        var results = new List<MetricValue>();

        foreach (var metricElement in valuesArray.EnumerateArray())
        {
            var name = metricElement.GetProperty("name").GetString()!;
            var pmid = metricElement.GetProperty("pmid").GetString()!;
            var instances = ParseInstances(metricElement.GetProperty("instances"));

            results.Add(new MetricValue(name, pmid, timestamp, instances));
        }

        return results;
    }

    private static IReadOnlyList<InstanceValue> ParseInstances(JsonElement instancesArray)
    {
        var values = new List<InstanceValue>();

        foreach (var inst in instancesArray.EnumerateArray())
        {
            var instanceId = inst.GetProperty("instance").GetInt32();
            // pmproxy uses -1 for singular metrics (no instance domain)
            int? parsedId = instanceId == -1 ? null : instanceId;

            var valueElement = inst.GetProperty("value");
            if (valueElement.ValueKind == JsonValueKind.String)
            {
                values.Add(new InstanceValue(parsedId, valueElement.GetString()!));
            }
            else
            {
                // All numeric JSON values (integer, float, scientific notation) → double
                values.Add(new InstanceValue(parsedId, valueElement.GetDouble()));
            }
        }

        return values;
    }

    public static bool IsContextExpiredResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var msg))
            {
                var message = msg.GetString() ?? "";
                return message.Contains("unknown context identifier", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // Not JSON, not a context expired response
        }

        return false;
    }
}
