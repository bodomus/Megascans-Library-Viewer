using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace ScanVault.App.Services;

public sealed class BoundedImageLoader(ILogger<BoundedImageLoader> logger) : IImageLoader
{
    private const long MaximumBytes = 128L * 1024 * 1024;
    private readonly object gate = new();
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> leastRecentlyUsed = [];
    private long currentBytes;

    public async Task<ImageSource?> LoadAsync(
        string? path,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var imagePath = path!;
        var key = $"{imagePath}|{decodePixelWidth}";
        lock (gate)
        {
            if (entries.TryGetValue(key, out var cached))
            {
                leastRecentlyUsed.Remove(cached.Node);
                leastRecentlyUsed.AddFirst(cached.Node);
                return cached.Image;
            }
        }

        try
        {
            var image = await Task.Run(
                () => Decode(imagePath, decodePixelWidth, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var size = Math.Max(1L, (long)image.PixelWidth * image.PixelHeight * 4);
            Add(key, image, size);
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ApplicationLog.ImageLoadFailed(logger, imagePath, exception);
            return null;
        }
    }

    private static BitmapImage Decode(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        cancellationToken.ThrowIfCancellationRequested();
        return image;
    }

    private void Add(string key, BitmapImage image, long size)
    {
        lock (gate)
        {
            if (entries.ContainsKey(key))
            {
                return;
            }

            var node = leastRecentlyUsed.AddFirst(key);
            entries.Add(key, new(image, size, node));
            currentBytes += size;

            while (currentBytes > MaximumBytes && leastRecentlyUsed.Last is { } last)
            {
                var evictedKey = last.Value;
                leastRecentlyUsed.RemoveLast();
                if (entries.Remove(evictedKey, out var evicted))
                {
                    currentBytes -= evicted.Size;
                }
            }
        }
    }

    private sealed record CacheEntry(
        BitmapImage Image,
        long Size,
        LinkedListNode<string> Node);
}
