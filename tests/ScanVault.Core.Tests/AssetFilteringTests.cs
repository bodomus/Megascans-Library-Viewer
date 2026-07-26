using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class AssetFilteringTests
{
    [Fact]
    public void FolderFilterIncludesSelectedFolderAndDescendantsNotPrefixSiblings()
    {
        var root = Path.Combine(Path.GetTempPath(), "Library");
        var selected = Path.Combine(root, "Nature");
        var direct = TestAssetFactory.Create("direct", selected);
        var descendant = TestAssetFactory.Create("child", Path.Combine(selected, "Forest"));
        var prefixSibling = TestAssetFactory.Create("sibling", Path.Combine(root, "Nature-old"));

        Assert.True(AssetFiltering.IsInFolder(direct, selected));
        Assert.True(AssetFiltering.IsInFolder(descendant, selected));
        Assert.False(AssetFiltering.IsInFolder(prefixSibling, selected));
    }

    [Fact]
    public void FolderTreeContainsOnlyAssetAncestors()
    {
        var root = Path.Combine(Path.GetTempPath(), "Library");
        var assets = new[]
        {
            TestAssetFactory.Create("a", Path.Combine(root, "Nature", "Forest")),
            TestAssetFactory.Create("b", Path.Combine(root, "Nature", "Rock"))
        };

        var tree = AssetFiltering.BuildFolderTree(root, assets);

        var rootNode = Assert.Single(tree);
        var nature = Assert.Single(rootNode.Children);
        Assert.Equal("Nature", nature.Name);
        Assert.Equal(["Forest", "Rock"], nature.Children.Select(static node => node.Name));
    }
}
