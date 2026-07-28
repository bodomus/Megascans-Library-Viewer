using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class AssetContentAnalyzerTests
{
    [Fact]
    public void ParsesArbitraryVariantLodFormatsAndKeepsUnexpectedMeshUnclassified()
    {
        var inventory = Analyze("3D Plant",
            "Var10/Var10_LOD12.abc",
            "var2/var2_lod3.FBX",
            "unexpected.fbx");

        Assert.Equal(["Var2", "Var10"], inventory.Variants.Select(static value => value.Name));
        Assert.Contains(inventory.Variants.SelectMany(static value => value.Meshes), static mesh => mesh.Lod == 12 && mesh.Format == MeshFormat.Abc);
        Assert.Single(inventory.UnclassifiedFiles);
    }

    [Theory]
    [InlineData("asset_BaseColor.jpg", TextureMapType.Albedo)]
    [InlineData("asset_Diffuse.jpg", TextureMapType.Albedo)]
    [InlineData("asset_Alpha.png", TextureMapType.Opacity)]
    [InlineData("asset_AmbientOcclusion.tif", TextureMapType.AmbientOcclusion)]
    [InlineData("asset_Height.exr", TextureMapType.Displacement)]
    public void NormalizesAliasesWithoutLosingRawToken(string fileName, TextureMapType expected)
    {
        Assert.True(AssetContentAnalyzer.TryNormalizeMap(fileName, out var map, out var raw));
        Assert.Equal(expected, map);
        Assert.NotEmpty(raw);
    }

    [Fact]
    public void FolderContextWinsAndFlatBillboardPrefixIsRecognized()
    {
        var inventory = Analyze("Atlas",
            "[Textures]/[Atlas]/asset_4K_Albedo.jpg",
            "Textures/BILLBOARD/asset_2K_Normal.exr",
            "Billboard_1K_Opacity.png");

        Assert.Contains(inventory.TextureSets, static set => set.Kind == TextureSetKind.Atlas && set.Resolution == 4096);
        Assert.Contains(inventory.TextureSets, static set => set.Kind == TextureSetKind.Billboard && set.Resolution == 2048);
        Assert.Contains(inventory.TextureSets, static set => set.Kind == TextureSetKind.Billboard && set.Resolution == 1024);
    }

    [Fact]
    public void CompletePlantUsesConfirmedAlternativesAndDoesNotRequireBillboard()
    {
        var inventory = Analyze("3D Plant",
            "Var1/Var1_LOD0.fbx",
            "Textures/Atlas/a_4K_Albedo.jpg",
            "Textures/Atlas/a_4K_Bump.jpg",
            "Textures/Atlas/a_4K_Gloss.jpg",
            "Textures/Atlas/a_4K_Opacity.jpg");

        Assert.Equal(AssetCompletenessStatus.Complete, inventory.Completeness);
        Assert.False(inventory.HasBillboard);
    }

    [Fact]
    public void AbcOnlyAssetIsUsableButNeverComplete()
    {
        var inventory = Analyze("3D Asset",
            "Var1/Var1_LOD0.abc",
            "a_4K_Albedo.jpg",
            "a_4K_Normal.jpg",
            "a_4K_Roughness.jpg");

        Assert.Equal(AssetCompletenessStatus.Usable, inventory.Completeness);
    }

    [Theory]
    [InlineData("3D Plant", "Var1/Var1_LOD0.fbx", "a_4K_Albedo.jpg", "a_4K_Normal.jpg", "a_4K_Roughness.jpg")]
    [InlineData("3D Asset", "a_4K_Albedo.jpg", "a_4K_Normal.jpg", "a_4K_Roughness.jpg", null)]
    public void MissingCriticalContentIsExplicit(string assetType, string first, string second, string third, string? fourth)
    {
        var paths = new[] { first, second, third, fourth }.Where(static value => value is not null).Cast<string>().ToArray();
        var inventory = Analyze(assetType, paths);

        Assert.Equal(AssetCompletenessStatus.MissingCriticalFiles, inventory.Completeness);
        Assert.Contains(inventory.Issues, static issue => issue.Code == AssetContentIssueCode.MissingCriticalFile);
    }

    [Fact]
    public void DuplicateCandidatesArePreservedAndMakeAssetAmbiguous()
    {
        var inventory = Analyze("3D Asset",
            "A/Var1_LOD0.fbx",
            "B/var1_lod0.FBX",
            "a_4K_Albedo.jpg");

        Assert.Equal(2, inventory.MeshCount);
        Assert.Equal(AssetCompletenessStatus.Ambiguous, inventory.Completeness);
        Assert.Contains(inventory.Issues, static issue => issue.Code == AssetContentIssueCode.DuplicateMesh && issue.Paths.Count == 2);
    }

    [Fact]
    public void UnknownAssetKindDoesNotGuessACompletenessProfile()
    {
        var inventory = Analyze("Unknown", "a_4K_Albedo.jpg");
        Assert.Equal(AssetCompletenessStatus.Unknown, inventory.Completeness);
    }

    private static AssetContentInventory Analyze(string assetType, params string[] relativePaths)
    {
        var root = Path.Combine(Path.GetTempPath(), "inventory-fixture");
        var files = relativePaths.Select(path => new AssetContentFileCandidate(Path.Combine(root, path), path)).ToArray();
        return AssetContentAnalyzer.Analyze(assetType, files);
    }
}
