using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class ContentInventoryViewModelTests
{
    [Fact]
    public void CardShowsBoundedBadgesSummaryAndIssueWarning()
    {
        var asset = CreateAsset() with { Content = CreateInventory(AssetCompletenessStatus.Ambiguous) };
        var requested = false;
        using var card = new AssetCardViewModel(
            asset,
            new NullImageLoader(),
            new RecordingInteractions(),
            static _ => Task.CompletedTask,
            static _ => { },
            NullLogger<AssetCardViewModel>.Instance,
            _ => requested = true);

        Assert.InRange(card.Badges.Count, 3, 5);
        Assert.Contains("FBX", card.Badges);
        Assert.Contains("ATLAS", card.Badges);
        Assert.True(card.HasContentWarning);
        Assert.Equal("AMBIGUOUS", card.ContentWarning);
        Assert.Contains("LOD0", card.ContentLodsDisplay, StringComparison.Ordinal);
        Assert.Equal("Issues: 1", card.ContentIssuesDisplay);
        card.ShowContentInventoryCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public void DetailGroupsContentAndProvidesCopyablePaths()
    {
        var interactions = new RecordingInteractions();
        var asset = CreateAsset() with { Content = CreateInventory(AssetCompletenessStatus.Complete) };
        var viewModel = new ContentInventoryViewModel(asset, interactions, NullLogger<ContentInventoryViewModel>.Instance);

        Assert.Single(viewModel.VariantLines);
        Assert.Contains("\u2014", Assert.Single(viewModel.TextureSetLines), StringComparison.Ordinal);
        Assert.Contains("\u2014", Assert.Single(viewModel.UnclassifiedLines), StringComparison.Ordinal);
        var file = Assert.Single(viewModel.Files, item => item.Path.EndsWith("mesh_LOD0.fbx", StringComparison.Ordinal));
        file.CopyPathCommand.Execute(null);
        Assert.Equal(file.Path, interactions.CopiedText);
    }

    private static AssetSummary CreateAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScanVault.App.Tests", "inventory");
        return new("asset", "Asset", "3D Asset", root, Path.Combine(root, "asset.json"), null, null,
            null, null, null, null, null, null, [], [], DateTimeOffset.UnixEpoch);
    }

    private static AssetContentInventory CreateInventory(AssetCompletenessStatus status)
    {
        var root = Path.Combine(Path.GetTempPath(), "ScanVault.App.Tests", "inventory");
        return new(
            [new("Var1", [new(Path.Combine(root, "mesh_LOD0.fbx"), "mesh_LOD0.fbx", "Var1", 0, MeshFormat.Fbx)])],
            [new(TextureSetKind.Atlas, null, [new(Path.Combine(root, "asset_4K_Albedo.jpg"), "asset_4K_Albedo.jpg", "Albedo", TextureMapType.Albedo, 4096, "JPG")])],
            [new(Path.Combine(root, "unknown.dat"), "Unrecognized content file.")],
            status,
            [new(AssetContentIssueCode.DuplicateTexture, "Duplicate texture.", ["a", "b"])]);
    }

    private sealed class NullImageLoader : IImageLoader
    {
        public Task<ImageSource?> LoadAsync(string? path, int decodePixelWidth, CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource?>(null);
    }

    private sealed class RecordingInteractions : IAssetInteractionService
    {
        public string? CopiedText { get; private set; }
        public void CopyText(string text) => CopiedText = text;
        public void OpenFolder(string folderPath) { }
        public void OpenFile(string filePath) { }
    }
}
