using System.Windows.Media;

namespace ScanVault.App.Services;

public interface IImageLoader
{
    Task<ImageSource?> LoadAsync(
        string? path,
        int decodePixelWidth,
        CancellationToken cancellationToken);
}
