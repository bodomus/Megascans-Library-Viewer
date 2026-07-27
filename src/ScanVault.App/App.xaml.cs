using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Infrastructure;

namespace ScanVault.App;

public partial class App : Application
{
    private IHost? host;
    private MainViewModel? mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
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
                    services.AddSingleton<IImageLoader, BoundedImageLoader>();
                    services.AddSingleton<IAssetInteractionService, DesktopAssetInteractionService>();
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
            window.Show();
        }
        catch (Exception exception)
        {
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
