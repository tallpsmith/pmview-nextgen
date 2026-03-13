using System.Xml.Linq;

namespace PmviewHostProjector;

/// <summary>
/// Patches a Godot project's .csproj to reference bundled addon DLLs.
/// </summary>
public static class CsprojPatcher
{
    private const string AddonLibPath = "addons/pmview-bridge/lib";

    /// <summary>
    /// Finds the .csproj file in the root of a Godot project directory.
    /// Only looks at the root level (not subdirectories).
    /// Returns null if no .csproj is found.
    /// </summary>
    public static string? FindTargetCsproj(string godotProjectRoot)
    {
        var csprojFiles = Directory.GetFiles(godotProjectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
        return csprojFiles.Length > 0 ? csprojFiles[0] : null;
    }

    /// <summary>
    /// Adds Reference entries for the given DLL names, pointing at
    /// addons/pmview-bridge/lib/{name}.dll. Idempotent — won't duplicate
    /// entries on repeated calls.
    /// </summary>
    public static void AddLibraryReferences(string csprojPath, string[] dllNames)
    {
        var doc = XDocument.Load(csprojPath);
        var project = doc.Root!;
        XNamespace ns = project.GetDefaultNamespace();

        // Find existing Reference includes to avoid duplicates
        var existingReferences = project
            .Descendants(ns + "Reference")
            .Select(r => r.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToHashSet();

        // Collect references to add
        var newReferences = dllNames
            .Where(name => !existingReferences.Contains(name))
            .ToList();

        if (newReferences.Count == 0)
            return;

        // Find or create an ItemGroup for our references
        var itemGroup = new XElement(ns + "ItemGroup",
            new XComment(" pmview-bridge addon libraries "));

        foreach (var name in newReferences)
        {
            var hintPath = $"{AddonLibPath}/{name}.dll";
            itemGroup.Add(new XElement(ns + "Reference",
                new XAttribute("Include", name),
                new XElement(ns + "HintPath", hintPath)));
        }

        project.Add(itemGroup);
        doc.Save(csprojPath);
    }
}
