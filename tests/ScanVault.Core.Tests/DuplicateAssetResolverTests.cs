using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class DuplicateAssetResolverTests
{
    [Fact]
    public void LexicographicallySmallestFullJsonPathWinsAndAllCopiesAreReported()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScanVault", "duplicates");
        var second = TestAssetFactory.Create(
            "same-id",
            Path.Combine(root, "B"),
            Path.Combine(root, "B", "same-id.json"));
        var winner = TestAssetFactory.Create(
            "SAME-ID",
            Path.Combine(root, "A"),
            Path.Combine(root, "A", "same-id.json"));
        var third = TestAssetFactory.Create(
            "same-id",
            Path.Combine(root, "C"),
            Path.Combine(root, "C", "same-id.json"));

        var result = DuplicateAssetResolver.Resolve([second, third, winner]);

        var selected = Assert.Single(result.Assets);
        Assert.Equal(Path.GetFullPath(winner.JsonPath), selected.JsonPath);
        var duplicate = Assert.Single(result.DuplicateGroups);
        Assert.Equal(Path.GetFullPath(winner.JsonPath), duplicate.WinnerJsonPath);
        Assert.Equal(
            [Path.GetFullPath(second.JsonPath), Path.GetFullPath(third.JsonPath)],
            duplicate.SkippedCopyJsonPaths);
    }
}
