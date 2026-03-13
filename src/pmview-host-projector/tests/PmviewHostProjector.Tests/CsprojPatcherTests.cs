using Xunit;
using PmviewHostProjector;

namespace PmviewHostProjector.Tests;

public class CsprojPatcherTests : IDisposable
{
    private readonly string _tempDir;

    public CsprojPatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"csproj-patcher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static readonly string MinimalGodotCsproj = """
        <Project Sdk="Godot.NET.Sdk/4.6.1">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <RootNamespace>MyProject</RootNamespace>
            <EnableDynamicLoading>true</EnableDynamicLoading>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private string WriteCsproj(string content, string name = "MyProject.csproj")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AddLibraryReferences_AddsReferenceEntriesForAllDlls()
    {
        var csprojPath = WriteCsproj(MinimalGodotCsproj);
        var dllNames = new[] { "PcpClient", "PcpGodotBridge", "Tomlyn" };

        CsprojPatcher.AddLibraryReferences(csprojPath, dllNames);

        var content = File.ReadAllText(csprojPath);
        foreach (var dll in dllNames)
        {
            Assert.Contains($"Include=\"{dll}\"", content);
            Assert.Contains($"addons/pmview-bridge/lib/{dll}.dll", content);
        }
    }

    [Fact]
    public void AddLibraryReferences_IsIdempotent()
    {
        var csprojPath = WriteCsproj(MinimalGodotCsproj);
        var dllNames = new[] { "PcpClient", "PcpGodotBridge", "Tomlyn" };

        CsprojPatcher.AddLibraryReferences(csprojPath, dllNames);
        CsprojPatcher.AddLibraryReferences(csprojPath, dllNames);

        var content = File.ReadAllText(csprojPath);
        var count = CountOccurrences(content, "Include=\"PcpClient\"");
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddLibraryReferences_PreservesExistingPropertyGroups()
    {
        var csprojPath = WriteCsproj(MinimalGodotCsproj);
        var dllNames = new[] { "PcpClient" };

        CsprojPatcher.AddLibraryReferences(csprojPath, dllNames);

        var content = File.ReadAllText(csprojPath);
        Assert.Contains("Godot.NET.Sdk/4.6.1", content);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", content);
        Assert.Contains("<RootNamespace>MyProject</RootNamespace>", content);
    }

    [Fact]
    public void AddLibraryReferences_PreservesExistingPackageReferences()
    {
        var csprojWithPackages = """
            <Project Sdk="Godot.NET.Sdk/4.6.1">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="SomePackage" Version="1.0" />
              </ItemGroup>
            </Project>
            """;
        var csprojPath = WriteCsproj(csprojWithPackages);
        var dllNames = new[] { "PcpClient" };

        CsprojPatcher.AddLibraryReferences(csprojPath, dllNames);

        var content = File.ReadAllText(csprojPath);
        Assert.Contains("Include=\"SomePackage\"", content);
        Assert.Contains("Include=\"PcpClient\"", content);
    }

    [Fact]
    public void FindTargetCsproj_FindsCsprojInGodotRoot()
    {
        var godotRoot = Path.Combine(_tempDir, "my-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");
        File.WriteAllText(Path.Combine(godotRoot, "MyProject.csproj"), MinimalGodotCsproj);

        var result = CsprojPatcher.FindTargetCsproj(godotRoot);

        Assert.NotNull(result);
        Assert.EndsWith(".csproj", result);
    }

    [Fact]
    public void FindTargetCsproj_ReturnsNullWhenNoCsproj()
    {
        var godotRoot = Path.Combine(_tempDir, "empty-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");

        var result = CsprojPatcher.FindTargetCsproj(godotRoot);

        Assert.Null(result);
    }

    [Fact]
    public void FindTargetCsproj_IgnoresCsprojInSubdirectories()
    {
        var godotRoot = Path.Combine(_tempDir, "my-project");
        Directory.CreateDirectory(godotRoot);
        File.WriteAllText(Path.Combine(godotRoot, "project.godot"), "");
        File.WriteAllText(Path.Combine(godotRoot, "MyProject.csproj"), MinimalGodotCsproj);

        // A csproj in a subdirectory should be ignored
        var subDir = Path.Combine(godotRoot, "addons", "other");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Other.csproj"), MinimalGodotCsproj);

        var result = CsprojPatcher.FindTargetCsproj(godotRoot);

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(godotRoot, "MyProject.csproj"), result);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
