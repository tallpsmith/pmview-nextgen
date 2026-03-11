using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for edge cases in metric value parsing:
/// - Scientific notation JSON numbers
/// - All numeric values should be double (not boxed object)
/// </summary>
public class MetricValueParsingEdgeCaseTests
{
    [Fact]
    public void Parse_ScientificNotation_ReturnsDouble()
    {
        var json = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "1.0.0",
              "name": "test.metric",
              "instances": [
                { "instance": -1, "value": 1e5 }
              ]
            }
          ]
        }
        """;

        var result = PcpMetricFetcher.ParseFetchResponse(json);
        Assert.Equal(100000.0, result[0].InstanceValues[0].Value);
    }

    [Fact]
    public void Parse_IntegerValue_ReturnsDouble()
    {
        var json = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "1.0.0",
              "name": "test.metric",
              "instances": [
                { "instance": -1, "value": 42 }
              ]
            }
          ]
        }
        """;

        var result = PcpMetricFetcher.ParseFetchResponse(json);
        Assert.Equal(42.0, result[0].InstanceValues[0].Value);
    }

    [Fact]
    public void Parse_StringValue_ReturnsStringValue()
    {
        var json = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "1.0.0",
              "name": "test.metric",
              "instances": [
                { "instance": -1, "value": "hello" }
              ]
            }
          ]
        }
        """;

        var result = PcpMetricFetcher.ParseFetchResponse(json);
        Assert.Equal("hello", result[0].InstanceValues[0].StringValue);
        Assert.True(result[0].InstanceValues[0].IsString);
    }

    [Fact]
    public void InstanceValue_NumericValue_IsNotString()
    {
        var iv = new InstanceValue(null, 42.0);
        Assert.False(iv.IsString);
        Assert.Null(iv.StringValue);
    }

    [Fact]
    public void InstanceValue_AsDouble_ReturnsValue()
    {
        var iv = new InstanceValue(null, 3.14);
        Assert.Equal(3.14, iv.AsDouble());
    }
}
