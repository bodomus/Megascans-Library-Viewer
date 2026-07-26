using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Settings;

namespace ScanVault.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SavesAndLoadsSettingsOutsideRepositoryContract()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new ScanVaultPaths(
            System.IO.Path.Combine(temporary.Path, "local", "index.db"),
            System.IO.Path.Combine(temporary.Path, "roaming", "settings.json"),
            System.IO.Path.Combine(temporary.Path, "cache"));
        var store = new JsonSettingsStore(paths);
        var expected = new LibrarySettings(@"C:\Megascans");

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, loaded);
        Assert.False(File.Exists(paths.SettingsPath + ".tmp"));
    }
}
