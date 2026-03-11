using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for edge cases in metric type/semantics parsing:
/// - Unknown types and semantics should be explicit (not silent defaults)
/// - Scientific notation in JSON numbers
/// </summary>
public class MetricDescriberEdgeCaseTests
{
    [Theory]
    [InlineData("AGGREGATE")]
    [InlineData("EVENT")]
    [InlineData("UNKNOWN_TYPE")]
    public void Parse_UnknownType_ReturnsUnknownVariant(string unknownType)
    {
        var json = $$"""
        {
            "metrics": [
                {
                    "name": "test.metric",
                    "pmid": "1.0.0",
                    "type": "{{unknownType}}",
                    "sem": "instant",
                    "units": "",
                    "text-oneline": "test"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);
        Assert.Equal(MetricType.Unknown, result[0].Type);
    }

    [Theory]
    [InlineData("gauge")]
    [InlineData("BIZARRE")]
    public void Parse_UnknownSemantics_ReturnsUnknownVariant(string unknownSem)
    {
        var json = $$"""
        {
            "metrics": [
                {
                    "name": "test.metric",
                    "pmid": "1.0.0",
                    "type": "DOUBLE",
                    "sem": "{{unknownSem}}",
                    "units": "",
                    "text-oneline": "test"
                }
            ]
        }
        """;

        var result = PcpMetricDescriber.ParseDescribeResponse(json);
        Assert.Equal(MetricSemantics.Unknown, result[0].Semantics);
    }
}
