using ScanVault.Core.Models;

namespace ScanVault.Core.Policies;

public static class PreviewCandidateSelector
{
    private static readonly IReadOnlyDictionary<ImageCandidateRole, int> ThumbnailPriority =
        new Dictionary<ImageCandidateRole, int>
        {
            [ImageCandidateRole.Thumb] = 0,
            [ImageCandidateRole.Preview] = 1,
            [ImageCandidateRole.KnownPattern] = 2,
            [ImageCandidateRole.Placeholder] = 3
        };

    private static readonly IReadOnlyDictionary<ImageCandidateRole, int> PreviewPriority =
        new Dictionary<ImageCandidateRole, int>
        {
            [ImageCandidateRole.Preview] = 0,
            [ImageCandidateRole.Retina] = 1,
            [ImageCandidateRole.Bake] = 2,
            [ImageCandidateRole.Thumb] = 3,
            [ImageCandidateRole.KnownPattern] = 4,
            [ImageCandidateRole.Placeholder] = 5
        };

    public static string? SelectThumbnail(IEnumerable<ImageCandidate> candidates) =>
        Select(candidates, ThumbnailPriority);

    public static string? SelectPreview(IEnumerable<ImageCandidate> candidates) =>
        Select(candidates, PreviewPriority);

    private static string? Select(
        IEnumerable<ImageCandidate> candidates,
        IReadOnlyDictionary<ImageCandidateRole, int> priorities) =>
        candidates
            .Where(candidate => priorities.ContainsKey(candidate.Role))
            .OrderBy(candidate => priorities[candidate.Role])
            .ThenByDescending(static candidate => candidate.Resolution ?? 0)
            .ThenBy(static candidate => candidate.Path, PathPolicy.Comparer)
            .Select(static candidate => candidate.Path)
            .FirstOrDefault();
}
