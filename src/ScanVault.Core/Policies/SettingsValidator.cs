using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public sealed record SettingsValidationResult(bool IsValid, string? Error)
{
    public static SettingsValidationResult Valid { get; } = new(true, null);
}

public static class SettingsValidator
{
    public static SettingsValidationResult Validate(LibrarySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LibraryRoot))
        {
            return new(false, "Choose a Megascans library root.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(settings.LibraryRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new(false, "The library root is not a valid Windows path.");
        }

        return Directory.Exists(fullPath)
            ? SettingsValidationResult.Valid
            : new(false, "The library root does not exist or is not a directory.");
    }
}
