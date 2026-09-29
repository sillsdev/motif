using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>The kinds of change a person can collect about a word's analyses, before any is written.</summary>
public static class ChangeKinds
{
    /// <summary>Approve the analysis the person selected from one word's reading list.</summary>
    public const string Approve = "approve";

    /// <summary>Reject the analysis, so FieldWorks never offers it as a guess.</summary>
    public const string Reject = "reject";

    /// <summary>Take back a human opinion, leaving the analysis a candidate again.</summary>
    public const string Candidate = "candidate";

    /// <summary>Mark the word's spelling as incorrect in FieldWorks.</summary>
    public const string IncorrectSpelling = "incorrect-spelling";

    /// <summary>Send the parser's reading to FieldWorks as a new candidate analysis.</summary>
    public const string AddCandidate = "add-candidate";

    /// <summary>Remove one stored analysis from FieldWorks.</summary>
    public const string RemoveAnalysis = "remove-analysis";

    /// <summary>The words a change of <paramref name="kind"/> is listed with.</summary>
    public static string LabelOf(string kind) => kind switch
    {
        Approve => "Approve",
        Reject => "Reject",
        Candidate => "Back to candidate",
        IncorrectSpelling => "Incorrect spelling",
        AddCandidate => "Add as candidate",
        RemoveAnalysis => "Remove analysis",
        _ => kind,
    };

}

/// <summary>
/// The observable view of pending changes and each change's current fit with the FieldWorks project.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject, IProjectStateParticipant
{
    private readonly ICommandClient _client;
    private readonly List<string> _collectionNotices = [];
    private int _projectGeneration;
    public ChangesViewModel(ICommandClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RemoveCommand = new AsyncRelayCommand<ChangeViewModel>(RemoveAsync);
        Items.CollectionChanged += (_, _) => Raise();
    }

    public ObservableCollection<ChangeViewModel> Items { get; } = [];

    /// <summary>The project these changes belong to, or <see langword="null"/> before one is open.</summary>
    public string? ProjectPath { get; private set; }

    /// <summary>Loads the pending changes belonging to <paramref name="projectPath"/>.</summary>
    public async Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ProjectPath = projectPath;
        Reset();
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>How many changes wait.</summary>
    public int Count => Items.Count;

    public IAsyncRelayCommand<ChangeViewModel> RemoveCommand { get; }

    public async Task ReconfirmAsync(ChangeViewModel change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.ReconfirmPendingChangeAsync(new ReconfirmPendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change.ChangeId), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }

    public PendingChangesSnapshot Snapshot { get; private set; } = new(null, "none", [], []);

    public Refusal? LastRefusal { get; private set; }

    /// <summary>The last refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => LastRefusal is { } refusal ? WindowRefusal.From(refusal) : null;

    /// <summary>Replacement and skipped-word results from the current collection action.</summary>
    public string? CollectionNotice => _collectionNotices.Count == 0
        ? null : string.Join(" ", _collectionNotices);

    public void BeginCollection()
    {
        _collectionNotices.Clear();
        OnPropertyChanged(nameof(CollectionNotice));
    }

    public bool HasError => LastRefusal is not null;

    public string? AssessmentId { get; set; }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) return;
        await ReloadAsync(path, _projectGeneration, cancellationToken).ConfigureAwait(true);
    }

    public async Task PutAsync(ChangeIntent change, CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        await PutAsync(change, path, _projectGeneration, cancellationToken).ConfigureAwait(true);
    }

    private async Task PutAsync(
        ChangeIntent change, string path, int generation, CancellationToken cancellationToken)
    {
        var outcome = await _client.PutPendingChangeAsync(new PutPendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change),
            cancellationToken).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken);
    }

    public async Task RecheckAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.RecheckPendingChangesAsync(new RecheckPendingChangesRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages removal of one stored analysis from its wordform.</summary>
    public async Task RemoveAnalysisAsync(string wordformId, string word, string analysisId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            CanonicalId.Mint().Value, wordformId, word, analysisId);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages removal of selected stored analyses.</summary>
    public async Task RemoveAnalysesAsync(IReadOnlyList<string> analysisIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysisIds);
        if (analysisIds.Count == 0) throw new ArgumentException("Choose at least one analysis.", nameof(analysisIds));
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            AnalysisIds: analysisIds);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages removal of the stored analyses used in one Text.</summary>
    public async Task RemoveAnalysesInTextAsync(Guid textId, CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            TextId: textId);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages the missing readings from a complete Assessment in one word, Selection, or Text.</summary>
    public async Task AcceptNewSetAsync(string assessmentId, string? wordformId = null, Guid? textId = null,
        bool selection = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId);
        if ((wordformId is null ? 0 : 1) + (textId is null ? 0 : 1) + (selection ? 1 : 0) != 1)
            throw new ArgumentException("Choose one word, one Selection, or one Text.");
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var request = new AcceptNewSetRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            assessmentId, wordformId, textId, selection);
        var outcome = await _client.AcceptNewSetAsync(request, cancellationToken).ConfigureAwait(true);
        await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    private async Task AcceptStagingOutcomeAsync(CommandOutcome<PendingChangesSnapshot> outcome,
        string path, int generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }

    public async Task AddAsync(string kind, CompareWordViewModel word,
        WorkspacePage originPage = WorkspacePage.Texts)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var readings = kind == ChangeKinds.AddCandidate ? word.ReadingChoices :
            word.SelectedReading is { } selected ? [selected] : [];
        if (kind == ChangeKinds.IncorrectSpelling)
        {
            await AddOneAsync(kind, word, null, originPage, path, generation).ConfigureAwait(true);
            return;
        }
        Refusal? firstRefusal = null;
        foreach (var reading in readings)
        {
            await AddOneAsync(kind, word, reading, originPage, path, generation).ConfigureAwait(true);
            firstRefusal ??= LastRefusal;
        }
        if (!IsCurrentProject(path, generation)) return;
        if (firstRefusal is not null)
        {
            LastRefusal = firstRefusal;
            OnPropertyChanged(nameof(LastRefusal));
            OnPropertyChanged(nameof(ShownRefusal));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public async Task AddFromTextAsync(string kind, ResultsTokenViewModel token, ResultsReadingViewModel? reading = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        var occurrence = kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate
            ? token.Occurrence : null;
        var wordformId = token.WordformId is { } id ? CanonicalId.FromGuid(id).Value : string.Empty;
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, wordformId, token.Form,
            AssessmentId, reading?.Analysis, DisplayReading: reading?.Text,
            ReadingIndex: reading?.Index, OriginPage: WorkspacePage.Texts.ToString(),
            Occurrence: occurrence)).ConfigureAwait(true);
    }

    public async Task AddFromMarkingAsync(AnalysisMarkingAction action, ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(token);
        var kind = action.ChangeKind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate or
            ChangeKinds.AddCandidate
            ? action.ChangeKind
            : throw new InvalidOperationException("This marking action does not stage a project change.");
        var hasParserReading = action.Reading is not null;
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, token.WordformId is { } id ? CanonicalId.FromGuid(id).Value : "", token.Form,
            hasParserReading ? AssessmentId : null, action.Reading, action.StoredAnalysisId,
            ReadingIndex: action.ReadingIndex, OriginPage: WorkspacePage.Texts.ToString(),
            Occurrence: kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate
                ? token.Occurrence : null)).ConfigureAwait(true);
    }

    /// <summary>Adds an Approve change for one stored analysis without a parser reading.</summary>
    /// <param name="word">The word form that owns the analysis.</param>
    /// <param name="storedAnalysisId">The canonical ID of the stored analysis.</param>
    /// <param name="displayReading">The analysis text shown in Review changes.</param>
    /// <param name="originPage">The page that collected this change.</param>
    public async Task ApproveStoredAnalysisAsync(string word, string storedAnalysisId,
        string displayReading, WorkspacePage originPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedAnalysisId);
        ArgumentNullException.ThrowIfNull(displayReading);
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, ChangeKinds.Approve, "", word,
            StoredAnalysisId: storedAnalysisId, DisplayReading: displayReading,
            OriginPage: originPage.ToString())).ConfigureAwait(true);
    }

    private async Task AddOneAsync(string kind, CompareWordViewModel word, CompareReadingChoice? choice,
        WorkspacePage originPage, string path, int generation)
    {
        if (!IsCurrentProject(path, generation)) return;
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, "", word.Word,
                AssessmentId, choice?.Reading, DisplayReading: choice?.Label ?? word.FirstReading,
                ReadingIndex: choice?.Index, OriginPage: originPage.ToString()), path, generation,
            CancellationToken.None).ConfigureAwait(true);
    }

    private async Task RemoveAsync(ChangeViewModel? change)
    {
        if (change is null) return;
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.RemovePendingChangeAsync(new RemovePendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change.GroupId ?? change.ChangeId),
            CancellationToken.None).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, CancellationToken.None);
    }

    private async Task ReloadAfterConflictAsync(
        Refusal conflict, string path, int generation, CancellationToken cancellationToken)
    {
        await ReloadAsync(path, generation, cancellationToken);
        if (!IsCurrentProject(path, generation)) return;
        if (LastRefusal is not null) return;
        LastRefusal = conflict;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
    }

    public void Reset()
    {
        _projectGeneration++;
        Items.Clear();
        Snapshot = new PendingChangesSnapshot(null, "none", [], []);
        LastRefusal = null;
        BeginCollection();
        AssessmentId = null;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
        Raise();
    }

    void IProjectStateParticipant.ClearProject()
    {
        ProjectPath = null;
        Reset();
    }

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        OpenProjectAsync(projectPath, cancellationToken);

    private async Task ReloadAsync(string path, int generation, CancellationToken cancellationToken)
    {
        var outcome = await _client.LoadPendingChangesAsync(new PendingChangesRequest(
            path, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true);
        Accept(outcome, path, generation);
    }

    private bool IsCurrentProject(string path, int generation) =>
        generation == _projectGeneration && string.Equals(path, ProjectPath, StringComparison.Ordinal);

    private void Accept(CommandOutcome<PendingChangesSnapshot> outcome, string path, int generation)
    {
        if (!IsCurrentProject(path, generation)) return;
        LastRefusal = outcome.Refusal;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
        if (outcome.Value is not { } snapshot) return;
        Snapshot = snapshot;
        OnPropertyChanged(nameof(Snapshot));
        if (snapshot.ReplacedChangeId is not null)
            AddCollectionNotice("Replaced an earlier pending change.");
        if (snapshot.CancelledChangeId is not null)
            AddCollectionNotice("Cancelled the pending choice.");
        if (snapshot.SkippedWord is not null)
            AddCollectionNotice("Skipped one word because a choice is already pending.");
        Items.Clear();
        foreach (var change in snapshot.Changes)
        {
            var fit = snapshot.FitSummary.FirstOrDefault(item => item.ChangeId == change.ChangeId);
            Items.Add(new ChangeViewModel(change.Kind, change.Word,
                change.DisplayReading ?? "", change.ChangeId, fit, change.Analyses, change.OriginPage,
                fit?.Occurrence ?? change.Occurrence, change.StoredAnalysisId, change.ReadingIndex,
                change.GroupId));
        }
        Raise();
    }

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Independent;

    private void AddCollectionNotice(string notice)
    {
        if (!_collectionNotices.Contains(notice, StringComparer.Ordinal))
            _collectionNotices.Add(notice);
        OnPropertyChanged(nameof(CollectionNotice));
    }

    public bool HasItems => Items.Count > 0;

    /// <summary>How many changes wait, as the Texts page's strip says it.</summary>
    public string CountText => Items.Count == 1 ? "1 change not applied yet" : $"{Items.Count:N0} changes not applied yet";

    private void Raise()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>One collected change: what should happen to one word, and what it held when the change was chosen.</summary>
public sealed partial class ChangeViewModel(string kind, string word, string reading,
    string? changeId = null, ChangeFit? fit = null, IReadOnlyList<ReviewAnalysis>? analyses = null,
    string? originPage = null, OccurrenceAnchor? occurrence = null, string? storedAnalysisId = null,
    int? readingIndex = null, string? groupId = null) : ObservableObject
{
    public WorkspacePage OriginPage { get; } = Enum.TryParse<WorkspacePage>(originPage, out var page) &&
        page != WorkspacePage.Review ? page : WorkspacePage.Texts;
    public string ChangeId { get; } = changeId ?? CanonicalId.Mint().Value;
    public string? GroupId { get; } = groupId;
    public ChangeFit? Fit { get; } = fit;
    public OccurrenceAnchor? Occurrence { get; } = occurrence;
    public string? StoredAnalysisId { get; } = storedAnalysisId;
    public int? ReadingIndex { get; } = readingIndex;
    public string FitStatus => Fit?.Status switch
    {
        null => string.Empty,
        ChangeFitStatus.Fits => "Still fits the project.",
        ChangeFitStatus.Uncertain => "Uncertain — check again",
        _ => "No longer fits the current project. Remove this change before review.",
    };
    public bool IsUncertain => Fit?.Status == ChangeFitStatus.Uncertain;
    public bool IsNoLongerFits => Fit?.Status == ChangeFitStatus.NoLongerFits;
    public bool HasUncertainty => Fit?.Uncertainty is not null;
    public bool HasContext => Occurrence is not null || HasUncertainty;
    public string ShowContextAutomationName => $"Show context: {Word}";
    public string UncertaintyReason => Fit?.Uncertainty?.Reason switch
    {
        null => string.Empty,
        "The source Segment is gone or no longer resolves uniquely." =>
            "The sentence this decision refers to is no longer available.",
        "The source occurrence no longer resolves uniquely." =>
            "The word this decision refers to is no longer in the sentence.",
        "The paragraph parse is not current." => "FieldWorks has not reparsed this paragraph after the edit.",
        "The paragraph parse was not current when the decision was collected." =>
            "FieldWorks had not parsed this paragraph when you made this decision.",
        "The words in the source sentence have changed." =>
            "The words in the sentence have changed since you made this decision.",
        _ => "This sentence needs another check.",
    };
    public IReadOnlyList<UncertaintyTokenViewModel> BeforeWords { get; } =
        UncertaintyTokenViewModel.Create(fit?.Uncertainty?.BeforeTokens, fit?.Uncertainty?.AfterTokens, beforeSide: true);
    public IReadOnlyList<UncertaintyTokenViewModel> AfterWords { get; } =
        UncertaintyTokenViewModel.Create(fit?.Uncertainty?.BeforeTokens, fit?.Uncertainty?.AfterTokens, beforeSide: false);

    [ObservableProperty]
    private bool _isContextExpanded;

    /// <summary>The sentence tokens surrounding the exact occurrence, when the Texts page has loaded them.</summary>
    public IReadOnlyList<ResultsTokenViewModel> ContextTokens { get; private set; } = [];

    public string ContextUnavailableText => IsContextExpanded && Occurrence is not null && ContextTokens.Count == 0
        ? "The sentence is not loaded. Open Analyze texts to see it."
        : string.Empty;

    public bool HasUnavailableContext => ContextUnavailableText.Length > 0;

    internal void SetContextTokens(IReadOnlyList<ResultsTokenViewModel> tokens)
    {
        ContextTokens = tokens;
        OnPropertyChanged(nameof(ContextTokens));
        OnPropertyChanged(nameof(ContextUnavailableText));
        OnPropertyChanged(nameof(HasUnavailableContext));
    }

    public void ToggleContext() => IsContextExpanded = !IsContextExpanded;

    partial void OnIsContextExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ContextUnavailableText));
        OnPropertyChanged(nameof(HasUnavailableContext));
    }
    public string Kind { get; } = kind;
    public string Label { get; } = ChangeKinds.LabelOf(kind);
    /// <summary>What this added analysis will be and where it came from.</summary>
    public string SourceText => IsAddition
        ? $"Added as {StagedTransition.AfterApply} from {AdditionSource}"
        : string.Empty;
    public bool HasSourceText => SourceText.Length > 0;

    private bool IsAddition => Kind == ChangeKinds.AddCandidate ||
        StoredAnalysisId is null && Kind is (ChangeKinds.Approve or ChangeKinds.Reject);

    private string AdditionSource => GroupId is not null ? "accepting a set"
        : Kind is ChangeKinds.Approve or ChangeKinds.Reject ? "PanGloss" : "Add";
    /// <summary>The original change shown beneath an Uncertain item.</summary>
    public string TransitionText => StagedTransition.Text;
    public OpinionMarkKind? NowOpinionMark => MarkFor(StagedTransition.Now);
    public OpinionMarkKind? AfterOpinionMark => MarkFor(StagedTransition.AfterApply);
    public bool HasNowOpinionMark => NowOpinionMark is not null;
    public bool HasAfterOpinionMark => AfterOpinionMark is not null;
    public string ReviewLabel => Kind == ChangeKinds.Approve && analyses is { Count: > 1 }
        ? $"Approve 1 of {analyses.Count} analyses" : Label;
    public string Word { get; } = word;
    public string CheckAgainAutomationName => $"Check again: {Word}";
    public string UndoAutomationName => GroupId is null ? $"Undo: {Word}"
        : $"Undo accepted set containing: {Word}";

    /// <summary>The parser's reading, for a change that sends it to FieldWorks or judges it.</summary>
    public string Reading { get; } = reading;

    /// <summary>The morphs and glosses of each reading, with the chosen reading marked.</summary>
    public IReadOnlyList<ReviewAnalysisViewModel> Analyses { get; } = BuildAnalyses(analyses, kind);

    public bool HasAnalyses => Analyses.Count > 0;

    public StagedMarkingTransition StagedTransition { get; } =
        TransitionFor(kind, storedAnalysisId, analyses) with
        {
            StoredAnalysisId = storedAnalysisId,
            ReadingIndex = readingIndex,
            FitStatus = fit?.Status,
        };

    public string Summary => $"{Word}: {Label}";

    private static IReadOnlyList<ReviewAnalysisViewModel> BuildAnalyses(
        IReadOnlyList<ReviewAnalysis>? analyses, string kind) =>
        analyses?.Select(analysis => new ReviewAnalysisViewModel(analysis, kind)).ToArray() ?? [];

    private static StagedMarkingTransition TransitionFor(string kind, string? storedAnalysisId,
        IReadOnlyList<ReviewAnalysis>? analyses)
    {
        var touched = analyses?.FirstOrDefault(analysis => analysis.Touched);
        var before = touched is { Stored: true } ? OpinionLabel(touched.Opinion)
            : touched is not null || storedAnalysisId is null ? "Not in FieldWorks"
            : kind == ChangeKinds.Approve ? "Unknown" : "Current opinion";
        var after = kind switch
        {
            ChangeKinds.Approve => "Approved",
            ChangeKinds.Reject => "Disapproved",
            ChangeKinds.Candidate or ChangeKinds.AddCandidate => "Unknown",
            ChangeKinds.RemoveAnalysis => "Removed",
            ChangeKinds.IncorrectSpelling => "Incorrect",
            _ => "Changed",
        };
        if (kind == ChangeKinds.IncorrectSpelling) before = "Current spelling";
        return new StagedMarkingTransition(before, after);
    }

    private static string OpinionLabel(string opinion) => opinion switch
    {
        ReadingGrade.Approved => "Approved",
        ReadingGrade.Disapproved => "Disapproved",
        _ => "Unknown",
    };

    private static OpinionMarkKind? MarkFor(string opinion) => opinion switch
    {
        "Approved" => OpinionMarkKind.Approved,
        "Disapproved" => OpinionMarkKind.Disapproved,
        "Unknown" => OpinionMarkKind.Unknown,
        "Not in FieldWorks" or "Removed" => OpinionMarkKind.None,
        _ => null,
    };
}
/// <summary>One word in the before or after sentence shown for an uncertain change.</summary>
public sealed record UncertaintyTokenViewModel(int Index, string WordformId, string Form, bool IsChanged)
{
    public static IReadOnlyList<UncertaintyTokenViewModel> Create(
        IReadOnlyList<OccurrenceWordToken>? before, IReadOnlyList<OccurrenceWordToken>? after, bool beforeSide)
    {
        if (before is null || after is null) return [];
        var left = before.Select(Key).ToArray();
        var right = after.Select(Key).ToArray();
        var matches = LongestCommonSubsequence(left, right);
        var changed = beforeSide
            ? Enumerable.Range(0, left.Length).Where(index => !matches.Contains((index, true))).ToHashSet()
            : Enumerable.Range(0, right.Length).Where(index => !matches.Contains((index, false))).ToHashSet();
        var words = beforeSide ? before : after;
        return words.Select((token, index) => new UncertaintyTokenViewModel(
            token.Index, token.WordformId, token.Form, changed.Contains(index))).ToArray();
    }

    private static (int, bool)[] LongestCommonSubsequence(string[] left, string[] right)
    {
        var lengths = new int[left.Length + 1, right.Length + 1];
        for (var i = left.Length - 1; i >= 0; i--)
            for (var j = right.Length - 1; j >= 0; j--)
                lengths[i, j] = left[i] == right[j] ? lengths[i + 1, j + 1] + 1 :
                    Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var result = new List<(int, bool)>();
        var x = 0;
        var y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (left[x] == right[y]) { result.Add((x++, true)); result.Add((y++, false)); }
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }
        return result.ToArray();
    }

    private static string Key(OccurrenceWordToken token) =>
        token.WordformId + "\0" + token.Form.Normalize(System.Text.NormalizationForm.FormD);
}

/// <summary>A reading as the Review page displays its morphs, prior opinion and proposed opinion.</summary>
public sealed class ReviewAnalysisViewModel
{
    public ReviewAnalysisViewModel(ReviewAnalysis analysis, string changeKind)
    {
        Morphs = analysis.Reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Touched = analysis.Touched;
        ParserBuilt = !analysis.Stored;
        Opinion = analysis.Touched ? changeKind switch
        {
            ChangeKinds.Approve => ReadingGradeLabels.Of(ReadingGrade.Approved),
            ChangeKinds.Reject => ReadingGradeLabels.Of(ReadingGrade.Disapproved),
            ChangeKinds.Candidate or ChangeKinds.AddCandidate => ReadingGradeLabels.Of(ReadingGrade.Candidate),
            _ => analysis.Opinion,
        } : analysis.Opinion is ReadingGrade.Approved or ReadingGrade.Disapproved or ReadingGrade.Candidate
            ? ReadingGradeLabels.Of(analysis.Opinion)
            : ReadingGradeLabels.NotPresent;
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public bool Touched { get; }
    public bool ParserBuilt { get; }
    public string Opinion { get; }
    public string Source => ParserBuilt ? "Parser reading" : "Stored analysis";
}
