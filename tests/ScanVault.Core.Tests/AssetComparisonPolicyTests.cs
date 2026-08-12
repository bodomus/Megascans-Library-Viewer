using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class AssetComparisonPolicyTests
{
    [Fact]
    public void ComparesTypedOverviewValuesAndDistinguishesMissingUnknownAndNotApplicable()
    {
        var left = Asset("left", "Surface", biome: null, resolution: null);
        var right = Asset("right", "Surface", biome: "Desert", resolution: new(4096, 4096));

        var result = AssetComparisonPolicy.Compare(left, right);

        Assert.Equal(ComparisonResult.Unknown, Row(result.Overview, "biome").Result);
        Assert.Equal(ComparisonResult.Unknown, Row(result.Overview, "resolution").Result);
        Assert.Equal(ComparisonResult.NotApplicable, Row(result.Overview, "lod-count").Result);
        Assert.Equal(ComparisonResult.Equal, Row(result.Overview, "has-fbx").Result);
        Assert.Equal(ComparisonValueKind.Missing, Row(result.Overview, "has-fbx").Left.Kind);
    }

    [Fact]
    public void MatchesVariantsLodsTexturesFilesAndIssuesIndependentlyOfOrder()
    {
        var left = Asset("left", "3D Asset", Content(
            variants:
            [
                new("Var2", [new(@"C:\Library\Left\Var2\mesh_LOD1.fbx", "mesh_LOD1.fbx", "Var2", 1, MeshFormat.Fbx)]),
                new("Var1", [new(@"C:\Library\Left\Var1\mesh_LOD0.fbx", "mesh_LOD0.fbx", "Var1", 0, MeshFormat.Fbx)])
            ],
            textures:
            [
                new(TextureSetKind.General, 4096,
                [
                    new(@"C:\Library\Left\rough.jpg", "rough.jpg", "Roughness", TextureMapType.Roughness, 4096, "jpg"),
                    new(@"C:\Library\Left\albedo.jpg", "albedo.jpg", "Albedo", TextureMapType.Albedo, 4096, "jpg")
                ])
            ],
            issues: [new(AssetContentIssueCode.MissingReference, "Missing reference", [@"C:\Library\Left\missing.bin"])]));
        var right = Asset("right", "3D Asset", Content(
            variants:
            [
                new("var1", [new(@"C:\Library\Right\Var1\other_LOD0.fbx", "other_LOD0.fbx", "var1", 0, MeshFormat.Fbx)]),
                new("var2", [new(@"C:\Library\Right\Var2\other_LOD1.fbx", "other_LOD1.fbx", "var2", 1, MeshFormat.Fbx)])
            ],
            textures:
            [
                new(TextureSetKind.General, 4096,
                [
                    new(@"C:\Library\Right\different_albedo.jpg", "different_albedo.jpg", "Albedo", TextureMapType.Albedo, 4096, "JPG"),
                    new(@"C:\Library\Right\different_rough.jpg", "different_rough.jpg", "Roughness", TextureMapType.Roughness, 4096, "JPG")
                ])
            ],
            issues: [new(AssetContentIssueCode.MissingReference, "Missing reference", [@"C:\Library\Right\missing.bin"])]));

        var result = AssetComparisonPolicy.Compare(left, right);

        Assert.All(result.VariantsAndLods, row => Assert.Equal(ComparisonResult.Equal, row.Result));
        Assert.All(result.TextureSets, row => Assert.Equal(ComparisonResult.Equal, row.Result));
        Assert.All(result.Files, row => Assert.Equal(ComparisonResult.Equal, row.Result));
        Assert.All(result.Issues, row => Assert.Equal(ComparisonResult.Equal, row.Result));
    }

    [Fact]
    public void RetainsDuplicateLogicalKeysAsAmbiguousRows()
    {
        var duplicate = new TextureSetInventory(TextureSetKind.General, 4096,
        [
            new(@"C:\Library\Left\a.jpg", "a.jpg", "Albedo", TextureMapType.Albedo, 4096, "jpg"),
            new(@"C:\Library\Left\b.jpg", "b.jpg", "Albedo", TextureMapType.Albedo, 4096, "jpg")
        ]);
        var left = Asset("left", "3D Asset", Content(textures: [duplicate]));
        var right = Asset("right", "3D Asset", Content(textures:
        [
            new(TextureSetKind.General, 4096,
            [new(@"C:\Library\Right\a.jpg", "a.jpg", "Albedo", TextureMapType.Albedo, 4096, "jpg")])
        ]));

        var result = AssetComparisonPolicy.Compare(left, right);

        Assert.Equal(2, result.TextureSets.Count);
        Assert.All(result.TextureSets, row => Assert.Equal(ComparisonResult.Ambiguous, row.Result));
        Assert.Equal(4, result.Summary.Ambiguous); // Two texture rows plus their two logical-file rows.
    }

    [Fact]
    public void DifferencesOnlyKeepsUnknownMissingAndAmbiguousButDropsEqual()
    {
        var rows = new[]
        {
            ComparisonRow("equal", ComparisonResult.Equal),
            ComparisonRow("unknown", ComparisonResult.Unknown),
            ComparisonRow("only-left", ComparisonResult.OnlyLeft),
            ComparisonRow("ambiguous", ComparisonResult.Ambiguous)
        };

        var filtered = AssetComparisonPolicy.FilterDifferences(rows, true);

        Assert.Equal(["unknown", "only-left", "ambiguous"], filtered.Select(static row => row.Key));
    }

    [Fact]
    public void RejectsSameNormalizedAssetIdentity()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            AssetComparisonPolicy.Compare(Asset(" Same ", "3D Asset"), Asset("same", "3D Asset")));

        Assert.Contains("cannot be compared with itself", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LargeInventoryComparisonRemainsDeterministicAndDoesNotReadFiles()
    {
        var left = Asset("left", "3D Asset", LargeContent(@"C:\does-not-exist\left", 2_000));
        var right = Asset("right", "3D Asset", LargeContent(@"C:\does-not-exist\right", 2_000));

        var first = AssetComparisonPolicy.Compare(left, right);
        var second = AssetComparisonPolicy.Compare(left, right);

        Assert.Equal(2_000, first.Files.Count);
        Assert.Equal(first.Files.Select(static row => row.Key), second.Files.Select(static row => row.Key));
        Assert.All(first.Files, row => Assert.Equal(ComparisonResult.Equal, row.Result));
    }

    private static AssetSummary Asset(
        string id,
        string type,
        AssetContentInventory? content = null,
        string? biome = "Forest",
        ImageResolution? resolution = null) =>
        new(
            id,
            $"Asset {id}",
            type,
            $@"C:\Library\{id.Trim()}",
            $@"C:\Library\{id.Trim()}\{id.Trim()}.json",
            null,
            null,
            biome,
            "Global",
            null,
            resolution,
            512,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch)
        {
            Content = content ?? AssetContentInventory.Empty
        };

    private static AssetContentInventory Content(
        IReadOnlyList<MeshVariantInventory>? variants = null,
        IReadOnlyList<TextureSetInventory>? textures = null,
        IReadOnlyList<AssetContentIssue>? issues = null) =>
        new(variants ?? [], textures ?? [], [], AssetCompletenessStatus.Complete, issues ?? []);

    private static AssetContentInventory LargeContent(string root, int count) =>
        Content(textures:
        [
            new(TextureSetKind.General, 4096, Enumerable.Range(0, count)
                .Select(index => new TextureComponentEntry(
                    Path.Combine(root, $"texture-{index}.jpg"),
                    $"texture-{index}.jpg",
                    $"Other{index}",
                    TextureMapType.Unknown,
                    4096,
                    $"jpg-{index}"))
                .ToArray())
        ]);

    private static ComparisonRow Row(IReadOnlyList<ComparisonRow> rows, string key) =>
        Assert.Single(rows, row => row.Key == key);

    private static ComparisonRow ComparisonRow(string key, ComparisonResult result) =>
        new(key, key, ComparisonValue.Present("value", "value"), ComparisonValue.Present("value", "value"), result);
}
