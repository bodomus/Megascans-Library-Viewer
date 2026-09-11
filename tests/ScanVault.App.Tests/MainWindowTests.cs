using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using ScanVault.App.Services;
using ScanVault.App.ViewModels;
using ScanVault.Core.Models;

namespace ScanVault.App.Tests;

public sealed class MainWindowTests
{
    // UI test: realizes application windows and catches XAML binding regressions.
    [Fact]
    public void RealizesApplicationWindowsWithExpectedBindings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            global::ScanVault.App.App? application = null;
            global::ScanVault.App.MainWindow? window = null;
            global::ScanVault.App.DiagnosticsWindow? diagnosticsWindow = null;
            global::ScanVault.App.ContentInventoryWindow? contentWindow = null;
            global::ScanVault.App.AssetComparisonWindow? comparisonWindow = null;
            global::ScanVault.App.ExportReportWindow? exportReportWindow = null;
            global::ScanVault.App.UnrealImportPackageWindow? unrealImportPackageWindow = null;
            try
            {
                application = new();
                application.InitializeComponent();
                using var card = new AssetCardViewModel(
                    CreateAsset(),
                    new NullImageLoader(),
                    new NullInteractions(),
                    static _ => Task.CompletedTask,
                    static _ => { },
                    NullLogger<AssetCardViewModel>.Instance);
                window = new()
                {
                    DataContext = new WindowDataContext([card]),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };

                window.Show();
                window.UpdateLayout();

                var listBox = Assert.IsType<ListBox>(FindVisualChildByName<ListBox>(window, "AssetList"));
                Assert.NotNull(listBox.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.Equal("ScanVault Test 9.8.7", window.Title);

                Assert.NotNull(FindVisualChildByName<Button>(window, "ExportReportButton"));
                diagnosticsWindow = new()
                {
                    DataContext = new DiagnosticsViewModel(
                        CreateDiagnosticsSnapshot(),
                        new NullInteractions(),
                        NullLogger<DiagnosticsViewModel>.Instance),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };
                diagnosticsWindow.Show();
                diagnosticsWindow.UpdateLayout();

                var diagnosticsList = Assert.IsType<ListBox>(
                    FindVisualChild<ListBox>(diagnosticsWindow));
                Assert.Equal(31, diagnosticsList.Items.Count);
                Assert.Equal("About / Diagnostics \u2014 ScanVault 9.8.7", diagnosticsWindow.Title);
                contentWindow = new()
                {
                    DataContext = new ContentInventoryViewModel(CreateAsset(), new NullInteractions(), NullLogger<ContentInventoryViewModel>.Instance),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };
                contentWindow.Show();
                contentWindow.UpdateLayout();
                var contentTabs = Assert.IsType<TabControl>(FindVisualChild<TabControl>(contentWindow));
                Assert.Equal(5, contentTabs.Items.Count);
                var leftAsset = CreateAsset() with { Id = "comparison-left", Name = "Comparison Left" };
                var rightAsset = CreateAsset() with { Id = "comparison-right", Name = "Comparison Right" };
                comparisonWindow = new()
                {
                    DataContext = new AssetComparisonViewModel(
                        leftAsset,
                        rightAsset,
                        new NullImageLoader(),
                        new NullInteractions(),
                        static _ => Task.CompletedTask,
                        static _ => { },
                        id => id == leftAsset.Id ? leftAsset : rightAsset,
                        static _ => { },
                        NullLogger<AssetComparisonViewModel>.Instance),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };
                comparisonWindow.Show();
                comparisonWindow.UpdateLayout();
                var comparisonTabs = Assert.IsType<TabControl>(FindVisualChild<TabControl>(comparisonWindow));
                Assert.Equal(5, comparisonTabs.Items.Count);
                Assert.Equal("Asset Comparison", comparisonWindow.Title);

                exportReportWindow = new()
                {
                    DataContext = new ExportReportWindowDataContext(),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };
                exportReportWindow.Show();
                exportReportWindow.UpdateLayout();
                Assert.Equal("Export Report", exportReportWindow.Title);

                unrealImportPackageWindow = new()
                {
                    DataContext = new UnrealImportPackageWindowDataContext(),
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Left = -10_000,
                    Top = -10_000
                };
                unrealImportPackageWindow.Show();
                unrealImportPackageWindow.UpdateLayout();
                Assert.Equal("Create UE Import Package", unrealImportPackageWindow.Title);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                unrealImportPackageWindow?.Close();
                exportReportWindow?.Close();
                comparisonWindow?.Close();
                contentWindow?.Close();
                diagnosticsWindow?.Close();
                window?.Close();
                application?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF layout test timed out.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T { Name: var childName } match && childName == name)
            {
                return match;
            }

            if (FindVisualChildByName<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static DiagnosticsSnapshot CreateDiagnosticsSnapshot() => new(
        "9.8.7",
        "9.8.7-test+abcdef1",
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
        "Index is compatible.");

    private static AssetSummary CreateAsset() =>
        new(
            "xaml-binding",
            "XAML Binding Test",
            "Surface",
            @"C:\fixtures\asset",
            @"C:\fixtures\asset\xaml-binding.json",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            DateTimeOffset.UnixEpoch);

    private sealed record WindowDataContext(IReadOnlyList<AssetCardViewModel> Assets)
    {
        public IReadOnlyList<AssetSortOption> SortOptions { get; } = [];

        public string WindowTitle { get; } = "ScanVault Test 9.8.7";
    }

    private sealed record ExportReportWindowDataContext
    {
        public int ProcessedAssets { get; } = 7;

        public long WrittenRows { get; } = 11;

        public TimeSpan Elapsed { get; } = TimeSpan.FromSeconds(2);
    }

    private sealed class UnrealImportPackageWindowDataContext
    {
        public string AssetName { get; } = "UE Binding Test";
        public string AssetType { get; } = "Surface";
        public string ReadinessDisplay { get; } = "UE Ready";
        public int SchemaVersion { get; } = 1;
        public string PackageId { get; } = "package-id";
        public string DestinationBasePath { get; set; } = "/Game/Megascans";
        public string FinalContentPath { get; } = "/Game/Megascans/Surfaces/UE_Binding_Test";
        public string DestinationPath { get; set; } = @"C:\Exports\package.scanvault-ue.json";
        public IReadOnlyList<object> Profiles { get; } = [];
        public object? SelectedProfile { get; set; }
        public bool IsEditableProfile { get; } = true;
        public string EditableProfileName { get; set; } = "Test Profile";
        public string EditableProfileDescription { get; set; } = "Description";
        public string EditableMasterMaterialPath { get; set; } = "/Game/M_Master";
        public string EditableMaterialInstancePrefix { get; set; } = "MI_";
        public IReadOnlyList<AssetTypeOption> AssetTypeOptions { get; } = [new("Surface", true)];
        public bool EditableDefaultImportLods { get; set; } = true;
        public bool EditableDefaultEnableNanite { get; set; }
        public bool EditableDefaultCreateMaterialInstance { get; set; } = true;
        public string SanitizedAssetName { get; } = "UE_Binding_Test";
        public string PrimaryVariant { get; } = "None";
        public string MaterialInstanceName { get; } = "MI_UE_Binding_Test";
        public bool ImportLods { get; set; } = true;
        public bool EnableNanite { get; set; }
        public bool CreateMaterialInstance { get; set; } = true;
        public IReadOnlyList<object> Lods { get; } = [];
        public IReadOnlyList<object> Textures { get; } = [];
        public IReadOnlyList<object> ParameterMappings { get; } = [];
        public IReadOnlyList<object> ValidationIssues { get; } = [];
        public string JsonPreview { get; } = "{}";
        public string StatusText { get; } = "Ready.";
        public bool CanExport { get; } = true;
    }

    private sealed class AssetTypeOption(string assetType, bool isSelected)
    {
        public string AssetType { get; } = assetType;
        public bool IsSelected { get; set; } = isSelected;
    }

    private sealed class NullImageLoader : IImageLoader
    {
        public Task<ImageSource?> LoadAsync(
            string? path,
            int decodePixelWidth,
            CancellationToken cancellationToken) =>
            Task.FromResult<ImageSource?>(null);
    }

    private sealed class NullInteractions : IAssetInteractionService
    {
        public void CopyText(string text) { }

        public void OpenFolder(string folderPath) { }
        public void OpenFile(string filePath) { }
    }
}
