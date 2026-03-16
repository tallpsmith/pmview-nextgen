using Xunit;
using PmviewHostProjector.Scaffolding;

namespace PmviewHostProjector.Tests.Scaffolding;

public class ProjectScaffolderTests
{
    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pmview-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private void Cleanup(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    [Fact]
    public void Scaffold_CreatesProjectGodot()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            Assert.True(File.Exists(Path.Combine(dir, "project.godot")));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectGodot_HasCorrectMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("run/main_scene=\"res://scenes/main.tscn\"", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectGodot_HasCSharpFeature()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("C#", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesCsproj()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var csprojFiles = Directory.GetFiles(dir, "*.csproj");
            Assert.Single(csprojFiles);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_Csproj_TargetsNet8()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var csproj = Directory.GetFiles(dir, "*.csproj")[0];
            var content = File.ReadAllText(csproj);
            Assert.Contains("net8.0", content);
            Assert.Contains("Godot.NET.Sdk", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesSln()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var slnFiles = Directory.GetFiles(dir, "*.sln");
            Assert.Single(slnFiles);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_CreatesMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            Assert.True(File.Exists(Path.Combine(dir, "scenes", "main.tscn")));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ProjectNameDerivedFromDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"my-cool-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("my-cool-project", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_Idempotent_DoesNotClobberExistingProjectGodot()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var marker = "[custom_section]\nmy_key=true\n";
            File.AppendAllText(Path.Combine(dir, "project.godot"), marker);

            // Run again
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.Contains("my_key=true", content);
        }
        finally { Cleanup(dir); }
    }
}
