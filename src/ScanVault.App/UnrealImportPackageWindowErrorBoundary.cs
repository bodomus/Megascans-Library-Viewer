using ScanVault.Core.Models;
using ScanVault.App.ViewModels;

namespace ScanVault.App;

internal static class UnrealImportPackageWindowErrorBoundary
{
    public static async Task OpenAsync(
        AssetSummary asset,
        Func<CancellationToken, Task> openAsync,
        Action<Exception> logFailure,
        Action<string> showWarning,
        CancellationToken cancellationToken)
    {
        try
        {
            await openAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logFailure(exception);
            showWarning(
                $"UE Import Package could not be opened.{Environment.NewLine}{Environment.NewLine}" +
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                "See the application log for technical details.");
        }
    }

    public static async Task ExportAsync(
        UnrealImportPackageViewModel viewModel,
        Func<CancellationToken, Task> exportAsync,
        Action<string> showWarning,
        CancellationToken cancellationToken)
    {
        try
        {
            await exportAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            viewModel.NotifyExportFailed(exception);
            showWarning(
                $"UE Import Package could not be exported.{Environment.NewLine}{Environment.NewLine}" +
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                "See the application log for technical details.");
        }
    }
}
