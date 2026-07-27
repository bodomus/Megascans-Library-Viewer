using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.Core.Tests;

public sealed class DiagnosticsFormatterTests
{
    [Fact]
    public void ProducesStableFieldOrderAndInvariantValues()
    {
        var snapshot = CreateSnapshot();

        var fields = DiagnosticsFormatter.CreateFields(snapshot);
        var text = DiagnosticsFormatter.Format(snapshot);

        Assert.Equal(
        [
            "Application version",
            "Informational version",
            "Commit SHA",
            "Build configuration",
            "Runtime version",
            "Operating system",
            "Process architecture",
            "Library root",
            "Indexed asset count",
            "Last successful scan",
            "Last scan duration",
            "Last scan status",
            "Last scan result",
            "SQLite database path",
            "Thumbnail/cache path",
            "Database schema version",
            "Metadata normalization version",
            "Index compatibility state",
            "Rescan required",
            "Compatibility guidance",
            "Settings file path",
            "Current sort mode",
            "Current selected folder"
        ], fields.Select(field => field.Label));
        Assert.StartsWith("ScanVault diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("Application version: 0.2.0", text, StringComparison.Ordinal);
        Assert.Contains("Last successful scan: 2026-07-27 15:30:12 +00:00", text, StringComparison.Ordinal);
        Assert.Contains("Last scan duration: 00:00:12.3450000", text, StringComparison.Ordinal);
        Assert.Contains("Rescan required: Yes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesOneUnavailableTokenAndNeverReadsEnvironmentInventory()
    {
        const string variable = "SCANVAULT_DIAGNOSTICS_SECRET_TEST";
        const string secret = "must-not-appear-in-diagnostics";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            var snapshot = CreateSnapshot() with
            {
                LibraryRoot = null,
                LastSuccessfulScan = null,
                LastScanDuration = null,
                LastScanResult = null,
                DatabaseSchemaVersion = null,
                MetadataNormalizationVersion = null,
                SettingsPath = null,
                CurrentSortMode = null,
                CurrentSelectedFolder = null
            };

            var text = DiagnosticsFormatter.Format(snapshot);

            Assert.Contains($"Library root: {DiagnosticsFormatter.UnavailableValue}", text, StringComparison.Ordinal);
            Assert.Contains($"Database schema version: {DiagnosticsFormatter.UnavailableValue}", text, StringComparison.Ordinal);
            Assert.DoesNotContain(variable, text, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private static DiagnosticsSnapshot CreateSnapshot() => new(
        "0.2.0",
        "0.2.0-ci.42+abcdef1",
        "abcdef1",
        "Release",
        ".NET 10.0.0",
        "Windows Test",
        "X64",
        @"J:\Megascans",
        1842,
        new DateTimeOffset(2026, 7, 27, 15, 30, 12, TimeSpan.Zero),
        TimeSpan.FromMilliseconds(12345),
        ScanAttemptStatus.Succeeded,
        "+10, ~4, -2; 3 skipped, 1 inaccessible",
        @"C:\Data\scanvault.db",
        @"C:\Data\thumbnails",
        2,
        1,
        IndexCompatibilityState.RequiresRescan,
        RequiresRescan: true,
        "Index metadata is outdated — Rescan required.",
        @"C:\Settings\settings.json",
        "NameAscending",
        @"J:\Megascans\3D");
}
