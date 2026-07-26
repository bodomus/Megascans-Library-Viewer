namespace ScanVault.Core.Policies;

/// <summary>Centralizes Windows path identity and deterministic ordering.</summary>
public static class PathPolicy
{
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
