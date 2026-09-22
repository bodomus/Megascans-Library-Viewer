using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Abstractions;
using ScanVault.Infrastructure;

namespace ScanVault.App;

public partial class App : Application
{
    private IHost? host;
    private MainViewModel? mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        LoadingWindow? loadingWindow = null;

        try
        {
            loadingWindow = new LoadingWindow();
            loadingWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);

            host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                })
                .ConfigureServices(services =>
                {
                    services.AddScanVaultInfrastructure();
                    services.AddSingleton(ApplicationBuildInfo.FromAssembly(typeof(App).Assembly));
                    services.AddSingleton<IScanBuildInfoProvider>(provider => provider.GetRequiredService<ApplicationBuildInfo>());
                    services.AddSingleton<IImageLoader, BoundedImageLoader>();
                    services.AddSingleton<IAssetInteractionService, DesktopAssetInteractionService>();
                    services.AddSingleton<IGlobalAssetSearchService, GlobalAssetSearchService>();
                    services.AddSingleton<DiagnosticsService>();
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<MainWindow>();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<App>>();
            var buildInfo = host.Services.GetRequiredService<ApplicationBuildInfo>();
            ApplicationLog.Starting(
                logger,
                buildInfo.ProductVersion,
                buildInfo.InformationalVersion,
                buildInfo.CommitSha,
                buildInfo.BuildConfiguration,
                buildInfo.RuntimeVersion,
                buildInfo.OperatingSystem,
                buildInfo.ProcessArchitecture);
            mainViewModel = host.Services.GetRequiredService<MainViewModel>();
            await mainViewModel.InitializeAsync(CancellationToken.None);

            var window = host.Services.GetRequiredService<MainWindow>();
            window.DataContext = mainViewModel;
            MainWindow = window;

            // Closing loading before Show keeps the first MainWindow layout on the
            // panel's normal single-window WPF lifecycle.
            loadingWindow.Close();
            loadingWindow = null;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception exception)
        {
            loadingWindow?.Close();
            MessageBox.Show(
                $"ScanVault could not start.{Environment.NewLine}{exception.Message}",
                "ScanVault startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        mainViewModel?.Dispose();
        host?.Dispose();
        base.OnExit(e);
    }
}
