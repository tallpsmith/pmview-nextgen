using PcpClient;
using PmviewHostProjector.Discovery;
using PmviewHostProjector.Emission;
using PmviewHostProjector.Layout;
using PmviewProjectionCore.Models;
using PmviewHostProjector.Profiles;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "init")
            return RunInit(args);

        return await RunGenerate(args);
    }

    private static int RunInit(string[] args)
    {
        var force = HasFlag(args, "--force");
        var projectDir = args.Where(a => a != "init" && a != "--force").FirstOrDefault();
        projectDir = projectDir != null ? Path.GetFullPath(projectDir) : Directory.GetCurrentDirectory();

        Console.WriteLine($"pmview init: scaffolding project in {projectDir}");

        try
        {
            var result = ProjectScaffolder.Scaffold(projectDir, force);
            LogScaffoldResult(result);

            var addonSource = AddonInstaller.FindAddonSource();
            if (addonSource != null)
            {
                var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                if (repoRoot != null)
                {
                    AddonInstaller.InstallAddonWithLibraries(addonSource, projectDir, repoRoot);
                    Console.WriteLine("  Addon installed with libraries");
                }
            }

            Console.WriteLine("Project ready — open in Godot editor");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunGenerate(string[] args)
    {
        var pmproxyUrl = GetArg(args, "--pmproxy") ?? "http://localhost:44322";
        var outputPath = Path.GetFullPath(ResolveOutputPath(GetArg(args, "-o") ?? GetArg(args, "--output") ?? "host-view.tscn"));
        var shouldInit = HasFlag(args, "--init");
        var force = HasFlag(args, "--force");
        var installAddon = HasFlag(args, "--install-addon");

        Console.WriteLine($"pmview-host-projector: connecting to {pmproxyUrl}");

        try
        {
            var godotRoot = AddonInstaller.FindGodotProjectRoot(outputPath);

            if (godotRoot == null && shouldInit)
            {
                // Derive project root from output path (parent of scenes/)
                var outputDir = Path.GetDirectoryName(outputPath)!;
                godotRoot = outputDir.EndsWith("scenes")
                    ? Path.GetDirectoryName(outputDir)!
                    : outputDir;

                Console.WriteLine($"No project.godot found — initialising project at {godotRoot}");
                var result = ProjectScaffolder.Scaffold(godotRoot, force);
                LogScaffoldResult(result);

                var addonSource = AddonInstaller.FindAddonSource();
                if (addonSource != null)
                {
                    var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                    if (repoRoot != null)
                    {
                        AddonInstaller.InstallAddonWithLibraries(addonSource, godotRoot, repoRoot);
                        Console.WriteLine("  Addon installed with libraries");
                    }
                    else
                    {
                        Console.Error.WriteLine("  Warning: could not find repo root — addon libraries not installed");
                    }
                }
                else
                {
                    Console.Error.WriteLine("  Warning: could not find addon source — addon not installed");
                }
            }
            else if (godotRoot == null)
            {
                Console.Error.WriteLine("Error: Cannot find Godot project root (no project.godot found).");
                Console.Error.WriteLine("Either run from inside a Godot project, or use --init to create one.");
                Console.Error.WriteLine("  pmview init <project-dir>");
                Console.Error.WriteLine("  pmview generate --init --pmproxy <url> -o <path>");
                return 1;
            }
            else if (installAddon)
            {
                var addonSource = AddonInstaller.FindAddonSource();
                if (addonSource == null)
                {
                    Console.Error.WriteLine("Error: Cannot find addon source. Run from the pmview-nextgen repository.");
                    return 1;
                }

                var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
                if (repoRoot == null)
                {
                    Console.Error.WriteLine("Error: Cannot find repo root to build libraries.");
                    return 1;
                }

                var csprojPath = CsprojPatcher.FindTargetCsproj(godotRoot);
                if (csprojPath == null)
                {
                    Console.Error.WriteLine("Error: No .csproj found. Use --init instead of --install-addon for new projects.");
                    return 1;
                }

                AddonInstaller.InstallAddonWithLibraries(addonSource, godotRoot, repoRoot);
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
            if (topology.NetworkInterfaces.Count > 0)
                Console.WriteLine($"  NIC instances: {string.Join(", ", topology.NetworkInterfaces)}");

            var zones = new HostProfileProvider().GetProfile(topology.Os);
            Console.WriteLine($"  Profile: {topology.Os} ({zones.Count} zones)");

            Console.WriteLine("Computing layout...");
            var layout = LayoutCalculator.Calculate(zones, topology);

            LogLayoutDiagnostics(layout);

            Console.WriteLine("Generating scene...");
            var tscn = SceneEmitter.Emit(layout, pmproxyUrl);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(outputPath, tscn);
            Console.WriteLine($"Scene written to: {outputPath}");
            Console.WriteLine($"  {layout.Zones.Count} zones, " +
                              $"{layout.Zones.Sum(z => z.Shapes.Count)} shapes");

            if (shouldInit)
                Console.WriteLine("\nNext: open project in Godot, build C# solution (Alt+B), then enable the pmview-bridge plugin.");

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

    private static string ResolveOutputPath(string path)
    {
        if (Directory.Exists(path) || !Path.HasExtension(path))
            return Path.Combine(path, "host-view.tscn");
        return path;
    }

    private static void LogLayoutDiagnostics(SceneLayout layout)
    {
        foreach (var zone in layout.Zones)
        {
            var allShapes = zone.Items.SelectMany(item => item switch
            {
                PlacedStack stack => stack.Members,
                PlacedShape shape => [shape],
                _ => Enumerable.Empty<PlacedShape>()
            }).ToList();

            var ghostCount = allShapes.Count(s => s.IsPlaceholder);
            var liveCount = allShapes.Count - ghostCount;

            var status = ghostCount > 0
                ? $"{liveCount} live, {ghostCount} ghost"
                : $"{liveCount} shapes";

            Console.WriteLine($"  [{zone.Name}] {status}");

            foreach (var shape in allShapes.Where(s => s.IsPlaceholder))
                Console.WriteLine($"    ghost: {shape.MetricName} ({shape.NodeName})");
        }
    }

    private static void LogScaffoldResult(ScaffoldResult result)
    {
        foreach (var file in result.Created)
            Console.WriteLine($"  {file} created");
        foreach (var file in result.Overwritten)
            Console.WriteLine($"  {file} overwritten");
        foreach (var file in result.Skipped)
            Console.Error.WriteLine($"  {file} already exists — skipping (use --force to overwrite)");
    }
}
