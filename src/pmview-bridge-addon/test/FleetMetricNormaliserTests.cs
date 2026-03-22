using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using static GdUnit4.Assertions;
using PmviewNextgen.Bridge;

namespace PmviewNextgen.Tests;

[TestSuite]
public partial class FleetMetricNormaliserTests
{
    // ── Shard assignment ────────────────────────────────────────────────

    [TestCase]
    public void AssignShards_FiveHosts_SingleShard()
    {
        var hostnames = new[] { "h1", "h2", "h3", "h4", "h5" };
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(1);
        AssertThat(result.Shards[0]).HasSize(5);
        AssertThat(result.DroppedHosts).IsEmpty();
    }

    [TestCase]
    public void AssignShards_FiftyHosts_TwoShards()
    {
        var hostnames = Enumerable.Range(1, 50)
            .Select(i => $"host-{i:D2}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(2);
        AssertThat(result.Shards[0]).HasSize(25);
        AssertThat(result.Shards[1]).HasSize(25);
        AssertThat(result.DroppedHosts).IsEmpty();
    }

    [TestCase]
    public void AssignShards_OverLimit_DropsExcessHosts()
    {
        var hostnames = Enumerable.Range(1, 260)
            .Select(i => $"host-{i:D3}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(10);
        var totalAssigned = result.Shards.Sum(s => s.Length);
        AssertThat(totalAssigned).IsEqual(250);
        AssertThat(result.DroppedHosts).HasSize(10);
        AssertThat(result.DroppedHosts[0]).IsEqual("host-251");
    }

    [TestCase]
    public void AssignShards_ExactlyMaxHosts_NoDrops()
    {
        var hostnames = Enumerable.Range(1, 250)
            .Select(i => $"host-{i:D3}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(10);
        AssertThat(result.DroppedHosts).IsEmpty();
    }

    [TestCase]
    public void AssignShards_SingleHost_SingleShard()
    {
        var result = FleetMetricNormaliser.AssignShards(new[] { "lonely" });

        AssertThat(result.Shards).HasSize(1);
        AssertThat(result.Shards[0]).HasSize(1);
        AssertThat(result.Shards[0][0]).IsEqual("lonely");
    }

    [TestCase]
    public void AssignShards_Empty_SingleEmptyShard()
    {
        var result = FleetMetricNormaliser.AssignShards(Array.Empty<string>());

        AssertThat(result.Shards).HasSize(1);
        AssertThat(result.Shards[0]).IsEmpty();
    }

    [TestCase]
    public void AssignShards_UnevenDistribution_LastShardSmaller()
    {
        // 30 hosts / 25 per shard = 2 shards: 25 + 5
        var hostnames = Enumerable.Range(1, 30)
            .Select(i => $"host-{i:D2}").ToArray();
        var result = FleetMetricNormaliser.AssignShards(hostnames);

        AssertThat(result.Shards).HasSize(2);
        AssertThat(result.Shards[0]).HasSize(25);
        AssertThat(result.Shards[1]).HasSize(5);
    }
}
