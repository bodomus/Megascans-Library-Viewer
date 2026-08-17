using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed record UnrealMaterialProfileOption(UnrealMaterialProfile Profile, string Label);
public sealed record UnrealImportTextureRow(UnrealImportSemanticRole Role, TextureMapType MapType, int? Resolution, string Format, string SourcePath);
public sealed record UnrealImportLodRow(string Variant, int Lod, MeshFormat Format, string SourcePath);
public sealed record UnrealImportValidationRow(UnrealImportValidationSeverity Severity, UnrealImportPackageIssueCode Code, string Message, string? RelatedPath);

public sealed class UnrealImportParameterRow(
    UnrealImportSemanticRole role,
    bool isEnabled,
    string parameterName,
    Action changed) : ObservableObject
{
    private bool isEnabled = isEnabled;
    private string parameterName = parameterName;
    public UnrealImportSemanticRole Role { get; } = role;
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                changed();
            }
        }
    }

    public string ParameterName
    {
        get => parameterName;
        set
        {
            if (SetProperty(ref parameterName, value))
            {
                changed();
            }
        }
    }
}

public sealed class UnrealImportAssetTypeOption(string assetType, bool isSelected, Action changed) : ObservableObject
{
    private bool isSelected = isSelected;
    public string AssetType { get; } = assetType;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                changed();
            }
        }
    }
}

public sealed class UnrealImportPackageViewModel : ObservableObject
{
    private readonly IUnrealMaterialProfileStore profileStore;
    private readonly IUnrealImportPackageExportService exportService;
    private readonly SettingsViewModel settings;
    private readonly ApplicationBuildInfo buildInfo;
    private readonly ILogger<UnrealImportPackageViewModel> logger;
    private bool isExporting;
    private string destinationBasePath;
    private string destinationPath = string.Empty;
    private UnrealMaterialProfile? selectedProfile;
    private bool importLods = true;
    private bool enableNanite;
    private bool createMaterialInstance = true;
    private string statusText = "Review the package before export.";
    private string editableProfileName = string.Empty;
    private string editableProfileDescription = string.Empty;
    private string editableMasterMaterialPath = string.Empty;
    private string editableMaterialInstancePrefix = "MI_";
    private bool editableDefaultImportLods = true;
    private bool editableDefaultEnableNanite;
    private bool editableDefaultCreateMaterialInstance = true;
    private bool suppressRefresh;
    private UnrealImportPackage package;

    public UnrealImportPackageViewModel(
        AssetSummary asset,
        IUnrealMaterialProfileStore profileStore,
        IUnrealImportPackageExportService exportService,
        SettingsViewModel settings,
        ApplicationBuildInfo buildInfo,
        ILogger<UnrealImportPackageViewModel> logger)
    {
        Asset = asset;
        this.profileStore = profileStore;
        this.exportService = exportService;
        this.settings = settings;
        this.buildInfo = buildInfo;
        this.logger = logger;
        destinationBasePath = settings.UnrealImportPackageSettings.DefaultDestinationBasePath;
        package = EmptyPackage(asset);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);
        NewProfileCommand = new AsyncRelayCommand(CreateNewProfileAsync);
        DuplicateProfileCommand = new AsyncRelayCommand(DuplicateSelectedProfileAsync, () => SelectedProfile is not null);
        SaveProfileCommand = new AsyncRelayCommand(SaveSelectedUserProfileAsync, () => SelectedProfile?.IsBuiltIn == false);
        DeleteProfileCommand = new AsyncRelayCommand(DeleteSelectedUserProfileAsync, () => SelectedProfile?.IsBuiltIn == false);
    }

    public AssetSummary Asset { get; }
    public ObservableCollection<UnrealMaterialProfileOption> Profiles { get; } = [];
    public ObservableCollection<UnrealImportTextureRow> Textures { get; } = [];
    public ObservableCollection<UnrealImportLodRow> Lods { get; } = [];
    public ObservableCollection<UnrealImportValidationRow> ValidationIssues { get; } = [];
    public ObservableCollection<UnrealImportParameterRow> ParameterMappings { get; } = [];
    public ObservableCollection<UnrealImportAssetTypeOption> AssetTypeOptions { get; } = [];
    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand NewProfileCommand { get; }
    public AsyncRelayCommand DuplicateProfileCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand DeleteProfileCommand { get; }

    public string AssetName => Asset.Name;
    public string AssetType => Asset.AssetType;
    public string ReadinessDisplay => Asset.UnrealReadiness.Status switch
    {
        UnrealReadinessStatus.Ready => "UE Ready",
        UnrealReadinessStatus.ReadyWithWarnings => "UE Ready With Warnings",
        UnrealReadinessStatus.NotReady => "Not UE Ready",
        UnrealReadinessStatus.NotApplicable => "Not Applicable",
        UnrealReadinessStatus.Unknown => "Unknown",
        _ => Asset.UnrealReadiness.Status.ToString()
    };
    public int SchemaVersion => Package.SchemaVersion;
    public string PackageId => Package.PackageId;
    public string FinalContentPath => Package.Destination.ContentPath;
    public string SanitizedAssetName => Package.Destination.AssetBaseName;
    public string MaterialInstanceName => Package.Material.MaterialInstanceName;
    public string MasterMaterialPath => Package.Material.MasterMaterialPath ?? string.Empty;
    public string PrimaryVariant => Package.Mesh?.PrimaryVariant ?? "None";
    public string JsonPreview => exportService.Serialize(Package);
    public UnrealImportPackage Package => package;
    public bool IsEditableProfile => SelectedProfile?.IsBuiltIn == false;

    public UnrealMaterialProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (SetProperty(ref selectedProfile, value))
            {
                ApplySelectedProfileDefaults();
                RefreshPackage();
                NotifyProfileCommands();
            }
        }
    }

    public string DestinationBasePath
    {
        get => destinationBasePath;
        set
        {
            if (SetProperty(ref destinationBasePath, value))
            {
                RefreshPackage();
            }
        }
    }

    public string DestinationPath
    {
        get => destinationPath;
        set
        {
            if (SetProperty(ref destinationPath, value))
            {
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ImportLods
    {
        get => importLods;
        set
        {
            if (SetProperty(ref importLods, value))
            {
                RefreshPackage();
            }
        }
    }

    public bool EnableNanite
    {
        get => enableNanite;
        set
        {
            if (SetProperty(ref enableNanite, value))
            {
                RefreshPackage();
            }
        }
    }

    public bool CreateMaterialInstance
    {
        get => createMaterialInstance;
        set
        {
            if (SetProperty(ref createMaterialInstance, value))
            {
                RefreshPackage();
            }
        }
    }

    public bool IsExporting
    {
        get => isExporting;
        private set
        {
            if (SetProperty(ref isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EditableProfileName
    {
        get => editableProfileName;
        set
        {
            if (SetProperty(ref editableProfileName, value))
            {
                RefreshPackage();
            }
        }
    }

    public string EditableProfileDescription
    {
        get => editableProfileDescription;
        set
        {
            if (SetProperty(ref editableProfileDescription, value))
            {
                RefreshPackage();
            }
        }
    }

    public string EditableMasterMaterialPath
    {
        get => editableMasterMaterialPath;
        set
        {
            if (SetProperty(ref editableMasterMaterialPath, value))
            {
                RefreshPackage();
            }
        }
    }

    public string EditableMaterialInstancePrefix
    {
        get => editableMaterialInstancePrefix;
        set
        {
            if (SetProperty(ref editableMaterialInstancePrefix, value))
            {
                RefreshPackage();
            }
        }
    }

    public bool EditableDefaultImportLods
    {
        get => editableDefaultImportLods;
        set
        {
            if (SetProperty(ref editableDefaultImportLods, value))
            {
                ImportLods = value;
                RefreshPackage();
            }
        }
    }

    public bool EditableDefaultEnableNanite
    {
        get => editableDefaultEnableNanite;
        set
        {
            if (SetProperty(ref editableDefaultEnableNanite, value))
            {
                EnableNanite = value;
                RefreshPackage();
            }
        }
    }

    public bool EditableDefaultCreateMaterialInstance
    {
        get => editableDefaultCreateMaterialInstance;
        set
        {
            if (SetProperty(ref editableDefaultCreateMaterialInstance, value))
            {
                CreateMaterialInstance = value;
                RefreshPackage();
            }
        }
    }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool HasErrors => Package.Validation.HasErrors;
    public bool CanExport => !IsExporting && !HasErrors && !string.IsNullOrWhiteSpace(DestinationPath);
    public string DefaultFileName => UnrealImportPackagePolicy.DefaultFileName(Asset);
    public string DestinationExtension => DefaultFileName.EndsWith(".scanvault-ue.json", StringComparison.Ordinal)
        ? "scanvault-ue.json"
        : "json";
    public string ExportFolder => string.IsNullOrWhiteSpace(DestinationPath)
        ? settings.UnrealImportPackageSettings.LastManifestExportFolder
        : Path.GetDirectoryName(DestinationPath) ?? string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var profiles = UnrealMaterialProfilePolicy.MergeWithBuiltIns(
            await profileStore.LoadUserProfilesAsync(cancellationToken).ConfigureAwait(true));
        RebuildProfiles(profiles);
        SelectedProfile = UnrealMaterialProfilePolicy.SelectDefault(
            Profiles.Select(static option => option.Profile).ToArray(),
            Asset.AssetType,
            settings.UnrealImportPackageSettings.DefaultProfilesByAssetType);
        DestinationPath = Path.Combine(
            string.IsNullOrWhiteSpace(settings.UnrealImportPackageSettings.LastManifestExportFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : settings.UnrealImportPackageSettings.LastManifestExportFolder,
            DefaultFileName);
        ApplicationLog.UnrealImportPackagePreviewCreated(logger, Asset.Id, Asset.JsonPath, Asset.UnrealReadiness.Status);
    }

    public async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (!CanExport)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        IsExporting = true;
        try
        {
            var result = await exportService.ExportAsync(Package, DestinationPath, cancellationToken).ConfigureAwait(true);
            var folder = Path.GetDirectoryName(result.DestinationPath) ?? string.Empty;
            await settings.SaveUnrealImportPackageSettingsAsync(
                settings.UnrealImportPackageSettings with
                {
                    DefaultDestinationBasePath = DestinationBasePath,
                    LastManifestExportFolder = folder,
                    DefaultProfilesByAssetType = UpsertDefaultProfile(settings.UnrealImportPackageSettings.DefaultProfilesByAssetType)
                },
                cancellationToken).ConfigureAwait(true);
            StatusText = $"Package exported ({result.OutputSizeBytes:N0} bytes) in {stopwatch.Elapsed:g}.";
            ApplicationLog.UnrealImportPackageExported(logger, Asset.Id, Package.PackageId, result.DestinationPath, result.OutputSizeBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Package export cancelled. No partial manifest was published.";
        }
        catch (Exception exception)
        {
            StatusText = $"Package export failed: {exception.Message}";
            ApplicationLog.UnrealImportPackageValidationFailed(logger, Asset.Id, Package.PackageId, exception);
        }
        finally
        {
            IsExporting = false;
        }
    }

    public string CopyManifest()
    {
        var manifest = exportService.Serialize(Package);
        StatusText = "Manifest copied.";
        return manifest;
    }

    private Task CreateNewProfileAsync(CancellationToken cancellationToken)
    {
        var profile = UnrealMaterialProfilePolicy.CreateUserProfile(
            $"user-{Guid.NewGuid():N}",
            $"New {Asset.AssetType} Profile",
            Asset.AssetType,
            Profiles.Select(static option => option.Profile).FirstOrDefault(static profile => profile.IsBuiltIn));
        Profiles.Add(new(profile, profile.Name));
        SelectedProfile = profile;
        StatusText = "New user profile created. Save it to persist.";
        return Task.CompletedTask;
    }

    private async Task DuplicateSelectedProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var copy = UnrealMaterialProfilePolicy.Duplicate(
            DraftProfile(),
            $"user-{Guid.NewGuid():N}",
            $"{SelectedProfile.Name} Copy");
        Profiles.Add(new(copy, copy.Name));
        SelectedProfile = copy;
        StatusText = "User profile copy created. Save it to persist.";
        ApplicationLog.UnrealMaterialProfileChanged(logger, "created", copy.Id, copy.Name);
    }

    private async Task SaveSelectedUserProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null || SelectedProfile.IsBuiltIn)
        {
            return;
        }

        var updated = DraftProfile();
        var userProfiles = Profiles.Select(static option => option.Profile)
            .Where(profile => !profile.IsBuiltIn && !StringComparer.OrdinalIgnoreCase.Equals(profile.Id, updated.Id))
            .Append(updated)
            .ToArray();
        var validation = UnrealMaterialProfilePolicy.Validate(updated, userProfiles.Where(profile => !ReferenceEquals(profile, updated)));
        if (validation.Any(static issue => issue.Severity == UnrealImportValidationSeverity.Error))
        {
            StatusText = string.Join("; ", validation.Select(static issue => issue.Message));
            return;
        }

        await profileStore.SaveUserProfilesAsync(userProfiles, cancellationToken).ConfigureAwait(true);
        RebuildProfiles(UnrealMaterialProfilePolicy.MergeWithBuiltIns(userProfiles));
        SelectedProfile = updated;
        StatusText = "User profile saved.";
        ApplicationLog.UnrealMaterialProfileChanged(logger, "updated", updated.Id, updated.Name);
    }

    private async Task DeleteSelectedUserProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null || SelectedProfile.IsBuiltIn)
        {
            return;
        }

        var deleted = SelectedProfile;
        var userProfiles = Profiles.Select(static option => option.Profile)
            .Where(profile => !profile.IsBuiltIn && !StringComparer.OrdinalIgnoreCase.Equals(profile.Id, deleted.Id))
            .ToArray();
        await profileStore.SaveUserProfilesAsync(userProfiles, cancellationToken).ConfigureAwait(true);
        RebuildProfiles(UnrealMaterialProfilePolicy.MergeWithBuiltIns(userProfiles));
        SelectedProfile = UnrealMaterialProfilePolicy.SelectDefault(Profiles.Select(static option => option.Profile).ToArray(), Asset.AssetType, []);
        ApplicationLog.UnrealMaterialProfileChanged(logger, "deleted", deleted.Id, deleted.Name);
    }

    private void RebuildProfiles(IEnumerable<UnrealMaterialProfile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in UnrealMaterialProfilePolicy.CompatibleProfiles(profiles, Asset.AssetType))
        {
            Profiles.Add(new(profile, profile.IsBuiltIn ? $"{profile.Name} (built-in)" : profile.Name));
        }
    }

    private void ApplySelectedProfileDefaults()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        suppressRefresh = true;
        try
        {
            ImportLods = SelectedProfile.DefaultOptions.ImportLods;
            EnableNanite = SelectedProfile.DefaultOptions.EnableNanite;
            CreateMaterialInstance = SelectedProfile.CreateMaterialInstance && SelectedProfile.DefaultOptions.CreateMaterialInstance;
            EditableProfileName = SelectedProfile.Name;
            EditableProfileDescription = SelectedProfile.Description ?? string.Empty;
            EditableMasterMaterialPath = SelectedProfile.MasterMaterialPath ?? string.Empty;
            EditableMaterialInstancePrefix = SelectedProfile.MaterialInstancePrefix;
            editableDefaultImportLods = SelectedProfile.DefaultOptions.ImportLods;
            editableDefaultEnableNanite = SelectedProfile.DefaultOptions.EnableNanite;
            editableDefaultCreateMaterialInstance = SelectedProfile.DefaultOptions.CreateMaterialInstance;
            OnPropertyChanged(nameof(EditableDefaultImportLods));
            OnPropertyChanged(nameof(EditableDefaultEnableNanite));
            OnPropertyChanged(nameof(EditableDefaultCreateMaterialInstance));
            RebuildProfileEditorCollections(SelectedProfile);
            OnPropertyChanged(nameof(IsEditableProfile));
        }
        finally
        {
            suppressRefresh = false;
        }
    }

    private void RefreshPackage()
    {
        if (suppressRefresh)
        {
            return;
        }

        if (SelectedProfile is null)
        {
            return;
        }

        package = UnrealImportPackagePolicy.Create(
            new(
                Asset,
                DraftProfile(),
                DestinationBasePath,
                new(ImportLods, EnableNanite, CreateMaterialInstance, false),
                buildInfo.ApplicationVersion,
                buildInfo.CommitSha,
                DateTimeOffset.UtcNow,
                ValidateSourceExists: true),
            File.Exists);
        RebuildPackageCollections();
        OnPropertyChanged(nameof(Package));
        OnPropertyChanged(nameof(PackageId));
        OnPropertyChanged(nameof(FinalContentPath));
        OnPropertyChanged(nameof(SanitizedAssetName));
        OnPropertyChanged(nameof(MaterialInstanceName));
        OnPropertyChanged(nameof(MasterMaterialPath));
        OnPropertyChanged(nameof(PrimaryVariant));
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void RebuildPackageCollections()
    {
        Textures.Clear();
        foreach (var texture in Package.Textures)
        {
            Textures.Add(new(texture.Role, texture.MapType, texture.Resolution, texture.Format, texture.SourcePath));
        }

        Lods.Clear();
        foreach (var lod in Package.Mesh?.Lods ?? [])
        {
            Lods.Add(new(lod.Variant, lod.Lod, lod.Format, lod.SourcePath));
        }

        ValidationIssues.Clear();
        foreach (var issue in Package.Validation.Issues)
        {
            ValidationIssues.Add(new(issue.Severity, issue.Code, issue.Message, issue.RelatedPath));
        }

    }

    private void RebuildProfileEditorCollections(UnrealMaterialProfile profile)
    {
        AssetTypeOptions.Clear();
        foreach (var assetType in UnrealMaterialProfilePolicy.SupportedAssetTypes)
        {
            AssetTypeOptions.Add(new(assetType, profile.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase), RefreshPackage));
        }

        ParameterMappings.Clear();
        var mappings = profile.TextureParameterMappings
            .GroupBy(static mapping => mapping.Role)
            .ToDictionary(static group => group.Key, static group => group.First().ParameterName);
        foreach (var mapping in UnrealMaterialProfilePolicy.DefaultTextureParameterMappings())
        {
            var isEnabled = mappings.TryGetValue(mapping.Role, out var parameterName);
            ParameterMappings.Add(new(
                mapping.Role,
                isEnabled,
                isEnabled ? parameterName ?? mapping.ParameterName : mapping.ParameterName,
                RefreshPackage));
        }
    }

    private UnrealMaterialProfile DraftProfile()
    {
        var profile = SelectedProfile ?? UnrealMaterialProfilePolicy.CreateUserProfile(
            $"user-{Guid.NewGuid():N}",
            $"New {Asset.AssetType} Profile",
            Asset.AssetType);
        var selectedTypes = AssetTypeOptions
            .Where(static option => option.IsSelected)
            .Select(static option => option.AssetType)
            .ToArray();
        var mappings = ParameterMappings
            .Where(static row => row.IsEnabled)
            .Select(static row => new UnrealImportTextureParameterMapping(row.Role, row.ParameterName.Trim()))
            .ToArray();
        return profile with
        {
            Name = EditableProfileName.Trim(),
            Description = string.IsNullOrWhiteSpace(EditableProfileDescription) ? null : EditableProfileDescription.Trim(),
            AssetTypes = selectedTypes,
            MasterMaterialPath = string.IsNullOrWhiteSpace(EditableMasterMaterialPath) ? null : EditableMasterMaterialPath.Trim(),
            CreateMaterialInstance = EditableDefaultCreateMaterialInstance,
            MaterialInstancePrefix = EditableMaterialInstancePrefix.Trim(),
            TextureParameterMappings = mappings,
            DefaultOptions = new(EditableDefaultImportLods, EditableDefaultEnableNanite, EditableDefaultCreateMaterialInstance, false),
            IsBuiltIn = profile.IsBuiltIn
        };
    }

    private IReadOnlyList<UnrealImportDefaultProfile> UpsertDefaultProfile(IReadOnlyList<UnrealImportDefaultProfile> defaults)
    {
        if (SelectedProfile is null)
        {
            return defaults;
        }

        return defaults
            .Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item.AssetType, Asset.AssetType))
            .Append(new(Asset.AssetType, SelectedProfile.Id))
            .OrderBy(static item => item.AssetType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void NotifyProfileCommands()
    {
        DuplicateProfileCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEditableProfile));
    }

    private static UnrealImportPackage EmptyPackage(AssetSummary asset) =>
        new(
            UnrealImportPackageSchema.CurrentVersion,
            string.Empty,
            new("ScanVault", string.Empty, string.Empty, DateTimeOffset.UnixEpoch),
            new(asset.Id, asset.Name, asset.AssetType, asset.JsonPath, asset.AssetFolderPath, asset.LastWriteTimeUtc),
            new(asset.UnrealReadiness.Status, asset.UnrealReadiness.ReadinessRuleVersion, asset.UnrealReadiness.BlockingCount, asset.UnrealReadiness.WarningCount, []),
            UnrealImportDestinationPolicy.Create(UnrealImportPackageSettings.Default.DefaultDestinationBasePath, asset),
            null,
            [],
            new(string.Empty, string.Empty, null, [], null, string.Empty, "MI_", [], false),
            new(true, false, true, false),
            new([]));
}
