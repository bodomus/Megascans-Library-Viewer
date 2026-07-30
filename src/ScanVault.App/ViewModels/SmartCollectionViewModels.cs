using System.Globalization;
using ScanVault.App.Presentation;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed class SmartCollectionItemViewModel(SmartCollectionRecord record) : ObservableObject
{
    private int count;
    private bool isActive;
    private bool isModified;
    private SmartCollectionCompatibility compatibility = SmartCollectionCompatibility.Compatible;
    private string? compatibilityMessage;

    public SmartCollectionRecord Record { get; private set; } = record;
    public string Id => Record.Id;
    public string Name => Record.Name;
    public string Description => Record.Description;
    public SmartCollectionKind Kind => Record.Kind;
    public bool IsUser => Kind == SmartCollectionKind.User;
    public string CountDisplay => Count < 0 ? "..." : Count.ToString("N0", CultureInfo.CurrentCulture);
    public string StatusDisplay => Compatibility == SmartCollectionCompatibility.Compatible
        ? string.Empty
        : CompatibilityMessage ?? Compatibility.ToString();
    public string ToolTip => string.IsNullOrWhiteSpace(StatusDisplay)
        ? Description
        : $"{Description}{Environment.NewLine}{StatusDisplay}";

    public int Count
    {
        get => count;
        set
        {
            if (SetProperty(ref count, value))
            {
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    public bool IsActive
    {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }

    public bool IsModified
    {
        get => isModified;
        set => SetProperty(ref isModified, value);
    }

    public SmartCollectionCompatibility Compatibility
    {
        get => compatibility;
        set
        {
            if (SetProperty(ref compatibility, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    public string? CompatibilityMessage
    {
        get => compatibilityMessage;
        set
        {
            if (SetProperty(ref compatibilityMessage, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    public void UpdateRecord(SmartCollectionRecord record)
    {
        Record = record;
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsUser));
        OnPropertyChanged(nameof(ToolTip));
    }
}

public sealed record SmartCollectionFolderScopeOption(SmartCollectionFolderScope Scope, string Label);

public sealed class SmartCollectionEditorViewModel : ObservableObject
{
    private string name;
    private string description;
    private SmartCollectionFolderScope folderScope;
    private bool saveSort;

    public SmartCollectionEditorViewModel(
        string title,
        string name,
        string description,
        SmartCollectionFolderScope folderScope,
        bool saveSort,
        bool canUseSpecificFolder)
    {
        Title = title;
        this.name = name;
        this.description = description;
        this.folderScope = folderScope;
        this.saveSort = saveSort;
        CanUseSpecificFolder = canUseSpecificFolder;
        FolderScopes = canUseSpecificFolder
            ? [
                new(SmartCollectionFolderScope.EntireLibrary, "Entire library"),
                new(SmartCollectionFolderScope.CurrentFolder, "Current folder at execution time"),
                new(SmartCollectionFolderScope.SpecificFolder, "Specific saved folder")]
            : [
                new(SmartCollectionFolderScope.EntireLibrary, "Entire library"),
                new(SmartCollectionFolderScope.CurrentFolder, "Current folder at execution time")];
        if (!canUseSpecificFolder && folderScope == SmartCollectionFolderScope.SpecificFolder)
        {
            this.folderScope = SmartCollectionFolderScope.EntireLibrary;
        }
    }

    public string Title { get; }
    public IReadOnlyList<SmartCollectionFolderScopeOption> FolderScopes { get; }
    public bool CanUseSpecificFolder { get; }
    public string CriteriaSummary { get; init; } = string.Empty;

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public SmartCollectionFolderScope FolderScope
    {
        get => folderScope;
        set => SetProperty(ref folderScope, value);
    }

    public bool SaveSort
    {
        get => saveSort;
        set => SetProperty(ref saveSort, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);
}
