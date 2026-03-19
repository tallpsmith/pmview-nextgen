using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for ArchiveSourceDiscovery: host listing and time bounds probing.
/// </summary>
public class ArchiveSourceDiscoveryTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    [Fact]
    public async Task GetHostnamesAsync_ReturnsHostnames()
    {
        var handler = new MockHttpHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"hostname": ["saas-prod-01", "container-abc"]}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var result = await discovery.GetHostnamesAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains("saas-prod-01", result);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_ReturnsMinMaxTimestamps()
    {
        var requestIndex = 0;
        var handler = new MockHttpHandler(req =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""["series_abc"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                    {"series": "series_abc", "timestamp": 1773348826000.0, "value": "0.5"},
                    {"series": "series_abc", "timestamp": 1773435226000.0, "value": "0.6"},
                    {"series": "series_abc", "timestamp": 1773521626000.0, "value": "0.7"}
                ]
                """, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("saas-prod-01");

        Assert.NotNull(bounds);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773348826000).UtcDateTime,
            bounds.Value.Start);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1773521626000).UtcDateTime,
            bounds.Value.End);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_NoSeriesFound_ReturnsNull()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("nonexistent");

        Assert.Null(bounds);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_NoValues_ReturnsNull()
    {
        var requestIndex = 0;
        var handler = new MockHttpHandler(_ =>
        {
            requestIndex++;
            if (requestIndex == 1)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""["series_abc"]""",
                        System.Text.Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        var bounds = await discovery.DiscoverTimeBoundsAsync("empty-host");

        Assert.Null(bounds);
    }

    [Fact]
    public async Task DiscoverTimeBoundsAsync_UsesHostnameFilteredQuery()
    {
        string? capturedQueryUrl = null;
        var requestIndex = 0;
        var handler = new MockHttpHandler(req =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                capturedQueryUrl = req.RequestUri!.AbsoluteUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]",
                        System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var discovery = new ArchiveSourceDiscovery(BaseUrl, new HttpClient(handler));

        await discovery.DiscoverTimeBoundsAsync("saas-prod-01");

        Assert.NotNull(capturedQueryUrl);
        Assert.Contains("kernel.all.load", capturedQueryUrl);
        Assert.Contains("%7B", capturedQueryUrl);
        Assert.Contains("saas-prod-01", capturedQueryUrl);
    }

    [Fact]
    public void ComputeDefaultStartTime_ClampsTo24HoursBeforeEnd()
    {
        var start = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc);

        var defaultStart = ArchiveSourceDiscovery.ComputeDefaultStartTime(start, end);

        Assert.Equal(new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc), defaultStart);
    }

    [Fact]
    public void ComputeDefaultStartTime_ShortArchive_ClampsToArchiveStart()
    {
        var start = new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 18, 12, 0, 0, DateTimeKind.Utc);

        var defaultStart = ArchiveSourceDiscovery.ComputeDefaultStartTime(start, end);

        Assert.Equal(start, defaultStart);
    }
}
