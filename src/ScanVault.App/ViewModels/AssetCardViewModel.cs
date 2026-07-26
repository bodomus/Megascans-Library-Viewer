using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed class AssetCardViewModel(
    AssetSummary asset,
    IImageLoader imageLoader,
    Func<AssetSummary, Task> openPreview) : ObservableObject, IDisposable
{
    private CancellationTokenSource? imageCancellation;
    private CancellationTokenSource? hoverCancellation;
    private ImageSource? thumbnail;
    private bool isHoverOpen;

    public AssetSummary Asset { get; } = asset;
    public string Name => Asset.Name;
    public string Id => Asset.Id;
    public string TypeAndCategory =>
        Asset.Categories.Count == 0
            ? Asset.AssetType
            : $"{Asset.AssetType} · {Asset.Categories[0]}";
    public string CategoriesText => string.Join(", ", Asset.Categories);
    public string TagsText => string.Join(
        ", ",
        Asset.Tags.Select(static tag => tag.Value).Distinct(StringComparer.OrdinalIgnoreCase));
    public string CategoriesDisplay => Display(CategoriesText);
    public string BiomeDisplay => Display(Asset.Biome);
    public string RegionDisplay => Display(Asset.Region);
    public string PhysicalSizeDisplay => Display(Asset.PhysicalSize);
    public string ResolutionDisplay => Asset.MaxResolution is { } value ? value.ToString("N0", CultureInfo.CurrentCulture) : "—";
    public string TexelDensityDisplay => Asset.TexelDensity is { } value ? value.ToString("0.##", CultureInfo.CurrentCulture) : "—";
    public string TagsDisplay => Display(TagsText);

    public ImageSource? Thumbnail
    {
        get => thumbnail;
        private set => SetProperty(ref thumbnail, value);
    }

    public bool IsHoverOpen
    {
        get => isHoverOpen;
        private set => SetProperty(ref isHoverOpen, value);
    }

    public ICommand OpenPreviewCommand { get; } =
        new AsyncRelayCommand(_ => openPreview(asset));

    public async Task LoadThumbnailAsync()
    {
        imageCancellation?.Cancel();
        imageCancellation?.Dispose();
        imageCancellation = new();
        var token = imageCancellation.Token;
        var loaded = await imageLoader.LoadAsync(Asset.ThumbnailPath, 360, token);
        if (!token.IsCancellationRequested)
        {
            Thumbnail = loaded;
        }
    }

    public async Task BeginHoverAsync()
    {
        hoverCancellation?.Cancel();
        hoverCancellation?.Dispose();
        hoverCancellation = new();
        var token = hoverCancellation.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(425), token);
            if (!token.IsCancellationRequested)
            {
                IsHoverOpen = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Rapid pointer movement invalidates the queued popup.
        }
    }

    public void EndHover()
    {
        hoverCancellation?.Cancel();
        IsHoverOpen = false;
    }

    public void CancelImageLoad() => imageCancellation?.Cancel();

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public void Dispose()
    {
        imageCancellation?.Cancel();
        imageCancellation?.Dispose();
        hoverCancellation?.Cancel();
        hoverCancellation?.Dispose();
    }
}
