using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Settings;

namespace ScanVault.Infrastructure.Tests;

public sealed class JsonSmartCollectionStoreTests
{
    [Fact]
    public async Task SavesAndLoadsUserCollections()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "local", "index.db"),
            Path.Combine(temporary.Path, "roaming", "settings.json"),
            Path.Combine(temporary.Path, "roaming", "smart-collections.json"),
            Path.Combine(temporary.Path, "cache"));
        var store = new JsonSmartCollectionStore(paths);
        var now = DateTimeOffset.UtcNow;
        var expected = new SmartCollectionRecord(
            "user-test",
            SmartCollectionKind.User,
            "Forest",
            "Trees and forest scans",
            SmartCollectionDefinition.Empty with { SearchText = "forest" },
            0,
            now,
            now);

        await store.SaveAsync([expected], CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        var actual = Assert.Single(loaded);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal("forest", actual.Definition.SearchText);
        Assert.False(File.Exists(paths.SmartCollectionsPath + ".tmp"));
    }

    [Fact]
    public async Task CorruptDocumentIsBackedUpAndReturnsEmptyList()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "local", "index.db"),
            Path.Combine(temporary.Path, "roaming", "settings.json"),
            Path.Combine(temporary.Path, "roaming", "smart-collections.json"),
            Path.Combine(temporary.Path, "cache"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SmartCollectionsPath)!);
        await File.WriteAllTextAsync(paths.SmartCollectionsPath, "{not-json", CancellationToken.None);
        var store = new JsonSmartCollectionStore(paths);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded);
        Assert.False(File.Exists(paths.SmartCollectionsPath));
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(paths.SmartCollectionsPath)!, "smart-collections.json.corrupt-*"));
    }
}
