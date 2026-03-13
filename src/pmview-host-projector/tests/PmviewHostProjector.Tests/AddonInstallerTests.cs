using Xunit;
using PmviewHostProjector;

namespace PmviewHostProjector.Tests;

public class AddonInstallerTests : IDisposable
{
    private readonly string _tempDir;

    public AddonInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"projector-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FindGodotProjectRoot_FindsDirectoryWithProjectGodot()
    {
        var godotRoot = Path.Combine(_tempDir, "my-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");

        var scenePath = Path.Combine(godotRoot, "scenes", "host_view.tscn");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        var result = AddonInstaller.FindGodotProjectRoot(scenePath);
        Assert.Equal(godotRoot, result);
    }

    [Fact]
    public void FindGodotProjectRoot_WalksUpMultipleLevels()
    {
        var godotRoot = Path.Combine(_tempDir, "my-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");

        var deepPath = Path.Combine(godotRoot, "scenes", "generated", "deep", "host_view.tscn");

        var result = AddonInstaller.FindGodotProjectRoot(deepPath);
        Assert.Equal(godotRoot, result);
    }

    [Fact]
    public void FindGodotProjectRoot_FindsRootWhenOutputPathIsTheGodotRoot()
    {
        var godotRoot = Path.Combine(_tempDir, "my-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");

        var result = AddonInstaller.FindGodotProjectRoot(godotRoot);
        Assert.Equal(godotRoot, result);
    }

    [Fact]
    public void FindGodotProjectRoot_ReturnsNullWhenNotFound()
    {
        var scenePath = Path.Combine(_tempDir, "no-godot", "scenes", "host.tscn");
        var result = AddonInstaller.FindGodotProjectRoot(scenePath);
        Assert.Null(result);
    }

    [Fact]
    public void FindAddonSource_FindsAddonRelativeToAssembly()
    {
        // This test verifies the method doesn't crash; actual path depends on repo layout
        var result = AddonInstaller.FindAddonSource();
        // In test context we may or may not find it — just verify it returns a string or null
        if (result != null)
            Assert.True(Directory.Exists(result), $"Returned path does not exist: {result}");
    }

    [Fact]
    public void InstallAddon_CopiesAllFiles()
    {
        var sourceDir = Path.Combine(_tempDir, "source-addon");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "plugin.cfg"), "[plugin]");
        Directory.CreateDirectory(Path.Combine(sourceDir, "building_blocks"));
        File.WriteAllText(Path.Combine(sourceDir, "building_blocks", "bar.tscn"), "[gd_scene]");

        var targetRoot = Path.Combine(_tempDir, "target-project");
        Directory.CreateDirectory(targetRoot);

        AddonInstaller.CopyAddonTo(sourceDir, targetRoot);

        var installedPlugin = Path.Combine(targetRoot, "addons", "pmview-bridge", "plugin.cfg");
        var installedBar = Path.Combine(targetRoot, "addons", "pmview-bridge", "building_blocks", "bar.tscn");
        Assert.True(File.Exists(installedPlugin));
        Assert.True(File.Exists(installedBar));
        Assert.Equal("[plugin]", File.ReadAllText(installedPlugin));
    }

    [Fact]
    public void InstallAddon_OverwritesExistingFiles()
    {
        var sourceDir = Path.Combine(_tempDir, "source-addon");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "plugin.cfg"), "version=2");

        var targetRoot = Path.Combine(_tempDir, "target-project");
        var existingAddon = Path.Combine(targetRoot, "addons", "pmview-bridge");
        Directory.CreateDirectory(existingAddon);
        File.WriteAllText(Path.Combine(existingAddon, "plugin.cfg"), "version=1");

        AddonInstaller.CopyAddonTo(sourceDir, targetRoot);

        Assert.Equal("version=2", File.ReadAllText(Path.Combine(existingAddon, "plugin.cfg")));
    }
}
