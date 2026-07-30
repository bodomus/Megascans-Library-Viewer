namespace ScanVault.Core.Abstractions;

public interface IScanBuildInfoProvider
{
    string ApplicationVersion { get; }
    string CommitSha { get; }
}
