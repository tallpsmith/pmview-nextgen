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

    // --- --force behaviour ---

    [Fact]
    public void Scaffold_DefaultSkipsExistingMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var scenePath = Path.Combine(dir, "scenes", "main.tscn");
            File.WriteAllText(scenePath, "user-customised content");

            // Re-scaffold without force
            ProjectScaffolder.Scaffold(dir);
            var content = File.ReadAllText(scenePath);
            Assert.Equal("user-customised content", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_DefaultReturnsSkippedForExistingMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);

            var result = ProjectScaffolder.Scaffold(dir);
            Assert.Contains("scenes/main.tscn", result.Skipped);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ForceOverwritesExistingMainScene()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var scenePath = Path.Combine(dir, "scenes", "main.tscn");
            File.WriteAllText(scenePath, "user-customised content");

            ProjectScaffolder.Scaffold(dir, force: true);
            var content = File.ReadAllText(scenePath);
            Assert.DoesNotContain("user-customised content", content);
            Assert.Contains("[gd_scene", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ForceOverwritesExistingProjectGodot()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var marker = "[custom_section]\nmy_key=true\n";
            File.AppendAllText(Path.Combine(dir, "project.godot"), marker);

            ProjectScaffolder.Scaffold(dir, force: true);
            var content = File.ReadAllText(Path.Combine(dir, "project.godot"));
            Assert.DoesNotContain("my_key=true", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ForceOverwritesExistingCsproj()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var csproj = Directory.GetFiles(dir, "*.csproj")[0];
            File.AppendAllText(csproj, "<!-- user marker -->");

            ProjectScaffolder.Scaffold(dir, force: true);
            var content = File.ReadAllText(csproj);
            Assert.DoesNotContain("user marker", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ForceOverwritesExistingSln()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);
            var sln = Directory.GetFiles(dir, "*.sln")[0];
            File.AppendAllText(sln, "# user marker");

            ProjectScaffolder.Scaffold(dir, force: true);
            var content = File.ReadAllText(sln);
            Assert.DoesNotContain("user marker", content);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ReturnsCreatedForFreshProject()
    {
        var dir = CreateTempDir();
        try
        {
            var result = ProjectScaffolder.Scaffold(dir);
            Assert.Contains("project.godot", result.Created);
            Assert.Contains("scenes/main.tscn", result.Created);
            Assert.Empty(result.Skipped);
            Assert.Empty(result.Overwritten);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_ForceReturnsOverwrittenForExistingFiles()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);

            var result = ProjectScaffolder.Scaffold(dir, force: true);
            Assert.Contains("project.godot", result.Overwritten);
            Assert.Contains("scenes/main.tscn", result.Overwritten);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Scaffold_DefaultReturnsSkippedForAllExistingFiles()
    {
        var dir = CreateTempDir();
        try
        {
            ProjectScaffolder.Scaffold(dir);

            var result = ProjectScaffolder.Scaffold(dir);
            Assert.Contains("project.godot", result.Skipped);
            Assert.Contains("scenes/main.tscn", result.Skipped);
        }
        finally { Cleanup(dir); }
    }
}
