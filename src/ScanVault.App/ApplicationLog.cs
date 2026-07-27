using Microsoft.Extensions.Logging;

namespace ScanVault.App;

internal static partial class ApplicationLog
{
    [LoggerMessage(
        2001,
        LogLevel.Information,
        "Starting ScanVault {ApplicationVersion}; informational {InformationalVersion}; commit {CommitSha}; configuration {BuildConfiguration}; runtime {RuntimeVersion}; OS {OperatingSystem}; architecture {ProcessArchitecture}")]
    public static partial void Starting(
        ILogger logger,
        string applicationVersion,
        string informationalVersion,
        string commitSha,
        string buildConfiguration,
        string runtimeVersion,
        string operatingSystem,
        string processArchitecture);

    [LoggerMessage(2002, LogLevel.Warning, "Cannot load image {ImagePath}")]
    public static partial void ImageLoadFailed(
        ILogger logger,
        string imagePath,
        Exception exception);

    [LoggerMessage(2003, LogLevel.Error, "Scan command failed")]
    public static partial void ScanCommandFailed(ILogger logger, Exception exception);

    [LoggerMessage(2004, LogLevel.Warning, "Asset action {Action} failed for {AssetId}")]
    public static partial void AssetActionFailed(
        ILogger logger,
        string action,
        string assetId,
        Exception exception);
}
