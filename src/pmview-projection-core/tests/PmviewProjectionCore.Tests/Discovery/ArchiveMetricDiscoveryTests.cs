using System.Net;
using PcpClient;
using PmviewProjectionCore.Discovery;
using PmviewProjectionCore.Models;
using Xunit;

namespace PmviewProjectionCore.Tests.Discovery;

public class ArchiveMetricDiscoveryTests
{
    private static readonly Uri BaseUrl = new("http://localhost:44322");

    private static HttpMessageHandler CreateLinuxArchiveHandler()
    {
        return new MockHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;

            if (url.Contains("kernel.uname.sysname") && url.Contains("/series/query"))
                return JsonResponse("[\"series_sysname\"]");

            if (url.Contains("series_sysname") && url.Contains("/series/values"))
                return JsonResponse("[{\"series\":\"series_sysname\",\"timestamp\":1773000000000.0,\"value\":\"Linux\"}]");

            if (url.Contains("kernel.percpu.cpu.user") && url.Contains("/series/query"))
                return JsonResponse("[\"series_cpu\"]");

            if (url.Contains("series_cpu") && url.Contains("/series/instances"))
                return JsonResponse("[{\"series\":\"series_cpu\",\"instance\":\"inst_cpu0\",\"id\":0,\"name\":\"cpu0\"},{\"series\":\"series_cpu\",\"instance\":\"inst_cpu1\",\"id\":1,\"name\":\"cpu1\"}]");

            if (url.Contains("disk.dev.read_bytes") && url.Contains("/series/query"))
                return JsonResponse("[\"series_disk\"]");

            if (url.Contains("series_disk") && url.Contains("/series/instances"))
                return JsonResponse("[{\"series\":\"series_disk\",\"instance\":\"inst_nvme\",\"id\":0,\"name\":\"nvme0n1\"}]");

            if (url.Contains("network.interface.in.bytes") && url.Contains("/series/query"))
                return JsonResponse("[\"series_net\"]");

            if (url.Contains("series_net") && url.Contains("/series/instances"))
                return JsonResponse("[{\"series\":\"series_net\",\"instance\":\"inst_eth0\",\"id\":0,\"name\":\"eth0\"},{\"series\":\"series_net\",\"instance\":\"inst_lo\",\"id\":1,\"name\":\"lo\"}]");

            return JsonResponse("[]");
        });
    }

    [Fact]
    public async Task DiscoverAsync_DetectsLinuxOs()
    {
        var handler = CreateLinuxArchiveHandler();
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("saas-prod-01");
        Assert.Equal(HostOs.Linux, topology.Os);
    }

    [Fact]
    public async Task DiscoverAsync_UsesProvidedHostname()
    {
        var handler = CreateLinuxArchiveHandler();
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("saas-prod-01");
        Assert.Equal("saas-prod-01", topology.Hostname);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversCpuInstances()
    {
        var handler = CreateLinuxArchiveHandler();
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("saas-prod-01");
        Assert.Equal(2, topology.CpuInstances.Count);
        Assert.Contains("cpu0", topology.CpuInstances);
        Assert.Contains("cpu1", topology.CpuInstances);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversDiskDevices()
    {
        var handler = CreateLinuxArchiveHandler();
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("saas-prod-01");
        Assert.Single(topology.DiskDevices);
        Assert.Contains("nvme0n1", topology.DiskDevices);
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversNetworkInterfaces_FilteringLoopback()
    {
        var handler = CreateLinuxArchiveHandler();
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("saas-prod-01");
        Assert.Single(topology.NetworkInterfaces);
        Assert.Contains("eth0", topology.NetworkInterfaces);
    }

    [Fact]
    public async Task DiscoverAsync_UsesHostnameFilteredQueries()
    {
        var capturedUrls = new List<string>();
        var handler = new MockHandler(req =>
        {
            capturedUrls.Add(req.RequestUri!.AbsoluteUri);
            return JsonResponse("[]");
        });
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        await discovery.DiscoverAsync("my-host");

        var queryUrls = capturedUrls.Where(u => u.Contains("/series/query")).ToList();
        Assert.True(queryUrls.Count > 0);
        foreach (var url in queryUrls)
        {
            Assert.Contains("my-host", url);
            Assert.Contains("%7B", url);
        }
    }

    [Fact]
    public async Task DiscoverAsync_NoSysnameSeries_DefaultsToUnknownOs()
    {
        var handler = new MockHandler(_ => JsonResponse("[]"));
        var discovery = new ArchiveMetricDiscovery(BaseUrl, new HttpClient(handler));
        var topology = await discovery.DiscoverAsync("empty-host");
        Assert.Equal(HostOs.Unknown, topology.Os);
        Assert.Empty(topology.CpuInstances);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
