using Microsoft.Extensions.Logging;

namespace ScanVault.App;

internal static partial class ApplicationLog
{
    [LoggerMessage(2001, LogLevel.Information, "Starting ScanVault")]
    public static partial void Starting(ILogger logger);

    [LoggerMessage(2002, LogLevel.Warning, "Cannot load image {ImagePath}")]
    public static partial void ImageLoadFailed(
        ILogger logger,
        string imagePath,
        Exception exception);

    [LoggerMessage(2003, LogLevel.Error, "Scan command failed")]
    public static partial void ScanCommandFailed(ILogger logger, Exception exception);
}
