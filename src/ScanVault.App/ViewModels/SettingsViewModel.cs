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

    public AssetSortMode SortMode
    {
        get => sortMode;
        private set => SetProperty(ref sortMode, value);
    }

    public string? ValidationError
    {
        get => validationError;
        private set => SetProperty(ref validationError, value);
    }

    public bool IsDirty =>
        !StringComparer.OrdinalIgnoreCase.Equals(
            savedSettings.LibraryRoot.Trim(),
            LibraryRoot.Trim());

    public bool CanRescan => !IsDirty && SettingsValidator.Validate(Current).IsValid;

    public LibrarySettings Current => new(LibraryRoot.Trim(), SortMode);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        savedSettings = await store.LoadAsync(cancellationToken);
        if (!Enum.IsDefined(savedSettings.SortMode))
        {
            savedSettings = savedSettings with { SortMode = AssetSortMode.NameAscending };
        }

        SortMode = savedSettings.SortMode;
        LibraryRoot = savedSettings.LibraryRoot;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
    }

    public async Task SaveSortModeAsync(
        AssetSortMode value,
        CancellationToken cancellationToken)
    {
        // Sorting is independent of an unsaved library-root edit in the settings dialog.
        var updated = savedSettings with { SortMode = value };
        await store.SaveAsync(updated, cancellationToken);
        savedSettings = updated;
        SortMode = value;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var validation = SettingsValidator.Validate(Current);
        ValidationError = validation.Error;
        if (!validation.IsValid)
        {
            return false;
        }

        await store.SaveAsync(Current, cancellationToken);
        savedSettings = Current;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanRescan));
        return true;
    }

    private void Validate() =>
        ValidationError = SettingsValidator.Validate(Current).Error;
}
