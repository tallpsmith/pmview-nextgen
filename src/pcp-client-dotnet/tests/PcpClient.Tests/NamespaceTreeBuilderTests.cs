using Xunit;

namespace PcpClient.Tests;

public class NamespaceTreeBuilderTests
{
    [Fact]
    public void BuildTree_EmptyList_ReturnsEmptyRoot()
    {
        var root = NamespaceTreeBuilder.BuildTree(Array.Empty<string>());
        Assert.Empty(root);
    }

    [Fact]
    public void BuildTree_SingleMetric_CreatesPathToLeaf()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[] { "disk.dev.read" });
        Assert.Single(root);
        Assert.Equal("disk", root[0].Name);
        Assert.False(root[0].IsLeaf);
        Assert.Single(root[0].Children);
        Assert.Equal("dev", root[0].Children[0].Name);
        Assert.Single(root[0].Children[0].Children);
        Assert.Equal("read", root[0].Children[0].Children[0].Name);
        Assert.True(root[0].Children[0].Children[0].IsLeaf);
        Assert.Equal("disk.dev.read", root[0].Children[0].Children[0].FullPath);
    }

    [Fact]
    public void BuildTree_SharedPrefix_MergesIntermediateNodes()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "disk.dev.read",
            "disk.dev.write",
            "disk.all.total"
        });
        Assert.Single(root);
        var disk = root[0];
        Assert.Equal(2, disk.Children.Count);
        var dev = disk.Children.First(c => c.Name == "dev");
        Assert.Equal(2, dev.Children.Count);
        Assert.All(dev.Children, c => Assert.True(c.IsLeaf));
    }

    [Fact]
    public void BuildTree_MultipleTopLevel_CreatesSiblings()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "disk.dev.read",
            "kernel.all.load"
        });
        Assert.Equal(2, root.Count);
        Assert.Contains(root, n => n.Name == "disk");
        Assert.Contains(root, n => n.Name == "kernel");
    }

    [Fact]
    public void BuildTree_TopLevelLeaf_HandledCorrectly()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[] { "uptime" });
        Assert.Single(root);
        Assert.Equal("uptime", root[0].Name);
        Assert.True(root[0].IsLeaf);
        Assert.Equal("uptime", root[0].FullPath);
    }

    [Fact]
    public void BuildTree_SortedAlphabetically()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[]
        {
            "zebra.metric",
            "alpha.metric",
            "middle.metric"
        });
        Assert.Equal("alpha", root[0].Name);
        Assert.Equal("middle", root[1].Name);
        Assert.Equal("zebra", root[2].Name);
    }

    [Fact]
    public void BuildTree_DeepNesting_PreservesFullPath()
    {
        var root = NamespaceTreeBuilder.BuildTree(new[] { "a.b.c.d.e.f" });
        var node = root[0];
        for (int i = 0; i < 5; i++)
            node = node.Children[0];
        Assert.True(node.IsLeaf);
        Assert.Equal("a.b.c.d.e.f", node.FullPath);
    }
}
