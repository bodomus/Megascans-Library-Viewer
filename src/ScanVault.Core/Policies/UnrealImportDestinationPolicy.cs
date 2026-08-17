using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class UnrealImportDestinationPolicy
{
    private static readonly Dictionary<string, string> AssetTypeSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["3D Asset"] = "3D_Assets",
        ["3D Plant"] = "3D_Plants",
        ["Surface"] = "Surfaces",
        ["Atlas"] = "Atlases",
        ["Decal"] = "Decals",
        ["Billboard"] = "Billboards"
    };

    public static UnrealImportDestination Create(string destinationBasePath, AssetSummary asset)
    {
        var basePath = NormalizeBasePath(destinationBasePath);
        var typeSegment = AssetTypeSegments.TryGetValue(asset.AssetType, out var segment)
            ? segment
            : UnrealImportNamePolicy.SanitizeSegment(asset.AssetType);
        var assetBaseName = UnrealImportNamePolicy.SanitizeSegment(asset.Name);
        return new(basePath, $"{basePath}/{typeSegment}/{assetBaseName}", assetBaseName, asset.Name);
    }

    public static bool IsValidGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("/Game", StringComparison.Ordinal) &&
        !path.Contains('\\', StringComparison.Ordinal) &&
        !path.Contains("//", StringComparison.Ordinal);

    public static string NormalizeBasePath(string? destinationBasePath)
    {
        var value = string.IsNullOrWhiteSpace(destinationBasePath)
            ? UnrealImportPackageSettings.Default.DefaultDestinationBasePath
            : destinationBasePath.Trim().Replace('\\', '/');
        value = "/" + value.Trim('/');
        return value.Equals("/Game", StringComparison.Ordinal) || value.StartsWith("/Game/", StringComparison.Ordinal)
            ? value.TrimEnd('/')
            : UnrealImportPackageSettings.Default.DefaultDestinationBasePath;
    }
}
