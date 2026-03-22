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
            // Newer pmproxy versions return objects {"series": "id"} instead of bare strings
            var id = item.ValueKind == JsonValueKind.Object
                ? item.GetProperty("series").GetString()
                : item.GetString();
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

    /// <summary>
    /// Builds a /series/query URL with a hostname label filter.
    /// pmproxy requires full RFC 3986 percent-encoding of the filter expression —
    /// { } " = must all be encoded. Returns 400 Bad Request otherwise.
    /// </summary>
    public static Uri BuildHostnameFilteredQueryUrl(Uri baseUrl, string metricName, string hostname)
    {
        var filter = $"{metricName}{{hostname==\"{hostname}\"}}";
        return new Uri(baseUrl, $"/series/query?expr={Uri.EscapeDataString(filter)}");
    }

    /// <summary>
    /// Builds a /series/query URL with an OR-chained hostname label filter for
    /// multiple hosts. Used during fleet discovery to batch hostname queries.
    /// Empty hostnames array returns an unfiltered query.
    /// </summary>
    public static Uri BuildMultiHostFilteredQueryUrl(
        Uri baseUrl, string metricName, string[] hostnames)
    {
        if (hostnames.Length == 0)
            return BuildQueryUrl(baseUrl, metricName);

        var clauses = hostnames
            .Select(h => $"hostname==\"{h}\"");
        var filter = $"{metricName}{{{string.Join(" || ", clauses)}}}";
        return new Uri(baseUrl, $"/series/query?expr={Uri.EscapeDataString(filter)}");
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
    /// instance hash → instance info (numeric ID + human-readable name).
    /// Multiple instances can share the same series hash (e.g. kernel.all.load
    /// has 3 instances under one series), so we key by the unique instance hash.
    /// Falls back to series hash when no instance field is present (singular metrics).
    /// </summary>
    public static Dictionary<string, SeriesInstanceInfo> ParseInstancesResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, SeriesInstanceInfo>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString()!;
            var pcpInstanceId = item.GetProperty("id").GetInt32();
            var name = item.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? ""
                : "";

            // Key by instance hash (unique per instance) when available,
            // fall back to series hash for singular metrics
            var key = item.TryGetProperty("instance", out var instanceProp)
                ? instanceProp.GetString() ?? seriesId
                : seriesId;

            result[key] = new SeriesInstanceInfo(pcpInstanceId, name);
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

    public static IReadOnlyList<SeriesDescriptor> ParseDescsResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<SeriesDescriptor>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString()!;
            var pmid = item.TryGetProperty("pmid", out var p) ? p.GetString() : null;
            var indom = item.TryGetProperty("indom", out var i) ? i.GetString() : null;
            var semantics = item.TryGetProperty("semantics", out var s) ? s.GetString() : null;
            var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
            var units = item.TryGetProperty("units", out var u) ? u.GetString() : null;
            results.Add(new SeriesDescriptor(seriesId, pmid, indom, semantics, type, units));
        }
        return results;
    }

    public static Uri BuildDescsUrl(Uri baseUrl, IEnumerable<string> seriesIds)
    {
        var ids = string.Join(",", seriesIds);
        return new Uri(baseUrl, $"/series/descs?series={Uri.EscapeDataString(ids)}");
    }

    public static IReadOnlyList<SeriesMetricName> ParseMetricsResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<SeriesMetricName>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString()!;
            var name = item.GetProperty("name").GetString()!;
            results.Add(new SeriesMetricName(seriesId, name));
        }
        return results;
    }

    public static Uri BuildMetricsUrl(Uri baseUrl, IEnumerable<string> seriesIds)
    {
        var ids = string.Join(",", seriesIds);
        return new Uri(baseUrl, $"/series/metrics?series={Uri.EscapeDataString(ids)}");
    }

    public static IReadOnlyList<string> ParseLabelsResponse(string json, string labelName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(labelName, out var values))
            return [];

        var results = new List<string>();
        foreach (var item in values.EnumerateArray())
        {
            var val = item.GetString();
            if (val != null)
                results.Add(val);
        }
        return results;
    }

    public static Uri BuildLabelsUrl(Uri baseUrl, string labelName)
    {
        return new Uri(baseUrl, $"/series/labels?names={Uri.EscapeDataString(labelName)}");
    }

    /// <summary>
    /// Builds a /series/labels URL to look up labels for specific series IDs.
    /// DISTINCT from BuildLabelsUrl which queries by label name (?names=).
    /// This queries by series IDs (?series=) to get all labels for those series.
    /// </summary>
    public static Uri BuildPerSeriesLabelsUrl(
        Uri baseUrl, IEnumerable<string> seriesIds)
    {
        // pmproxy expects literal commas in the series parameter — do NOT
        // percent-encode them or you get 400 Bad Request.
        var ids = string.Join(",", seriesIds);
        return new Uri(baseUrl, $"/series/labels?series={ids}");
    }

    /// <summary>
    /// Parses a /series/labels?series=... response to extract hostname labels.
    /// Returns series_id → hostname mapping. Skips entries without a hostname label.
    /// DISTINCT from ParseLabelsResponse which returns a flat list of label values.
    ///
    /// Response format (from pmwebapi docs):
    /// [{"series": "abc123", "labels": {"hostname": "host-01", ...}}, ...]
    /// </summary>
    public static Dictionary<string, string> ParsePerSeriesHostnameLabels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var seriesId = item.GetProperty("series").GetString();
            if (seriesId == null)
                continue;

            // Labels are nested under a "labels" object
            if (item.TryGetProperty("labels", out var labelsProp)
                && labelsProp.TryGetProperty("hostname", out var hostnameProp))
            {
                var hostname = hostnameProp.GetString();
                if (hostname != null)
                    result[seriesId] = hostname;
            }
        }

        return result;
    }

    private static double ToEpochSeconds(DateTime utcTime)
    {
        return ((DateTimeOffset)utcTime).ToUnixTimeMilliseconds() / 1000.0;
    }
}

/// <summary>
/// Instance metadata from a /series/instances response:
/// the numeric PCP instance ID and its human-readable name.
/// </summary>
public record SeriesInstanceInfo(int PcpInstanceId, string Name);

/// <summary>
/// Maps a series ID to its PCP metric name from a /series/metrics response.
/// </summary>
public record SeriesMetricName(string SeriesId, string Name);

/// <summary>
/// Metric descriptor from a /series/descs response: PMID, instance domain,
/// semantics, value type, and units. All fields except SeriesId are optional
/// — pmproxy may omit them for metrics without instances or units.
/// </summary>
public record SeriesDescriptor(
    string SeriesId,
    string? Pmid,
    string? Indom,
    string? Semantics,
    string? Type,
    string? Units);

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
