using Xunit;

namespace PcpClient.Tests.Integration;

[Trait("Category", "Integration")]
public class MetricFetchIntegrationTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task FetchAsync_SingularMetric_ReturnsSingleValue()
    {
        await Client.ConnectAsync();

        var values = await Client.FetchAsync(["hinv.ncpu"]);

        Assert.Single(values);
        var mv = values[0];
        Assert.Equal("hinv.ncpu", mv.Name);
        Assert.NotEmpty(mv.Pmid);
        Assert.True(mv.Timestamp > 0, "Timestamp should be positive");
        Assert.Single(mv.InstanceValues);
        Assert.Null(mv.InstanceValues[0].InstanceId);
        Assert.True(mv.InstanceValues[0].Value > 0, "ncpu should be > 0");
    }

    [SkippableFact]
    public async Task FetchAsync_InstancedMetric_ReturnsMultipleInstanceValues()
    {
        await Client.ConnectAsync();

        var values = await Client.FetchAsync(["kernel.all.load"]);

        Assert.Single(values);
        var mv = values[0];
        Assert.Equal("kernel.all.load", mv.Name);
        // kernel.all.load has instances: 1min, 5min, 15min
        Assert.True(mv.InstanceValues.Count >= 3,
            $"Expected >= 3 load average instances, got {mv.InstanceValues.Count}");
        Assert.All(mv.InstanceValues, iv => Assert.NotNull(iv.InstanceId));
    }

    [SkippableFact]
    public async Task FetchAsync_MultipleMetrics_ReturnsAllValues()
    {
        await Client.ConnectAsync();

        var names = new[] { "kernel.all.load", "hinv.ncpu" };
        var values = await Client.FetchAsync(names);

        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.Name == "kernel.all.load");
        Assert.Contains(values, v => v.Name == "hinv.ncpu");
    }

    [SkippableFact]
    public async Task FetchAsync_ConsecutiveFetches_ReturnFreshTimestamps()
    {
        await Client.ConnectAsync();

        var first = await Client.FetchAsync(["hinv.ncpu"]);
        await Task.Delay(1100); // pmproxy timestamp resolution is ~1s
        var second = await Client.FetchAsync(["hinv.ncpu"]);

        Assert.True(second[0].Timestamp >= first[0].Timestamp,
            "Second fetch timestamp should be >= first");
    }
}
