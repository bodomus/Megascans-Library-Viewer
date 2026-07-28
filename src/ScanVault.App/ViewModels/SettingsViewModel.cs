using ScanVault.App.Presentation;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed class SettingsViewModel(ISettingsStore store) : ObservableObject
{
    private string libraryRoot = string.Empty;
    private string? validationError;
    private AssetSortMode sortMode;
    private AssetInventoryFilter inventoryFilter;
    private LibrarySettings savedSettings = LibrarySettings.Empty;

    public string LibraryRoot
    {
        get => libraryRoot;
        set
        {
            if (SetProperty(ref libraryRoot, value))
            {
                Validate();
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(CanRescan));
            }
        }
    }

    public AssetSortMode SortMode { get => sortMode; private set => SetProperty(ref sortMode, value); }
    public AssetInventoryFilter InventoryFilter { get => inventoryFilter; private set => SetProperty(ref inventoryFilter, value); }
    public string? ValidationError { get => validationError; private set => SetProperty(ref validationError, value); }
    public bool IsDirty => !StringComparer.OrdinalIgnoreCase.Equals(savedSettings.LibraryRoot.Trim(), LibraryRoot.Trim());
    public bool CanRescan => !IsDirty && SettingsValidator.Validate(Current).IsValid;
    public LibrarySettings Current => new(LibraryRoot.Trim(), SortMode, InventoryFilter);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        savedSettings = await store.LoadAsync(cancellationToken);
        if (!Enum.IsDefined(savedSettings.SortMode)) savedSettings = savedSettings with { SortMode = AssetSortMode.NameAscending };
        var knownFilters = AssetInventoryFilter.HasFbx | AssetInventoryFilter.HasLods |
            AssetInventoryFilter.HasBillboard | AssetInventoryFilter.HasAtlas |
            AssetInventoryFilter.Complete | AssetInventoryFilter.Incomplete | AssetInventoryFilter.Ambiguous;
        if ((savedSettings.InventoryFilter & ~knownFilters) != 0) savedSettings = savedSettings with { InventoryFilter = AssetInventoryFilter.None };
        SortMode = savedSettings.SortMode;
        InventoryFilter = savedSettings.InventoryFilter;
        LibraryRoot = savedSettings.LibraryRoot;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
    }

    public Task SaveSortModeAsync(AssetSortMode value, CancellationToken cancellationToken) =>
        SaveNavigationAsync(savedSettings with { SortMode = value }, cancellationToken);

    public Task SaveInventoryFilterAsync(AssetInventoryFilter value, CancellationToken cancellationToken) =>
        SaveNavigationAsync(savedSettings with { InventoryFilter = value }, cancellationToken);

    private async Task SaveNavigationAsync(LibrarySettings updated, CancellationToken cancellationToken)
    {
        await store.SaveAsync(updated, cancellationToken);
        savedSettings = updated;
        SortMode = updated.SortMode;
        InventoryFilter = updated.InventoryFilter;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.Validate(Current);
        ValidationError = validation.Error;
        if (!validation.IsValid) return false;
        await store.SaveAsync(Current, cancellationToken);
        savedSettings = Current;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
        return true;
    }

    private void Validate() => ValidationError = SettingsValidator.Validate(Current).Error;
}
