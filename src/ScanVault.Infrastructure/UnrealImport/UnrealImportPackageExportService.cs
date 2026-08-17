using System.Text.Json;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.UnrealImport;

public sealed class UnrealImportPackageExportService : IUnrealImportPackageExportService
{
    public string Serialize(UnrealImportPackage package) =>
        JsonSerializer.Serialize(package, UnrealImportJson.SerializerOptions);

    public async Task<UnrealImportPackageExportResult> ExportAsync(
        UnrealImportPackage package,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));
        }

        var validation = UnrealImportPackageValidationPolicy.Validate(package, File.Exists);
        if (validation.HasErrors)
        {
            throw new InvalidOperationException(
                "Package validation failed: " +
                string.Join("; ", validation.Issues.Where(static issue => issue.Severity == UnrealImportValidationSeverity.Error)
                    .Select(static issue => $"{issue.Code}: {issue.Message}")));
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Path.GetFileName(destinationPath) + $".{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    package with { Validation = validation },
                    UnrealImportJson.SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => File.Move(temporaryPath, destinationPath, true), cancellationToken)
                .ConfigureAwait(false);
            var info = new FileInfo(destinationPath);
            return new(destinationPath, info.Length);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
