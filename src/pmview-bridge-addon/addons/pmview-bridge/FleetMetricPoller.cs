using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using PcpClient;
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
        string hostname, Godot.Collections.Dictionary metrics);

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

    private const int MaxHostsPerQueryBatch = 20;
    private readonly System.Net.Http.HttpClient _sharedHttpClient = new();

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
    /// discover series mapping, then distribute to shards.
    /// </summary>
    public void StartPolling(string[] hostnames)
    {
        Log.LogWarning("FleetMetricPoller.StartPolling called with {Count} hostnames, endpoint={Endpoint}",
            hostnames.Length, Endpoint);
        ConfigureShards(hostnames, Endpoint, PollIntervalMs);
        CreateShardPollers();
        // Shards auto-start via MetricPoller._Ready() since MetricNames is pre-set.
        // Discovery runs deferred to populate series mapping cache.
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
            shard.ShardPollCompleted += OnShardPollCompleted;
            if (i == 0)
                shard.ConnectionStateChanged += state =>
                    EmitSignal(SignalName.ConnectionStateChanged, state);
            AddChild(shard);
            _shards.Add(shard);
        }
    }

    private async void DeferredStartShards()
    {
        await DiscoverSeriesMapping();
    }

    private async Task DiscoverSeriesMapping()
    {
        if (_assignment == null) return;

        var allHostnames = _assignment.Shards.SelectMany(s => s).ToArray();
        var endpointUri = new Uri(Endpoint);
        var allSeriesIds = new HashSet<string>();
        var seriesIdsPerMetric = new Dictionary<string, List<string>>();

        // Default ncpu to 1 for all hosts (refined when hinv.ncpu wired)
        foreach (var host in allHostnames)
            _hostNcpu[host] = 1;

        // Phase 1: Query series IDs per metric, batched by hostname
        foreach (var metricName in FleetMetricNames)
        {
            var metricSeriesIds = new List<string>();
            for (var i = 0; i < allHostnames.Length; i += MaxHostsPerQueryBatch)
            {
                var batch = allHostnames
                    .Skip(i)
                    .Take(MaxHostsPerQueryBatch)
                    .ToArray();
                try
                {
                    var queryUrl = PcpSeriesQuery.BuildMultiHostFilteredQueryUrl(
                        endpointUri, metricName, batch);
                    var response = await _sharedHttpClient.GetAsync(queryUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.LogWarning(
                            "Discovery query failed for {Metric} batch {Batch}: {Status}",
                            metricName, i / MaxHostsPerQueryBatch, response.StatusCode);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var ids = PcpSeriesQuery.ParseQueryResponse(json);
                    metricSeriesIds.AddRange(ids);
                    foreach (var id in ids)
                        allSeriesIds.Add(id);
                }
                catch (Exception ex)
                {
                    Log.LogWarning(
                        "Discovery query error for {Metric}: {Message}",
                        metricName, ex.Message);
                }
            }

            if (metricSeriesIds.Count > 0)
                seriesIdsPerMetric[metricName] = metricSeriesIds;
        }

        if (allSeriesIds.Count == 0)
        {
            Log.LogWarning("Discovery found no series IDs — fleet polling will be idle");
            return;
        }

        // Phase 2: Map series_id -> hostname via /series/labels
        Dictionary<string, string> seriesIdToHostname;
        try
        {
            var labelsUrl = PcpSeriesQuery.BuildPerSeriesLabelsUrl(
                endpointUri, allSeriesIds);
            var labelsResponse = await _sharedHttpClient.GetAsync(labelsUrl);
            if (!labelsResponse.IsSuccessStatusCode)
            {
                Log.LogWarning(
                    "Discovery labels query failed: {Status}",
                    labelsResponse.StatusCode);
                return;
            }

            var labelsJson = await labelsResponse.Content.ReadAsStringAsync();
            seriesIdToHostname = PcpSeriesQuery.ParsePerSeriesHostnameLabels(labelsJson);
        }
        catch (Exception ex)
        {
            Log.LogWarning("Discovery labels error: {Message}", ex.Message);
            return;
        }

        Log.LogInformation(
            "Discovery complete: {SeriesCount} series IDs mapped to {HostCount} hostnames across {MetricCount} metrics",
            seriesIdToHostname.Count, allHostnames.Length, seriesIdsPerMetric.Count);

        // Phase 3: Partition by shard and distribute
        var partitioned = FleetMetricNormaliser.PartitionSeriesMapByShard(
            _assignment, seriesIdToHostname, seriesIdsPerMetric);

        for (var i = 0; i < _shards.Count && i < partitioned.Count; i++)
        {
            _shards[i].InitialiseWithCachedSeriesMap(
                partitioned[i].SeriesIdToHostname,
                partitioned[i].SeriesIdsPerMetric);
            Log.LogInformation(
                "Shard {Index}: {SeriesCount} series IDs for {HostCount} hosts",
                i, partitioned[i].SeriesIdToHostname.Count,
                _assignment.Shards[i].Length);
        }
    }

    // -- Signal aggregation -----------------------------------------------

    private Stopwatch? _scrapeStopwatch;
    private int _shardsCompletedThisTick;

    private void OnShardMetricsUpdated(string hostname, Godot.Collections.Dictionary metrics)
    {
        StartScrapeTimerIfNeeded();
        var normalised = NormaliseHostMetrics(hostname, metrics);
        Log.LogInformation(
            "[Fleet] Normalised {Host}: cpu={Cpu:F3} mem={Mem:F3} disk={Disk:F3} net={Net:F3}",
            hostname,
            normalised.ContainsKey("cpu") ? normalised["cpu"].AsDouble() : -1,
            normalised.ContainsKey("memory") ? normalised["memory"].AsDouble() : -1,
            normalised.ContainsKey("disk") ? normalised["disk"].AsDouble() : -1,
            normalised.ContainsKey("network") ? normalised["network"].AsDouble() : -1);
        EmitSignal(SignalName.FleetMetricsUpdated, hostname, normalised);
    }

    /// <summary>
    /// Called when the first shard reports its MetricsUpdated for a tick.
    /// Starts the scrape stopwatch — measures actual scrape time, not idle
    /// time between poll intervals.
    /// </summary>
    private void StartScrapeTimerIfNeeded()
    {
        if (_shardsCompletedThisTick == 0)
            _scrapeStopwatch = Stopwatch.StartNew();
    }

    private void OnShardPollCompleted()
    {
        _shardsCompletedThisTick++;

        if (_shardsCompletedThisTick < _shards.Count)
            return;

        var elapsed = _scrapeStopwatch?.ElapsedMilliseconds ?? 0;
        _budgetTracker?.RecordScrapeCompleted(elapsed);

        Log.LogInformation(
            "[Fleet] All shards complete: scrape={Elapsed}ms, budget={Interval}ms, lagging={Lagging}",
            elapsed, PollIntervalMs, _budgetTracker?.IsLagging ?? false);

        if (_budgetTracker is { IsLagging: true })
        {
            Log.LogWarning(
                "Fleet scrape took {Elapsed}ms, exceeding {Interval}ms interval",
                elapsed, PollIntervalMs);
            EmitSignal(SignalName.ScrapeBudgetExceeded);
        }

        _shardsCompletedThisTick = 0;
    }

    private Godot.Collections.Dictionary NormaliseHostMetrics(
        string hostname, Godot.Collections.Dictionary rawMetrics)
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

        return hostDict;
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

    public override void _ExitTree()
    {
        foreach (var shard in _shards)
            shard.QueueFree();
        _shards.Clear();
        _sharedHttpClient.Dispose();
    }
}
