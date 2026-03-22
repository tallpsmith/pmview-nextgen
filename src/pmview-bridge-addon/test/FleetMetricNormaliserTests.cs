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

    // ── CPU normalisation ───────────────────────────────────────────────

    [TestCase]
    public void NormaliseCpu_HalfIdle_ReturnsHalfUtilisation()
    {
        // 4 cores, idle rate = 2000ms/s (half of 4*1000)
        var result = FleetMetricNormaliser.NormaliseCpu(idleRate: 2000.0, ncpu: 4);
        AssertThat(result).IsBetween(0.499, 0.501);
    }

    [TestCase]
    public void NormaliseCpu_FullyBusy_ReturnsOne()
    {
        var result = FleetMetricNormaliser.NormaliseCpu(idleRate: 0.0, ncpu: 8);
        AssertThat(result).IsBetween(0.999, 1.001);
    }

    [TestCase]
    public void NormaliseCpu_FullyIdle_ReturnsZero()
    {
        var result = FleetMetricNormaliser.NormaliseCpu(idleRate: 4000.0, ncpu: 4);
        AssertThat(result).IsBetween(-0.001, 0.001);
    }

    [TestCase]
    public void NormaliseCpu_ZeroCores_ReturnsZero()
    {
        var result = FleetMetricNormaliser.NormaliseCpu(idleRate: 1000.0, ncpu: 0);
        AssertThat(result).IsBetween(-0.001, 0.001);
    }

    [TestCase]
    public void NormaliseCpu_NegativeIdle_ClampsToOne()
    {
        // Shouldn't happen, but guard against it
        var result = FleetMetricNormaliser.NormaliseCpu(idleRate: -500.0, ncpu: 4);
        AssertThat(result).IsBetween(0.999, 1.001);
    }

    // ── Rate normalisation (paging, disk, network) ──────────────────────

    [TestCase]
    public void NormaliseRate_HalfMax_ReturnsHalf()
    {
        var result = FleetMetricNormaliser.NormaliseRate(
            combinedRate: 5000.0, maxRate: 10000.0);
        AssertThat(result).IsBetween(0.499, 0.501);
    }

    [TestCase]
    public void NormaliseRate_ExceedsMax_ClampedToOne()
    {
        var result = FleetMetricNormaliser.NormaliseRate(
            combinedRate: 20000.0, maxRate: 10000.0);
        AssertThat(result).IsBetween(0.999, 1.001);
    }

    [TestCase]
    public void NormaliseRate_Zero_ReturnsZero()
    {
        var result = FleetMetricNormaliser.NormaliseRate(
            combinedRate: 0.0, maxRate: 10000.0);
        AssertThat(result).IsBetween(-0.001, 0.001);
    }

    [TestCase]
    public void NormaliseRate_ZeroMax_ReturnsZero()
    {
        var result = FleetMetricNormaliser.NormaliseRate(
            combinedRate: 5000.0, maxRate: 0.0);
        AssertThat(result).IsBetween(-0.001, 0.001);
    }

    [TestCase]
    public void NormaliseRate_CombinesTwoInputs()
    {
        var result = FleetMetricNormaliser.NormaliseRate(
            rate1: 3000.0, rate2: 2000.0, maxRate: 10000.0);
        AssertThat(result).IsBetween(0.499, 0.501);
    }
}
