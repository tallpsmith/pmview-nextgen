using PcpClient;
using PmviewProjectionCore.Models;

namespace PmviewHostProjector.Discovery;

/// <summary>
/// Discovers host topology from a live pmproxy endpoint via IPcpClient.
/// Fetches OS, hostname, memory, and instance domains for CPU, disk, and network.
/// </summary>
public static class MetricDiscovery
{
    private const string SysnameMetric   = "kernel.uname.sysname";
    private const string NodenameMetric  = "kernel.uname.nodename";
    private const string PhysmemMetric   = "mem.physmem";
    private const string CpuMetric       = "kernel.percpu.cpu.user";
    private const string DiskMetric      = "disk.dev.read_bytes";
    private const string NetworkMetric   = "network.interface.in.bytes";

    public static async Task<HostTopology> DiscoverAsync(
        IPcpClient client,
        CancellationToken cancellationToken = default)
    {
        var scalarResults = await client.FetchAsync(
            [SysnameMetric, NodenameMetric, PhysmemMetric],
            cancellationToken);

        var sysname  = RequireString(scalarResults, SysnameMetric);
        var hostname = RequireString(scalarResults, NodenameMetric);
        var os       = ParseOs(sysname);
        var physmem  = ReadPhysmemBytes(scalarResults);

        var cpuDomain  = await client.GetInstanceDomainAsync(CpuMetric, cancellationToken);
        var diskDomain = await client.GetInstanceDomainAsync(DiskMetric, cancellationToken);
        var netDomain  = await client.GetInstanceDomainAsync(NetworkMetric, cancellationToken);

        return new HostTopology(
            Os: os,
            Hostname: hostname,
            CpuInstances: cpuDomain.Instances.Select(i => i.Name).ToList(),
            DiskDevices: FilterDiskDevices(diskDomain.Instances.Select(i => i.Name)),
            NetworkInterfaces: FilterNetworkInterfaces(netDomain.Instances.Select(i => i.Name)),
            PhysicalMemoryBytes: physmem);
    }

    private static string RequireString(IReadOnlyList<MetricValue> results, string metricName)
    {
        var mv = results.FirstOrDefault(r => r.Name == metricName);
        if (mv is null || mv.InstanceValues.Count == 0)
            throw new InvalidOperationException($"Missing metric: {metricName}");
        return mv.InstanceValues[0].StringValue
               ?? throw new InvalidOperationException($"Metric {metricName} has no string value");
    }

    private static long? ReadPhysmemBytes(IReadOnlyList<MetricValue> results)
    {
        var mv = results.FirstOrDefault(r => r.Name == PhysmemMetric);
        if (mv is null || mv.InstanceValues.Count == 0) return null;
        var iv = mv.InstanceValues[0];
        if (iv.IsString) return null;
        return (long)(iv.Value * 1024);  // KB → bytes
    }

    internal static IReadOnlyList<string> FilterDiskDevices(IEnumerable<string> devices) =>
        devices
            .Where(d => !d.StartsWith("loop", StringComparison.Ordinal))
            .Where(d => !d.StartsWith("dm-",  StringComparison.Ordinal))
            .ToList();

    internal static IReadOnlyList<string> FilterNetworkInterfaces(IEnumerable<string> interfaces) =>
        interfaces
            .Where(i => i != "lo")
            .Where(i => !i.StartsWith("veth", StringComparison.Ordinal))
            .Where(i => !i.StartsWith("cni",  StringComparison.Ordinal))
            .Where(i => !i.StartsWith("br-",  StringComparison.Ordinal))
            .ToList();

    private static HostOs ParseOs(string sysname) => sysname switch
    {
        "Linux"  => HostOs.Linux,
        "Darwin" => HostOs.MacOs,
        _ => throw new InvalidOperationException(
            $"Unsupported OS sysname '{sysname}' — go find a penguin or an apple.")
    };
}
