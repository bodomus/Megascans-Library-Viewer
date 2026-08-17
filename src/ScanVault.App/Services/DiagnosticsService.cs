using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;
using ScanVault.Infrastructure.Configuration;

namespace ScanVault.App.Services;

public sealed record DiagnosticsCaptureContext(
    string? LibraryRoot,
    ScanAttemptStatus LastScanStatus,
    TimeSpan? LastScanDuration,
    string? LastScanResult,
    string? CurrentSortMode,
    string? CurrentSelectedFolder);

public sealed class DiagnosticsService(
    IAssetIndex index,
    ScanVaultPaths paths,
    ApplicationBuildInfo buildInfo)
{
    public async Task<DiagnosticsSnapshot> CaptureAsync(
        DiagnosticsCaptureContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var indexDiagnostics = await index.GetDiagnosticsAsync(cancellationToken)
            .ConfigureAwait(false);
        var persisted = indexDiagnostics.LastSuccessfulScan;
        var hasCurrentAttempt = context.LastScanStatus != ScanAttemptStatus.NotRun;

        var readiness = indexDiagnostics.UnrealReadiness ?? UnrealReadinessSummary.Empty;
        return new(
            buildInfo.ProductVersion,
            buildInfo.InformationalVersion,
            buildInfo.CommitSha,
            buildInfo.BuildConfiguration,
            buildInfo.RuntimeVersion,
            buildInfo.OperatingSystem,
            buildInfo.ProcessArchitecture,
            context.LibraryRoot,
            indexDiagnostics.IndexedAssetCount,
            persisted?.LastSuccessfulScanUtc,
            hasCurrentAttempt ? context.LastScanDuration : persisted?.LastScanDuration,
            hasCurrentAttempt
                ? context.LastScanStatus
                : persisted?.LastScanStatus ?? ScanAttemptStatus.NotRun,
            hasCurrentAttempt ? context.LastScanResult : persisted?.ResultSummary,
            paths.DatabasePath,
            paths.ThumbnailCacheDirectory,
            indexDiagnostics.Compatibility.DatabaseSchemaVersion,
            indexDiagnostics.Compatibility.MetadataNormalizationVersion,
            indexDiagnostics.Compatibility.State,
            indexDiagnostics.Compatibility.RequiresRescan,
            indexDiagnostics.Compatibility.Guidance,
            paths.SettingsPath,
            context.CurrentSortMode,
            context.CurrentSelectedFolder,
            readiness.RuleVersion,
            readiness.LastEvaluatedAtUtc,
            readiness.ReadyCount,
            readiness.ReadyWithWarningsCount,
            readiness.NotReadyCount,
            readiness.UnknownCount,
            readiness.NotApplicableCount,
            readiness.RequiresRecalculationCount > 0);
    }
}
