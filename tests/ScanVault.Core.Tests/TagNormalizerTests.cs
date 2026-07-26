using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class TagNormalizerTests
{
    [Fact]
    public void TrimsDeduplicatesCaseInsensitivelyAndSortsTags()
    {
        var result = TagNormalizer.Normalize(
        [
            new(AssetTagKind.Theme, "  Ancient "),
            new(AssetTagKind.Color, "Green"),
            new(AssetTagKind.Color, "green"),
            new(AssetTagKind.State, " ")
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(new(AssetTagKind.Color, "Green"), result[0]);
        Assert.Equal(new(AssetTagKind.Theme, "Ancient"), result[1]);
    }
}
