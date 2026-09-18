using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class GlobalAssetSearchPolicyTests
{
    // Unit test: plain queries match a complete token without case sensitivity.
    [Fact]
    public void PlainQueryMatchesWholeTokenIgnoringCase()
    {
        var asset = TestAssetFactory.Create("wood", Path.GetTempPath()) with { Name = "Wooden Sticks and Twigs" };

        var match = Assert.Single(GlobalAssetSearchPolicy.Search([asset], "WOODEN"));

        Assert.Equal("Name", match.Field);
    }

    // Unit test: plain tokens use non-alphanumeric separators in names and folder paths, and match typed tags.
    [Fact]
    public void PlainQueryMatchesTokenAcrossSupportedSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "Dining_Table_4K");
        var assets = new[]
        {
            TestAssetFactory.Create("name", Path.GetTempPath()) with { Name = "Wooden Table" },
            TestAssetFactory.Create("path", root) with { Name = "Chair" },
            TestAssetFactory.Create("tag", Path.GetTempPath()) with
            {
                Name = "Chair",
                Tags = [new AssetTag(AssetTagKind.Descriptive, "table")]
            }
        };

        Assert.Equal(["name", "path", "tag"], GlobalAssetSearchPolicy.Search(assets, "table").Select(static match => match.Asset.Id));
    }

    // Unit test: plain tokens do not match partial words.
    [Theory]
    [InlineData("vegetable")]
    [InlineData("tabletop")]
    public void PlainQueryDoesNotMatchPartialWord(string name)
    {
        var asset = TestAssetFactory.Create("partial", Path.GetTempPath()) with { Name = name };

        Assert.Empty(GlobalAssetSearchPolicy.Search([asset], "Table"));
    }

    // Unit test: multiword plain queries require consecutive complete tokens.
    [Fact]
    public void MultiwordPlainQueryMatchesConsecutiveTokens()
    {
        var asset = TestAssetFactory.Create("dining", Path.GetTempPath()) with { Name = "Wooden_Table_4K" };

        Assert.Single(GlobalAssetSearchPolicy.Search([asset], "wooden table"));
        Assert.Empty(GlobalAssetSearchPolicy.Search([asset], "table wooden"));
    }

    // Unit test: star wildcard spans any number of characters across a whole field.
    [Fact]
    public void StarWildcardMatchesExpectedName()
    {
        var asset = TestAssetFactory.Create("wood", Path.GetTempPath()) with { Name = "Wooden Sticks and Twigs" };

        Assert.Single(GlobalAssetSearchPolicy.Search([asset], "*Wooden*Twig*"));
    }

    // Unit test: explicit star wildcards retain whole-field substring behavior.
    [Fact]
    public void StarWildcardMatchesPartialWord()
    {
        var asset = TestAssetFactory.Create("vegetable", Path.GetTempPath()) with { Name = "vegetable" };

        Assert.Single(GlobalAssetSearchPolicy.Search([asset], "*Table*"));
    }

    // Unit test: underscore wildcard matches exactly one character.
    [Fact]
    public void UnderscoreWildcardDoesNotConsumeExtraCharacters()
    {
        var assets = new[]
        {
            TestAssetFactory.Create("one", Path.GetTempPath()) with { Name = "VAR1" },
            TestAssetFactory.Create("two", Path.GetTempPath()) with { Name = "VAR2" },
            TestAssetFactory.Create("ten", Path.GetTempPath()) with { Name = "VAR10" }
        };

        Assert.Equal(["one", "two"], GlobalAssetSearchPolicy.Search(assets, "VAR_").Select(static match => match.Asset.Id));
    }

    // Unit test: regex metacharacters other than supported wildcards remain literal.
    [Fact]
    public void RegexMetacharactersAreLiteralInsideWildcardQuery()
    {
        var asset = TestAssetFactory.Create("literal", Path.GetTempPath()) with { Name = "prefix .+()[]?^$ suffix" };

        Assert.Single(GlobalAssetSearchPolicy.Search([asset], "*.+()[]?^$*"));
    }

    // Unit test: empty queries leave global-search mode without returning every asset.
    [Fact]
    public void EmptyQueryReturnsNoMatches()
    {
        Assert.Empty(GlobalAssetSearchPolicy.Search([TestAssetFactory.Create("one", Path.GetTempPath())], "  "));
    }

    // Unit test: indexed IDs, paths, tags, variants and file names report deterministic reasons.
    [Fact]
    public void SearchCoversIndexedMetadataAndContentFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "Forest");
        var asset = TestAssetFactory.Create("asset-42", root) with
        {
            Name = "Wooden Kit",
            Tags = [new AssetTag(AssetTagKind.Descriptive, "gnarled")],
            Content = new(
                [new("VAR3", [new(Path.Combine(root, "wood_LOD1.fbx"), "wood_LOD1.fbx", "VAR3", 1, MeshFormat.Fbx)])],
                [new(TextureSetKind.General, 4096, [new(Path.Combine(root, "wood_Normal.png"), "wood_Normal.png", "Normal", TextureMapType.Normal, 4096, "png")])],
                [], AssetCompletenessStatus.Complete, [])
        };

        Assert.Equal("Asset ID", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "asset-42")).Field);
        Assert.Equal("Folder", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "Forest")).Field);
        Assert.Equal("Descriptive tag", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "gnarled")).Field);
        Assert.Equal("Variant", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "VAR3")).Field);
        Assert.Equal("Mesh file", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "LOD1")).Field);
        Assert.Equal("Texture file", Assert.Single(GlobalAssetSearchPolicy.Search([asset], "*wood_Normal*")).Field);
    }

    // Unit test: global result types use canonical metadata and indexed content signals.
    [Fact]
    public void ResultTypeClassificationCoversPrimaryGroups()
    {
        var asset = TestAssetFactory.Create("type", Path.GetTempPath());

        Assert.Equal(GlobalSearchAssetType.Material, GlobalAssetSearchPolicy.ClassifyType(asset with { AssetType = "Surface" }));
        Assert.Equal(GlobalSearchAssetType.Mesh, GlobalAssetSearchPolicy.ClassifyType(asset with { AssetType = "3D Asset" }));
        Assert.Equal(GlobalSearchAssetType.Texture, GlobalAssetSearchPolicy.ClassifyType(asset with { AssetType = "Texture" }));
        Assert.Equal(GlobalSearchAssetType.AtlasOrBillboard, GlobalAssetSearchPolicy.ClassifyType(asset with { AssetType = "Atlas" }));
        Assert.Equal(GlobalSearchAssetType.Other, GlobalAssetSearchPolicy.ClassifyType(asset with { AssetType = "Unknown" }));
    }
}
