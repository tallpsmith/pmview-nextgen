using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Parses pmproxy /pmapi/children JSON responses into MetricNamespace records.
/// Provides namespace tree traversal for metric discovery.
/// </summary>
internal static class PcpNamespaceTraverser
{
    public static MetricNamespace ParseChildrenResponse(string json, string prefix)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var leafNames = ParseStringArray(root, "leaf");
        var nonLeafNames = ParseStringArray(root, "nonleaf");

        return new MetricNamespace(prefix, leafNames, nonLeafNames);
    }

    public static Uri BuildChildrenUrl(Uri baseUrl, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return new Uri(baseUrl, "/pmapi/children");

        return new Uri(baseUrl, $"/pmapi/children?prefix={Uri.EscapeDataString(prefix)}");
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array))
            return [];

        var results = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.GetString();
            if (value != null)
                results.Add(value);
        }
        return results;
    }
}
