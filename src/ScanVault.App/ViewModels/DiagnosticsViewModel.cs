using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly IAssetInteractionService interactions;
    private readonly ILogger<DiagnosticsViewModel> logger;
    private string copyStatus = string.Empty;

    public DiagnosticsViewModel(
        DiagnosticsSnapshot snapshot,
        IAssetInteractionService interactions,
        ILogger<DiagnosticsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.interactions = interactions;
        this.logger = logger;
        Fields = DiagnosticsFormatter.CreateFields(snapshot);
        FormattedText = DiagnosticsFormatter.Format(snapshot);
        WindowTitle = $"About / Diagnostics — ScanVault {snapshot.ApplicationVersion}";
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
    }

    public string WindowTitle { get; }
    public IReadOnlyList<DiagnosticField> Fields { get; }
    public string FormattedText { get; }
    public RelayCommand CopyDiagnosticsCommand { get; }

    public string CopyStatus
    {
        get => copyStatus;
        private set => SetProperty(ref copyStatus, value);
    }

    private void CopyDiagnostics()
    {
        try
        {
            interactions.CopyText(FormattedText);
            CopyStatus = "Diagnostics copied to the clipboard.";
        }
        catch (Exception exception)
        {
            ApplicationLog.DiagnosticsCopyFailed(logger, exception);
            CopyStatus = "Diagnostics could not be copied. The information remains visible.";
        }
    }
}
