namespace ScanVault.Core.Models;

/// <summary>A physical folder containing indexed assets or relevant descendants.</summary>
public sealed class FolderNode(string name, string fullPath)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public IList<FolderNode> Children { get; } = [];
}
