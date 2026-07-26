using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Policies;
using ScanVault.Infrastructure.Scanning;

namespace ScanVault.Infrastructure.Tests;

public sealed class FileSystemScannerTests
{
    [Fact]
    public async Task RecursivelyDiscoversJsonInDeterministicOrder()
    {
        using var temporary = new TemporaryDirectory();
        var second = temporary.WriteFile("B/second.json", "{}");
        var first = temporary.WriteFile("A/first.json", "{}");
        temporary.WriteFile("A/image.jpg", string.Empty);
        var scanner = new FileSystemScanner(NullLogger<FileSystemScanner>.Instance);

        var result = await scanner.DiscoverAsync(
            temporary.Path,
            progress: null,
            CancellationToken.None);

        Assert.Equal(
            new[] { PathPolicy.Normalize(first), PathPolicy.Normalize(second) }
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            result.MetadataFiles);
        Assert.Empty(result.InaccessibleDirectories);
    }

    [Fact]
    public async Task CancellationIsObservedBeforeTraversal()
    {
        using var temporary = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scanner = new FileSystemScanner(NullLogger<FileSystemScanner>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.DiscoverAsync(temporary.Path, null, cancellation.Token));
    }
}
