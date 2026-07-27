namespace ScanVault.Core.Models;

/// <summary>A physical folder containing indexed assets or relevant descendants.</summary>
public sealed class FolderNode(string name, string fullPath)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public int AssetCount { get; internal set; }
    public string DisplayName => $"{Name} ({AssetCount})";
    public IList<FolderNode> Children { get; } = [];
}
