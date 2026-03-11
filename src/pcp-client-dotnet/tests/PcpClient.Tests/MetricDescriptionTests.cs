using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for PcpMetricDescriber: parses /pmapi/metric JSON responses
/// into MetricDescriptor records.
/// </summary>
public class MetricDescriptionTests
{
    // ── Happy Path: Counter metric ──

    [Fact]
    public void Parse_CounterMetric_ReturnsCorrectDescriptor()
    {
        var json = """
        {
            "metrics": [
                {
                    "name": "disk.dev.read",
                    "pmid": "60.0.4",
                    "type": "U64",
                    "sem": "counter",
                    "units": "count",
                    "indom": "60.1",
                    "text-oneline": "per-disk read operations",
                    "text-help": "Cumulative number of disk read operations since boot."
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);

        Assert.Single(result);
        var desc = result[0];
        Assert.Equal("disk.dev.read", desc.Name);
        Assert.Equal("60.0.4", desc.Pmid);
        Assert.Equal(MetricType.U64, desc.Type);
        Assert.Equal(MetricSemantics.Counter, desc.Semantics);
        Assert.Equal("count", desc.Units);
        Assert.Equal("60.1", desc.IndomId);
        Assert.Equal("per-disk read operations", desc.OneLineHelp);
        Assert.Equal("Cumulative number of disk read operations since boot.", desc.LongHelp);
    }

    // ── Instant metric ──

    [Fact]
    public void Parse_InstantMetric_ReturnsCorrectSemantics()
    {
        var json = """
        {
            "metrics": [
                {
                    "name": "kernel.all.load",
                    "pmid": "60.2.0",
                    "type": "FLOAT",
                    "sem": "instant",
                    "units": "",
                    "text-oneline": "1, 5 and 15 minute load average"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);

        Assert.Single(result);
        Assert.Equal(MetricSemantics.Instant, result[0].Semantics);
        Assert.Equal(MetricType.Float, result[0].Type);
        Assert.Null(result[0].IndomId); // no indom field
        Assert.Null(result[0].LongHelp); // no text-help field
    }

    // ── Discrete metric ──

    [Fact]
    public void Parse_DiscreteMetric_ReturnsCorrectSemantics()
    {
        var json = """
        {
            "metrics": [
                {
                    "name": "hinv.ncpu",
                    "pmid": "60.0.32",
                    "type": "U32",
                    "sem": "discrete",
                    "units": "",
                    "text-oneline": "number of CPUs"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);

        Assert.Equal(MetricSemantics.Discrete, result[0].Semantics);
        Assert.Equal(MetricType.U32, result[0].Type);
    }

    // ── Multiple metrics in one response ──

    [Fact]
    public void Parse_MultipleMetrics_ReturnsAll()
    {
        var json = """
        {
            "metrics": [
                {
                    "name": "disk.dev.read",
                    "pmid": "60.0.4",
                    "type": "U64",
                    "sem": "counter",
                    "units": "count",
                    "text-oneline": "reads"
                },
                {
                    "name": "disk.dev.write",
                    "pmid": "60.0.5",
                    "type": "U64",
                    "sem": "counter",
                    "units": "count",
                    "text-oneline": "writes"
                },
                {
                    "name": "kernel.all.load",
                    "pmid": "60.2.0",
                    "type": "FLOAT",
                    "sem": "instant",
                    "units": "",
                    "text-oneline": "load average"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(d => d.Semantics == MetricSemantics.Counter));
        Assert.Equal(1, result.Count(d => d.Semantics == MetricSemantics.Instant));
    }

    // ── All metric types parse correctly ──

    [Theory]
    [InlineData("FLOAT", MetricType.Float)]
    [InlineData("DOUBLE", MetricType.Double)]
    [InlineData("U32", MetricType.U32)]
    [InlineData("U64", MetricType.U64)]
    [InlineData("32", MetricType.I32)]
    [InlineData("64", MetricType.I64)]
    [InlineData("STRING", MetricType.String)]
    public void Parse_MetricType_MapsCorrectly(string pmproxyType, MetricType expected)
    {
        var json = $$"""
        {
            "metrics": [
                {
                    "name": "test.metric",
                    "pmid": "1.0.0",
                    "type": "{{pmproxyType}}",
                    "sem": "instant",
                    "units": "",
                    "text-oneline": "test"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);
        Assert.Equal(expected, result[0].Type);
    }

    // ── URL building ──

    [Fact]
    public void BuildDescribeUrl_FormatsCorrectly()
    {
        var baseUrl = new Uri("http://localhost:44322");
        var names = new[] { "disk.dev.read", "kernel.all.load" };

        var url = PcpMetricDescriber.BuildDescribeUrl(baseUrl, names);

        Assert.Contains("/pmapi/metric", url.ToString());
        Assert.Contains("names=disk.dev.read%2Ckernel.all.load", url.ToString());
    }
}
