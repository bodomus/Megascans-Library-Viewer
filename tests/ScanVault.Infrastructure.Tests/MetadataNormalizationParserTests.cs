using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Parsing;

namespace ScanVault.Infrastructure.Tests;

public sealed class MetadataNormalizationParserTests
{
    [Theory]
    [InlineData("atlas", "Atlas")]
    [InlineData("3d", "3D Asset")]
    public async Task SemanticAssetTypeHasDocumentedPriority(
        string rawType,
        string expectedType)
    {
        using var temporary = new TemporaryDirectory();
        var json = $$"""
            {
              "id": "ExactCaseId",
              "name": "Asset",
              "type": "normal",
              "categories": ["surface"],
              "semanticTags": { "asset_type": "{{rawType}}" }
            }
            """;
        var asset = await ParseAssetAsync(temporary.WriteFile("asset.json", json));

        Assert.Equal(expectedType, asset.AssetType);
        Assert.Equal(rawType, asset.RawAssetType);
        Assert.Equal("ExactCaseId", asset.Id);
    }

    [Fact]
    public async Task ComponentTypesNeverClassifyAssetAndMaximumUsesAllComponents()
    {
        using var temporary = new TemporaryDirectory();
        var jsonPath = temporary.WriteFile(
            "asset.json",
            """
            {
              "id": "KeepMyCase",
              "name": "Brick Debris",
              "type": "specular",
              "categories": ["surface", " brick ", "BRICK", "debris", "undefined"],
              "biome": "undefined",
              "region": "N/A",
              "physicalSize": "0.96x0.96 m",
              "meta": [{ "key": "texelDensity", "value": "4264" }],
              "descriptiveTags": [" rubble", "RUBBLE", "null"],
              "components": [
                { "type": "normal", "resolution": "2048x2048" },
                { "type": "specular", "resolution": "4096x2048" },
                { "type": "albedo", "resolution": "4K" },
                { "type": "roughness", "resolution": "malformed" }
              ]
            }
            """);

        var asset = await ParseAssetAsync(jsonPath);

        Assert.Equal("Surface", asset.AssetType);
        Assert.False(StringComparer.OrdinalIgnoreCase.Equals("normal", asset.AssetType));
        Assert.False(StringComparer.OrdinalIgnoreCase.Equals("specular", asset.AssetType));
        Assert.Equal(new ImageResolution(4096, 4096), asset.MaxResolution);
        Assert.Equal("0.96 × 0.96 m", asset.PhysicalSize);
        Assert.Equal(4264, asset.TexelDensity);
        Assert.Null(asset.Biome);
        Assert.Null(asset.Region);
        Assert.Equal(["surface", "brick", "debris"], asset.Categories);
        Assert.Single(
            asset.Tags,
            static tag => tag.Kind == AssetTagKind.Descriptive && tag.Value == "rubble");
        Assert.Equal("KeepMyCase", asset.Id);
    }

    [Fact]
    public async Task AssetCategoriesProvideFallbackAndUnknownIsDeliberate()
    {
        using var temporary = new TemporaryDirectory();
        var fallbackPath = temporary.WriteFile(
            "fallback.json",
            """
            {
              "id": "fallback",
              "name": "Plant",
              "components": [{ "type": "normal" }],
              "assetCategories": { "3dplant": {}, "bush": {} }
            }
            """);
        var unknownPath = temporary.WriteFile(
            "unknown.json",
            """{ "id": "unknown-id", "name": "Mystery", "type": "normal" }""");

        var fallback = await ParseAssetAsync(fallbackPath);
        var unknown = await ParseAssetAsync(unknownPath);

        Assert.Equal("3D Plant", fallback.AssetType);
        Assert.Equal(["3dplant", "bush"], fallback.Categories);
        Assert.Equal("Unknown", unknown.AssetType);
    }

    private static async Task<AssetSummary> ParseAssetAsync(string path)
    {
        var parser = new MegascansMetadataParser(
            NullLogger<MegascansMetadataParser>.Instance);
        var result = await parser.ParseAsync(path, CancellationToken.None);
        Assert.Equal(AssetParseStatus.Success, result.Status);
        return Assert.IsType<AssetSummary>(result.Asset);
    }
}
