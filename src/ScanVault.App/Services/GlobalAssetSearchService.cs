using ScanVault.Core.Models;
using ScanVault.Core.Policies;

namespace ScanVault.App.Services;

public interface IGlobalAssetSearchService
{
    Task<IReadOnlyList<GlobalAssetSearchMatch>> SearchAsync(
        IReadOnlyList<AssetSummary> assets,
        string query,
        CancellationToken cancellationToken);
}

public sealed class GlobalAssetSearchService : IGlobalAssetSearchService
{
    public Task<IReadOnlyList<GlobalAssetSearchMatch>> SearchAsync(
        IReadOnlyList<AssetSummary> assets,
        string query,
        CancellationToken cancellationToken) =>
        Task.Run(() => GlobalAssetSearchPolicy.Search(assets, query, cancellationToken), cancellationToken);
}

