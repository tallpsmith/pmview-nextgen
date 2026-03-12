using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class SeriesQueryIntegrationTests : IntegrationTestBase
{
    // Valkey/pmseries may not be running even when pmproxy is up.
    // Probe with a tight timeout; any non-response means skip.
    private static readonly Lazy<bool> SeriesAvailable = new(() =>
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var url = PcpSeriesQuery.BuildQueryUrl(
                new Uri("http://localhost:44322"), "kernel.all.cpu.user", samples: 1);
            probe.GetStringAsync(url).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    });

    private static void SkipIfSeriesUnavailable() =>
        Skip.If(!SeriesAvailable.Value,
            "pmseries/Valkey not available — start dev-environment stack with archive data loaded");

    // Fresh client per test: avoids stale keep-alive sockets hanging until default 100s timeout.
    private static HttpClient NewHttp() => new() { Timeout = TimeSpan.FromSeconds(10) };

    [SkippableFact]
    public async Task QuerySeries_KnownMetric_ReturnsSeriesIds()
    {
        SkipIfSeriesUnavailable();
        using var http = NewHttp();

        var url = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.user", samples: 1);
        var json = await http.GetStringAsync(url);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.NotEmpty(seriesIds);
        Assert.All(seriesIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [SkippableFact]
    public async Task QuerySeries_TwoDistinctMetrics_ReturnDifferentSeriesIds()
    {
        SkipIfSeriesUnavailable();
        using var http = NewHttp();

        var urlUser = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.user", samples: 1);
        var urlSys = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.sys", samples: 1);

        var jsonUser = await http.GetStringAsync(urlUser);
        var jsonSys = await http.GetStringAsync(urlSys);

        var idsUser = PcpSeriesQuery.ParseQueryResponse(jsonUser);
        var idsSys = PcpSeriesQuery.ParseQueryResponse(jsonSys);

        Assert.NotEmpty(idsUser);
        Assert.NotEmpty(idsSys);

        // The two metrics must not share all series IDs — they are distinct counters
        var userSet = new HashSet<string>(idsUser);
        Assert.False(userSet.SetEquals(idsSys),
            "kernel.all.cpu.user and kernel.all.cpu.sys should have different series IDs");
    }

    [SkippableFact]
    public async Task QuerySeries_NonExistentMetric_ReturnsEmpty()
    {
        SkipIfSeriesUnavailable();
        using var http = NewHttp();

        var url = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "no.such.metric.exists", samples: 1);
        var json = await http.GetStringAsync(url);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Empty(seriesIds);
    }

    [SkippableFact]
    public async Task QueryInstances_InstancedMetric_ReturnsInstanceInfo()
    {
        SkipIfSeriesUnavailable();
        using var http = NewHttp();

        // First resolve the series IDs for the instanced metric
        var queryUrl = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.load", samples: 1);
        var queryJson = await http.GetStringAsync(queryUrl);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);

        Skip.If(!seriesIds.Any(),
            "No series found for kernel.all.load — archive data may not be loaded into Valkey");

        var instancesUrl = PcpSeriesQuery.BuildInstancesUrl(PmproxyUri, seriesIds);
        var instancesJson = await http.GetStringAsync(instancesUrl);
        var instances = PcpSeriesQuery.ParseInstancesResponse(instancesJson);

        Assert.NotEmpty(instances);
        // kernel.all.load has 3 load average instances: 1min, 5min, 15min
        Assert.True(instances.Count >= 3,
            $"Expected >= 3 load average instances, got {instances.Count}");
        Assert.All(instances.Values, info => Assert.False(string.IsNullOrWhiteSpace(info.Name)));
    }
}
