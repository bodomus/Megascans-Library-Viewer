using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class SmartCollectionPolicyTests
{
    [Fact]
    public void MissingMeshBuiltInMatchesAssetsWithoutFbxOrAbc()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Library", "Rocks");
        var missingMesh = TestAssetFactory.Create("missing", folder) with
        {
            Content = new([], [], [], AssetCompletenessStatus.MissingCriticalFiles, [])
        };
        var hasFbx = TestAssetFactory.Create("fbx", folder) with
        {
            Content = new(
                [new MeshVariantInventory("default", [new MeshLodEntry("mesh.fbx", "mesh.fbx", "default", 0, MeshFormat.Fbx)])],
                [],
                [],
                AssetCompletenessStatus.Complete,
                [])
        };
        var collection = SmartCollectionPolicy.BuiltIns.Single(static item => item.Id == "builtin-missing-mesh");

        Assert.True(SmartCollectionPolicy.Matches(missingMesh, collection.Definition, null, null));
        Assert.False(SmartCollectionPolicy.Matches(hasFbx, collection.Definition, null, null));
    }

    [Fact]
    public void SpecificFolderStoresRelativePathAndFiltersWithinCurrentLibraryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ScanVault.Core.Tests-{Guid.NewGuid():N}");
        var selectedFolder = Path.Combine(root, "Nature", "Forest");
        Directory.CreateDirectory(selectedFolder);
        var inside = TestAssetFactory.Create("inside", selectedFolder);
        var outside = TestAssetFactory.Create("outside", Path.Combine(root, "Nature", "Rock"));
        var definition = SmartCollectionPolicy.FromUiState(
            string.Empty,
            AssetInventoryFilter.None,
            SmartCollectionFolderScope.SpecificFolder,
            root,
            selectedFolder,
            AssetSortMode.NameAscending,
            false);

        Assert.Equal(Path.Combine("Nature", "Forest"), definition.RelativeFolderPath);
        Assert.True(SmartCollectionPolicy.Matches(inside, definition, root, null));
        Assert.False(SmartCollectionPolicy.Matches(outside, definition, root, null));
        Directory.Delete(root, recursive: true);
    }
}
