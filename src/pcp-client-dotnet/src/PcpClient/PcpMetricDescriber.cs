using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Parses pmproxy /pmapi/metric JSON responses into MetricDescriptor records.
/// </summary>
internal static class PcpMetricDescriber
{
    public static IReadOnlyList<MetricDescriptor> ParseDescribeResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var metricsArray = root.GetProperty("metrics");

        var results = new List<MetricDescriptor>();

        foreach (var metric in metricsArray.EnumerateArray())
        {
            var name = metric.GetProperty("name").GetString()!;
            var pmid = metric.GetProperty("pmid").GetString()!;
            var type = ParseMetricType(metric.GetProperty("type").GetString()!);
            var sem = ParseSemantics(metric.GetProperty("sem").GetString()!);
            var units = GetOptionalString(metric, "units");
            var indomId = GetOptionalString(metric, "indom");
            var oneLineHelp = metric.GetProperty("text-oneline").GetString() ?? "";
            var longHelp = GetOptionalString(metric, "text-help");

            // Treat empty strings as null for optional fields
            if (string.IsNullOrEmpty(units)) units = null;
            if (string.IsNullOrEmpty(indomId)) indomId = null;

            results.Add(new MetricDescriptor(
                name, pmid, type, sem, units, indomId, oneLineHelp, longHelp));
        }

        return results;
    }

    public static Uri BuildDescribeUrl(Uri baseUrl, IEnumerable<string> metricNames)
    {
        var names = string.Join(",", metricNames);
        return new Uri(baseUrl, $"/pmapi/metric?names={Uri.EscapeDataString(names)}");
    }

    private static MetricType ParseMetricType(string typeStr) =>
        typeStr.ToUpperInvariant() switch
        {
            "FLOAT" => MetricType.Float,
            "DOUBLE" => MetricType.Double,
            "U32" => MetricType.U32,
            "U64" => MetricType.U64,
            "32" => MetricType.I32,
            "64" => MetricType.I64,
            "STRING" => MetricType.String,
            _ => MetricType.Unknown
        };

    private static MetricSemantics ParseSemantics(string semStr) =>
        semStr.ToLowerInvariant() switch
        {
            "counter" => MetricSemantics.Counter,
            "instant" => MetricSemantics.Instant,
            "discrete" => MetricSemantics.Discrete,
            _ => MetricSemantics.Unknown
        };

    public static bool IsUnknownMetricResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var msg))
            {
                var message = msg.GetString() ?? "";
                return message.Contains("Unknown metric name", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    public static string? ExtractUnknownMetricName(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var msg))
            {
                var message = msg.GetString() ?? "";
                const string prefix = "Unknown metric name - ";
                var idx = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return message[(idx + prefix.Length)..].Trim();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
