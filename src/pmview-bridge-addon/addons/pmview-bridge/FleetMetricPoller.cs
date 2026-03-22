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

    /// <summary>
    /// Start polling: create MetricPoller child nodes for each shard,
    /// discover host capacities (hinv.ncpu), then start all shards.
    /// </summary>
    public void StartPolling(string[] hostnames)
    {
        Log.LogWarning("FleetMetricPoller.StartPolling called with {Count} hostnames, endpoint={Endpoint}",
            hostnames.Length, Endpoint);
        ConfigureShards(hostnames, Endpoint, PollIntervalMs);
        _scrapeStopwatch = Stopwatch.StartNew();
        CreateShardPollers();
        // Shards auto-start via MetricPoller._Ready() since MetricNames is pre-set.
        // Discovery runs deferred to populate ncpu cache.
        CallDeferred(nameof(DeferredStartShards));
    }

    private void CreateShardPollers()
    {
        // Clean up any existing shards
        foreach (var shard in _shards)
            shard.QueueFree();
        _shards.Clear();

        if (_assignment == null) return;

        for (var i = 0; i < _assignment.Shards.Length; i++)
        {
            var shard = new MetricPoller
            {
                Name = $"Shard_{i}",
                Endpoint = Endpoint,
                PollIntervalMs = PollIntervalMs,
                MetricNames = FleetMetricNames,
                // Set hostname to first host in shard for logging context
                Hostname = _assignment.Shards[i].Length > 0
                    ? _assignment.Shards[i][0] : "",
            };
            shard.MetricsUpdated += OnShardMetricsUpdated;
            if (i == 0)
                shard.ConnectionStateChanged += state =>
                    EmitSignal(SignalName.ConnectionStateChanged, state);
            AddChild(shard);
            _shards.Add(shard);
        }
    }

    private async void DeferredStartShards()
    {
        // One-time discovery: fetch hinv.ncpu per host
        await DiscoverHostCapacities();
        // Shards already auto-started via MetricPoller._Ready()
    }

    private async Task DiscoverHostCapacities()
    {
        // TODO: Query hinv.ncpu via series API for each host.
        // For now default to 1 — refined when wired to real data.
        Log.LogInformation(
            "Discovering host capacities for {Count} hosts (defaulting ncpu=1)",
            AssignedHostCount);
        if (_assignment == null) return;
        foreach (var shard in _assignment.Shards)
            foreach (var host in shard)
                _hostNcpu[host] = 1;
        await Task.CompletedTask;
    }

    // ── Signal aggregation ──────────────────────────────────────────────

    private readonly Dictionary<string, Godot.Collections.Dictionary> _shardResults = new();
    private Stopwatch? _scrapeStopwatch;
    private int _shardsCompletedThisTick;

    private void OnShardMetricsUpdated(string hostname, Godot.Collections.Dictionary metrics)
    {
        Log.LogWarning("Shard metrics received: {Count} metrics, shard {Idx}/{Total}",
            metrics.Count, _shardsCompletedThisTick + 1, _shards.Count);
        _shardsCompletedThisTick++;

        // Store this shard's raw results
        var shardIndex = _shardsCompletedThisTick - 1;
        _shardResults[$"shard_{shardIndex}"] = metrics;

        if (_shardsCompletedThisTick < _shards.Count)
            return; // wait for all shards

        // All shards reported — check budget
        var elapsed = _scrapeStopwatch?.ElapsedMilliseconds ?? 0;
        _budgetTracker?.RecordScrapeCompleted(elapsed);

        if (_budgetTracker is { IsLagging: true })
        {
            Log.LogWarning(
                "Fleet scrape took {Elapsed}ms, exceeding {Interval}ms interval",
                elapsed, PollIntervalMs);
            EmitSignal(SignalName.ScrapeBudgetExceeded);
        }

        // Normalise and emit
        var normalised = NormaliseAllHosts(metrics);
        Log.LogWarning("Emitting FleetMetricsUpdated for {Count} hosts", normalised.Count);
        EmitSignal(SignalName.FleetMetricsUpdated, normalised);

        // Reset for next tick
        _shardResults.Clear();
        _shardsCompletedThisTick = 0;
        _scrapeStopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Normalise raw metric values into 0-1 range for each host.
    /// For now, uses the last shard's metrics for all hosts (all shards
    /// query the same pmproxy and get all hosts' data). Per-host partitioning
    /// from series instance metadata will be refined when wired to real data.
    /// </summary>
    private Godot.Collections.Dictionary NormaliseAllHosts(
        Godot.Collections.Dictionary rawMetrics)
    {
        var result = new Godot.Collections.Dictionary();
        if (_assignment == null) return result;

        foreach (var shard in _assignment.Shards)
        {
            foreach (var hostname in shard)
            {
                var ncpu = _hostNcpu.GetValueOrDefault(hostname, 1);
                var hostDict = new Godot.Collections.Dictionary();

                var idleRate = ExtractRate(rawMetrics, "kernel.all.cpu.idle");
                hostDict["cpu"] = FleetMetricNormaliser.NormaliseCpu(idleRate, ncpu);

                var pgIn = ExtractRate(rawMetrics, "mem.vmstat.pgpgin");
                var pgOut = ExtractRate(rawMetrics, "mem.vmstat.pgpgout");
                hostDict["memory"] = FleetMetricNormaliser.NormaliseRate(
                    pgIn, pgOut, PagingMaxPagesPerSec);

                var diskRead = ExtractRate(rawMetrics, "disk.all.read_bytes");
                var diskWrite = ExtractRate(rawMetrics, "disk.all.write_bytes");
                hostDict["disk"] = FleetMetricNormaliser.NormaliseRate(
                    diskRead, diskWrite, DiskMaxBytesPerSec);

                var netIn = ExtractRate(rawMetrics, "network.interface.in.bytes");
                var netOut = ExtractRate(rawMetrics, "network.interface.out.bytes");
                hostDict["network"] = FleetMetricNormaliser.NormaliseRate(
                    netIn, netOut, NetworkMaxBytesPerSec);

                result[hostname] = hostDict;
            }
        }

        return result;
    }

    /// <summary>
    /// Extract a numeric rate from the MetricPoller's MetricsUpdated dictionary.
    /// Sums all instances for aggregate metrics with instance domains.
    /// </summary>
    private static double ExtractRate(
        Godot.Collections.Dictionary metrics, string metricName)
    {
        if (!metrics.ContainsKey(metricName))
            return 0.0;
        var metricDict = metrics[metricName].AsGodotDictionary();
        if (!metricDict.ContainsKey("instances"))
            return 0.0;
        var instances = metricDict["instances"].AsGodotDictionary();
        // Sum all instances — handles both singular (-1 key) and multi-instance
        var sum = 0.0;
        foreach (var key in instances.Keys)
            sum += instances[key].AsDouble();
        return sum;
    }
}
