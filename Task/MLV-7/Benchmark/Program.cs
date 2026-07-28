using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;
using ScanVault.Infrastructure.Parsing;
using ScanVault.Infrastructure.Persistence;
using ScanVault.Infrastructure.Scanning;

var libraryRoot = Path.GetFullPath(args.Single());
var scratch = Path.Combine(Path.GetTempPath(), "ScanVault-MLV-7-benchmark", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var scanner = new FileSystemScanner(NullLogger<FileSystemScanner>.Instance);
    var parser = new MegascansMetadataParser(NullLogger<MegascansMetadataParser>.Instance);
    var inventory = new AssetContentInventoryService(NullLogger<AssetContentInventoryService>.Instance);
    var paths = new ScanVaultPaths(Path.Combine(scratch, "scanvault.db"), Path.Combine(scratch, "settings.json"), Path.Combine(scratch, "cache"));
    var index = new SqliteAssetIndex(paths, NullLogger<SqliteAssetIndex>.Instance);
    var service = new LibraryScanService(scanner, parser, index, NullLogger<LibraryScanService>.Instance, inventory);
    var wallClock = Stopwatch.StartNew();
    var result = await service.ScanAsync(new LibrarySettings(libraryRoot), null, CancellationToken.None);
    wallClock.Stop();
    Console.WriteLine($"Root={libraryRoot}");
    Console.WriteLine($"SourceFiles={Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories).Count()}");
    Console.WriteLine($"Assets={result.AssetsInventoried};Meshes={result.MeshFilesFound};Textures={result.TextureFilesFound}");
    Console.WriteLine($"Ambiguous={result.AmbiguousAssets};MissingCritical={result.AssetsMissingCriticalFiles}");
    Console.WriteLine($"ScanElapsedMs={result.Elapsed.TotalMilliseconds:F1};WallClockMs={wallClock.Elapsed.TotalMilliseconds:F1}");
    Console.WriteLine($"DatabaseBytes={new FileInfo(paths.DatabasePath).Length}");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}
