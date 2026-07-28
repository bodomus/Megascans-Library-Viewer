using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed class ContentInventoryViewModel
{
    public ContentInventoryViewModel(AssetSummary asset, IAssetInteractionService interactions, ILogger logger)
    {
        Name = asset.Name;
        AssetId = asset.Id;
        Status = asset.Content.Completeness.ToString();
        VariantLines = asset.Content.Variants.SelectMany(variant =>
            variant.Meshes.Select(mesh => $"{variant.Name}  LOD{mesh.Lod}  {mesh.Format.ToString().ToUpperInvariant()}  {mesh.FileName}")).ToArray();
        TextureSetLines = asset.Content.TextureSets.SelectMany(set => set.Components.Select(component =>
            $"{set.Kind}  {FormatResolution(set.Resolution)}  {component.MapType}  {component.FileName}")).ToArray();
        IssueLines = asset.Content.Issues.Select(issue => $"{issue.Code}: {issue.Message}").ToArray();
        UnclassifiedLines = asset.Content.UnclassifiedFiles.Select(file => $"{file.Path} \u2014 {file.Reason}").ToArray();
        Files = asset.Content.Variants.SelectMany(static variant => variant.Meshes).Select(static mesh => mesh.Path)
            .Concat(asset.Content.TextureSets.SelectMany(static set => set.Components).Select(static component => component.Path))
            .Concat(asset.Content.UnclassifiedFiles.Select(static file => file.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new InventoryFileViewModel(path, asset.Id, interactions, logger))
            .ToArray();
    }

    public string Name { get; }
    public string AssetId { get; }
    public string Status { get; }
    public IReadOnlyList<string> VariantLines { get; }
    public IReadOnlyList<string> TextureSetLines { get; }
    public IReadOnlyList<string> IssueLines { get; }
    public IReadOnlyList<string> UnclassifiedLines { get; }
    public IReadOnlyList<InventoryFileViewModel> Files { get; }

    private static string FormatResolution(int? value) => value is null ? "\u2014" : value >= 1024 ? $"{value / 1024}K" : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class InventoryFileViewModel
{
    public InventoryFileViewModel(string path, string assetId, IAssetInteractionService interactions, ILogger logger)
    {
        Path = path;
        CopyPathCommand = CreateActionCommand("Copy content path", () => interactions.CopyText(path), assetId, logger);
        OpenContainingFolderCommand = CreateActionCommand("Open content folder", () =>
        {
            var directory = System.IO.Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The content path has no containing folder.");
            interactions.OpenFolder(directory);
        }, assetId, logger);
    }

    public string Path { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand OpenContainingFolderCommand { get; }

    private static RelayCommand CreateActionCommand(string action, Action execute, string assetId, ILogger logger) =>
        new(() =>
        {
            try
            {
                execute();
            }
            catch (Exception exception)
            {
                ApplicationLog.AssetActionFailed(logger, action, assetId, exception);
            }
        });
}
