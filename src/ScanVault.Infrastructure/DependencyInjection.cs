using Microsoft.Extensions.DependencyInjection;
using ScanVault.Core.Abstractions;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Parsing;
using ScanVault.Infrastructure.Persistence;
using ScanVault.Infrastructure.Scanning;
using ScanVault.Infrastructure.Settings;

namespace ScanVault.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScanVaultInfrastructure(
        this IServiceCollection services,
        ScanVaultPaths? paths = null)
    {
        services.AddSingleton(paths ?? ScanVaultPaths.ForCurrentUser());
        services.AddSingleton<IFileSystemScanner, FileSystemScanner>();
        services.AddSingleton<IAssetMetadataParser, MegascansMetadataParser>();
        services.AddSingleton<IAssetContentInventoryService, AssetContentInventoryService>();
        services.AddSingleton<IAssetIndex, SqliteAssetIndex>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ILibraryScanService, LibraryScanService>();
        return services;
    }
}
