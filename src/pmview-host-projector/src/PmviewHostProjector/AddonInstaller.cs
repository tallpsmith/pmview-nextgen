namespace PmviewHostProjector;

/// <summary>
/// Finds and copies the pmview-bridge addon into a target Godot project.
/// </summary>
public static class AddonInstaller
{
    private const string AddonDirName = "pmview-bridge";
    private const string GodotProjectFile = "project.godot";

    /// <summary>
    /// Walks parent directories from the output file path looking for project.godot.
    /// Returns the directory containing it, or null if not found.
    /// </summary>
    public static string? FindGodotProjectRoot(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);

        // If the path is an existing directory, start searching from it directly.
        // Otherwise treat it as a file path and start from its parent directory.
        var dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, GodotProjectFile)))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Finds the addon source directory by walking up from the executing assembly's
    /// location looking for src/pmview-bridge-addon/addons/pmview-bridge/.
    /// Returns null if not found (e.g. running as a standalone published binary).
    /// </summary>
    public static string? FindAddonSource()
    {
        var dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "pmview-bridge-addon", "addons", AddonDirName);
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "plugin.cfg")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static readonly string[] BundledDllNames =
    {
        "PcpClient",
        "PcpGodotBridge",
        "Tomlyn",
    };

    /// <summary>
    /// Copies the addon source directory into the target Godot project's
    /// addons/pmview-bridge/ directory, overwriting existing files.
    /// </summary>
    public static void CopyAddonTo(string addonSourceDir, string godotProjectRoot)
    {
        var targetDir = Path.Combine(godotProjectRoot, "addons", AddonDirName);
        CopyDirectoryRecursive(addonSourceDir, targetDir);
    }

    /// <summary>
    /// Full addon installation: copies addon source files, builds and places DLLs
    /// directly into the target project's lib/ directory, and patches the .csproj.
    /// </summary>
    public static void InstallAddonWithLibraries(string addonSourceDir, string godotProjectRoot, string repoRoot)
    {
        CopyAddonTo(addonSourceDir, godotProjectRoot);

        var targetLibDir = Path.Combine(godotProjectRoot, "addons", AddonDirName, "lib");
        LibraryBuilder.PublishLibraries(repoRoot, targetLibDir);

        var csprojPath = CsprojPatcher.FindTargetCsproj(godotProjectRoot);
        if (csprojPath != null)
            CsprojPatcher.AddLibraryReferences(csprojPath, BundledDllNames);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }
}
