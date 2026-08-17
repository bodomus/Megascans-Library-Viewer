using System.Text.Json;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.UnrealImport;

namespace ScanVault.Infrastructure.Tests;

public sealed class UnrealImportPackageInfrastructureTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    // Integration test: user material profiles persist separately from built-in definitions.
    [Fact]
    public async Task UserMaterialProfilesPersistAcrossRestart()
    {
        using var temporary = new TemporaryDirectory();
        var paths = Paths(temporary);
        var store = new JsonUnrealMaterialProfileStore(paths);
        var profile = UnrealMaterialProfilePolicy.Duplicate(
            UnrealMaterialProfilePolicy.BuiltInProfiles[0],
            "user-profile",
            "User Surface");

        await store.SaveUserProfilesAsync([profile], CancellationToken.None);
        var reloaded = await new JsonUnrealMaterialProfileStore(paths).LoadUserProfilesAsync(CancellationToken.None);

        var actual = Assert.Single(reloaded);
        Assert.Equal("user-profile", actual.Id);
        Assert.False(actual.IsBuiltIn);
    }

    // Integration test: all editable material profile fields survive a profile store restart.
    [Fact]
    public async Task EditableMaterialProfileFieldsRoundTrip()
    {
        using var temporary = new TemporaryDirectory();
        var paths = Paths(temporary);
        var store = new JsonUnrealMaterialProfileStore(paths);
        var profile = new UnrealMaterialProfile(
            "user-full",
            "Full User Profile",
            "Round-trip profile",
            ["Surface", "Decal"],
            "/Game/Materials/M_Custom",
            true,
            "CustomMI_",
            [
                new(UnrealImportSemanticRole.BaseColor, "MyBaseColor"),
                new(UnrealImportSemanticRole.Normal, "MyNormal")
            ],
            new(ImportLods: false, EnableNanite: true, CreateMaterialInstance: false, ReadinessOverride: false),
            IsBuiltIn: false);

        await store.SaveUserProfilesAsync([profile, UnrealMaterialProfilePolicy.BuiltInProfiles[0]], CancellationToken.None);
        var loaded = await new JsonUnrealMaterialProfileStore(paths).LoadUserProfilesAsync(CancellationToken.None);

        var actual = Assert.Single(loaded);
        Assert.Equal(profile.Name, actual.Name);
        Assert.Equal(profile.Description, actual.Description);
        Assert.Equal(profile.AssetTypes, actual.AssetTypes);
        Assert.Equal(profile.MasterMaterialPath, actual.MasterMaterialPath);
        Assert.Equal(profile.MaterialInstancePrefix, actual.MaterialInstancePrefix);
        Assert.Equal(profile.TextureParameterMappings, actual.TextureParameterMappings);
        Assert.Equal(2, actual.TextureParameterMappings.Count);
        Assert.DoesNotContain(actual.TextureParameterMappings, static mapping => mapping.Role == UnrealImportSemanticRole.Displacement);
        Assert.Equal(profile.DefaultOptions, actual.DefaultOptions);
    }

    // Integration test: built-in profiles are never persisted as mutable user definitions.
    [Fact]
    public async Task BuiltInProfilesAreNotPersistedAsUserDefinitions()
    {
        using var temporary = new TemporaryDirectory();
        var store = new JsonUnrealMaterialProfileStore(Paths(temporary));

        await store.SaveUserProfilesAsync(UnrealMaterialProfilePolicy.BuiltInProfiles, CancellationToken.None);
        var loaded = await store.LoadUserProfilesAsync(CancellationToken.None);

        Assert.Empty(loaded);
    }

    // Integration test: malformed profile storage is backed up and fails safely.
    [Fact]
    public async Task MalformedProfileStorageReturnsEmptyAndCreatesBackup()
    {
        using var temporary = new TemporaryDirectory();
        var paths = Paths(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.UnrealMaterialProfilesPath)!);
        await File.WriteAllTextAsync(paths.UnrealMaterialProfilesPath, "{not-json", CancellationToken.None);
        var store = new JsonUnrealMaterialProfileStore(paths);

        var loaded = await store.LoadUserProfilesAsync(CancellationToken.None);

        Assert.Empty(loaded);
        Assert.False(File.Exists(paths.UnrealMaterialProfilesPath));
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(paths.UnrealMaterialProfilesPath)!, "unreal-material-profiles.json.corrupt-*"));
    }

    // Integration test: exported manifest JSON round-trips with schema version and UTF-8 source data.
    [Fact]
    public async Task ManifestExportWritesUtf8JsonAndRoundTrips()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePackage(temporary, "Камень 水");
        var destination = Path.Combine(temporary.Path, "Камень.scanvault-ue.json");
        var service = new UnrealImportPackageExportService();

        var result = await service.ExportAsync(package, destination, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));

        Assert.True(result.OutputSizeBytes > 0);
        Assert.Equal(UnrealImportPackageSchema.CurrentVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Камень 水", document.RootElement.GetProperty("source").GetProperty("name").GetString());
        Assert.Equal(package.PackageId, document.RootElement.GetProperty("packageId").GetString());
    }

    // Integration test: cancellation does not publish partial manifests or leave temporary files.
    [Fact]
    public async Task CancelledExportLeavesExistingFileAndNoTemporaryFiles()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePackage(temporary, "Cancel Test");
        var destination = temporary.WriteFile("asset.scanvault-ue.json", "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new UnrealImportPackageExportService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(package, destination, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp"));
    }

    // Integration test: missing required source paths block export before publication.
    [Fact]
    public async Task MissingSourcePathBlocksManifestExport()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePackage(temporary, "Missing Source");
        File.Delete(package.Textures[0].SourcePath);
        var destination = Path.Combine(temporary.Path, "missing.scanvault-ue.json");
        var service = new UnrealImportPackageExportService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExportAsync(package, destination, CancellationToken.None));

        Assert.Contains(nameof(UnrealImportPackageIssueCode.MissingSourcePath), exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
    }

    // Integration test: exported validation preserves generation-time ambiguity warnings.
    [Fact]
    public async Task ExportedValidationPreservesAmbiguousTextureWarning()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePackage(
            temporary,
            "Ambiguous",
            "asset_4K_Albedo.jpg",
            "asset_4K_Albedo.png",
            "asset_4K_Normal.jpg",
            "asset_4K_Roughness.jpg");
        var destination = Path.Combine(temporary.Path, "ambiguous.scanvault-ue.json");

        await new UnrealImportPackageExportService().ExportAsync(package, destination, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
        var issues = document.RootElement.GetProperty("validation").GetProperty("issues");

        Assert.Contains(issues.EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "ambiguousTextureRole");
    }

    // Integration test: finalization failure leaves no temporary manifest file behind.
    [Fact]
    public async Task FinalizationFailureRemovesTemporaryManifest()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreatePackage(temporary, "Failure");
        var destination = temporary.CreateDirectory("occupied.scanvault-ue.json");

        var exception = await Record.ExceptionAsync(() =>
            new UnrealImportPackageExportService().ExportAsync(package, destination, CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp"));
        Assert.True(Directory.Exists(destination));
    }

    private static UnrealImportPackage CreatePackage(
        TemporaryDirectory temporary,
        string name,
        params string[] textureFiles)
    {
        var folder = temporary.CreateDirectory("library", UnrealImportNamePolicy.SanitizeSegment(name));
        var jsonPath = temporary.WriteFile(Path.Combine("library", UnrealImportNamePolicy.SanitizeSegment(name), "asset.json"), "{}");
        var files = (textureFiles.Length == 0
                ? ["asset_4K_Albedo.jpg", "asset_4K_Normal.jpg"]
                : textureFiles)
            .Select(file => temporary.WriteFile(Path.Combine("library", UnrealImportNamePolicy.SanitizeSegment(name), file), "texture"))
            .ToArray();
        var inventory = AssetContentAnalyzer.Analyze(
            "Surface",
            files.Select(path => new AssetContentFileCandidate(path, Path.GetRelativePath(folder, path))).ToArray());
        var asset = new AssetSummary(
            "asset-id",
            name,
            "Surface",
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
            Timestamp)
        {
            Content = inventory
        };
        asset = UnrealReadinessPolicy.EnsureCurrent(asset, Timestamp);
        return UnrealImportPackagePolicy.Create(
            new(
                asset,
                UnrealMaterialProfilePolicy.BuiltInProfiles[0],
                "/Game/Megascans",
                UnrealMaterialProfilePolicy.BuiltInProfiles[0].DefaultOptions,
                "1.0.0",
                "abcdef1",
                Timestamp),
            File.Exists);
    }

    private static ScanVaultPaths Paths(TemporaryDirectory temporary) =>
        new(
            Path.Combine(temporary.Path, "local", "scanvault.db"),
            Path.Combine(temporary.Path, "roaming", "settings.json"),
            Path.Combine(temporary.Path, "roaming", "smart-collections.json"),
            Path.Combine(temporary.Path, "roaming", "unreal-material-profiles.json"),
            Path.Combine(temporary.Path, "cache"));
}
