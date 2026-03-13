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
    public static string? FindGodotProjectRoot(string outputFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputFilePath));
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
    /// location looking for the repo's godot-project/addons/pmview-bridge/.
    /// Returns null if not found (e.g. running as a standalone published binary).
    /// </summary>
    public static string? FindAddonSource()
    {
        var assemblyDir = AppContext.BaseDirectory;
        var dir = assemblyDir;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "godot-project", "addons", AddonDirName);
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "plugin.cfg")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Copies the addon source directory into the target Godot project's
    /// addons/pmview-bridge/ directory, overwriting existing files.
    /// </summary>
    public static void CopyAddonTo(string addonSourceDir, string godotProjectRoot)
    {
        var targetDir = Path.Combine(godotProjectRoot, "addons", AddonDirName);
        CopyDirectoryRecursive(addonSourceDir, targetDir);
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
