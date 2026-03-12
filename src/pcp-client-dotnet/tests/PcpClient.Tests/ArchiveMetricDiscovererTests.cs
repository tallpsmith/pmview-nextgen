using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for ArchiveMetricDiscoverer: orchestrates series queries
/// into high-level host and metric discovery operations.
/// </summary>
public class ArchiveMetricDiscovererTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    private static MockHttpHandler CreateHandler(
        Dictionary<string, string> urlToResponse)
    {
        return new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            foreach (var kvp in urlToResponse)
            {
                if (url.Contains(kvp.Key))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(kvp.Value,
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    // ── GetHostnamesAsync ──

    [Fact]
    public async Task GetHostnamesAsync_DelegatesToSeriesClient()
    {
        var handler = CreateHandler(new()
        {
            ["/series/labels"] = """{"hostname": ["host1", "host2"]}"""
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.GetHostnamesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("host1", result);
    }

    // ── DiscoverMetricsForHostAsync ──

    [Fact]
    public async Task DiscoverMetricsForHostAsync_ReturnsSortedDeduplicatedNames()
    {
        var handler = CreateHandler(new()
        {
            ["/series/query"] = """["series1", "series2", "series3"]""",
            ["/series/metrics"] = """
                [
                    {"series": "series1", "name": "disk.dev.write"},
                    {"series": "series2", "name": "disk.dev.read"},
                    {"series": "series3", "name": "disk.dev.read"}
                ]
                """
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.DiscoverMetricsForHostAsync("host1");

        Assert.Equal(2, result.Count); // deduplicated
        Assert.Equal("disk.dev.read", result[0]); // sorted
        Assert.Equal("disk.dev.write", result[1]);
    }

    [Fact]
    public async Task DiscoverMetricsForHostAsync_NoSeries_ReturnsEmpty()
    {
        var handler = CreateHandler(new()
        {
            ["/series/query"] = "[]"
        });
        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        var result = await discoverer.DiscoverMetricsForHostAsync("host1");

        Assert.Empty(result);
    }

    // ── DescribeMetricAsync ──

    [Fact]
    public async Task DescribeMetricAsync_UsesCache_NoExtraQueryCall()
    {
        int queryCallCount = 0;
        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("/series/query"))
            {
                queryCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """["series1"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("/series/metrics"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"series": "series1", "name": "disk.dev.read"}]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            if (url.Contains("/series/descs"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"series": "series1", "semantics": "counter", "type": "u64", "units": "count"}]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            if (url.Contains("/series/instances"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]",
                        System.Text.Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        // First call populates cache
        await discoverer.DiscoverMetricsForHostAsync("host1");
        Assert.Equal(1, queryCallCount);

        // Describe uses cached series IDs — no extra /series/query call
        var detail = await discoverer.DescribeMetricAsync("disk.dev.read", "host1");
        Assert.Equal(1, queryCallCount); // still 1 — cache hit

        Assert.Equal("disk.dev.read", detail.Name);
        Assert.Equal("counter", detail.Semantics);
        Assert.Equal("u64", detail.Type);
        Assert.Equal("count", detail.Units);
    }

    [Fact]
    public async Task DescribeMetricAsync_CacheMiss_QueriesDirectly()
    {
        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("/series/query"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """["series1"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            if (url.Contains("/series/descs"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"series": "series1", "semantics": "instant", "type": "float"}]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            if (url.Contains("/series/instances"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]",
                        System.Text.Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var discoverer = new ArchiveMetricDiscoverer(
            BaseUrl, new HttpClient(handler));

        // No prior DiscoverMetricsForHostAsync — cache is empty
        var detail = await discoverer.DescribeMetricAsync(
            "kernel.all.load", "host1");

        Assert.Equal("instant", detail.Semantics);
        Assert.Equal("float", detail.Type);
    }
}
