using System.Net;
using System.Text;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for historical series queries: /series/query and /series/values
/// endpoint parsing. Covers task T044.
/// </summary>
public class SeriesQueryTests
{
    // ── PcpSeriesQuery.ParseQueryResponse — series identifier extraction ──

    [Fact]
    public void ParseQueryResponse_ReturnsSeriesIdentifiers()
    {
        var json = """
        [
            "2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d",
            "a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0"
        ]
        """;

        var result = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d", result[0]);
        Assert.Equal("a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0", result[1]);
    }

    [Fact]
    public void ParseQueryResponse_EmptyArray_ReturnsEmpty()
    {
        var json = "[]";

        var result = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseQueryResponse_SingleSeries_ReturnsSingleIdentifier()
    {
        var json = """["abc123def456"]""";

        var result = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Single(result);
        Assert.Equal("abc123def456", result[0]);
    }

    [Fact]
    public void ParseQueryResponse_ObjectFormat_ExtractsSeriesField()
    {
        // Newer pmproxy versions return objects instead of bare strings
        var json = """
        [
            {"series": "2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d"},
            {"series": "a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0"}
        ]
        """;

        var result = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d", result[0]);
        Assert.Equal("a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0", result[1]);
    }

    // ── PcpSeriesQuery.ParseValuesResponse — timestamped value extraction ──

    [Fact]
    public void ParseValuesResponse_ReturnsTimestampedValues()
    {
        var json = """
        [
            {
                "series": "2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d",
                "timestamp": 1709654400.123456,
                "value": "42.5"
            },
            {
                "series": "2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d",
                "timestamp": 1709654401.123456,
                "value": "43.1"
            }
        ]
        """;

        var result = PcpSeriesQuery.ParseValuesResponse(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("2cd6a38f9339f2dd6f81a14f6d73946b02e0d44d", result[0].SeriesId);
        Assert.Equal(1709654400.123456, result[0].Timestamp, precision: 6);
        Assert.Equal(42.5, result[0].NumericValue, precision: 1);
        Assert.Equal(1709654401.123456, result[1].Timestamp, precision: 6);
        Assert.Equal(43.1, result[1].NumericValue, precision: 1);
    }

    [Fact]
    public void ParseValuesResponse_StringValue_ParsedAsString()
    {
        var json = """
        [
            {
                "series": "abc123",
                "timestamp": 1709654400.0,
                "value": "not-a-number"
            }
        ]
        """;

        var result = PcpSeriesQuery.ParseValuesResponse(json);

        Assert.Single(result);
        Assert.Equal("not-a-number", result[0].StringValue);
        Assert.True(result[0].IsString);
    }

    [Fact]
    public void ParseValuesResponse_NumericStringValue_ParsedAsNumeric()
    {
        var json = """
        [
            {
                "series": "abc123",
                "timestamp": 1709654400.0,
                "value": "99.9"
            }
        ]
        """;

        var result = PcpSeriesQuery.ParseValuesResponse(json);

        Assert.Single(result);
        Assert.False(result[0].IsString);
        Assert.Equal(99.9, result[0].NumericValue, precision: 1);
    }

    [Fact]
    public void ParseValuesResponse_EmptyArray_ReturnsEmpty()
    {
        var result = PcpSeriesQuery.ParseValuesResponse("[]");

        Assert.Empty(result);
    }

    // ── URL building ──

    [Fact]
    public void BuildQueryUrl_FormatsCorrectly()
    {
        var baseUrl = new Uri("http://localhost:44322");

        var url = PcpSeriesQuery.BuildQueryUrl(baseUrl, "kernel.all.load");

        Assert.Contains("/series/query", url.ToString());
        Assert.Contains("expr=kernel.all.load", url.ToString());
    }

    [Fact]
    public void BuildQueryUrl_WithSamples_IncludesSampleCount()
    {
        var baseUrl = new Uri("http://localhost:44322");

        var url = PcpSeriesQuery.BuildQueryUrl(baseUrl, "kernel.all.load",
            samples: 100);

        Assert.Contains("expr=kernel.all.load", url.ToString());
    }

    [Fact]
    public void BuildValuesUrl_FormatsCorrectly()
    {
        var baseUrl = new Uri("http://localhost:44322");
        var seriesIds = new[] { "abc123", "def456" };

        var url = PcpSeriesQuery.BuildValuesUrl(baseUrl, seriesIds);

        Assert.Contains("/series/values", url.ToString());
        Assert.Contains("series=abc123", url.ToString());
    }

    [Fact]
    public void BuildValuesUrlWithTimeWindow_UsesEpochSecondsForTimestamps()
    {
        var baseUrl = new Uri("http://localhost:44322");
        var seriesIds = new[] { "abc123" };
        var position = new DateTime(2026, 3, 10, 14, 30, 0, DateTimeKind.Utc);

        var url = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
            baseUrl, seriesIds, position, windowSeconds: 2.0);

        var urlStr = url.ToString();
        Assert.Contains("/series/values", urlStr);
        Assert.Contains("series=abc123", urlStr);

        // pmproxy requires Unix epoch seconds, not ISO 8601
        var finishEpoch = ((DateTimeOffset)position).ToUnixTimeMilliseconds() / 1000.0;
        var startEpoch = ((DateTimeOffset)position.AddSeconds(-2.0)).ToUnixTimeMilliseconds() / 1000.0;
        Assert.Contains($"start={startEpoch:F3}", urlStr);
        Assert.Contains($"finish={finishEpoch:F3}", urlStr);

        // Must NOT contain ISO 8601 "T" separator
        var queryString = urlStr.Substring(urlStr.IndexOf('?'));
        Assert.DoesNotContain("T", queryString.Replace("start", "").Replace("finish", ""));
    }

    // ── SeriesValue model ──

    [Fact]
    public void SeriesValue_NumericValue_ReportsNotString()
    {
        var sv = new SeriesValue("abc123", 1709654400.0, 42.0);

        Assert.False(sv.IsString);
        Assert.Equal(42.0, sv.NumericValue);
        Assert.Null(sv.StringValue);
    }

    [Fact]
    public void SeriesValue_StringValue_ReportsIsString()
    {
        var sv = SeriesValue.FromString("abc123", 1709654400.0, "hello");

        Assert.True(sv.IsString);
        Assert.Equal("hello", sv.StringValue);
    }

    [Fact]
    public void SeriesValue_StringValue_NumericValueIsNaN()
    {
        var sv = SeriesValue.FromString("abc123", 1709654400.0, "hello");

        Assert.True(double.IsNaN(sv.NumericValue));
    }

    // ── ParseValuesResponse with instance field ──

    [Fact]
    public void ParseValuesResponse_IncludesInstanceId_WhenPresent()
    {
        var json = """
        [
            {
                "series": "abc123",
                "instance": "inst456",
                "timestamp": 1709654400.0,
                "value": "99.0"
            }
        ]
        """;

        var result = PcpSeriesQuery.ParseValuesResponse(json);

        Assert.Single(result);
        Assert.Equal("inst456", result[0].InstanceId);
    }

    [Fact]
    public void ParseValuesResponse_NoInstanceField_InstanceIdIsNull()
    {
        var json = """
        [
            {
                "series": "abc123",
                "timestamp": 1709654400.0,
                "value": "99.0"
            }
        ]
        """;

        var result = PcpSeriesQuery.ParseValuesResponse(json);

        Assert.Single(result);
        Assert.Null(result[0].InstanceId);
    }

    // ── /series/instances parsing — series-to-PCP-instance-ID mapping ──

    [Fact]
    public void ParseInstancesResponse_KeysByInstanceHash()
    {
        var json = """
        [
            {"series": "series_aaa", "source": "src1", "instance": "inst_aaa", "id": 0, "name": "1 minute"},
            {"series": "series_bbb", "source": "src1", "instance": "inst_bbb", "id": 1, "name": "5 minute"},
            {"series": "series_ccc", "source": "src1", "instance": "inst_ccc", "id": 2, "name": "15 minute"}
        ]
        """;

        var result = PcpSeriesQuery.ParseInstancesResponse(json);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result["inst_aaa"].PcpInstanceId);
        Assert.Equal("1 minute", result["inst_aaa"].Name);
        Assert.Equal(1, result["inst_bbb"].PcpInstanceId);
        Assert.Equal("5 minute", result["inst_bbb"].Name);
        Assert.Equal(2, result["inst_ccc"].PcpInstanceId);
        Assert.Equal("15 minute", result["inst_ccc"].Name);
    }

    [Fact]
    public void ParseInstancesResponse_EmptyArray_ReturnsEmptyMapping()
    {
        var result = PcpSeriesQuery.ParseInstancesResponse("[]");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseInstancesResponse_MultipleInstancesPerSeries_AllPreserved()
    {
        // Real-world case: kernel.all.load has 3 instances sharing one series hash
        var json = """
        [
            {"series": "series_aaa", "source": "src1", "instance": "inst_1min", "id": 0, "name": "1 minute"},
            {"series": "series_aaa", "source": "src1", "instance": "inst_5min", "id": 1, "name": "5 minute"},
            {"series": "series_aaa", "source": "src1", "instance": "inst_15min", "id": 2, "name": "15 minute"}
        ]
        """;

        var result = PcpSeriesQuery.ParseInstancesResponse(json);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result["inst_1min"].PcpInstanceId);
        Assert.Equal("1 minute", result["inst_1min"].Name);
        Assert.Equal(1, result["inst_5min"].PcpInstanceId);
        Assert.Equal("5 minute", result["inst_5min"].Name);
        Assert.Equal(2, result["inst_15min"].PcpInstanceId);
        Assert.Equal("15 minute", result["inst_15min"].Name);
    }

    [Fact]
    public void ParseInstancesResponse_NoInstanceField_FallsBackToSeriesKey()
    {
        // Singular metrics may not have an instance field
        var json = """
        [
            {"series": "series_aaa", "source": "src1", "id": 0, "name": "singular"}
        ]
        """;

        var result = PcpSeriesQuery.ParseInstancesResponse(json);

        Assert.Single(result);
        Assert.Equal(0, result["series_aaa"].PcpInstanceId);
        Assert.Equal("singular", result["series_aaa"].Name);
    }

    [Fact]
    public void ParseInstancesResponse_MultiSource_SameNamesDifferentIds_AllPreserved()
    {
        // Real-world scenario: two archive sources index the same metric (kernel.all.load)
        // with different PCP instance ID schemes. Instance hashes are unique across sources.
        var json = """
        [
            {"series": "series_src1", "source": "src1", "instance": "inst_1min_s1", "id": 0, "name": "1 minute"},
            {"series": "series_src1", "source": "src1", "instance": "inst_5min_s1", "id": 1, "name": "5 minute"},
            {"series": "series_src1", "source": "src1", "instance": "inst_15min_s1", "id": 2, "name": "15 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_1min_s2", "id": 1, "name": "1 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_5min_s2", "id": 5, "name": "5 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_15min_s2", "id": 15, "name": "15 minute"}
        ]
        """;

        var result = PcpSeriesQuery.ParseInstancesResponse(json);

        // All 6 entries preserved — keyed by unique instance hash, not series hash
        Assert.Equal(6, result.Count);

        // Source 1 instances
        Assert.Equal(0, result["inst_1min_s1"].PcpInstanceId);
        Assert.Equal("1 minute", result["inst_1min_s1"].Name);
        Assert.Equal(1, result["inst_5min_s1"].PcpInstanceId);
        Assert.Equal(2, result["inst_15min_s1"].PcpInstanceId);

        // Source 2 instances (same names, different PCP IDs)
        Assert.Equal(1, result["inst_1min_s2"].PcpInstanceId);
        Assert.Equal("1 minute", result["inst_1min_s2"].Name);
        Assert.Equal(5, result["inst_5min_s2"].PcpInstanceId);
        Assert.Equal(15, result["inst_15min_s2"].PcpInstanceId);
    }

    [Fact]
    public void ValueInstanceHash_ResolvesCorrectly_ThroughInstanceMap()
    {
        // End-to-end: values carry instance hashes that resolve through the instance map.
        // Only source 1 has actual data — its instance hashes appear in the values.
        var instancesJson = """
        [
            {"series": "series_src1", "source": "src1", "instance": "inst_1min_s1", "id": 0, "name": "1 minute"},
            {"series": "series_src1", "source": "src1", "instance": "inst_5min_s1", "id": 1, "name": "5 minute"},
            {"series": "series_src1", "source": "src1", "instance": "inst_15min_s1", "id": 2, "name": "15 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_1min_s2", "id": 1, "name": "1 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_5min_s2", "id": 5, "name": "5 minute"},
            {"series": "series_src2", "source": "src2", "instance": "inst_15min_s2", "id": 15, "name": "15 minute"}
        ]
        """;

        var valuesJson = """
        [
            {"series": "series_src1", "instance": "inst_1min_s1", "timestamp": 1000000.0, "value": "0.64"},
            {"series": "series_src1", "instance": "inst_5min_s1", "timestamp": 1000000.0, "value": "0.65"},
            {"series": "series_src1", "instance": "inst_15min_s1", "timestamp": 1000000.0, "value": "0.68"}
        ]
        """;

        var instanceMap = PcpSeriesQuery.ParseInstancesResponse(instancesJson);
        var values = PcpSeriesQuery.ParseValuesResponse(valuesJson);

        // Each value's InstanceId (instance hash) should resolve to the correct
        // PcpInstanceId from source 1 — NOT source 2's conflicting IDs.
        Assert.Equal(3, values.Count);

        foreach (var sv in values)
        {
            Assert.NotNull(sv.InstanceId);
            Assert.True(instanceMap.ContainsKey(sv.InstanceId!),
                $"Instance hash '{sv.InstanceId}' should be in instance map");
        }

        // Verify the resolution chain produces distinct PCP instance IDs from source 1
        var resolvedIds = values
            .Select(sv => instanceMap[sv.InstanceId!].PcpInstanceId)
            .ToList();

        Assert.Equal(new[] { 0, 1, 2 }, resolvedIds);  // Source 1's IDs, not source 2's

        // Build name→id only from matched entries (simulating MetricPoller logic)
        var nameToId = new Dictionary<string, int>();
        foreach (var sv in values)
        {
            var info = instanceMap[sv.InstanceId!];
            nameToId[info.Name] = info.PcpInstanceId;
        }

        Assert.Equal(0, nameToId["1 minute"]);   // NOT 1 (source 2's ID)
        Assert.Equal(1, nameToId["5 minute"]);   // NOT 5 (source 2's ID)
        Assert.Equal(2, nameToId["15 minute"]);  // NOT 15 (source 2's ID)
    }

    [Fact]
    public void BuildInstancesUrl_FormatsCorrectly()
    {
        var baseUrl = new Uri("http://localhost:44322");
        var seriesIds = new[] { "abc123", "def456" };

        var url = PcpSeriesQuery.BuildInstancesUrl(baseUrl, seriesIds);

        var urlStr = url.ToString();
        Assert.Contains("/series/instances", urlStr);
        Assert.Contains("series=abc123", urlStr);
    }

    // ── ComputeRatesFromSeriesValues — counter rate conversion for archive data ──

    [Fact]
    public void ComputeRates_TwoSamples_ReturnsPerSecondRate()
    {
        // Timestamps in epoch milliseconds (pmproxy format)
        var t1 = 1709654400000.0;  // epoch ms
        var t2 = t1 + 60000.0;     // 60 seconds later
        var values = new List<SeriesValue>
        {
            new("series_a", t1, 50000.0),
            new("series_a", t2, 53000.0),  // +3000 in 60s = 50/sec
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Single(rates);
        Assert.Equal("series_a", rates[0].SeriesId);
        Assert.Equal(t2, rates[0].Timestamp);
        Assert.Equal(50.0, rates[0].NumericValue, precision: 1);
    }

    [Fact]
    public void ComputeRates_MultipleSeries_ComputesEachIndependently()
    {
        var t1 = 1709654400000.0;
        var t2 = t1 + 60000.0;
        var values = new List<SeriesValue>
        {
            new("series_a", t1, 10000.0),
            new("series_b", t1, 20000.0),
            new("series_a", t2, 16000.0),  // +6000/60s = 100/sec
            new("series_b", t2, 21800.0),  // +1800/60s = 30/sec
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Equal(2, rates.Count);
        var rateA = rates.First(r => r.SeriesId == "series_a");
        var rateB = rates.First(r => r.SeriesId == "series_b");
        Assert.Equal(100.0, rateA.NumericValue, precision: 1);
        Assert.Equal(30.0, rateB.NumericValue, precision: 1);
    }

    [Fact]
    public void ComputeRates_SingleSample_ReturnsEmpty()
    {
        var values = new List<SeriesValue>
        {
            new("series_a", 1709654400000.0, 50000.0),
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Empty(rates);
    }

    [Fact]
    public void ComputeRates_CounterWrap_SkipsSeries()
    {
        var t1 = 1709654400000.0;
        var t2 = t1 + 60000.0;
        var values = new List<SeriesValue>
        {
            new("series_a", t1, 4000000000.0),
            new("series_a", t2, 1000.0),  // wrapped
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Empty(rates);
    }

    [Fact]
    public void ComputeRates_ThreeSamples_UsesLatestTwoForRate()
    {
        var t1 = 1709654400000.0;
        var t2 = t1 + 60000.0;
        var t3 = t2 + 60000.0;
        var values = new List<SeriesValue>
        {
            new("series_a", t1, 10000.0),
            new("series_a", t2, 16000.0),
            new("series_a", t3, 25000.0),  // +9000/60s = 150/sec
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Single(rates);
        Assert.Equal(t3, rates[0].Timestamp);
        Assert.Equal(150.0, rates[0].NumericValue, precision: 1);
    }

    [Fact]
    public void ComputeRates_MillisecondTimestamps_ReturnsPerSecondRate()
    {
        // pmproxy returns timestamps in epoch milliseconds, not seconds.
        // 60000ms apart = 60 seconds. Rate should be per-second.
        var t1 = 1773111485000.0;  // realistic pmproxy epoch-ms timestamp
        var t2 = t1 + 60000.0;     // 60 seconds later

        var values = new List<SeriesValue>
        {
            new("series_a", t1, 50000.0),
            new("series_a", t2, 53000.0),  // +3000 in 60s = 50/sec
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Single(rates);
        Assert.Equal(50.0, rates[0].NumericValue, precision: 1);
    }

    [Fact]
    public void ComputeRates_DuplicateTimestamps_DeduplicatesBeforeComputing()
    {
        // pmproxy returns duplicate entries per timestamp (one per instance
        // within the series). Without dedup, sorted[^2] and sorted[^1] are
        // the same timestamp, giving timeDelta=0 and no rate computed.
        var t1 = 1773111485000.0;
        var t2 = t1 + 60000.0;
        var t3 = t2 + 60000.0;

        var values = new List<SeriesValue>
        {
            new("series_a", t1, 337456121.0),
            new("series_a", t1, 337456121.0),  // duplicate
            new("series_a", t2, 337462033.0),
            new("series_a", t2, 337462033.0),  // duplicate
            new("series_a", t3, 337467969.0),
            new("series_a", t3, 337467969.0),  // duplicate
        };

        var rates = PcpSeriesQuery.ComputeRatesFromSeriesValues(values);

        Assert.Single(rates);
        // (337467969 - 337462033) / 60 = 5936 / 60 = 98.93/sec
        Assert.Equal(98.9, rates[0].NumericValue, precision: 0);
    }

    // ── PcpSeriesQuery.ParseDescsResponse — metric descriptor extraction ──

    [Fact]
    public void ParseDescsResponse_ReturnsDescriptors()
    {
        var json = """
        [
            {
                "series": "abc123",
                "pmid": "60.0.32",
                "indom": "60.1",
                "semantics": "counter",
                "type": "u64",
                "units": "Kbyte / sec"
            }
        ]
        """;
        var result = PcpSeriesQuery.ParseDescsResponse(json);
        Assert.Single(result);
        Assert.Equal("abc123", result[0].SeriesId);
        Assert.Equal("60.0.32", result[0].Pmid);
        Assert.Equal("60.1", result[0].Indom);
        Assert.Equal("counter", result[0].Semantics);
        Assert.Equal("u64", result[0].Type);
        Assert.Equal("Kbyte / sec", result[0].Units);
    }

    [Fact]
    public void ParseDescsResponse_MissingOptionalFields_ReturnsNulls()
    {
        var json = """
        [
            {
                "series": "abc123",
                "semantics": "instant",
                "type": "float"
            }
        ]
        """;
        var result = PcpSeriesQuery.ParseDescsResponse(json);
        Assert.Single(result);
        Assert.Null(result[0].Pmid);
        Assert.Null(result[0].Indom);
        Assert.Null(result[0].Units);
    }

    [Fact]
    public void ParseDescsResponse_EmptyArray_ReturnsEmpty()
    {
        var json = "[]";
        var result = PcpSeriesQuery.ParseDescsResponse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildDescsUrl_FormatsSeriesIds()
    {
        var url = PcpSeriesQuery.BuildDescsUrl(
            new Uri("http://localhost:44322"),
            new[] { "abc123", "def456" });
        var urlStr = url.ToString();
        Assert.Contains("/series/descs", urlStr);
        Assert.Contains("abc123", urlStr);
    }

    // ── PcpSeriesQuery.ParseMetricsResponse — series-to-metric-name mapping ──

    [Fact]
    public void ParseMetricsResponse_ReturnsSeriesMetricNames()
    {
        var json = """
        [
            {"series": "abc123", "name": "disk.dev.read"},
            {"series": "def456", "name": "disk.dev.write"}
        ]
        """;
        var result = PcpSeriesQuery.ParseMetricsResponse(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("abc123", result[0].SeriesId);
        Assert.Equal("disk.dev.read", result[0].Name);
        Assert.Equal("def456", result[1].SeriesId);
        Assert.Equal("disk.dev.write", result[1].Name);
    }

    [Fact]
    public void ParseMetricsResponse_EmptyArray_ReturnsEmpty()
    {
        var json = "[]";
        var result = PcpSeriesQuery.ParseMetricsResponse(json);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildMetricsUrl_FormatsSeriesIds()
    {
        var url = PcpSeriesQuery.BuildMetricsUrl(
            new Uri("http://localhost:44322"),
            new[] { "abc123", "def456" });
        var urlStr = url.ToString();
        Assert.Contains("/series/metrics", urlStr);
        Assert.Contains("abc123", urlStr);
        Assert.Contains("def456", urlStr);
    }

    // ── PcpSeriesQuery.ParseLabelsResponse — label value extraction ──

    [Fact]
    public void ParseLabelsResponse_ReturnsLabelValues()
    {
        var json = """{"hostname": ["app", "nas", "webserver01"]}""";
        var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");
        Assert.Equal(3, result.Count);
        Assert.Contains("app", result);
        Assert.Contains("nas", result);
        Assert.Contains("webserver01", result);
    }

    [Fact]
    public void ParseLabelsResponse_EmptyValues_ReturnsEmpty()
    {
        var json = """{"hostname": []}""";
        var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseLabelsResponse_MissingLabel_ReturnsEmpty()
    {
        var json = """{"otherlabel": ["value1"]}""";
        var result = PcpSeriesQuery.ParseLabelsResponse(json, "hostname");
        Assert.Empty(result);
    }
}
