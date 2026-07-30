using System.Windows.Media;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed class PreviewViewModel(IImageLoader imageLoader) : ObservableObject, IDisposable
{
    private CancellationTokenSource? loadCancellation;
    private bool disposed;
    private AssetSummary? asset;
    private ImageSource? image;
    private bool isOpen;

    public AssetSummary? Asset
    {
        get => asset;
        private set => SetProperty(ref asset, value);
    }

    public ImageSource? Image
    {
        get => image;
        private set => SetProperty(ref image, value);
    }

    public bool IsOpen
    {
        get => isOpen;
        private set => SetProperty(ref isOpen, value);
    }

    public async Task OpenAsync(AssetSummary selectedAsset)
    {
        CancelAndDisposeLoadCancellation();
        loadCancellation = new();
        Asset = selectedAsset;
        Image = null;
        IsOpen = true;
        var token = loadCancellation.Token;
        var loaded = await imageLoader.LoadAsync(
            selectedAsset.PreviewPath ?? selectedAsset.ThumbnailPath,
            2048,
            token);
        if (!token.IsCancellationRequested)
        {
            Image = loaded;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();
        CancelAndDisposeLoadCancellation();
    }

    public void Close()
    {
        if (loadCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
        }

        Image = null;
        Asset = null;
        IsOpen = false;
    }

    private void CancelAndDisposeLoadCancellation()
    {
        var cancellation = loadCancellation;
        loadCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        if (!cancellation.IsCancellationRequested)
        {
            cancellation.Cancel();
        }

        cancellation.Dispose();
    }
}

