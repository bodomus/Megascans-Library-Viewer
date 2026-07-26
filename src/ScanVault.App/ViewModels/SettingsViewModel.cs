using ScanVault.App.Presentation;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed class SettingsViewModel(ISettingsStore store) : ObservableObject
{
    private string libraryRoot = string.Empty;
    private string? validationError;
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

    public LibrarySettings Current => new(LibraryRoot.Trim());

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        savedSettings = await store.LoadAsync(cancellationToken);
        LibraryRoot = savedSettings.LibraryRoot;
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
