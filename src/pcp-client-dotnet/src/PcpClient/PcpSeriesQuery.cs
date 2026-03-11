using System.Text.Json;

namespace PcpClient;

/// <summary>
/// Parses pmproxy /series/query and /series/values responses
/// for historical time-series playback via Valkey backend.
/// </summary>
public static class PcpSeriesQuery
{
    public static IReadOnlyList<string> ParseQueryResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<string>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.GetString();
            if (id != null)
                results.Add(id);
        }

        return results;
    }

    public static IReadOnlyList<SeriesValue> ParseValuesResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<SeriesValue>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString()!;
            var timestamp = item.GetProperty("timestamp").GetDouble();
            var valueStr = item.GetProperty("value").GetString()!;

            string? instanceId = null;
            if (item.TryGetProperty("instance", out var instanceProp))
                instanceId = instanceProp.GetString();

            if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                results.Add(new SeriesValue(seriesId, timestamp, numericValue, instanceId));
            }
            else
            {
                results.Add(SeriesValue.FromString(seriesId, timestamp, valueStr, instanceId));
            }
        }

        return results;
    }

    public static Uri BuildQueryUrl(Uri baseUrl, string metricExpression, int? samples = null)
    {
        var expr = samples.HasValue
            ? $"{metricExpression}[samples:{samples.Value}]"
            : metricExpression;

        return new Uri(baseUrl, $"/series/query?expr={Uri.EscapeDataString(expr)}");
    }

    public static Uri BuildValuesUrl(Uri baseUrl, IEnumerable<string> seriesIds)
    {
        var ids = string.Join(",", seriesIds);
        return new Uri(baseUrl, $"/series/values?series={Uri.EscapeDataString(ids)}");
    }

    public static Uri BuildValuesUrlWithTimeWindow(Uri baseUrl, IEnumerable<string> seriesIds,
        DateTime position, double windowSeconds = 2.0)
    {
        var ids = string.Join(",", seriesIds);
        var startEpoch = ToEpochSeconds(position.AddSeconds(-windowSeconds));
        var finishEpoch = ToEpochSeconds(position);
        return new Uri(baseUrl,
            $"/series/values?series={Uri.EscapeDataString(ids)}" +
            $"&start={startEpoch:F3}" +
            $"&finish={finishEpoch:F3}");
    }

    /// <summary>
    /// Parses a /series/instances response into a mapping of
    /// series ID → PCP instance ID (the numeric instance identifier).
    /// </summary>
    public static Dictionary<string, int> ParseInstancesResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, int>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString()!;
            var pcpInstanceId = item.GetProperty("id").GetInt32();
            result[seriesId] = pcpInstanceId;
        }

        return result;
    }

    public static Uri BuildInstancesUrl(Uri baseUrl, IEnumerable<string> seriesIds)
    {
        var ids = string.Join(",", seriesIds);
        return new Uri(baseUrl, $"/series/instances?series={Uri.EscapeDataString(ids)}");
    }

    /// <summary>
    /// Computes per-second rates from consecutive archive samples.
    /// Groups values by series ID, sorts by timestamp, and uses the
    /// latest two points. Handles counter wraps by skipping the series.
    /// Returns one SeriesValue per series with the computed rate.
    /// </summary>
    public static IReadOnlyList<SeriesValue> ComputeRatesFromSeriesValues(
        IReadOnlyList<SeriesValue> values)
    {
        var results = new List<SeriesValue>();

        var bySeries = values
            .Where(v => !v.IsString)
            .GroupBy(v => v.SeriesId);

        foreach (var group in bySeries)
        {
            // Deduplicate by timestamp — pmproxy can return multiple entries
            // per timestamp (one per instance within the series)
            var sorted = group
                .GroupBy(v => v.Timestamp)
                .Select(g => g.First())
                .OrderBy(v => v.Timestamp)
                .ToList();
            if (sorted.Count < 2)
                continue;

            var prev = sorted[^2];
            var curr = sorted[^1];

            var timeDeltaMs = curr.Timestamp - prev.Timestamp;
            if (timeDeltaMs <= 0)
                continue;

            if (curr.NumericValue < prev.NumericValue)
                continue;  // counter wrap

            // Timestamps from pmproxy are epoch milliseconds — convert to seconds
            var timeDeltaSeconds = timeDeltaMs / 1000.0;
            var rate = (curr.NumericValue - prev.NumericValue) / timeDeltaSeconds;
            results.Add(new SeriesValue(curr.SeriesId, curr.Timestamp, rate,
                curr.InstanceId));
        }

        return results;
    }

    private static double ToEpochSeconds(DateTime utcTime)
    {
        return ((DateTimeOffset)utcTime).ToUnixTimeMilliseconds() / 1000.0;
    }
}

/// <summary>
/// A single timestamped value from a historical series query.
/// </summary>
public class SeriesValue
{
    public string SeriesId { get; }
    public double Timestamp { get; }
    public double NumericValue { get; }
    public string? StringValue { get; }
    public string? InstanceId { get; }
    public bool IsString { get; }

    public SeriesValue(string seriesId, double timestamp, double numericValue,
        string? instanceId = null)
    {
        SeriesId = seriesId;
        Timestamp = timestamp;
        NumericValue = numericValue;
        InstanceId = instanceId;
        IsString = false;
    }

    private SeriesValue(string seriesId, double timestamp, string stringValue,
        string? instanceId)
    {
        SeriesId = seriesId;
        Timestamp = timestamp;
        StringValue = stringValue;
        InstanceId = instanceId;
        NumericValue = double.NaN;
        IsString = true;
    }

    public static SeriesValue FromString(string seriesId, double timestamp,
        string stringValue, string? instanceId = null)
    {
        return new SeriesValue(seriesId, timestamp, stringValue, instanceId);
    }
}
