using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class MetadataNormalizerTests
{
    [Theory]
    [InlineData("4096x4096", 4096, 4096)]
    [InlineData("4096 × 4096", 4096, 4096)]
    [InlineData("2048x2048", 2048, 2048)]
    [InlineData("4096x2048", 4096, 2048)]
    [InlineData("4K", 4096, 4096)]
    [InlineData("1024", 1024, 1024)]
    public void ParsesSupportedResolutionForms(string raw, int width, int height)
    {
        Assert.True(MetadataNormalizer.TryParseResolution(raw, out var resolution));
        Assert.Equal(new ImageResolution(width, height), resolution);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("")]
    [InlineData("undefined")]
    public void RejectsMalformedOrMissingResolution(string raw) =>
        Assert.False(MetadataNormalizer.TryParseResolution(raw, out _));

    [Fact]
    public void SelectsMaximumResolutionAndFormatsSquareAndNonSquareValues()
    {
        var maximum = MetadataNormalizer.SelectMaximum(
        [
            new(2048, 2048),
            new(4096, 2048),
            new(4096, 4096)
        ]);

        Assert.Equal(new ImageResolution(4096, 4096), maximum);
        Assert.Equal("4K (4096 × 4096)", maximum?.ToDisplayString());
        Assert.Equal("4096 × 2048", new ImageResolution(4096, 2048).ToDisplayString());
    }

    [Theory]
    [InlineData("0.96x0.96", "0.96 × 0.96 m")]
    [InlineData("1x1", "1 × 1 m")]
    [InlineData("0.96x0.96 m", "0.96 × 0.96 m")]
    public void NormalizesPhysicalSize(string raw, string expected) =>
        Assert.Equal(expected, MetadataNormalizer.NormalizePhysicalSize(raw));

    [Theory]
    [InlineData("malformed")]
    [InlineData("")]
    [InlineData("undefined")]
    public void RejectsMalformedOrMissingPhysicalSize(string raw) =>
        Assert.Null(MetadataNormalizer.NormalizePhysicalSize(raw));

    [Fact]
    public void AssetTypeResolutionRejectsComponentTypesAndUsesKnownFallback()
    {
        Assert.Equal(
            "Atlas",
            MetadataNormalizer.ResolveAssetType(["atlas", "3d"]).Canonical);
        Assert.Equal(
            "3D Asset",
            MetadataNormalizer.ResolveAssetType(["normal", "specular", "3d"]).Canonical);
        Assert.Equal(
            "Unknown",
            MetadataNormalizer.ResolveAssetType(["roughness", "undefined"]).Canonical);
    }

    [Fact]
    public void OptionalAndCollectionNormalizationRemovesPlaceholdersAndDuplicates()
    {
        Assert.Null(MetadataNormalizer.NormalizeOptional(" N/A "));
        Assert.Null(MetadataNormalizer.NormalizeOptional("unknown"));
        Assert.Equal(
            ["3D", "brick", "debris"],
            MetadataNormalizer.NormalizeValues(
                [" 3D ", "brick", "BRICK", "undefined", "debris"]));
    }
}
