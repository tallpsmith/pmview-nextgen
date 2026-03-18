namespace PcpClient;

public record NamespaceNode(
    string Name,
    string FullPath,
    IReadOnlyList<NamespaceNode> Children,
    bool IsLeaf);

public static class NamespaceTreeBuilder
{
    /// <summary>Intermediate trie node — null value means leaf, non-null means subtree.</summary>
    private class TrieNode : Dictionary<string, TrieNode?>;

    public static IReadOnlyList<NamespaceNode> BuildTree(IReadOnlyList<string> metricNames)
    {
        if (metricNames.Count == 0)
            return [];

        var root = new TrieNode();

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
                    current.TryAdd(part, null);
                }
                else
                {
                    if (!current.TryGetValue(part, out var existing) || existing is null)
                    {
                        // New intermediate node, or promote leaf to intermediate
                        var next = new TrieNode();
                        current[part] = next;
                        current = next;
                    }
                    else
                    {
                        current = existing;
                    }
                }
            }
        }

        return BuildNodes(root, "");
    }

    private static IReadOnlyList<NamespaceNode> BuildNodes(TrieNode node, string prefix)
    {
        var nodes = new List<NamespaceNode>();

        foreach (var kvp in node.OrderBy(k => k.Key))
        {
            var fullPath = string.IsNullOrEmpty(prefix)
                ? kvp.Key
                : $"{prefix}.{kvp.Key}";

            if (kvp.Value is { } children)
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
                    [],
                    IsLeaf: true));
            }
        }

        return nodes;
    }
}
