using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using ScanVault.App.Presentation;
using ScanVault.App.Services;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.App.ViewModels;

public sealed class DuplicateAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly IDuplicateAnalysisService analysisService;
    private readonly IAssetIndex index;
    private readonly LibrarySettings settings;
    private readonly IReadOnlyList<AssetSummary> currentAssets;
    private readonly IAssetInteractionService interactions;
    private readonly Action<AssetSummary, AssetSummary> comparePair;
    private readonly ILogger<DuplicateAnalysisViewModel> logger;
    private CancellationTokenSource? analysisCancellation;
    private DuplicateAnalysisResult? result;
    private DuplicateGroupViewModel? selectedGroup;
    private DuplicateMemberViewModel? selectedMember;
    private bool showExact = true;
    private bool showConflictingId = true;
    private bool showProbable = true;
    private bool showPartial = true;
    private bool showHasIssues;
    private DuplicateConfidence? minimumConfidence;
    private bool isRunning;
    private string statusText = "No duplicate analysis has been run.";

    public DuplicateAnalysisViewModel(
        IDuplicateAnalysisService analysisService,
        IAssetIndex index,
        LibrarySettings settings,
        IReadOnlyList<AssetSummary> currentAssets,
        IAssetInteractionService interactions,
        Action<AssetSummary, AssetSummary> comparePair,
        ILogger<DuplicateAnalysisViewModel> logger)
    {
        this.analysisService = analysisService;
        this.index = index;
        this.settings = settings;
        this.currentAssets = currentAssets;
        this.interactions = interactions;
        this.comparePair = comparePair;
        this.logger = logger;
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        OpenAssetCommand = new RelayCommand(OpenAsset, () => SelectedMember is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => SelectedMember is not null);
        CompareSelectedPairCommand = new RelayCommand(CompareSelectedPair, () => SelectedGroup?.Members.Count >= 2);
        ExportGroupCommand = new RelayCommand(ExportGroup, () => SelectedGroup is not null);
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];
    public IReadOnlyList<DuplicateConfidence?> ConfidenceOptions { get; } = [null, DuplicateConfidence.Exact, DuplicateConfidence.High, DuplicateConfidence.Medium, DuplicateConfidence.Low];
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenAssetCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CompareSelectedPairCommand { get; }
    public RelayCommand ExportGroupCommand { get; }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsRunning { get => isRunning; private set { if (SetProperty(ref isRunning, value)) NotifyCommandStates(); } }
    public DuplicateAnalysisSummary Summary => result?.Run.Summary ?? new(0, 0, 0, 0, 0, 0);
    public bool IsStale => result?.Run.IsStale == true;
    public string StaleText => IsStale ? "Stale after Rescan" : "Current";
    public string PotentialReclaimableSize => FormatBytes(Summary.PotentialReclaimableSizeBytes);
    public bool ShowExact { get => showExact; set { if (SetProperty(ref showExact, value)) RefreshGroups(); } }
    public bool ShowConflictingId { get => showConflictingId; set { if (SetProperty(ref showConflictingId, value)) RefreshGroups(); } }
    public bool ShowProbable { get => showProbable; set { if (SetProperty(ref showProbable, value)) RefreshGroups(); } }
    public bool ShowPartial { get => showPartial; set { if (SetProperty(ref showPartial, value)) RefreshGroups(); } }
    public bool ShowHasIssues { get => showHasIssues; set { if (SetProperty(ref showHasIssues, value)) RefreshGroups(); } }
    public DuplicateConfidence? MinimumConfidence { get => minimumConfidence; set { if (SetProperty(ref minimumConfidence, value)) RefreshGroups(); } }

    public DuplicateGroupViewModel? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                SelectedMember = selectedGroup is null || selectedGroup.Members.Count == 0 ? null : selectedGroup.Members[0];
                NotifyCommandStates();
            }
        }
    }

    public DuplicateMemberViewModel? SelectedMember
    {
        get => selectedMember;
        set
        {
            if (SetProperty(ref selectedMember, value)) NotifyCommandStates();
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        result = await index.GetLatestDuplicateAnalysisAsync(settings.LibraryRoot, includeStale: true, cancellationToken).ConfigureAwait(true);
        RefreshResultProperties();
        RefreshGroups();
    }

    public void Dispose()
    {
        var cancellation = analysisCancellation;
        analysisCancellation = null;
        if (cancellation is null) return;
        if (!cancellation.IsCancellationRequested) cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken commandCancellation)
    {
        Dispose();
        analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(commandCancellation);
        var token = analysisCancellation.Token;
        IsRunning = true;
        StatusText = "Running duplicate analysis...";
        try
        {
            var progress = new Progress<DuplicateAnalysisProgress>(value =>
            {
                StatusText = value.Phase switch
                {
                    DuplicateAnalysisPhase.LoadingAssets => "Loading indexed assets...",
                    DuplicateAnalysisPhase.GeneratingCandidates => "Generating duplicate candidates...",
                    DuplicateAnalysisPhase.Hashing => $"Hashing files... {value.ProcessedFiles:N0}/{value.TotalFiles:N0}",
                    DuplicateAnalysisPhase.Classifying => "Classifying duplicate groups...",
                    DuplicateAnalysisPhase.Persisting => "Persisting duplicate analysis...",
                    DuplicateAnalysisPhase.Completed => "Duplicate analysis completed.",
                    _ => value.Phase.ToString()
                };
            });
            result = await analysisService.AnalyzeAsync(settings, progress, token).ConfigureAwait(true);
            RefreshResultProperties();
            RefreshGroups();
            StatusText = Groups.Count == 0 ? "No duplicates found." : $"Duplicate analysis completed: {Groups.Count:N0} groups.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = "Duplicate analysis is incomplete because hashing was cancelled.";
        }
        catch (Exception exception)
        {
            ApplicationLog.DuplicateAnalysisCommandFailed(logger, exception);
            StatusText = $"Duplicate analysis failed: {exception.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Cancel() => analysisCancellation?.Cancel();

    private void OpenAsset()
    {
        if (SelectedMember is not null) interactions.OpenFile(SelectedMember.JsonPath);
    }

    private void OpenFolder()
    {
        if (SelectedMember is not null) interactions.OpenFolder(SelectedMember.AssetFolderPath);
    }

    private void CompareSelectedPair()
    {
        var members = SelectedGroup?.Members.Take(2).ToArray();
        if (members is not { Length: 2 }) return;
        var left = ResolveAsset(members[0]);
        var right = ResolveAsset(members[1]);
        if (left is null || right is null)
        {
            StatusText = "Compare selected pair requires both assets to be present in the current index.";
            return;
        }

        comparePair(left, right);
    }

    private void ExportGroup()
    {
        if (SelectedGroup is null) return;
        var lines = new List<string>
        {
            $"# Duplicate group {SelectedGroup.GroupId}",
            "",
            $"- Category: {SelectedGroup.Category}",
            $"- Confidence: {SelectedGroup.Confidence}",
            $"- Estimated duplicate size: {SelectedGroup.EstimatedDuplicateSize}",
            "",
            "## Reasons"
        };
        lines.AddRange(SelectedGroup.Reasons.Select(reason => $"- {reason}"));
        lines.Add("");
        lines.Add("## Members");
        lines.AddRange(SelectedGroup.Members.Select(member => $"- {member.AssetName} ({member.AssetId}) - {member.RelativePath}"));
        interactions.CopyText(string.Join(Environment.NewLine, lines));
        StatusText = "Duplicate group export copied as Markdown.";
    }

    private AssetSummary? ResolveAsset(DuplicateMemberViewModel member) =>
        currentAssets.FirstOrDefault(asset =>
            StringComparer.OrdinalIgnoreCase.Equals(asset.Id, member.AssetId) &&
            StringComparer.OrdinalIgnoreCase.Equals(asset.JsonPath, member.JsonPath));

    private void RefreshGroups()
    {
        Groups.Clear();
        if (result is null)
        {
            StatusText = "No duplicate analysis has been run.";
            return;
        }

        foreach (var group in result.Groups.Where(MatchesFilters))
        {
            Groups.Add(new(group));
        }

        SelectedGroup = Groups.FirstOrDefault();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PotentialReclaimableSize));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StaleText));
    }

    private bool MatchesFilters(DuplicateGroupResult group)
    {
        if (group.Category is DuplicateCategory.ExactIdDuplicate or DuplicateCategory.ExactContentDuplicate && !ShowExact) return false;
        if (group.Category == DuplicateCategory.ConflictingIdDuplicate && !ShowConflictingId) return false;
        if (group.Category == DuplicateCategory.ProbableDuplicate && !ShowProbable) return false;
        if (group.Category == DuplicateCategory.PartialDuplicate && !ShowPartial) return false;
        if (ShowHasIssues && !group.Members.Any(static member => member.Completeness is AssetCompletenessStatus.Ambiguous or AssetCompletenessStatus.MissingCriticalFiles)) return false;
        return MinimumConfidence is null || group.Confidence <= MinimumConfidence.Value;
    }

    private void RefreshResultProperties()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PotentialReclaimableSize));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StaleText));
    }

    private void NotifyCommandStates()
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OpenAssetCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CompareSelectedPairCommand.NotifyCanExecuteChanged();
        ExportGroupCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }
}

public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroupResult group)
    {
        GroupId = group.GroupId;
        Category = group.Category;
        Confidence = group.Confidence;
        AssetCount = group.Members.Count;
        EstimatedDuplicateSize = DuplicateAnalysisViewModelFormat.FormatBytes(group.EstimatedDuplicateSizeBytes);
        Reasons = group.Reasons.Select(static reason => $"{reason.Field}: {reason.Message}").ToArray();
        ReasonDisplay = string.Join("; ", Reasons);
        Members = group.Members.Select(static member => new DuplicateMemberViewModel(member)).ToArray();
    }

    public string GroupId { get; }
    public DuplicateCategory Category { get; }
    public DuplicateConfidence Confidence { get; }
    public int AssetCount { get; }
    public string EstimatedDuplicateSize { get; }
    public IReadOnlyList<string> Reasons { get; }
    public string ReasonDisplay { get; }
    public IReadOnlyList<DuplicateMemberViewModel> Members { get; }
}

public sealed class DuplicateMemberViewModel(DuplicateGroupMember member)
{
    public string AssetName => member.AssetName;
    public string AssetId => member.AssetId;
    public string RelativePath => member.RelativePath;
    public string AssetFolderPath => member.AssetFolderPath;
    public string JsonPath => member.JsonPath;
    public AssetCompletenessStatus Completeness => member.Completeness;
    public int FileCount => member.FileCount;
    public string TotalSize => DuplicateAnalysisViewModelFormat.FormatBytes(member.TotalSizeBytes);
    public DuplicateHashStatus HashStatus => member.HashStatus;
}

internal static class DuplicateAnalysisViewModelFormat
{
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }
}
