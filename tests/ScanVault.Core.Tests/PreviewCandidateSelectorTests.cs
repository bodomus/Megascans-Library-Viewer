using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class PreviewCandidateSelectorTests
{
    [Fact]
    public void ThumbnailPrefersThumbOverHigherResolutionPreview()
    {
        var result = PreviewCandidateSelector.SelectThumbnail(
        [
            new("preview.jpg", ImageCandidateRole.Preview, 4096),
            new("thumb.jpg", ImageCandidateRole.Thumb, 256)
        ]);

        Assert.Equal("thumb.jpg", result);
    }

    [Fact]
    public void LargePreviewPrefersBestPreviewThenRetinaBakeAndThumb()
    {
        var result = PreviewCandidateSelector.SelectPreview(
        [
            new("thumb.jpg", ImageCandidateRole.Thumb, 512),
            new("preview-small.jpg", ImageCandidateRole.Preview, 1024),
            new("preview-large.jpg", ImageCandidateRole.Preview, 2048),
            new("retina.jpg", ImageCandidateRole.Retina, 4096)
        ]);

        Assert.Equal("preview-large.jpg", result);
    }
}
