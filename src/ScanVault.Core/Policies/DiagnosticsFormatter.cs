using System.Globalization;
using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class DiagnosticsFormatter
{
    public const string UnavailableValue = "Unavailable";

    public static IReadOnlyList<DiagnosticField> CreateFields(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return
        [
            new("Application version", Value(snapshot.ApplicationVersion)),
            new("Informational version", Value(snapshot.InformationalVersion)),
            new("Commit SHA", Value(snapshot.CommitSha)),
            new("Build configuration", Value(snapshot.BuildConfiguration)),
            new("Runtime version", Value(snapshot.RuntimeVersion)),
            new("Operating system", Value(snapshot.OperatingSystem)),
            new("Process architecture", Value(snapshot.ProcessArchitecture)),
            new("Library root", Value(snapshot.LibraryRoot)),
            new("Indexed asset count", snapshot.IndexedAssetCount.ToString(CultureInfo.InvariantCulture)),
            new("Last successful scan", FormatTimestamp(snapshot.LastSuccessfulScan)),
            new("Last scan duration", FormatDuration(snapshot.LastScanDuration)),
            new("Last scan status", FormatStatus(snapshot.LastScanStatus)),
            new("Last scan result", Value(snapshot.LastScanResult)),
            new("SQLite database path", Value(snapshot.DatabasePath)),
            new("Thumbnail/cache path", Value(snapshot.ThumbnailCachePath)),
            new("Database schema version", FormatNumber(snapshot.DatabaseSchemaVersion)),
            new("Metadata normalization version", FormatNumber(snapshot.MetadataNormalizationVersion)),
            new("Index compatibility state", snapshot.IndexCompatibilityState.ToString()),
            new("Rescan required", snapshot.RequiresRescan ? "Yes" : "No"),
            new("Compatibility guidance", Value(snapshot.CompatibilityGuidance)),
            new("Settings file path", Value(snapshot.SettingsPath)),
            new("Current sort mode", Value(snapshot.CurrentSortMode)),
            new("Current selected folder", Value(snapshot.CurrentSelectedFolder))
        ];
    }

    public static string Format(DiagnosticsSnapshot snapshot)
    {
        var lines = new List<string> { "ScanVault diagnostics" };
        lines.AddRange(CreateFields(snapshot).Select(field => $"{field.Label}: {field.Value}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? UnavailableValue : value.Trim();

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ??
        UnavailableValue;

    private static string FormatDuration(TimeSpan? value) =>
        value?.ToString("c", CultureInfo.InvariantCulture) ?? UnavailableValue;

    private static string FormatNumber(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? UnavailableValue;

    private static string FormatStatus(ScanAttemptStatus status) => status switch
    {
        ScanAttemptStatus.NotRun => "Not run",
        _ => status.ToString()
    };
}
