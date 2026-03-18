using System.Diagnostics;

namespace PmviewHostProjector;

/// <summary>
/// Builds PcpClient and PcpGodotBridge libraries and produces the DLLs
/// needed for the addon to be self-contained.
/// </summary>
public static class LibraryBuilder
{
    private const string RepoMarkerDir = "src";
    private const string RepoMarkerFile = "pmview-nextgen.sln";

    private static readonly string[] ExpectedDlls =
    {
        "PcpClient.dll",
        "PcpGodotBridge.dll",
        "Tomlyn.dll",
    };

    /// <summary>
    /// Walks up from startPath looking for the repo root (contains both
    /// "src" directory and "pmview-nextgen.sln" file).
    /// </summary>
    public static string? FindRepoRoot(string startPath)
    {
        var dir = Path.GetFullPath(startPath);
        if (!Directory.Exists(dir))
            dir = Path.GetDirectoryName(dir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, RepoMarkerDir)) &&
                File.Exists(Path.Combine(dir, RepoMarkerFile)))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// Publishes PcpGodotBridge (which transitively includes PcpClient and Tomlyn)
    /// and copies the resulting DLLs to outputDir.
    /// </summary>
    public static void PublishLibraries(string repoRoot, string outputDir)
    {
        var bridgeCsproj = Path.Combine(repoRoot,
            "src", "pcp-godot-bridge", "src", "PcpGodotBridge", "PcpGodotBridge.csproj");

        if (!File.Exists(bridgeCsproj))
            throw new FileNotFoundException(
                $"Cannot find PcpGodotBridge.csproj at expected path: {bridgeCsproj}");

        var publishDir = Path.Combine(Path.GetTempPath(), $"pmview-publish-{Guid.NewGuid():N}");

        try
        {
            RunDotnetPublish(bridgeCsproj, publishDir);
            CopyExpectedDlls(publishDir, outputDir);
        }
        finally
        {
            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, recursive: true);
        }
    }

    private static void RunDotnetPublish(string csprojPath, string publishDir)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojPath}\" -c Release -o \"{publishDir}\" --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // First restore (separate step for clearer error messages)
        var restoreInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{csprojPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var restoreProcess = Process.Start(restoreInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet restore");
        var restoreStderr = restoreProcess.StandardError.ReadToEndAsync();
        restoreProcess.WaitForExit();

        if (restoreProcess.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet restore failed: {restoreStderr.GetAwaiter().GetResult()}");

        // Now publish (with --no-restore since we just restored)
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet publish");
        var publishStderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish failed: {publishStderr.GetAwaiter().GetResult()}");
    }

    private static void CopyExpectedDlls(string publishDir, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var dllName in ExpectedDlls)
        {
            var source = Path.Combine(publishDir, dllName);
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"Expected DLL not found in publish output: {dllName}");

            File.Copy(source, Path.Combine(outputDir, dllName), overwrite: true);
        }
    }
}
