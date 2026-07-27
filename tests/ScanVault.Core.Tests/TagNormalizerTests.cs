using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class TagNormalizerTests
{
    [Fact]
    public void TrimsDeduplicatesCaseInsensitivelyAndPreservesStableOrder()
    {
        var result = TagNormalizer.Normalize(
        [
            new(AssetTagKind.Theme, "  Ancient "),
            new(AssetTagKind.Color, "Green"),
            new(AssetTagKind.Color, "green"),
            new(AssetTagKind.State, " "),
            new(AssetTagKind.State, "undefined")
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(new(AssetTagKind.Theme, "Ancient"), result[0]);
        Assert.Equal(new(AssetTagKind.Color, "Green"), result[1]);
    }
}
