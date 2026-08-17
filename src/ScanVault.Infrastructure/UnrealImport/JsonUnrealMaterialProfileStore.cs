using System.Text.Json;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;

namespace ScanVault.Infrastructure.UnrealImport;

public sealed class JsonUnrealMaterialProfileStore(ScanVaultPaths paths) : IUnrealMaterialProfileStore
{
    private const int CurrentDocumentVersion = 1;

    public async Task<IReadOnlyList<UnrealMaterialProfile>> LoadUserProfilesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.UnrealMaterialProfilesPath))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                paths.UnrealMaterialProfilesPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<MaterialProfileDocument>(
                stream,
                UnrealImportJson.SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (document?.SchemaVersion != CurrentDocumentVersion)
            {
                BackupCorruptFile();
                return [];
            }

            return document.Profiles
                .Where(static profile => !profile.IsBuiltIn)
                .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return [];
        }
    }

    public async Task SaveUserProfilesAsync(IReadOnlyList<UnrealMaterialProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.UnrealMaterialProfilesPath)
            ?? throw new InvalidOperationException("Unreal material profile path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = paths.UnrealMaterialProfilesPath + ".tmp";
        var document = new MaterialProfileDocument(
            CurrentDocumentVersion,
            profiles.Where(static profile => !profile.IsBuiltIn)
                .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    UnrealImportJson.SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(
                () => File.Move(temporaryPath, paths.UnrealMaterialProfilesPath, true),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void BackupCorruptFile()
    {
        if (!File.Exists(paths.UnrealMaterialProfilesPath))
        {
            return;
        }

        var backupPath = paths.UnrealMaterialProfilesPath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(paths.UnrealMaterialProfilesPath, backupPath, true);
    }

    private sealed record MaterialProfileDocument(
        int SchemaVersion,
        IReadOnlyList<UnrealMaterialProfile> Profiles);
}
