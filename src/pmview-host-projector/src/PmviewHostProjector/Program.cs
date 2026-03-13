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
        var outputPath = ResolveOutputPath(GetArg(args, "-o") ?? GetArg(args, "--output") ?? "host-view.tscn");
        var installAddon = HasFlag(args, "--install-addon");

        Console.WriteLine($"pmview-host-projector: connecting to {pmproxyUrl}");

        try
        {
            if (installAddon)
            {
                var godotRoot = AddonInstaller.FindGodotProjectRoot(outputPath);
                if (godotRoot == null)
                {
                    Console.Error.WriteLine("Error: Cannot find Godot project root (no project.godot found). " +
                                            "Ensure the output path is inside a Godot project.");
                    return 1;
                }

                var addonSource = AddonInstaller.FindAddonSource();
                if (addonSource == null)
                {
                    Console.Error.WriteLine("Error: Cannot find addon source. Run from the pmview-nextgen repository.");
                    return 1;
                }

                AddonInstaller.CopyAddonTo(addonSource, godotRoot);
                Console.WriteLine($"Addon installed to: {Path.Combine(godotRoot, "addons", "pmview-bridge")}");
            }

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

    private static bool HasFlag(string[] args, string flag) =>
        Array.IndexOf(args, flag) >= 0;

    /// <summary>
    /// If the output path is an existing directory or has no file extension,
    /// treat it as a directory and append the default filename.
    /// </summary>
    private static string ResolveOutputPath(string path)
    {
        if (Directory.Exists(path) || (!Path.HasExtension(path) && !path.EndsWith(".tscn")))
            return Path.Combine(path, "host-view.tscn");
        return path;
    }
}
