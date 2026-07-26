using System.Text.Json;
using ScanVault.Infrastructure.Parsing;

namespace ScanVault.Infrastructure.Tests;

public sealed class PreviewPathResolverTests
{
    [Fact]
    public void MetadataThumbAndPreviewBeatKnownFilenamePattern()
    {
        using var temporary = new TemporaryDirectory();
        var folder = temporary.CreateDirectory("asset");
        var thumb = temporary.WriteFile("asset/thumb.jpg", string.Empty);
        var preview = temporary.WriteFile("asset/preview.jpg", string.Empty);
        temporary.WriteFile("asset/id_fallback.jpg", string.Empty);
        using var document = JsonDocument.Parse(
            """
            {
              "images": [
                { "tag": "thumb", "path": "thumb.jpg" },
                { "tag": "preview", "path": "preview.jpg" }
              ]
            }
            """);

        var result = PreviewPathResolver.Resolve(document.RootElement, folder, "id");

        Assert.Equal(Path.GetFullPath(thumb), result.ThumbnailPath, ignoreCase: true);
        Assert.Equal(Path.GetFullPath(preview), result.PreviewPath, ignoreCase: true);
    }
}
