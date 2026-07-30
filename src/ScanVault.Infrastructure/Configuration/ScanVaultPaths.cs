namespace ScanVault.Infrastructure.Configuration;

/// <summary>Resolved per-user paths; tests may provide isolated alternatives.</summary>
public sealed record ScanVaultPaths(
    string DatabasePath,
    string SettingsPath,
    string SmartCollectionsPath,
    string ThumbnailCacheDirectory)
{
    public ScanVaultPaths(
        string databasePath,
        string settingsPath,
        string thumbnailCacheDirectory)
        : this(
            databasePath,
            settingsPath,
            Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, "smart-collections.json"),
            thumbnailCacheDirectory)
    {
    }

    public static ScanVaultPaths ForCurrentUser()
    {
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScanVault");
        var roamingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScanVault");

        return new(
            Path.Combine(localRoot, "scanvault.db"),
            Path.Combine(roamingRoot, "settings.json"),
            Path.Combine(roamingRoot, "smart-collections.json"),
            Path.Combine(localRoot, "thumbnails"));
    }
}
