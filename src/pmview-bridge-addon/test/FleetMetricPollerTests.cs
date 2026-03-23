using System;
using System.Linq;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using PmviewNextgen.Bridge;

namespace PmviewNextgen.Tests;

[TestSuite]
public partial class FleetMetricPollerTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void ConfigureShards_SmallFleet_CreatesOneShard()
    {
        var poller = new FleetMetricPoller();
        var hostnames = new[] { "h1", "h2", "h3" };

        poller.ConfigureShards(hostnames, "http://localhost:44322", 2000);

        AssertThat(poller.ShardCount).IsEqual(1);
        AssertThat(poller.AssignedHostCount).IsEqual(3);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ConfigureShards_FiftyHosts_CreatesTwoShards()
    {
        var poller = new FleetMetricPoller();
        var hostnames = Enumerable.Range(1, 50)
            .Select(i => $"host-{i:D2}").ToArray();

        poller.ConfigureShards(hostnames, "http://localhost:44322", 2000);

        AssertThat(poller.ShardCount).IsEqual(2);
        AssertThat(poller.AssignedHostCount).IsEqual(50);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ConfigureShards_OverLimit_ReportsDroppedCount()
    {
        var poller = new FleetMetricPoller();
        var hostnames = Enumerable.Range(1, 260)
            .Select(i => $"host-{i:D3}").ToArray();

        poller.ConfigureShards(hostnames, "http://localhost:44322", 2000);

        AssertThat(poller.ShardCount).IsEqual(10);
        AssertThat(poller.AssignedHostCount).IsEqual(250);
        AssertThat(poller.DroppedHostCount).IsEqual(10);
    }

    [TestCase]
    public void FleetMetricNames_ContainsExpectedMetrics()
    {
        AssertThat(FleetMetricPoller.FleetMetricNames).Contains(
            "kernel.all.cpu.idle",
            "mem.vmstat.pgpgin",
            "mem.vmstat.pgpgout",
            "disk.all.read_bytes",
            "disk.all.write_bytes",
            "network.interface.in.bytes",
            "network.interface.out.bytes");
    }
}
