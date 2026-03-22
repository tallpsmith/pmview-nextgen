using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using PmviewApp;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Godot Node that orchestrates multiple MetricPoller shards for fleet-wide
/// metric collection. Handles host sharding, discovery, normalisation,
/// and scrape budget tracking.
/// </summary>
public partial class FleetMetricPoller : Node
{
    [Signal]
    public delegate void FleetMetricsUpdatedEventHandler(
        Godot.Collections.Dictionary metrics);

    [Signal]
    public delegate void HostsDroppedEventHandler(int count, string[] hostnames);

    [Signal]
    public delegate void ScrapeBudgetExceededEventHandler();

    [Signal]
    public delegate void ConnectionStateChangedEventHandler(string state);

    [Export] public string Endpoint { get; set; } = "http://localhost:44322";
    [Export] public int PollIntervalMs { get; set; } = 2000;
    [Export] public int MaxHostsPerShard { get; set; } = 25;
    [Export] public int MaxShards { get; set; } = 10;
    [Export] public double DiskMaxBytesPerSec { get; set; } = 500_000_000;
    [Export] public double NetworkMaxBytesPerSec { get; set; } = 125_000_000;
    [Export] public double PagingMaxPagesPerSec { get; set; } = 10_000;

    private ILogger? _log;
    private ILogger Log => _log ??= PmviewLogger.GetLogger("FleetMetricPoller");

    private readonly List<MetricPoller> _shards = new();
    private FleetMetricNormaliser.ScrapeBudgetTracker? _budgetTracker;
    private readonly Dictionary<string, int> _hostNcpu = new();
    private readonly Dictionary<string, int> _hostToShardIndex = new();
    private FleetMetricNormaliser.ShardAssignment? _assignment;

    /// <summary>Number of active MetricPoller shards.</summary>
    public int ShardCount => _assignment?.Shards.Length ?? 0;

    /// <summary>Total hosts assigned across all shards.</summary>
    public int AssignedHostCount =>
        _assignment?.Shards.Sum(s => s.Length) ?? 0;

    /// <summary>Hosts that exceeded the cap and were dropped.</summary>
    public int DroppedHostCount => _assignment?.DroppedHosts.Length ?? 0;

    /// <summary>
    /// Metrics polled per host for fleet view.
    /// </summary>
    public static readonly string[] FleetMetricNames =
    [
        "kernel.all.cpu.idle",
        "mem.vmstat.pgpgin",
        "mem.vmstat.pgpgout",
        "disk.all.read_bytes",
        "disk.all.write_bytes",
        "network.interface.in.bytes",
        "network.interface.out.bytes",
    ];

    /// <summary>
    /// Configure shard assignment from a list of hostnames.
    /// Does not start polling — call StartPolling() after.
    /// </summary>
    public void ConfigureShards(
        string[] hostnames, string endpoint, int pollIntervalMs)
    {
        Endpoint = endpoint;
        PollIntervalMs = pollIntervalMs;
        _budgetTracker = new FleetMetricNormaliser.ScrapeBudgetTracker(
            pollIntervalMs);

        _assignment = FleetMetricNormaliser.AssignShards(
            hostnames, MaxHostsPerShard, MaxShards);

        if (_assignment.DroppedHosts.Length > 0)
        {
            Log.LogWarning(
                "Fleet host cap reached. Dropped {Count} hosts: {Hosts}",
                _assignment.DroppedHosts.Length,
                string.Join(", ", _assignment.DroppedHosts));
            EmitSignal(SignalName.HostsDropped,
                _assignment.DroppedHosts.Length, _assignment.DroppedHosts);
        }

        // Build host-to-shard index
        _hostToShardIndex.Clear();
        for (var si = 0; si < _assignment.Shards.Length; si++)
        {
            foreach (var host in _assignment.Shards[si])
                _hostToShardIndex[host] = si;
        }

        Log.LogInformation(
            "Fleet configured: {HostCount} hosts across {ShardCount} shards",
            AssignedHostCount, ShardCount);
    }
}
