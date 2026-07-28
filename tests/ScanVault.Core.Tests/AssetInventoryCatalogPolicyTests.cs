using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class AssetInventoryCatalogPolicyTests
{
    [Fact]
    public void SearchFilterAndSortComposeOverInventoryFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "inventory-catalog");
        var complete = TestAssetFactory.Create("complete", root) with
        {
            Content = Inventory(AssetCompletenessStatus.Complete, "Var2", 2, TextureSetKind.Billboard, TextureMapType.Albedo)
        };
        var ambiguous = TestAssetFactory.Create("ambiguous", root) with
        {
            Content = Inventory(AssetCompletenessStatus.Ambiguous, "Var1", 0, TextureSetKind.Atlas, TextureMapType.Normal)
        };

        Assert.True(AssetFiltering.MatchesSearch(complete, "LOD2"));
        Assert.True(AssetFiltering.MatchesSearch(ambiguous, "Normal"));
        Assert.True(AssetFiltering.MatchesInventoryFilter(complete, AssetInventoryFilter.HasFbx | AssetInventoryFilter.HasBillboard));
        Assert.False(AssetFiltering.MatchesInventoryFilter(ambiguous, AssetInventoryFilter.Complete));
        Assert.Equal(["complete", "ambiguous"], AssetSorting.Apply([ambiguous, complete], AssetSortMode.Completeness).Select(static asset => asset.Id));
        Assert.Equal(["complete", "ambiguous"], AssetSorting.Apply([ambiguous, complete], AssetSortMode.LodCountDescending).Select(static asset => asset.Id));
    }

    private static AssetContentInventory Inventory(
        AssetCompletenessStatus status,
        string variant,
        int lod,
        TextureSetKind kind,
        TextureMapType map) => new(
            [new(variant, Enumerable.Range(0, lod + 1).Select(level => new MeshLodEntry($"{variant}_LOD{level}.fbx", $"{variant}_LOD{level}.fbx", variant, level, MeshFormat.Fbx)).ToArray())],
            [new(kind, 4096, [new($"asset_4K_{map}.jpg", $"asset_4K_{map}.jpg", map.ToString(), map, 4096, "JPG")])],
            [], status, status == AssetCompletenessStatus.Ambiguous
                ? [new(AssetContentIssueCode.ConflictingName, "Conflicting inventory name.", [])]
                : []);
}
