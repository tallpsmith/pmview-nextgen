using Xunit;
using PmviewHostProjector;

namespace PmviewHostProjector.Tests;

public class LibraryBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public LibraryBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"libbuilder-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FindRepoRoot_FindsDirectoryWithSolutionFile()
    {
        var result = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);

        Assert.NotNull(result);
        Assert.True(File.Exists(Path.Combine(result, "PmviewHostProjector.sln"))
                 || File.Exists(Path.Combine(result, "pmview-nextgen.sln"))
                 || Directory.Exists(Path.Combine(result, "src")),
            $"Expected repo root markers not found at: {result}");
    }

    [Fact]
    public void FindRepoRoot_ReturnsNullFromUnrelatedPath()
    {
        var result = LibraryBuilder.FindRepoRoot("/tmp");
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void PublishLibraries_ProducesExpectedDlls()
    {
        var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        var outputDir = Path.Combine(_tempDir, "lib");

        LibraryBuilder.PublishLibraries(repoRoot, outputDir);

        Assert.True(File.Exists(Path.Combine(outputDir, "PcpClient.dll")),
            "PcpClient.dll not found in publish output");
        Assert.True(File.Exists(Path.Combine(outputDir, "PcpGodotBridge.dll")),
            "PcpGodotBridge.dll not found in publish output");
        Assert.True(File.Exists(Path.Combine(outputDir, "Tomlyn.dll")),
            "Tomlyn.dll not found in publish output");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void PublishLibraries_DoesNotIncludeTestAssemblies()
    {
        var repoRoot = LibraryBuilder.FindRepoRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);

        var outputDir = Path.Combine(_tempDir, "lib");

        LibraryBuilder.PublishLibraries(repoRoot, outputDir);

        var dlls = Directory.GetFiles(outputDir, "*.dll")
            .Select(Path.GetFileName)
            .ToList();
        Assert.DoesNotContain("PcpClient.Tests.dll", dlls);
        Assert.DoesNotContain("PcpGodotBridge.Tests.dll", dlls);
    }
}
