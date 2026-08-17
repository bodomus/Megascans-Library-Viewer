using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class AssetComparisonViewModelTests
{
    [Fact]
    public async Task LoadsFiltersSwapsAndRefreshesAStaleSnapshot()
    {
        var left = Asset("left", hasFbx: true);
        var right = Asset("right", hasFbx: false);
        var current = new Dictionary<string, AssetSummary>(StringComparer.OrdinalIgnoreCase)
        {
            [left.Id] = left,
            [right.Id] = right
        };
        using var viewModel = Create(left, right, id => current.GetValueOrDefault(id));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.False(viewModel.IsLoading);
        Assert.Equal("left", viewModel.LeftHeader.AssetId);
        Assert.Contains(viewModel.OverviewRows, row => row.Key == "has-fbx" && row.Result == ComparisonResult.OnlyLeft);
        viewModel.ShowDifferencesOnly = true;
        Assert.DoesNotContain(viewModel.OverviewRows, row => row.Result == ComparisonResult.Equal);

        viewModel.SwapCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading && viewModel.LeftHeader.AssetId == "right");
        Assert.Equal("right", viewModel.LeftHeader.AssetId);

        current["right"] = Asset("right", hasFbx: true);
        viewModel.MarkStale();
        Assert.True(viewModel.IsStale);
        viewModel.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading && !viewModel.IsStale);
        Assert.False(viewModel.IsStale);
    }

    [Fact]
    public async Task RefreshIsolatesAMissingSideAndCancellationIsSafe()
    {
        var left = Asset("left", hasFbx: true);
        var right = Asset("right", hasFbx: false);
        var current = new Dictionary<string, AssetSummary>(StringComparer.OrdinalIgnoreCase)
        {
            [right.Id] = right
        };
        using var viewModel = Create(left, right, id => current.GetValueOrDefault(id));
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.MarkStale();

        viewModel.RefreshCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading && viewModel.HasLeftError);

        Assert.Equal("This asset is no longer present in the current index.", viewModel.LeftError);
        Assert.Null(viewModel.Snapshot);
        Assert.Equal("Comparison data could not be loaded.", viewModel.StatusText);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            viewModel.InitializeAsync(cancellation.Token));
    }

    [Fact]
    public void ReplaceRequestsTheDisplayedIdentityAfterSwapSemantics()
    {
        var left = Asset("left", hasFbx: true);
        var right = Asset("right", hasFbx: false);
        string? replacement = null;
        using var viewModel = Create(left, right, _ => null, id => replacement = id);

        viewModel.ReplaceLeftCommand.Execute(null);

        Assert.Equal("left", replacement);
    }

    private static AssetComparisonViewModel Create(
        AssetSummary left,
        AssetSummary right,
        Func<string, AssetSummary?> resolver,
        Action<string>? replace = null) =>
        new(
            left,
            right,
            new NullImageLoader(),
            new NullInteractions(),
            static _ => Task.CompletedTask,
            static _ => { },
            resolver,
            replace ?? (_ => { }),
            NullLogger<AssetComparisonViewModel>.Instance);

    private static AssetSummary Asset(string id, bool hasFbx)
    {
        var content = hasFbx
            ? new AssetContentInventory(
            [
                new MeshVariantInventory("Var1",
                [
                    new MeshLodEntry($@"C:\Library\{id}\Var1\mesh_LOD0.fbx", "mesh_LOD0.fbx", "Var1", 0, MeshFormat.Fbx)
                ])
            ], [], [], AssetCompletenessStatus.Complete, [])
            : new AssetContentInventory([], [], [], AssetCompletenessStatus.Partial, []);
        return new(
            id,
            $"Asset {id}",
            "3D Asset",
            $@"C:\Library\{id}",
            $@"C:\Library\{id}\{id}.json",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch)
        {
            Content = content
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for comparison view-model state.");
    }

    private sealed class NullImageLoader : IImageLoader
    {
        public Task<ImageSource?> LoadAsync(string? path, int decodePixelWidth, CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource?>(null);
    }

    private sealed class NullInteractions : IAssetInteractionService
    {
        public void CopyText(string text) { }
        public void OpenFolder(string folderPath) { }
        public void OpenFile(string filePath) { }
    }
}
