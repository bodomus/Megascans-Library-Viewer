using Microsoft.Extensions.Logging;
using ScanVault.Core.Models;

namespace ScanVault.App;

internal static partial class ApplicationLog
{
    [LoggerMessage(2201, LogLevel.Information,
        "UE import package preview created for asset {AssetId}; source {JsonPath}; readiness {ReadinessStatus}")]
    public static partial void UnrealImportPackagePreviewCreated(
        ILogger logger,
        string assetId,
        string jsonPath,
        UnrealReadinessStatus readinessStatus);

    [LoggerMessage(2202, LogLevel.Warning,
        "UE import package validation/export failed for asset {AssetId}; package {PackageId}")]
    public static partial void UnrealImportPackageValidationFailed(
        ILogger logger,
        string assetId,
        string packageId,
        Exception exception);

    [LoggerMessage(2203, LogLevel.Information,
        "UE import package exported for asset {AssetId}; package {PackageId}; destination {DestinationPath}; bytes {OutputSizeBytes}")]
    public static partial void UnrealImportPackageExported(
        ILogger logger,
        string assetId,
        string packageId,
        string destinationPath,
        long outputSizeBytes);

    [LoggerMessage(2204, LogLevel.Information,
        "UE material profile {Action}: {ProfileId} {ProfileName}")]
    public static partial void UnrealMaterialProfileChanged(
        ILogger logger,
        string action,
        string profileId,
        string profileName);
}
