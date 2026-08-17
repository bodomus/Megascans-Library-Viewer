namespace ScanVault.Infrastructure.Scanning;

public interface IDuplicateContentHasher
{
    Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken);
}
