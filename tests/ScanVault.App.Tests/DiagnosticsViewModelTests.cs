using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void CopyDiagnosticsCopiesTheExactVisibleReport()
    {
        var interactions = new RecordingInteractions();
        var viewModel = new DiagnosticsViewModel(
            CreateSnapshot(),
            interactions,
            NullLogger<DiagnosticsViewModel>.Instance);

        viewModel.CopyDiagnosticsCommand.Execute(null);

        Assert.Equal(viewModel.FormattedText, interactions.CopiedText);
        Assert.Equal("Diagnostics copied to the clipboard.", viewModel.CopyStatus);
    }

    [Fact]
    public void ClipboardFailureLeavesDiagnosticsVisibleAndReportsFailure()
    {
        var viewModel = new DiagnosticsViewModel(
            CreateSnapshot(),
            new ThrowingInteractions(),
            NullLogger<DiagnosticsViewModel>.Instance);

        var exception = Record.Exception(() => viewModel.CopyDiagnosticsCommand.Execute(null));

        Assert.Null(exception);
        Assert.NotEmpty(viewModel.FormattedText);
        Assert.Contains("could not be copied", viewModel.CopyStatus, StringComparison.Ordinal);
    }

    private static DiagnosticsSnapshot CreateSnapshot() => new(
        "1.2.3",
        "1.2.3-test+abcdef1",
        "abcdef1",
        "Test",
        ".NET test runtime",
        "Test OS",
        "X64",
        @"C:\Library",
        17,
        DateTimeOffset.UnixEpoch,
        TimeSpan.FromSeconds(4),
        ScanAttemptStatus.Succeeded,
        "+17, ~0, -0",
        @"C:\Data\scanvault.db",
        @"C:\Data\thumbnails",
        2,
        2,
        IndexCompatibilityState.Compatible,
        false,
        "Index is compatible.",
        @"C:\Data\settings.json",
        "NameAscending",
        @"C:\Library\Stone");

    private sealed class RecordingInteractions : IAssetInteractionService
    {
        public string? CopiedText { get; private set; }

        public void CopyText(string text) => CopiedText = text;

        public void OpenFolder(string folderPath) { }
        public void OpenFile(string filePath) { }
    }

    private sealed class ThrowingInteractions : IAssetInteractionService
    {
        public void CopyText(string text) => throw new InvalidOperationException("Clipboard unavailable.");

        public void OpenFolder(string folderPath) { }
        public void OpenFile(string filePath) { }
    }
}
