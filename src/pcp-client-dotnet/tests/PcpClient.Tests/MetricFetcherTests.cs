using System.Net;
using System.Text.Json;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for metric fetching: FetchAsync parses pmproxy JSON response,
/// handles singular and instanced metrics, returns typed MetricValue list.
/// </summary>
public class MetricFetcherTests
{
    // Sample pmproxy /pmapi/fetch response — singular metric with JSON null instance (as pmproxy sends for hinv.ncpu etc.)
    private const string NullInstanceSingularResponse = """
        {"timestamp":1234567.89,"values":[{"pmid":"60.0.32","name":"hinv.ncpu","instances":[{"instance":null,"value":4}]}]}
        """;

    // Sample pmproxy /pmapi/fetch response — one metric present, second silently omitted
    private const string PartialFetchResponse = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "60.0.32",
              "name": "hinv.ncpu",
              "instances": [
                { "instance": null, "value": 4 }
              ]
            }
          ]
        }
        """;

    // Sample pmproxy /pmapi/fetch response for a singular metric
    private const string SingularMetricResponse = """
        {
          "timestamp": 1709654400.123456,
          "values": [
            {
              "pmid": "60.2.0",
              "name": "kernel.all.load",
              "instances": [
                { "instance": -1, "value": 2.45 }
              ]
            }
          ]
        }
        """;

    // Sample pmproxy /pmapi/fetch response for an instanced metric
    private const string InstancedMetricResponse = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "60.10.3",
              "name": "disk.dev.read",
              "instances": [
                { "instance": 0, "value": 1234 },
                { "instance": 1, "value": 5678 }
              ]
            }
          ]
        }
        """;

    // Multi-metric response
    private const string MultiMetricResponse = """
        {
          "timestamp": 1709654400.0,
          "values": [
            {
              "pmid": "60.2.0",
              "name": "kernel.all.load",
              "instances": [
                { "instance": -1, "value": 3.5 }
              ]
            },
            {
              "pmid": "60.10.3",
              "name": "disk.dev.read",
              "instances": [
                { "instance": 0, "value": 999 }
              ]
            }
          ]
        }
        """;

    // ── Singular Metrics ──

    [Fact]
    public async Task FetchAsync_SingularMetric_ReturnsSingleValue()
    {
        var client = await CreateConnectedClientAsync(SingularMetricResponse);

        var values = await client.FetchAsync(new[] { "kernel.all.load" });

        Assert.Single(values);
        Assert.Equal("kernel.all.load", values[0].Name);
        Assert.Equal("60.2.0", values[0].Pmid);
    }

    [Fact]
    public async Task FetchAsync_SingularMetric_ParsesTimestamp()
    {
        var client = await CreateConnectedClientAsync(SingularMetricResponse);

        var values = await client.FetchAsync(new[] { "kernel.all.load" });

        Assert.Equal(1709654400.123456, values[0].Timestamp, precision: 6);
    }

    [Fact]
    public async Task FetchAsync_SingularMetric_InstanceIdIsNull()
    {
        var client = await CreateConnectedClientAsync(SingularMetricResponse);

        var values = await client.FetchAsync(new[] { "kernel.all.load" });

        Assert.Single(values[0].InstanceValues);
        Assert.Null(values[0].InstanceValues[0].InstanceId);
    }

    [Fact]
    public async Task FetchAsync_SingularMetric_ParsesFloatValue()
    {
        var client = await CreateConnectedClientAsync(SingularMetricResponse);

        var values = await client.FetchAsync(new[] { "kernel.all.load" });

        var value = values[0].InstanceValues[0].Value;
        Assert.Equal(2.45, value, precision: 2);
    }

    // ── Instanced Metrics ──

    [Fact]
    public async Task FetchAsync_InstancedMetric_ReturnsMultipleInstances()
    {
        var client = await CreateConnectedClientAsync(InstancedMetricResponse);

        var values = await client.FetchAsync(new[] { "disk.dev.read" });

        Assert.Single(values);
        Assert.Equal(2, values[0].InstanceValues.Count);
    }

    [Fact]
    public async Task FetchAsync_InstancedMetric_InstanceIdsPreserved()
    {
        var client = await CreateConnectedClientAsync(InstancedMetricResponse);

        var values = await client.FetchAsync(new[] { "disk.dev.read" });

        Assert.Equal(0, values[0].InstanceValues[0].InstanceId);
        Assert.Equal(1, values[0].InstanceValues[1].InstanceId);
    }

    [Fact]
    public async Task FetchAsync_InstancedMetric_ValuesPreserved()
    {
        var client = await CreateConnectedClientAsync(InstancedMetricResponse);

        var values = await client.FetchAsync(new[] { "disk.dev.read" });

        Assert.Equal(1234.0, values[0].InstanceValues[0].Value);
        Assert.Equal(5678.0, values[0].InstanceValues[1].Value);
    }

    // ── Multiple Metrics ──

    [Fact]
    public async Task FetchAsync_MultipleMetrics_ReturnsAll()
    {
        var client = await CreateConnectedClientAsync(MultiMetricResponse);

        var values = await client.FetchAsync(new[] { "kernel.all.load", "disk.dev.read" });

        Assert.Equal(2, values.Count);
        Assert.Equal("kernel.all.load", values[0].Name);
        Assert.Equal("disk.dev.read", values[1].Name);
    }

    // ── Null Instance Field (Bug Fix) ──

    [Fact]
    public async Task FetchAsync_JsonNullInstance_InstanceIdIsNull()
    {
        // pmproxy returns "instance": null (JSON null) for singular metrics like hinv.ncpu
        // Before fix: GetInt32() on Null JsonElement threw InvalidOperationException
        var client = await CreateConnectedClientAsync(NullInstanceSingularResponse);

        var values = await client.FetchAsync(new[] { "hinv.ncpu" });

        Assert.Single(values[0].InstanceValues);
        Assert.Null(values[0].InstanceValues[0].InstanceId);
    }

    [Fact]
    public async Task FetchAsync_JsonNullInstance_ValueParsedCorrectly()
    {
        var client = await CreateConnectedClientAsync(NullInstanceSingularResponse);

        var values = await client.FetchAsync(new[] { "hinv.ncpu" });

        Assert.Equal(4.0, values[0].InstanceValues[0].Value);
    }

    // ── ThrowIfMetricsMissing (Bug Fix) ──

    [Fact]
    public async Task FetchAsync_MetricSilentlyOmittedByPmproxy_ThrowsPcpMetricNotFoundException()
    {
        // pmproxy can return HTTP 200 but silently omit a metric from the values array
        // ThrowIfMetricsMissing detects the gap and throws rather than returning partial data
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"context\":1}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    PartialFetchResponse,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);
        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<PcpMetricNotFoundException>(
            () => client.FetchAsync(new[] { "hinv.ncpu", "no.such.metric" }));

        Assert.Equal("no.such.metric", ex.MetricName);
    }

    // ── Error Cases ──

    [Fact]
    public async Task FetchAsync_WithoutConnect_ThrowsInvalidOperationException()
    {
        await using var client = new PcpClientConnection(new Uri("http://localhost:44322"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchAsync(new[] { "kernel.all.load" }));
    }

    [Fact]
    public async Task FetchAsync_EmptyMetricNames_ThrowsArgumentException()
    {
        var client = await CreateConnectedClientAsync(SingularMetricResponse);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.FetchAsync(Array.Empty<string>()));
    }

    // ── Helpers ──

    private static async Task<PcpClientConnection> CreateConnectedClientAsync(string fetchResponse)
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"context\":1}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    fetchResponse,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new PcpClientConnection(new Uri("http://localhost:44322"), httpClient);
        await client.ConnectAsync();
        return client;
    }
}
