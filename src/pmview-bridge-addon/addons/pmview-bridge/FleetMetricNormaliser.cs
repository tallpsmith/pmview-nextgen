using System;
using System.Collections.Generic;
using System.Linq;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Pure C# logic for fleet metric polling: sharding, normalisation,
/// host-cap enforcement, and scrape budget tracking.
/// No Godot dependencies — fully unit-testable.
/// </summary>
public static class FleetMetricNormaliser
{
    public const int DefaultMaxHostsPerShard = 25;
    public const int DefaultMaxShards = 10;

    public record ShardAssignment(
        string[][] Shards,
        string[] DroppedHosts);

    /// <summary>
    /// Distributes hostnames across shards. Drops hosts beyond the hard cap
    /// (maxHostsPerShard * maxShards) and returns their names for logging.
    /// </summary>
    public static ShardAssignment AssignShards(
        string[] hostnames,
        int maxHostsPerShard = DefaultMaxHostsPerShard,
        int maxShards = DefaultMaxShards)
    {
        var maxHosts = maxHostsPerShard * maxShards;
        string[] dropped = [];
        string[] accepted = hostnames;

        if (hostnames.Length > maxHosts)
        {
            accepted = hostnames[..maxHosts];
            dropped = hostnames[maxHosts..];
        }

        var shardCount = Math.Max(1,
            (int)Math.Ceiling((double)accepted.Length / maxHostsPerShard));
        shardCount = Math.Min(shardCount, maxShards);

        var shards = new string[shardCount][];
        for (var i = 0; i < shardCount; i++)
        {
            var start = i * maxHostsPerShard;
            var length = Math.Min(maxHostsPerShard, accepted.Length - start);
            shards[i] = accepted[start..(start + length)];
        }

        return new ShardAssignment(shards, dropped);
    }

    /// <summary>
    /// Normalise CPU utilisation from idle counter rate.
    /// kernel.all.cpu.idle is a counter in milliseconds — its rate gives
    /// idle-ms/s. Dividing by (ncpu * 1000) gives idle fraction.
    /// Result: 1.0 - idle_fraction, clamped to [0, 1].
    /// </summary>
    public static double NormaliseCpu(double idleRate, int ncpu)
    {
        if (ncpu <= 0) return 0.0;
        var maxIdle = ncpu * 1000.0;
        var utilisation = 1.0 - (idleRate / maxIdle);
        return Math.Clamp(utilisation, 0.0, 1.0);
    }

    /// <summary>
    /// Normalise a combined rate against a configurable maximum.
    /// Used for paging (pages/s), disk (bytes/s), network (bytes/s).
    /// </summary>
    public static double NormaliseRate(double combinedRate, double maxRate)
    {
        if (maxRate <= 0.0) return 0.0;
        return Math.Clamp(combinedRate / maxRate, 0.0, 1.0);
    }

    /// <summary>
    /// Convenience: sum two rates before normalising.
    /// E.g. disk read + write, network in + out, pgpgin + pgpgout.
    /// </summary>
    public static double NormaliseRate(double rate1, double rate2, double maxRate)
    {
        return NormaliseRate(rate1 + rate2, maxRate);
    }

    /// <summary>
    /// Tracks whether poll scrapes are completing within their budget.
    /// When a scrape overruns the poll interval, flags skip-next-tick
    /// so the fleet poller can apply backpressure.
    /// </summary>
    public class ScrapeBudgetTracker
    {
        private readonly int _pollIntervalMs;

        public bool ShouldSkipNextTick { get; private set; }
        public bool IsLagging { get; private set; }

        public ScrapeBudgetTracker(int pollIntervalMs)
        {
            _pollIntervalMs = pollIntervalMs;
        }

        public void RecordScrapeCompleted(long elapsedMs)
        {
            IsLagging = elapsedMs > _pollIntervalMs;
            if (IsLagging)
                ShouldSkipNextTick = true;
        }

        public void ConsumeSkip()
        {
            ShouldSkipNextTick = false;
        }
    }
}
