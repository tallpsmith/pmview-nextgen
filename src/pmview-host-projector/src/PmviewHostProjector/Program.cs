using PcpClient;
using PmviewHostProjector.Discovery;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Layout;
using PmviewHostProjector.Profiles;

namespace PmviewHostProjector;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pmproxyUrl = GetArg(args, "--pmproxy") ?? "http://localhost:44322";
        var outputPath = GetArg(args, "-o") ?? GetArg(args, "--output") ?? "host-view.tscn";

        Console.WriteLine($"pmview-host-projector: connecting to {pmproxyUrl}");

        try
        {
            await using var pcpClient = new PcpClientConnection(new Uri(pmproxyUrl));
            await pcpClient.ConnectAsync();

            Console.WriteLine("Discovering host topology...");
            var topology = await MetricDiscovery.DiscoverAsync(pcpClient);
            Console.WriteLine($"  OS: {topology.Os}, Host: {topology.Hostname}");
            Console.WriteLine($"  CPUs: {topology.CpuInstances.Count}, " +
                              $"Disks: {topology.DiskDevices.Count}, " +
                              $"NICs: {topology.NetworkInterfaces.Count}");

            var zones = new HostProfileProvider().GetProfile(topology.Os);

            Console.WriteLine("Computing layout...");
            var layout = LayoutCalculator.Calculate(zones, topology);

            Console.WriteLine("Generating scene...");
            var tscn = SceneEmitter.Emit(layout);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, tscn);
            Console.WriteLine($"Scene written to: {outputPath}");
            Console.WriteLine($"  {layout.Zones.Count} zones, " +
                              $"{layout.Zones.Sum(z => z.Shapes.Count)} shapes");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
