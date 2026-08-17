using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.Tests;

public sealed class UnrealImportPackageViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ScanVault.App.Tests",
        Guid.NewGuid().ToString("N"));

    public UnrealImportPackageViewModelTests() => Directory.CreateDirectory(root);

    // Unit test: ready assets create a package and enable export when a destination is present.
    [Fact]
    public async Task ReadyAssetEnablesPackageExport()
    {
        var service = new RecordingPackageExportService();
        var viewModel = CreateViewModel(CreateAsset("ready", "Surface"), service: service);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DestinationPath = Path.Combine(root, "ready.scanvault-ue.json");

        Assert.True(viewModel.CanExport);
        Assert.Equal(UnrealReadinessStatus.Ready, viewModel.Package.Readiness.Status);
        Assert.Contains(viewModel.Textures, static row => row.Role == UnrealImportSemanticRole.BaseColor);
    }

    // Unit test: NotReady and stale assets surface validation errors and disable export.
    [Fact]
    public async Task BlockedReadinessDisablesPackageExport()
    {
        var notReady = CreateViewModel(CreateAsset("not-ready", "Surface", includeAlbedo: false));
        await notReady.LoadAsync(CancellationToken.None);
        notReady.DestinationPath = Path.Combine(root, "not-ready.scanvault-ue.json");

        var staleAsset = CreateAsset("stale", "Surface") with { UnrealReadiness = UnrealReadinessEvaluation.Unknown };
        var stale = CreateViewModel(staleAsset);
        await stale.LoadAsync(CancellationToken.None);
        stale.DestinationPath = Path.Combine(root, "stale.scanvault-ue.json");

        Assert.False(notReady.CanExport);
        Assert.Contains(notReady.ValidationIssues, static issue => issue.Code == UnrealImportPackageIssueCode.ReadinessBlocked);
        Assert.False(stale.CanExport);
        Assert.Contains(stale.ValidationIssues, static issue => issue.Code == UnrealImportPackageIssueCode.ReadinessStale);
    }

    // Unit test: changing destination, profile fields, and options refreshes the package model.
    [Fact]
    public async Task DestinationProfileAndOptionChangesRefreshPackage()
    {
        var viewModel = CreateViewModel(CreateAsset("refresh", "3D Asset", includeMesh: true));
        await viewModel.LoadAsync(CancellationToken.None);
        var originalPackageId = viewModel.PackageId;

        viewModel.DestinationBasePath = "/Game/Changed";
        viewModel.EditableMaterialInstancePrefix = "CustomMI";
        viewModel.EnableNanite = !viewModel.EnableNanite;

        Assert.StartsWith("/Game/Changed/", viewModel.FinalContentPath, StringComparison.Ordinal);
        Assert.StartsWith("CustomMI_", viewModel.MaterialInstanceName, StringComparison.Ordinal);
        Assert.NotEqual(originalPackageId, viewModel.PackageId);
    }

    // Unit test: Copy Manifest delegates to the configured package serializer.
    [Fact]
    public async Task CopyManifestUsesExportServiceSerializer()
    {
        var service = new RecordingPackageExportService { SerializedText = "{\"schemaVersion\":1}" };
        var viewModel = CreateViewModel(CreateAsset("copy", "Surface"), service: service);
        await viewModel.LoadAsync(CancellationToken.None);

        var manifest = viewModel.CopyManifest();

        Assert.Equal("{\"schemaVersion\":1}", manifest);
        Assert.True(service.SerializeCalled);
    }

    // Unit test: Export passes the generated package to the export service and saves UE import settings.
    [Fact]
    public async Task ExportCallsPackageExportServiceAndPersistsSettings()
    {
        var service = new RecordingPackageExportService();
        var settingsStore = new MemorySettingsStore(new(root));
        var viewModel = CreateViewModel(CreateAsset("export", "Surface"), settingsStore, service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.DestinationBasePath = "/Game/Exports";
        viewModel.DestinationPath = Path.Combine(root, "export.scanvault-ue.json");

        await viewModel.ExportAsync(CancellationToken.None);

        Assert.Equal(viewModel.Package.PackageId, service.ExportedPackage?.PackageId);
        Assert.Equal("/Game/Exports", settingsStore.Value.UnrealImportPackageOrDefault.DefaultDestinationBasePath);
        Assert.Equal(root, settingsStore.Value.UnrealImportPackageOrDefault.LastManifestExportFolder);
    }

    // Unit test: built-in profiles can be duplicated, saved as user profiles, and deleted.
    [Fact]
    public async Task ProfileCrudCommandStatesFollowBuiltInAndUserProfiles()
    {
        var store = new MemoryProfileStore();
        var viewModel = CreateViewModel(CreateAsset("profile", "Surface"), profileStore: store);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.SaveProfileCommand.CanExecute(null));
        await viewModel.DuplicateProfileCommand.ExecuteAsync(CancellationToken.None);
        Assert.True(viewModel.SaveProfileCommand.CanExecute(null));

        viewModel.EditableProfileName = "Custom Surface";
        await viewModel.SaveProfileCommand.ExecuteAsync(CancellationToken.None);
        Assert.Contains(store.Profiles, static profile => profile.Name == "Custom Surface");

        await viewModel.DeleteProfileCommand.ExecuteAsync(CancellationToken.None);
        Assert.Empty(store.Profiles);
    }

    // Unit test: New Profile creates a mutable unsaved user profile from the current asset type template.
    [Fact]
    public async Task NewProfileCreatesEditableProfileWithoutImmediatePersistence()
    {
        var store = new MemoryProfileStore();
        var viewModel = CreateViewModel(CreateAsset("new-profile", "Surface"), profileStore: store);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NewProfileCommand.ExecuteAsync(CancellationToken.None);

        Assert.True(viewModel.IsEditableProfile);
        Assert.StartsWith("New Surface Profile", viewModel.EditableProfileName, StringComparison.Ordinal);
        Assert.Empty(store.Profiles);
        Assert.Contains(viewModel.AssetTypeOptions, static option => option.AssetType == "Surface" && option.IsSelected);
    }

    // Unit test: profile asset types, parameter mappings, and default options are saved to the user profile store.
    [Fact]
    public async Task EditableProfileContractFieldsSaveToProfileStore()
    {
        var store = new MemoryProfileStore();
        var viewModel = CreateViewModel(CreateAsset("contract", "Surface"), profileStore: store);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NewProfileCommand.ExecuteAsync(CancellationToken.None);

        viewModel.EditableProfileName = "Contract Profile";
        viewModel.EditableProfileDescription = "Editable contract";
        viewModel.AssetTypeOptions.Single(static option => option.AssetType == "Decal").IsSelected = true;
        viewModel.ParameterMappings.Single(static row => row.Role == UnrealImportSemanticRole.Normal).ParameterName = "CustomNormal";
        viewModel.EditableDefaultImportLods = false;
        viewModel.EditableDefaultEnableNanite = true;
        viewModel.EditableDefaultCreateMaterialInstance = false;
        await viewModel.SaveProfileCommand.ExecuteAsync(CancellationToken.None);

        var saved = Assert.Single(store.Profiles);
        Assert.Equal("Contract Profile", saved.Name);
        Assert.Equal("Editable contract", saved.Description);
        Assert.Contains("Surface", saved.AssetTypes);
        Assert.Contains("Decal", saved.AssetTypes);
        Assert.Contains(saved.TextureParameterMappings, static mapping =>
            mapping.Role == UnrealImportSemanticRole.Normal && mapping.ParameterName == "CustomNormal");
        Assert.False(saved.DefaultOptions.ImportLods);
        Assert.True(saved.DefaultOptions.EnableNanite);
        Assert.False(saved.DefaultOptions.CreateMaterialInstance);
    }

    // Unit test: invalid profile edits block save and keep storage unchanged.
    [Fact]
    public async Task InvalidProfileEditBlocksSave()
    {
        var store = new MemoryProfileStore();
        var viewModel = CreateViewModel(CreateAsset("invalid-profile", "Surface"), profileStore: store);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NewProfileCommand.ExecuteAsync(CancellationToken.None);
        viewModel.EditableProfileName = " ";
        foreach (var option in viewModel.AssetTypeOptions)
        {
            option.IsSelected = false;
        }

        await viewModel.SaveProfileCommand.ExecuteAsync(CancellationToken.None);

        Assert.Empty(store.Profiles);
        Assert.Contains("Profile name is required", viewModel.StatusText, StringComparison.Ordinal);
    }

    // Unit test: unsaved material contract edits refresh the preview and PackageId immediately.
    [Fact]
    public async Task UnsavedMaterialContractEditsRefreshPackageId()
    {
        var viewModel = CreateViewModel(CreateAsset("identity-refresh", "Surface"));
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NewProfileCommand.ExecuteAsync(CancellationToken.None);
        var original = viewModel.PackageId;

        viewModel.EditableMasterMaterialPath = "/Game/Materials/M_Changed";
        var afterMaster = viewModel.PackageId;
        viewModel.ParameterMappings.Single(static row => row.Role == UnrealImportSemanticRole.Normal).ParameterName = "ChangedNormal";

        Assert.NotEqual(original, afterMaster);
        Assert.NotEqual(afterMaster, viewModel.PackageId);
        Assert.Contains(viewModel.Package.Material.TextureParameterMappings, static mapping =>
            mapping.Role == UnrealImportSemanticRole.Normal && mapping.ParameterName == "ChangedNormal");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private UnrealImportPackageViewModel CreateViewModel(
        AssetSummary asset,
        MemorySettingsStore? settingsStore = null,
        RecordingPackageExportService? service = null,
        MemoryProfileStore? profileStore = null)
    {
        var store = settingsStore ?? new MemorySettingsStore(new(root));
        var settings = new SettingsViewModel(store);
        settings.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new(
            asset,
            profileStore ?? new MemoryProfileStore(),
            service ?? new RecordingPackageExportService(),
            settings,
            ApplicationBuildInfo.Create("1.0.0", "1.0.0+abcdef1", "abcdef1", "Test"),
            NullLogger<UnrealImportPackageViewModel>.Instance);
    }

    private AssetSummary CreateAsset(
        string id,
        string assetType,
        bool includeAlbedo = true,
        bool includeMesh = false)
    {
        var folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        var jsonPath = Path.Combine(folder, $"{id}.json");
        File.WriteAllText(jsonPath, "{}");
        var candidates = new List<AssetContentFileCandidate>();
        if (includeMesh)
        {
            var mesh0 = WriteNested(folder, "Var1", $"{id}_LOD0.fbx");
            var mesh1 = WriteNested(folder, "Var1", $"{id}_LOD1.fbx");
            candidates.Add(new(mesh0, Path.GetRelativePath(folder, mesh0)));
            candidates.Add(new(mesh1, Path.GetRelativePath(folder, mesh1)));
        }

        if (includeAlbedo)
        {
            var albedo = WriteFile(folder, $"{id}_4K_Albedo.jpg");
            candidates.Add(new(albedo, Path.GetRelativePath(folder, albedo)));
        }

        var normal = WriteFile(folder, $"{id}_4K_Normal.jpg");
        var roughness = WriteFile(folder, $"{id}_4K_Roughness.jpg");
        var displacement = WriteFile(folder, $"{id}_4K_Displacement.exr");
        candidates.Add(new(normal, Path.GetRelativePath(folder, normal)));
        candidates.Add(new(roughness, Path.GetRelativePath(folder, roughness)));
        candidates.Add(new(displacement, Path.GetRelativePath(folder, displacement)));
        var asset = new AssetSummary(
            id,
            $"Asset {id}",
            assetType,
            folder,
            jsonPath,
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
            Content = AssetContentAnalyzer.Analyze(assetType, candidates)
        };
        return UnrealReadinessPolicy.EnsureCurrent(asset, DateTimeOffset.UtcNow);
    }

    private static string WriteFile(string folder, string fileName) =>
        WriteNested(folder, fileName);

    private static string WriteNested(string folder, params string[] segments)
    {
        var path = segments.Aggregate(folder, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
        return path;
    }

    private sealed class MemorySettingsStore(LibrarySettings settings) : ISettingsStore
    {
        public LibrarySettings Value { get; private set; } = settings;
        public Task<LibrarySettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Value);
        public Task SaveAsync(LibrarySettings settings, CancellationToken cancellationToken)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryProfileStore : IUnrealMaterialProfileStore
    {
        public IReadOnlyList<UnrealMaterialProfile> Profiles { get; private set; } = [];
        public Task<IReadOnlyList<UnrealMaterialProfile>> LoadUserProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Profiles);
        public Task SaveUserProfilesAsync(IReadOnlyList<UnrealMaterialProfile> profiles, CancellationToken cancellationToken)
        {
            Profiles = profiles.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPackageExportService : IUnrealImportPackageExportService
    {
        public string SerializedText { get; init; } = "{}";
        public bool SerializeCalled { get; private set; }
        public UnrealImportPackage? ExportedPackage { get; private set; }
        public string Serialize(UnrealImportPackage package)
        {
            SerializeCalled = true;
            return SerializedText;
        }

        public Task<UnrealImportPackageExportResult> ExportAsync(
            UnrealImportPackage package,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            ExportedPackage = package;
            return Task.FromResult(new UnrealImportPackageExportResult(destinationPath, 123));
        }
    }
}
