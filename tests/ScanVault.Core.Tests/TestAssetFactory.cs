using ScanVault.Core.Models;

namespace ScanVault.Core.Tests;

internal static class TestAssetFactory
{
    public static AssetSummary Create(
        string id,
        string folder,
        string? jsonPath = null,
        DateTimeOffset? lastWrite = null) =>
        new(
            id,
            $"Asset {id}",
            "surface",
            folder,
            jsonPath ?? Path.Combine(folder, $"{id}.json"),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            lastWrite ?? DateTimeOffset.UnixEpoch);
}
