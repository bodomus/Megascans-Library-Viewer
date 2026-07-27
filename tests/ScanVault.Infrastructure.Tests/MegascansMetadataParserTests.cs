using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Parsing;

namespace ScanVault.Infrastructure.Tests;

public sealed class MegascansMetadataParserTests
{
    [Fact]
    public async Task ParsesRepresentativeLegacyMetadataAndResolvesLocalImages()
    {
        using var temporary = new TemporaryDirectory();
        temporary.WriteFile("asset/thumb.jpg", string.Empty);
        temporary.WriteFile("asset/preview.jpg", string.Empty);
        var jsonPath = temporary.WriteFile(
            "asset/qgphP2.json",
            """
            {
              "id": "qgphP2",
              "name": "Mossy Rock",
              "assetType": "3d",
              "categories": ["nature", "rock"],
              "biome": "forest",
              "region": "northern",
              "physicalSize": "2 x 1 x 1 m",
              "maxResolution": "8K",
              "texelDensity": 10.5,
              "averageColor": "#667755",
              "descriptiveTags": ["mossy", "weathered"],
              "stateTags": ["aged"],
              "images": [
                { "type": "thumb", "path": "thumb.jpg", "resolution": 256 },
                { "type": "preview", "path": "preview.jpg", "resolution": "2048x1024" }
              ]
            }
            """);
        var parser = new MegascansMetadataParser(
            NullLogger<MegascansMetadataParser>.Instance);

        var result = await parser.ParseAsync(jsonPath, CancellationToken.None);

        Assert.Equal(AssetParseStatus.Success, result.Status);
        var asset = Assert.IsType<AssetSummary>(result.Asset);
        Assert.Equal("qgphP2", asset.Id);
        Assert.Equal("Mossy Rock", asset.Name);
        Assert.Equal(8192, asset.MaxResolution);
        Assert.Equal(10.5, asset.TexelDensity);
        Assert.EndsWith("thumb.jpg", asset.ThumbnailPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("preview.jpg", asset.PreviewPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(asset.Tags, static tag =>
            tag.Kind == AssetTagKind.Descriptive && tag.Value == "mossy");
    }

    [Fact]
    public async Task MissingOptionalValuesAreNullOrEmpty()
    {
        using var temporary = new TemporaryDirectory();
        var jsonPath = temporary.WriteFile(
            "minimal.json",
            """{ "id": "minimal", "name": "Minimal Asset" }""");
        var parser = new MegascansMetadataParser(
            NullLogger<MegascansMetadataParser>.Instance);

        var result = await parser.ParseAsync(jsonPath, CancellationToken.None);

        var asset = Assert.IsType<AssetSummary>(result.Asset);
        Assert.Null(asset.Biome);
        Assert.Null(asset.PreviewPath);
        Assert.Empty(asset.Categories);
    }

    [Fact]
    public async Task MalformedAndUnrelatedJsonAreSkippedWithoutThrowing()
    {
        using var temporary = new TemporaryDirectory();
        var malformed = temporary.WriteFile("malformed.json", "{ not json");
        var unrelated = temporary.WriteFile("unrelated.json", """{ "theme": "dark" }""");
        var parser = new MegascansMetadataParser(
            NullLogger<MegascansMetadataParser>.Instance);

        var malformedResult = await parser.ParseAsync(malformed, CancellationToken.None);
        var unrelatedResult = await parser.ParseAsync(unrelated, CancellationToken.None);

        Assert.Equal(AssetParseStatus.MalformedJson, malformedResult.Status);
        Assert.Equal(AssetParseStatus.UnrelatedJson, unrelatedResult.Status);
    }
}
