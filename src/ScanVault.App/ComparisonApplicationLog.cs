using Microsoft.Extensions.Logging;

namespace ScanVault.App;

internal static partial class ComparisonApplicationLog
{
    [LoggerMessage(2101, LogLevel.Information, "Asset comparison opened: {LeftAssetId} vs {RightAssetId}")]
    public static partial void Opened(ILogger logger, string leftAssetId, string rightAssetId);

    [LoggerMessage(2102, LogLevel.Information, "Asset comparison loaded: {LeftAssetId} vs {RightAssetId}; left files {LeftFileCount}; right files {RightFileCount}; differences {DifferenceCount}; duration {DurationMs} ms")]
    public static partial void Loaded(ILogger logger, string leftAssetId, string rightAssetId, int leftFileCount, int rightFileCount, int differenceCount, long durationMs);

    [LoggerMessage(2103, LogLevel.Warning, "Asset comparison failed: {LeftAssetId} vs {RightAssetId}")]
    public static partial void Failed(ILogger logger, string leftAssetId, string rightAssetId, Exception exception);

    [LoggerMessage(2104, LogLevel.Information, "Asset comparison refreshed: {LeftAssetId} vs {RightAssetId}; duration {DurationMs} ms")]
    public static partial void Refreshed(ILogger logger, string leftAssetId, string rightAssetId, long durationMs);

    [LoggerMessage(2105, LogLevel.Information, "Asset comparison sides swapped: {LeftAssetId} vs {RightAssetId}")]
    public static partial void Swapped(ILogger logger, string leftAssetId, string rightAssetId);
}

