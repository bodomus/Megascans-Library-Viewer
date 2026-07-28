using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.ViewModels;

public sealed class AssetCardViewModel : ObservableObject, IDisposable
{
    private readonly IImageLoader imageLoader;
    private CancellationTokenSource? imageCancellation;
    private CancellationTokenSource? hoverCancellation;
    private ImageSource? thumbnail;
    private bool isHoverOpen;

    public AssetCardViewModel(
        AssetSummary asset,
        IImageLoader imageLoader,
        IAssetInteractionService interactions,
        Func<AssetSummary, Task> openPreview,
        Action<string> reportStatus,
        ILogger<AssetCardViewModel> logger,
        Action<AssetSummary>? showInventory = null)
    {
        Asset = asset;
        this.imageLoader = imageLoader;
        OpenPreviewCommand = new AsyncRelayCommand(_ => openPreview(asset));
        ShowContentInventoryCommand = new RelayCommand(() => showInventory?.Invoke(asset), () => showInventory is not null);
        OpenFolderCommand = CreateActionCommand("Open folder", () => interactions.OpenFolder(asset.AssetFolderPath), "Opened asset folder.", reportStatus, logger);
        CopyAssetIdCommand = CreateActionCommand("Copy asset ID", () => interactions.CopyText(asset.Id), "Asset ID copied.", reportStatus, logger);
        CopyFolderPathCommand = CreateActionCommand("Copy folder path", () => interactions.CopyText(asset.AssetFolderPath), "Asset folder path copied.", reportStatus, logger);
        CopyJsonPathCommand = CreateActionCommand("Copy JSON path", () => interactions.CopyText(asset.JsonPath), "Metadata JSON path copied.", reportStatus, logger);
        Badges = BuildBadges(asset.Content);
    }

    public AssetSummary Asset { get; }
    public string Name => Asset.Name;
    public string IdDisplay => $"ID: {Asset.Id}";
    public string TypeAndCategory
    {
        get
        {
            var categories = Asset.Categories.Where(category =>
                !StringComparer.OrdinalIgnoreCase.Equals(MetadataNormalizer.ResolveAssetType([category]).Canonical, Asset.AssetType)).Take(2).ToArray();
            return categories.Length == 0 ? Asset.AssetType : $"{Asset.AssetType} · {string.Join(" / ", categories)}";
        }
    }

    public string CompactDetails => Asset.MaxResolution is { } resolution ? $"{resolution.ToCompactString()} · {IdDisplay}" : IdDisplay;
    public string CategoriesDisplay => Asset.Categories.Count == 0 ? "—" : string.Join(" / ", Asset.Categories);
    public string? BiomeDisplay => Asset.Biome;
    public string? RegionDisplay => Asset.Region;
    public string? PhysicalSizeDisplay => Asset.PhysicalSize;
    public string? ResolutionDisplay => Asset.MaxResolution?.ToDisplayString();
    public string? TexelDensityDisplay => Asset.TexelDensity is { } value ? $"{value.ToString("0.##", CultureInfo.CurrentCulture)} px/m" : null;
    public string? DescriptiveTagsDisplay => TagDisplay(AssetTagKind.Descriptive);
    public string? StateDisplay => TagDisplay(AssetTagKind.State);
    public string? ColorsDisplay => TagDisplay(AssetTagKind.Color);
    public string? IndustriesDisplay => TagDisplay(AssetTagKind.Industry);
    public bool HasBiome => BiomeDisplay is not null;
    public bool HasRegion => RegionDisplay is not null;
    public bool HasPhysicalSize => PhysicalSizeDisplay is not null;
    public bool HasResolution => ResolutionDisplay is not null;
    public bool HasTexelDensity => TexelDensityDisplay is not null;
    public bool HasDescriptiveTags => DescriptiveTagsDisplay is not null;
    public bool HasState => StateDisplay is not null;
    public bool HasColors => ColorsDisplay is not null;
    public bool HasIndustries => IndustriesDisplay is not null;
    public IReadOnlyList<string> Badges { get; }
    public bool HasContentWarning => Asset.Content.Completeness is AssetCompletenessStatus.Partial or AssetCompletenessStatus.MissingCriticalFiles or AssetCompletenessStatus.Ambiguous;
    public string ContentWarning => Asset.Content.Completeness switch
    {
        AssetCompletenessStatus.Ambiguous => "AMBIGUOUS",
        AssetCompletenessStatus.MissingCriticalFiles => "INCOMPLETE",
        AssetCompletenessStatus.Partial => "PARTIAL",
        _ => string.Empty
    };
    public string ContentVariantsDisplay => $"Variants: {Asset.Content.VariantCount}";
    public string ContentLodsDisplay
    {
        get
        {
            var lods = Asset.Content.Variants.SelectMany(static variant => variant.Meshes).Select(static mesh => mesh.Lod).Distinct().Order().ToArray();
            return lods.Length == 0 ? "LODs: —" : $"LODs: {string.Join(", ", lods.Select(static lod => $"LOD{lod}"))}";
        }
    }
    public string ContentMeshesDisplay
    {
        get
        {
            var formats = Asset.Content.Variants.SelectMany(static variant => variant.Meshes).Select(static mesh => mesh.Format.ToString().ToUpperInvariant()).Distinct().Order().ToArray();
            return formats.Length == 0 ? "Meshes: —" : $"Meshes: {string.Join(", ", formats)}";
        }
    }
    public string ContentSetsDisplay
    {
        get
        {
            var sets = Asset.Content.TextureSets.Select(static set => set.Kind).Distinct().Order().ToArray();
            return sets.Length == 0 ? "Texture sets: —" : $"Texture sets: {string.Join(", ", sets)}";
        }
    }
    public string ContentMapsDisplay
    {
        get
        {
            var maps = Asset.Content.TextureSets.SelectMany(static set => set.Components).Select(static component => component.MapType).Distinct().Order().ToArray();
            return maps.Length == 0 ? "Maps: —" : $"Maps: {string.Join(", ", maps)}";
        }
    }
    public string ContentStatusDisplay => $"Status: {Asset.Content.Completeness}";
    public string ContentIssuesDisplay => $"Issues: {Asset.Content.Issues.Count}";

    public ImageSource? Thumbnail { get => thumbnail; private set => SetProperty(ref thumbnail, value); }
    public bool IsHoverOpen { get => isHoverOpen; private set => SetProperty(ref isHoverOpen, value); }
    public ICommand OpenPreviewCommand { get; }
    public ICommand ShowContentInventoryCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyAssetIdCommand { get; }
    public ICommand CopyFolderPathCommand { get; }
    public ICommand CopyJsonPathCommand { get; }

    public async Task LoadThumbnailAsync()
    {
        imageCancellation?.Cancel();
        imageCancellation?.Dispose();
        imageCancellation = new();
        var token = imageCancellation.Token;
        var loaded = await imageLoader.LoadAsync(Asset.ThumbnailPath, 360, token);
        if (!token.IsCancellationRequested) Thumbnail = loaded;
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
            if (!token.IsCancellationRequested) IsHoverOpen = true;
        }
        catch (OperationCanceledException) { }
    }

    public void EndHover() { hoverCancellation?.Cancel(); IsHoverOpen = false; }
    public void CancelImageLoad() => imageCancellation?.Cancel();
    public void Dispose()
    {
        imageCancellation?.Cancel(); imageCancellation?.Dispose();
        hoverCancellation?.Cancel(); hoverCancellation?.Dispose();
    }

    private static string[] BuildBadges(AssetContentInventory content)
    {
        var badges = new List<string>();
        if (content.HasFbx) badges.Add("FBX");
        else if (content.Variants.SelectMany(static variant => variant.Meshes).Any(static mesh => mesh.Format == MeshFormat.Abc)) badges.Add("ABC");
        var maxLod = content.Variants.SelectMany(static variant => variant.Meshes).Select(static mesh => (int?)mesh.Lod).Max();
        if (maxLod is not null) badges.Add($"LOD{maxLod}");
        if (content.VariantCount > 1) badges.Add($"VAR{content.VariantCount}");
        if (content.HasAtlas) badges.Add("ATLAS");
        if (content.HasBillboard) badges.Add("BILLBOARD");
        var maxResolution = content.TextureSets.Select(static set => set.Resolution).Where(static value => value is not null).Max();
        if (maxResolution is not null && badges.Count < 5) badges.Add(maxResolution.Value >= 1024 ? $"{maxResolution.Value / 1024}K" : maxResolution.Value.ToString(CultureInfo.InvariantCulture));
        return badges.Take(5).ToArray();
    }

    private string? TagDisplay(AssetTagKind kind)
    {
        var values = Asset.Tags.Where(tag => tag.Kind == kind).Select(static tag => tag.Value).ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private RelayCommand CreateActionCommand(string action, Action execute, string successMessage, Action<string> reportStatus, ILogger<AssetCardViewModel> logger) => new(() =>
    {
        try { execute(); reportStatus(successMessage); }
        catch (Exception exception)
        {
            ApplicationLog.AssetActionFailed(logger, action, Asset.Id, exception);
            reportStatus($"{action} failed: {exception.Message}");
        }
    });
}
