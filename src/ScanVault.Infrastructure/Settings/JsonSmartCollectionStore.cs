using System.Text.Json;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;

namespace ScanVault.Infrastructure.Settings;

public sealed class JsonSmartCollectionStore(ScanVaultPaths paths) : ISmartCollectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<SmartCollectionRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.SmartCollectionsPath))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                paths.SmartCollectionsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<SmartCollectionDocument>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return document?.Collections
                       .Where(static collection => collection.Kind == SmartCollectionKind.User)
                       .OrderBy(static collection => collection.Order)
                       .ThenBy(static collection => collection.Name, StringComparer.OrdinalIgnoreCase)
                       .ToArray()
                   ?? [];
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<SmartCollectionRecord> collections, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.SmartCollectionsPath)
            ?? throw new InvalidOperationException("Smart collection path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = paths.SmartCollectionsPath + ".tmp";
        var document = new SmartCollectionDocument(
            SmartCollectionDefinition.CurrentVersion,
            collections.Where(static collection => collection.Kind == SmartCollectionKind.User)
                .OrderBy(static collection => collection.Order)
                .ThenBy(static collection => collection.Name, StringComparer.OrdinalIgnoreCase)
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
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(
                () => File.Move(temporaryPath, paths.SmartCollectionsPath, true),
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
        if (!File.Exists(paths.SmartCollectionsPath))
        {
            return;
        }

        var backupPath = paths.SmartCollectionsPath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(paths.SmartCollectionsPath, backupPath, true);
    }

    private sealed record SmartCollectionDocument(
        int DefinitionVersion,
        IReadOnlyList<SmartCollectionRecord> Collections);
}
