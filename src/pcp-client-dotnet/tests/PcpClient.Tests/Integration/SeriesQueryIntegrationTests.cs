using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class SeriesQueryIntegrationTests : IntegrationTestBase
{
    private static HttpClient NewHttp() => new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task QuerySeries_KnownMetric_ReturnsSeriesIds()
    {
        using var http = NewHttp();

        var url = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.user", samples: 1);
        var json = await http.GetStringAsync(url);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.NotEmpty(seriesIds);
        Assert.All(seriesIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public async Task QuerySeries_TwoDistinctMetrics_ReturnDifferentSeriesIds()
    {
        using var http = NewHttp();

        var urlUser = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.user", samples: 1);
        var urlSys = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.cpu.sys", samples: 1);

        var jsonUser = await http.GetStringAsync(urlUser);
        var jsonSys = await http.GetStringAsync(urlSys);

        var idsUser = PcpSeriesQuery.ParseQueryResponse(jsonUser);
        var idsSys = PcpSeriesQuery.ParseQueryResponse(jsonSys);

        Assert.NotEmpty(idsUser);
        Assert.NotEmpty(idsSys);

        var userSet = new HashSet<string>(idsUser);
        Assert.False(userSet.SetEquals(idsSys),
            "kernel.all.cpu.user and kernel.all.cpu.sys should have different series IDs");
    }

    [Fact]
    public async Task QuerySeries_NonExistentMetric_ReturnsEmpty()
    {
        using var http = NewHttp();

        var url = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "no.such.metric.exists", samples: 1);
        var json = await http.GetStringAsync(url);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(json);

        Assert.Empty(seriesIds);
    }

    [Fact]
    public async Task QueryInstances_InstancedMetric_ReturnsInstanceInfo()
    {
        using var http = NewHttp();

        var queryUrl = PcpSeriesQuery.BuildQueryUrl(PmproxyUri, "kernel.all.load", samples: 1);
        var queryJson = await http.GetStringAsync(queryUrl);
        var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);

        Assert.NotEmpty(seriesIds);

        var instancesRequest = PcpSeriesQuery.BuildSeriesRequest(
            PmproxyUri, "/series/instances", seriesIds);
        var instancesResponse = await http.SendAsync(instancesRequest);
        instancesResponse.EnsureSuccessStatusCode();
        var instancesJson = await instancesResponse.Content.ReadAsStringAsync();
        var instances = PcpSeriesQuery.ParseInstancesResponse(instancesJson);

        Assert.NotEmpty(instances);
        // kernel.all.load has 3 load average instances: 1min, 5min, 15min
        Assert.True(instances.Count >= 3,
            $"Expected >= 3 load average instances, got {instances.Count}");
        Assert.All(instances.Values, info => Assert.False(string.IsNullOrWhiteSpace(info.Name)));
    }
}
