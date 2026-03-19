using System.Net;
using PcpClient.Tests.TestHelpers;
using Xunit;

namespace PcpClient.Tests;

public class PcpSeriesClientTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    [Fact]
    public async Task GetHostnamesAsync_ReturnsHostnames()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/labels", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"hostname": ["app", "nas"]}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.GetHostnamesAsync();
        Assert.Equal(2, result.Count);
        Assert.Contains("app", result);
        Assert.Contains("nas", result);
    }

    [Fact]
    public async Task GetHostnamesAsync_EmptyResponse_ReturnsEmpty()
    {
        var handler = new MockHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"hostname": []}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.GetHostnamesAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task QuerySeriesAsync_ReturnsSeriesIds()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/query", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""["abc123", "def456"]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.QuerySeriesAsync("disk.*");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task QuerySeriesAsync_EncodesSpecialCharacters()
    {
        string? capturedUrl = null;
        var handler = new MockHttpHandler(req =>
        {
            // AbsoluteUri preserves percent-encoding; ToString() normalises it back
            capturedUrl = req.RequestUri!.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        await client.QuerySeriesAsync("""*{hostname=="web server.local"}""");
        Assert.NotNull(capturedUrl);
        Assert.Contains("/series/query", capturedUrl);
        var encodedPart = capturedUrl.Split("expr=")[1];
        Assert.DoesNotContain(" ", encodedPart);
        Assert.Contains("%7B", encodedPart);
        Assert.Contains("%22", encodedPart);
    }

    [Fact]
    public async Task GetMetricNamesAsync_ReturnsNames()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/metrics", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "abc123", "name": "disk.dev.read"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.GetMetricNamesAsync(new[] { "abc123" });
        Assert.Single(result);
        Assert.Equal("disk.dev.read", result[0].Name);
    }

    [Fact]
    public async Task GetDescriptorsAsync_ReturnsDescriptors()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/descs", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "abc123", "semantics": "counter", "type": "u64", "units": "count"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.GetDescriptorsAsync(new[] { "abc123" });
        Assert.Single(result);
        Assert.Equal("counter", result[0].Semantics);
    }

    [Fact]
    public async Task GetInstancesAsync_ReturnsInstanceMapping()
    {
        var handler = new MockHttpHandler(req =>
        {
            Assert.Contains("/series/instances", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "abc123", "instance": "inst1", "id": 0, "name": "sda"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var result = await client.GetInstancesAsync(new[] { "abc123" });
        Assert.Single(result);
        Assert.Equal("sda", result["inst1"].Name);
    }

    [Fact]
    public async Task GetValuesAsync_WithTimeWindow_ReturnsValues()
    {
        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            Assert.Contains("/series/values", url);
            Assert.Contains("start=", url);
            Assert.Contains("finish=", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"series": "abc123", "timestamp": 1773348826000.0, "value": "0.42"}]""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var client = new PcpSeriesClient(BaseUrl, httpClient);
        var position = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        var result = await client.GetValuesAsync(
            new[] { "abc123" }, position, windowSeconds: 60.0);
        Assert.Single(result);
        Assert.Equal(0.42, result[0].NumericValue, precision: 2);
    }
}
