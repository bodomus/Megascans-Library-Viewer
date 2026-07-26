using System.Security;
using Microsoft.Extensions.Logging;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Infrastructure.Scanning;

public sealed class FileSystemScanner(ILogger<FileSystemScanner> logger) : IFileSystemScanner
{
    public Task<FileDiscoveryResult> DiscoverAsync(
        string libraryRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Discover(PathPolicy.Normalize(libraryRoot), progress, cancellationToken),
            cancellationToken);

    private FileDiscoveryResult Discover(
        string root,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var inaccessible = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(root));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            FileInfo[] jsonFiles;
            DirectoryInfo[] childDirectories;
            try
            {
                jsonFiles = directory.GetFiles("*.json", SearchOption.TopDirectoryOnly);
                childDirectories = directory.GetDirectories();
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or SecurityException)
            {
                var path = PathPolicy.Normalize(directory.FullName);
                inaccessible.Add(path);
                InfrastructureLog.CannotEnumerate(logger, path, exception);
                continue;
            }

            foreach (var file in jsonFiles.OrderBy(static file => file.FullName, PathPolicy.Comparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(PathPolicy.Normalize(file.FullName));
                progress?.Report(new(
                    ScanPhase.Discovering,
                    files.Count,
                    0,
                    file.FullName));
            }

            // Reverse push preserves ascending traversal while using a stack.
            foreach (var child in childDirectories
                         .Where(static child => (child.Attributes & FileAttributes.ReparsePoint) == 0)
                         .OrderByDescending(static child => child.FullName, PathPolicy.Comparer))
            {
                pending.Push(child);
            }
        }

        return new(
            files.OrderBy(static path => path, PathPolicy.Comparer).ToArray(),
            inaccessible.OrderBy(static path => path, PathPolicy.Comparer).ToArray());
    }
}
