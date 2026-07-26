namespace ScanVault.Core.Models;

public enum ImageCandidateRole
{
    Thumb,
    Preview,
    Retina,
    Bake,
    KnownPattern,
    Placeholder
}

public sealed record ImageCandidate(
    string Path,
    ImageCandidateRole Role,
    int? Resolution = null);
