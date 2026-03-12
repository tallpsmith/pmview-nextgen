using System.Net;
using System.Text;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for metric namespace traversal (GetChildrenAsync),
/// metric description error handling (DescribeMetricsAsync with unknown metrics),
/// and instance domain enumeration (GetInstanceDomainAsync).
/// Covers tasks T036, T037, T038.
/// </summary>
public class MetricDiscoveryTests
{
    private const string ContextResponse = """{"context":42}""";

    // ── T036: GetChildrenAsync — namespace traversal ──

    [Fact]
    public async Task GetChildrenAsync_ParsesLeafAndNonLeafNodes()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/children"))
                return JsonResponse("""
                {
                    "leaf": ["load", "pswitch", "sysfork"],
                    "nonleaf": ["cpu", "nprocs"]
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetChildrenAsync("kernel.all");

        Assert.Equal("kernel.all", result.Prefix);
        Assert.Equal(3, result.LeafNames.Count);
        Assert.Contains("load", result.LeafNames);
        Assert.Contains("pswitch", result.LeafNames);
        Assert.Contains("sysfork", result.LeafNames);
        Assert.Equal(2, result.NonLeafNames.Count);
        Assert.Contains("cpu", result.NonLeafNames);
        Assert.Contains("nprocs", result.NonLeafNames);
    }

    [Fact]
    public async Task GetChildrenAsync_EmptyPrefix_QueriesRoot()
    {
        var capturedUri = "";
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/children"))
            {
                capturedUri = request.RequestUri!.PathAndQuery;
                return JsonResponse("""
                {
                    "leaf": [],
                    "nonleaf": ["kernel", "disk", "network", "mem"]
                }
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetChildrenAsync();

        Assert.Equal("", result.Prefix);
        Assert.Empty(result.LeafNames);
        Assert.Equal(4, result.NonLeafNames.Count);
        Assert.Contains("kernel", result.NonLeafNames);
    }

    [Fact]
    public async Task GetChildrenAsync_LeafOnlyNode_ReturnsEmptyNonLeaf()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/children"))
                return JsonResponse("""
                {
                    "leaf": ["read", "write", "total"],
                    "nonleaf": []
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetChildrenAsync("disk.dev");

        Assert.Equal(3, result.LeafNames.Count);
        Assert.Empty(result.NonLeafNames);
    }

    [Fact]
    public async Task GetChildrenAsync_NotConnected_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new PcpClientConnection(new Uri("http://localhost:44322"), new HttpClient(handler));

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.GetChildrenAsync("kernel"));
    }

    [Fact]
    public async Task GetChildrenAsync_HttpFailure_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var client = await ConnectClient(handler);
        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.GetChildrenAsync("kernel"));
    }

    [Fact]
    public async Task GetChildrenAsync_UrlEncodesPrefix()
    {
        string? capturedPath = null;
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/children"))
            {
                capturedPath = request.RequestUri!.PathAndQuery;
                return JsonResponse("""{"leaf": [], "nonleaf": []}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        await client.GetChildrenAsync("kernel.all");

        Assert.NotNull(capturedPath);
        Assert.Contains("prefix=kernel.all", capturedPath);
    }

    // ── T037: DescribeMetricsAsync — unknown metric handling ──

    [Fact]
    public async Task DescribeMetricsAsync_UnknownMetric_ThrowsPcpMetricNotFoundException()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/metric"))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"message":"Unknown metric name - bogus.nonexistent"}""",
                        Encoding.UTF8, "application/json")
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var ex = await Assert.ThrowsAsync<PcpMetricNotFoundException>(
            () => client.DescribeMetricsAsync(new[] { "bogus.nonexistent" }));

        Assert.Equal("bogus.nonexistent", ex.MetricName);
    }

    // ── T038: GetInstanceDomainAsync — instance domain enumeration ──

    [Fact]
    public async Task GetInstanceDomainAsync_ParsesInstancesCorrectly()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/indom"))
                return JsonResponse("""
                {
                    "indom": "60.1",
                    "instances": [
                        {"instance": 0, "name": "sda"},
                        {"instance": 1, "name": "sdb"},
                        {"instance": 2, "name": "nvme0n1"}
                    ]
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetInstanceDomainAsync("disk.dev.read");

        Assert.Equal("60.1", result.IndomId);
        Assert.Equal(3, result.Instances.Count);
        Assert.Equal(0, result.Instances[0].Id);
        Assert.Equal("sda", result.Instances[0].Name);
        Assert.Equal(1, result.Instances[1].Id);
        Assert.Equal("sdb", result.Instances[1].Name);
        Assert.Equal(2, result.Instances[2].Id);
        Assert.Equal("nvme0n1", result.Instances[2].Name);
    }

    [Fact]
    public async Task GetInstanceDomainAsync_SingularMetric_ReturnsEmptyInstances()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/indom"))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"message":"no InDom for metric - kernel.all.load"}""",
                        Encoding.UTF8, "application/json")
                };

            // Need to describe the metric to check if it's singular
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/metric"))
                return JsonResponse("""
                {
                    "metrics": [{
                        "name": "kernel.all.load",
                        "pmid": "60.2.0",
                        "type": "FLOAT",
                        "sem": "instant",
                        "units": "",
                        "text-oneline": "load average"
                    }]
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetInstanceDomainAsync("kernel.all.load");

        Assert.Empty(result.Instances);
    }

    [Fact]
    public async Task GetInstanceDomainAsync_NullIndomMessageVariant_ReturnsEmptyInstances()
    {
        // pmproxy also returns "metric has null indom" for some singular metrics (e.g. hinv.ncpu).
        // Before fix: only "no InDom" was handled; "null indom" fell through and threw.
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/indom"))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        """{"message":"metric has null indom"}""",
                        Encoding.UTF8, "application/json")
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        var result = await client.GetInstanceDomainAsync("hinv.ncpu");

        Assert.Empty(result.Instances);
    }

    [Fact]
    public async Task GetInstanceDomainAsync_NotConnected_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new PcpClientConnection(new Uri("http://localhost:44322"), new HttpClient(handler));

        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.GetInstanceDomainAsync("disk.dev.read"));
    }

    [Fact]
    public async Task GetInstanceDomainAsync_HttpFailure_ThrowsPcpConnectionException()
    {
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var client = await ConnectClient(handler);
        await Assert.ThrowsAsync<PcpConnectionException>(
            () => client.GetInstanceDomainAsync("disk.dev.read"));
    }

    [Fact]
    public async Task GetInstanceDomainAsync_UrlEncodesMetricName()
    {
        string? capturedPath = null;
        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/context"))
                return JsonResponse(ContextResponse);

            if (request.RequestUri!.PathAndQuery.Contains("/pmapi/indom"))
            {
                capturedPath = request.RequestUri!.PathAndQuery;
                return JsonResponse("""
                {
                    "indom": "60.1",
                    "instances": [{"instance": 0, "name": "sda"}]
                }
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = await ConnectClient(handler);
        await client.GetInstanceDomainAsync("disk.dev.read");

        Assert.NotNull(capturedPath);
        Assert.Contains("name=disk.dev.read", capturedPath);
    }

    // ── Helpers ──

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static async Task<PcpClientConnection> ConnectClient(MockHttpHandler handler)
    {
        var client = new PcpClientConnection(
            new Uri("http://localhost:44322"),
            new HttpClient(handler));
        await client.ConnectAsync();
        return client;
    }
}
