using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class AssetCatalogPolicyTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ScanVault.Core.Tests",
        Guid.NewGuid().ToString("N"));

    public AssetCatalogPolicyTests() => Directory.CreateDirectory(root);

    [Fact]
    public void FolderCountsIncludeDescendantsAndRootTotal()
    {
        var nested = Path.Combine(root, "3D", "Rock");
        var sibling = Path.Combine(root, "Atlas");
        var assets = new[]
        {
            Create("a", "A", nested),
            Create("b", "B", nested),
            Create("c", "C", sibling)
        };

        var tree = AssetFiltering.BuildFolderTree(root, assets);

        var rootNode = Assert.Single(tree);
        Assert.Equal(3, rootNode.AssetCount);
        Assert.Equal(2, Assert.Single(rootNode.Children, node => node.Name == "3D").AssetCount);
        Assert.Equal(1, Assert.Single(rootNode.Children, node => node.Name == "Atlas").AssetCount);
    }

    [Fact]
    public void SearchIncludesAllNormalizedCatalogFields()
    {
        var asset = Create("AbC", "Moss Wall", root) with
        {
            AssetType = "3D Asset",
            Categories = ["brick"],
            Tags = [new AssetTag(AssetTagKind.Theme, "ancient")],
            Biome = "forest",
            Region = "Asia"
        };

        foreach (var term in new[] { "moss", "abc", "3d", "brick", "ancient", "forest", "asia" })
        {
            Assert.True(AssetFiltering.MatchesSearch(asset, term), term);
        }
    }

    [Fact]
    public void EverySortModeIsDeterministicAndResolutionMissingIsLast()
    {
        var older = DateTimeOffset.UnixEpoch;
        var newer = older.AddDays(1);
        var assets = new[]
        {
            Create("b", "Alpha", root) with
            {
                AssetType = "Surface",
                MaxResolution = new ImageResolution(2048, 2048),
                LastWriteTimeUtc = newer
            },
            Create("a", "Alpha", root) with
            {
                AssetType = "Atlas",
                MaxResolution = new ImageResolution(4096, 4096),
                LastWriteTimeUtc = older
            },
            Create("c", "Zulu", root) with
            {
                AssetType = "Surface",
                MaxResolution = null,
                LastWriteTimeUtc = newer
            }
        };

        Assert.Equal(["a", "b", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.NameAscending)));
        Assert.Equal(["c", "a", "b"], Ids(AssetSorting.Apply(assets, AssetSortMode.NameDescending)));
        Assert.Equal(["a", "b", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.TypeAscending)));
        Assert.Equal(["a", "b", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.ResolutionDescending)));
        Assert.Equal(["b", "a", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.ResolutionAscending)));
        Assert.Equal(["b", "c", "a"], Ids(AssetSorting.Apply(assets, AssetSortMode.RecentlyModified)));
        Assert.Equal(["a", "b", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.OldestModified)));
        Assert.Equal(["a", "b", "c"], Ids(AssetSorting.Apply(assets, AssetSortMode.AssetIdAscending)));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string[] Ids(IReadOnlyList<AssetSummary> assets) =>
        assets.Select(static asset => asset.Id).ToArray();

    private static AssetSummary Create(string id, string name, string folder) =>
        new(
            id,
            name,
            "Surface",
            folder,
            Path.Combine(folder, $"{id}.json"),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch);
}
