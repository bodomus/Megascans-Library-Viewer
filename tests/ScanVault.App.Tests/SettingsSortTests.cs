using System.IO;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class SettingsSortTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ScanVault.App.Tests",
        Guid.NewGuid().ToString("N"));

    public SettingsSortTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task SavingSortDoesNotPersistUnsavedLibraryRootEdit()
    {
        var store = new Store(new(root));
        var viewModel = new SettingsViewModel(store);
        await viewModel.LoadAsync(CancellationToken.None);
        var unsaved = Path.Combine(root, "unsaved");
        Directory.CreateDirectory(unsaved);
        viewModel.LibraryRoot = unsaved;

        await viewModel.SaveSortModeAsync(
            AssetSortMode.TypeAscending,
            CancellationToken.None);

        Assert.Equal(root, store.Value.LibraryRoot);
        Assert.Equal(AssetSortMode.TypeAscending, store.Value.SortMode);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task InventoryFilterPersistsWithoutSavingAnEditedLibraryRoot()
    {
        var store = new Store(new(root));
        var viewModel = new SettingsViewModel(store);
        await viewModel.LoadAsync(CancellationToken.None);
        var unsaved = Path.Combine(root, "unsaved-filter-root");
        Directory.CreateDirectory(unsaved);
        viewModel.LibraryRoot = unsaved;

        await viewModel.SaveInventoryFilterAsync(
            AssetInventoryFilter.HasFbx | AssetInventoryFilter.HasAtlas,
            CancellationToken.None);

        Assert.Equal(root, store.Value.LibraryRoot);
        Assert.Equal(AssetInventoryFilter.HasFbx | AssetInventoryFilter.HasAtlas, store.Value.InventoryFilter);
        Assert.True(viewModel.IsDirty);
    }
    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class Store(LibrarySettings settings) : ISettingsStore
    {
        public LibrarySettings Value { get; private set; } = settings;

        public Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }
}
