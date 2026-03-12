namespace PcpClient;

public record NamespaceNode(
    string Name,
    string FullPath,
    IReadOnlyList<NamespaceNode> Children,
    bool IsLeaf);

public static class NamespaceTreeBuilder
{
    public static IReadOnlyList<NamespaceNode> BuildTree(IReadOnlyList<string> metricNames)
    {
        if (metricNames.Count == 0)
            return Array.Empty<NamespaceNode>();

        var root = new Dictionary<string, object>();

        foreach (var name in metricNames)
        {
            var parts = name.Split('.');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == parts.Length - 1)
                {
                    // Leaf — only store if not already an intermediate node
                    if (!current.ContainsKey(part))
                        current[part] = null!;
                    // If it's already a dict (intermediate), leave it
                }
                else
                {
                    if (!current.TryGetValue(part, out var existing))
                    {
                        var next = new Dictionary<string, object>();
                        current[part] = next;
                        current = next;
                    }
                    else if (existing is Dictionary<string, object> existingDict)
                    {
                        current = existingDict;
                    }
                    else
                    {
                        // Was a leaf, now also needed as intermediate — promote to dict
                        var next = new Dictionary<string, object>();
                        current[part] = next;
                        current = next;
                    }
                }
            }
        }

        return BuildNodes(root, "");
    }

    private static IReadOnlyList<NamespaceNode> BuildNodes(
        Dictionary<string, object> dict, string prefix)
    {
        var nodes = new List<NamespaceNode>();

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            var fullPath = string.IsNullOrEmpty(prefix)
                ? kvp.Key
                : $"{prefix}.{kvp.Key}";

            if (kvp.Value is Dictionary<string, object> children)
            {
                nodes.Add(new NamespaceNode(
                    kvp.Key, fullPath,
                    BuildNodes(children, fullPath),
                    IsLeaf: false));
            }
            else
            {
                nodes.Add(new NamespaceNode(
                    kvp.Key, fullPath,
                    Array.Empty<NamespaceNode>(),
                    IsLeaf: true));
            }
        }

        return nodes;
    }
}
