using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Persistence;
using ScanVault.Infrastructure.Scanning;

namespace ScanVault.Infrastructure.Tests;

public sealed class AssetContentInventoryTests
{
    [Fact]
    public async Task ServiceInventoriesNestedFixtureWithoutReadingPayloads()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("asset");
        temporary.WriteFile("asset/Var1/Var1_LOD0.fbx", "not an FBX payload");
        temporary.WriteFile("asset/Textures/Atlas/a_4K_Albedo.jpg", "not an image payload");
        temporary.WriteFile("asset/Textures/Atlas/a_4K_Normal.jpg", "x");
        temporary.WriteFile("asset/Textures/Atlas/a_4K_Roughness.jpg", "x");
        temporary.WriteFile("asset/Textures/Atlas/a_4K_Opacity.jpg", "x");
        var service = new AssetContentInventoryService(NullLogger<AssetContentInventoryService>.Instance);

        var result = await service.InventoryAsync(CreateAsset("plant", root, "3D Plant"), CancellationToken.None);

        Assert.Equal(AssetCompletenessStatus.Complete, result.Inventory.Completeness);
        Assert.Equal(1, result.Inventory.MeshCount);
        Assert.Equal(4, result.Inventory.TextureCount);
        Assert.Empty(result.InaccessibleDirectories);
    }

    [Fact]
    public async Task VersionThreePersistsFullInventoryAndIndexedMapProjection()
    {
        using var temporary = new TemporaryDirectory();
        var root = temporary.CreateDirectory("library", "asset");
        var paths = new ScanVaultPaths(
            Path.Combine(temporary.Path, "index", "scanvault.db"),
            Path.Combine(temporary.Path, "settings.json"),
            Path.Combine(temporary.Path, "cache"));
        var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var inventory = ScanVault.Core.Policies.AssetContentAnalyzer.Analyze("Atlas",
        [
            new(Path.Combine(root, "a_4K_Albedo.jpg"), "a_4K_Albedo.jpg"),
            new(Path.Combine(root, "a_4K_Normal.jpg"), "a_4K_Normal.jpg"),
            new(Path.Combine(root, "a_4K_Roughness.jpg"), "a_4K_Roughness.jpg"),
            new(Path.Combine(root, "a_4K_Opacity.jpg"), "a_4K_Opacity.jpg")
        ]);
        var asset = CreateAsset("atlas", root, "Atlas") with { Content = inventory };

        await index.ReplaceLibraryAsync(root, [asset], Draft(), CancellationToken.None);
        var reopened = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
        var saved = Assert.Single(await reopened.GetAssetsAsync(CancellationToken.None));

        Assert.Equal(AssetCompletenessStatus.Complete, saved.Content.Completeness);
        Assert.Equal(4, saved.Content.TextureCount);
        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM asset_inventory_maps WHERE asset_id = 'atlas';";
        Assert.Equal(4L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static AssetSummary CreateAsset(string id, string root, string type) => new(
        id, id, type, root, Path.Combine(root, $"{id}.json"), null, null, null, null, null,
        null, null, null, [], [], DateTimeOffset.UnixEpoch);

    private static ScanResult Draft() => new(0, 0, 0, 1, 0, 0, [], [], [], TimeSpan.Zero);
}
