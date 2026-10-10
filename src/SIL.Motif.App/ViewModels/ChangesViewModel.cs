using System.Collections.ObjectModel;
using System.Text.Json;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Projection.Usage;

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

    /// <summary>Add all missing readings from the current completed Assessment.</summary>

    /// <summary>The words a change of <paramref name="kind"/> is listed with.</summary>
    public static string LabelOf(string kind) => kind switch
    {
        Approve => "Approve",
        Reject => "Disapprove",
        Candidate => "Make Unknown",
        IncorrectSpelling => "Incorrect spelling",
        AddCandidate => "Add as Unknown",
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
    private bool _isReplacingItems;
    private readonly Stack<StagingHistoryEntry> _undoHistory = new();
    private readonly Stack<StagingHistoryEntry> _redoHistory = new();
    private PendingChangesSnapshot? _stagingActionBefore;
    private int _stagingActionDepth;
    private bool _isReplayingStagingHistory;
    public ChangesViewModel(ICommandClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        RemoveCommand = new AsyncRelayCommand<ChangeViewModel>(RemoveAsync);
        Items.CollectionChanged += (_, _) =>
        {
            if (!_isReplacingItems) Raise();
        };
    }

    public ObservableCollection<ChangeViewModel> Items { get; } = [];

    internal int ProjectGeneration => _projectGeneration;

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
        using var usageAction = _client.BeginUsageAction("reconfirm-pending-change",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("changeId"));
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
        using var usageAction = _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.List("changes", 1));
        _ = await PutAsync(change, path, _projectGeneration, cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool> PutAsync(
        ChangeIntent change, string path, int generation, CancellationToken cancellationToken,
        ExpectedContext? expectedContext = null)
    {
        var before = Snapshot;
        var outcome = await _client.PutPendingChangeAsync(new PutPendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, change) { ExpectedContext = expectedContext },
            cancellationToken).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return false;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken);
        if (outcome.Succeeded) RecordStagingAction(before);
        return outcome.Succeeded;
    }

    public async Task RecheckAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path) return;
        using var usageAction = _client.BeginUsageAction("recheck-pending-changes", UsageArgumentShape.Text("fwDataPath"));
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
    public async Task<bool> RemoveAnalysisAsync(string wordformId, string word, string analysisId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        using var usageAction = _client.BeginUsageAction("remove-analysis", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.List("analyses", 1));
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            CanonicalId.Mint().Value, wordformId, word, analysisId);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        return await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    internal Task<bool> RemoveCapturedAnalysisAsync(WordActionTarget target, string analysisId)
    {
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        return PutAsync(new ChangeIntent(CanonicalId.Mint().Value, ChangeKinds.RemoveAnalysis,
            target.WordformId is { } id ? CanonicalId.FromGuid(id).Value : string.Empty, target.Form,
            StoredAnalysisId: analysisId, OriginPage: WorkspacePage.Texts.ToString()), path,
            _projectGeneration, CancellationToken.None, target.ExpectedContext);
    }

    /// <summary>Stages removal of selected stored analyses.</summary>
    public async Task<bool> RemoveAnalysesAsync(IReadOnlyList<string> analysisIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysisIds);
        if (analysisIds.Count == 0) throw new ArgumentException("Choose at least one analysis.", nameof(analysisIds));
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        using var usageAction = _client.BeginUsageAction("remove-analysis", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.List("analyses", analysisIds.Count));
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            AnalysisIds: analysisIds);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        return await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages removal of the stored analyses used in one Text.</summary>
    public async Task<bool> RemoveAnalysesInTextAsync(Guid textId, CancellationToken cancellationToken = default)
    {
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        using var usageAction = _client.BeginUsageAction("remove-analysis", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("textId"));
        var generation = _projectGeneration;
        var request = new RemoveAnalysisRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            TextId: textId);
        var outcome = await _client.RemoveAnalysisAsync(request, cancellationToken).ConfigureAwait(true);
        return await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stages the missing readings from a complete Assessment in one word, Selection, or Text.</summary>
    public async Task<bool> AcceptNewSetAsync(string assessmentId, string? wordformId = null, Guid? textId = null,
        bool selection = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId);
        if ((wordformId is null ? 0 : 1) + (textId is null ? 0 : 1) + (selection ? 1 : 0) != 1)
            throw new ArgumentException("Choose one word, one Selection, or one Text.");
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var targetShape = selection ? UsageArgumentShape.Flag("selection") : textId is not null
            ? UsageArgumentShape.Text("textId") : UsageArgumentShape.Text("wordformId");
        using var usageAction = _client.BeginUsageAction("accept-new-set", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("assessmentId"), targetShape);
        var generation = _projectGeneration;
        var request = new AcceptNewSetRequest(path, MotifProductVersion.CurrentText, Snapshot.Revision,
            assessmentId, wordformId, textId, selection);
        var outcome = await _client.AcceptNewSetAsync(request, cancellationToken).ConfigureAwait(true);
        return await AcceptStagingOutcomeAsync(outcome, path, generation, cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool> AcceptStagingOutcomeAsync(CommandOutcome<PendingChangesSnapshot> outcome,
        string path, int generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentProject(path, generation)) return false;
        var before = Snapshot;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
        if (outcome.Succeeded) RecordStagingAction(before);
        return outcome.Succeeded;
    }

    internal IDisposable BeginMatrixStagingAction(
        string kind, IReadOnlyList<CompareWordViewModel> chosenWords)
    {
        ArgumentNullException.ThrowIfNull(chosenWords);
        var count = kind switch
        {
            ChangeKinds.IncorrectSpelling => chosenWords.Count,
            ChangeKinds.AddCandidate => chosenWords.Sum(word => word.ReadingChoices.Count),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"), UsageArgumentShape.List("changes", count));
    }

    internal IDisposable BeginMatrixStagingAction(string kind, int count) =>
        _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"), UsageArgumentShape.List("changes", count));

    internal async Task AddMatrixActionAsync(WordChangeAction action)
    {
        if (!action.IsAvailable) return;
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        var target = action.Target;
        var readings = action.Kind == ChangeKinds.IncorrectSpelling ? new WordActionReading?[] { null }
            : action.Readings.Cast<WordActionReading?>().ToArray();
        using var usageAction = BeginMatrixStagingAction(action.Kind, readings.Length);
        Refusal? firstRefusal = null;
        foreach (var reading in readings)
        {
            await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, action.Kind,
                target.WordformId is { } id ? CanonicalId.FromGuid(id).Value : string.Empty, target.Form,
                target.AssessmentId, reading?.Analysis, DisplayReading: reading?.Text ?? string.Empty,
                ReadingIndex: reading?.Index, OriginPage: WorkspacePage.Texts.ToString()), path, generation,
                CancellationToken.None, target.ExpectedContext).ConfigureAwait(true);
            firstRefusal ??= LastRefusal;
            if (!IsCurrentProject(path, generation)) return;
        }
        if (firstRefusal is not null)
        {
            LastRefusal = firstRefusal;
            OnPropertyChanged(nameof(LastRefusal));
            OnPropertyChanged(nameof(ShownRefusal));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public async Task AddAsync(string kind, CompareWordViewModel word,
        WorkspacePage originPage = WorkspacePage.Texts)
    {
        ArgumentNullException.ThrowIfNull(word);
        using var historyAction = BeginStagingAction();
        if (kind is not (ChangeKinds.AddCandidate or ChangeKinds.IncorrectSpelling))
            throw new InvalidOperationException("Opinions change one analysis at a time, in the text.");
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        IReadOnlyList<CompareReadingChoice> readings = kind == ChangeKinds.AddCandidate ? word.ReadingChoices : [];
        var operationCount = kind == ChangeKinds.IncorrectSpelling ? 1 : readings.Count;
        using var usageAction = _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"), UsageArgumentShape.List("changes", operationCount));
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

    public Task<bool> AddFromTextAsync(string kind, ResultsTokenViewModel token,
        ResultsReadingViewModel? reading = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        return AddFromTextAsync(kind, token.CaptureActionTarget(AssessmentId),
            reading is null ? null : new WordActionReading(reading.Analysis, reading.Text, reading.Index));
    }

    /// <summary>Stages a Text action from frozen identity and reading records without constructing display models.</summary>
    public async Task<bool> AddFromTextAsync(string kind, WordActionTarget target, WordActionReading? reading = null,
        string? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ProjectPath is not { } path)
            throw new InvalidOperationException("Open a project before collecting changes.");
        var generation = _projectGeneration;
        using var usageAction = _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"));
        var occurrence = kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate
            ? target.Occurrence : null;
        var wordformId = target.WordformId is { } id ? CanonicalId.FromGuid(id).Value : string.Empty;
        return await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, wordformId, target.Form,
            target.AssessmentId, reading?.Analysis, DisplayReading: reading?.Text,
            ReadingIndex: reading?.Index, OriginPage: WorkspacePage.Texts.ToString(),
            Occurrence: occurrence) { GroupId = groupId }, path, generation, CancellationToken.None, target.ExpectedContext).ConfigureAwait(true);
    }

    public Task<bool> AddFromMarkingAsync(AnalysisMarkingAction action, ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return AddFromMarkingAsync(action, token.CaptureActionTarget(AssessmentId));
    }

    /// <summary>Stages a marking choice against its frozen target and captured Selection evidence.</summary>
    public async Task<bool> AddFromMarkingAsync(AnalysisMarkingAction action, WordActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(target);
        var kind = action.ChangeKind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate or
            ChangeKinds.AddCandidate
            ? action.ChangeKind
            : throw new InvalidOperationException("This marking action does not stage a project change.");
        var hasParserReading = action.Reading is not null;
        if (ProjectPath is not { } path) throw new InvalidOperationException("Open a project before collecting changes.");
        var argumentShape = new List<string>
        {
            UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"),
        };
        if (hasParserReading)
            argumentShape.Add(UsageArgumentShape.Text("reading"));
        using var usageAction = _client.BeginUsageAction("put-pending-change", [.. argumentShape]);
        return await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, target.WordformId is { } id ? CanonicalId.FromGuid(id).Value : "", target.Form,
            hasParserReading ? target.AssessmentId : null, action.Reading, action.StoredAnalysisId,
            ReadingIndex: action.ReadingIndex, OriginPage: WorkspacePage.Texts.ToString(),
            Occurrence: kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate
                ? target.Occurrence : null), path, _projectGeneration, CancellationToken.None, target.ExpectedContext).ConfigureAwait(true);
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
        await StageStoredOpinionAsync(new PendingStoredOpinionChange(ChangeKinds.Approve, word, null,
            storedAnalysisId, displayReading), originPage).ConfigureAwait(true);
    }

    internal async Task<bool> StageStoredOpinionAsync(PendingStoredOpinionChange change,
        WorkspacePage originPage = WorkspacePage.Texts)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind is not (ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate))
            throw new ArgumentOutOfRangeException(nameof(change), "A stored analysis opinion must be Approved, Disapproved or Unknown.");
        ArgumentException.ThrowIfNullOrWhiteSpace(change.Word);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.StoredAnalysisId);
        if (ProjectPath is not { } path) return false;
        var generation = _projectGeneration;
        using var usageAction = _client.BeginUsageAction("put-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("kind"), UsageArgumentShape.Text("storedAnalysisId"));
        return await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, change.Kind,
            change.WordformId is { } wordformId ? CanonicalId.FromGuid(wordformId).Value : string.Empty,
            change.Word, StoredAnalysisId: change.StoredAnalysisId, DisplayReading: change.DisplayReading,
            OriginPage: originPage.ToString()), path, generation, CancellationToken.None, change.ExpectedContext).ConfigureAwait(true);
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

    private Task RemoveAsync(ChangeViewModel? change) => RemoveAsync(change, wholeGroup: false);

    internal Task RemoveGroupAsync(ChangeViewModel change) => RemoveAsync(change, wholeGroup: true);

    private async Task RemoveAsync(ChangeViewModel? change, bool wholeGroup)
    {
        if (change is null) return;
        if (ProjectPath is not { } path) return;
        using var usageAction = _client.BeginUsageAction("remove-pending-change", UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("changeId"));
        var generation = _projectGeneration;
        var before = Snapshot;
        var outcome = await _client.RemovePendingChangeAsync(new RemovePendingChangeRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision,
            wholeGroup ? change.GroupId ?? change.ChangeId : change.ChangeId),
            CancellationToken.None).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Succeeded) RecordStagingAction(before);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, CancellationToken.None);
    }

    internal IDisposable BeginStagingAction()
    {
        if (_stagingActionDepth++ == 0) _stagingActionBefore = Snapshot;
        return new StagingAction(this);
    }

    internal void ClearStagingHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _stagingActionBefore = null;
        _stagingActionDepth = 0;
    }

    internal Task<string> UndoStagingActionAsync() => MoveStagingHistoryAsync(redo: false);

    internal Task<string> RedoStagingActionAsync() => MoveStagingHistoryAsync(redo: true);

    private void EndStagingAction()
    {
        if (_stagingActionDepth == 0) return;
        if (--_stagingActionDepth != 0) return;
        var before = _stagingActionBefore;
        _stagingActionBefore = null;
        if (before is not null) RecordStagingAction(before);
    }

    private void RecordStagingAction(PendingChangesSnapshot before)
    {
        if (_isReplayingStagingHistory || _stagingActionDepth > 0 ||
            HistorySignature(before) == HistorySignature(Snapshot)) return;
        _undoHistory.Push(new StagingHistoryEntry(before, Snapshot));
        _redoHistory.Clear();
    }

    private async Task<string> MoveStagingHistoryAsync(bool redo)
    {
        var source = redo ? _redoHistory : _undoHistory;
        var destination = redo ? _undoHistory : _redoHistory;
        if (source.Count == 0) return redo ? "There is nothing to redo." : "There is nothing to undo.";
        if (ProjectPath is not { } path) return "Open a project before undoing changes.";

        var generation = _projectGeneration;
        var history = source.Peek();
        var expected = redo ? history.Before : history.After;
        var target = redo ? history.After : history.Before;
        _isReplayingStagingHistory = true;
        try
        {
            var loaded = await _client.LoadPendingChangesAsync(
                new PendingChangesRequest(path, MotifProductVersion.CurrentText), CancellationToken.None)
                .ConfigureAwait(true);
            if (!IsCurrentProject(path, generation)) return "The open project changed before the action could be undone.";
            if (!loaded.Succeeded || loaded.Value is not { } current)
            {
                source.Clear();
                destination.Clear();
                return "Motif couldn't check the pending changes. Refresh and try again.";
            }
            Accept(loaded, path, generation);
            if (HistorySignature(current) != HistorySignature(expected))
            {
                source.Clear();
                destination.Clear();
                return "The pending changes changed outside this window. Refresh before undoing.";
            }

            var currentById = current.Changes.ToDictionary(change => change.ChangeId, StringComparer.Ordinal);
            var targetById = target.Changes.ToDictionary(change => change.ChangeId, StringComparer.Ordinal);
            var removals = current.Changes.Where(change => !targetById.TryGetValue(change.ChangeId, out var wanted) ||
                ChangeSignature(change) != ChangeSignature(wanted)).ToArray();
            var additions = target.Changes.Where(change => !currentById.TryGetValue(change.ChangeId, out var existing) ||
                ChangeSignature(change) != ChangeSignature(existing)).ToArray();
            if (additions.Any(change => change.StagingIntent is null))
            {
                source.Clear();
                destination.Clear();
                return "Motif can't restore one of these changes. Refresh pending changes before continuing.";
            }

            foreach (var change in removals)
            {
                var outcome = await _client.RemovePendingChangeAsync(new RemovePendingChangeRequest(
                    path, MotifProductVersion.CurrentText, Snapshot.Revision, change.ChangeId), CancellationToken.None)
                    .ConfigureAwait(true);
                if (!IsCurrentProject(path, generation)) return "The open project changed before the action could be undone.";
                if (!outcome.Succeeded)
                {
                    Accept(outcome, path, generation);
                    source.Clear();
                    destination.Clear();
                    return "Motif couldn't undo that staging action. Refresh and try again.";
                }
                Accept(outcome, path, generation);
            }

            foreach (var change in additions)
            {
                using var usageAction = _client.BeginUsageAction("put-pending-change",
                    UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("changes", 1));
                var outcome = await _client.PutPendingChangeAsync(new PutPendingChangeRequest(
                    path, MotifProductVersion.CurrentText, Snapshot.Revision, change.StagingIntent!),
                    CancellationToken.None).ConfigureAwait(true);
                if (!IsCurrentProject(path, generation)) return "The open project changed before the action could be undone.";
                if (!outcome.Succeeded)
                {
                    Accept(outcome, path, generation);
                    source.Clear();
                    destination.Clear();
                    return "Motif couldn't restore that staging action. Refresh and try again.";
                }
                Accept(outcome, path, generation);
            }

            if (HistorySignature(Snapshot) != HistorySignature(target))
            {
                source.Clear();
                destination.Clear();
                return "The pending changes didn't return to their earlier state. Refresh before continuing.";
            }
            source.Pop();
            destination.Push(history);
            return HistoryStatus(history, redo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or JsonException)
        {
            source.Clear();
            destination.Clear();
            return "Motif couldn't undo that staging action. Refresh and try again.";
        }
        finally
        {
            _isReplayingStagingHistory = false;
        }
    }

    private static string HistoryStatus(StagingHistoryEntry history, bool redo)
    {
        var before = history.Before.Changes.ToDictionary(change => change.ChangeId, StringComparer.Ordinal);
        var after = history.After.Changes.ToDictionary(change => change.ChangeId, StringComparer.Ordinal);
        var affected = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal)
            .Where(id => !before.TryGetValue(id, out var oldChange) || !after.TryGetValue(id, out var newChange) ||
                ChangeSignature(oldChange) != ChangeSignature(newChange))
            .Select(id => after.GetValueOrDefault(id)?.Word ?? before[id].Word)
            .Distinct(StringComparer.Ordinal).ToArray();
        var verb = redo ? "Redid" : "Undid";
        return affected.Length switch
        {
            1 => $"{verb} the change for {affected[0]}.",
            _ => $"{verb} changes to {affected.Length:N0} words.",
        };
    }

    private static string HistorySignature(PendingChangesSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        Changes = snapshot.Changes.OrderBy(change => change.ChangeId, StringComparer.Ordinal)
            .Select(change => ChangeSignature(change)),
        Fits = snapshot.FitSummary.OrderBy(fit => fit.ChangeId, StringComparer.Ordinal),
    });

    private static string ChangeSignature(PendingChange change) => JsonSerializer.Serialize(new
    {
        change.ChangeId, change.WordformId, change.Word, change.Kind, change.AssessmentId,
        change.DisplayReading, change.OperationIds, change.OriginPage, change.Occurrence,
        change.StoredAnalysisId, change.ReadingIndex, change.GroupId, change.StagingIntent,
    });

    private sealed record StagingHistoryEntry(PendingChangesSnapshot Before, PendingChangesSnapshot After);

    private sealed class StagingAction(ChangesViewModel owner) : IDisposable
    {
        private ChangesViewModel? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndStagingAction();
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
        ClearStagingHistory();
        _projectGeneration++;
        ReplaceItems([]);
        Snapshot = new PendingChangesSnapshot(null, "none", [], []);
        LastRefusal = null;
        BeginCollection();
        AssessmentId = null;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ShownRefusal));
        OnPropertyChanged(nameof(HasError));
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
        ReplaceItems(snapshot.Changes.Select(change =>
        {
            var fit = snapshot.FitSummary.FirstOrDefault(item => item.ChangeId == change.ChangeId);
            return new ChangeViewModel(change.Kind, change.Word,
                change.DisplayReading ?? "", change.ChangeId, fit, change.Analyses, change.OriginPage,
                fit?.Occurrence ?? change.Occurrence, change.StoredAnalysisId, change.ReadingIndex,
                change.GroupId, change.WordformId);
        }).ToArray());
    }

    private void ReplaceItems(IReadOnlyList<ChangeViewModel> changes)
    {
        _isReplacingItems = true;
        try
        {
            Items.Clear();
            foreach (var change in changes) Items.Add(change);
        }
        finally
        {
            _isReplacingItems = false;
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
    int? readingIndex = null, string? groupId = null, string? wordformId = null) : ObservableObject
{
    private static long _nextPresentationRevision;
    private readonly long _presentationRevision = Interlocked.Increment(ref _nextPresentationRevision);
    private WordPresentationKey PresentationKey => new($"change:{ChangeId}");

    public WorkspacePage OriginPage { get; } = Enum.TryParse<WorkspacePage>(originPage, out var page) &&
        page != WorkspacePage.Review ? page : WorkspacePage.Texts;
    public string ChangeId { get; } = changeId ?? CanonicalId.Mint().Value;
    public string? GroupId { get; } = groupId;
    public string? WordformId { get; } = wordformId;
    public ChangeFit? Fit { get; } = fit;
    public OccurrenceAnchor? Occurrence { get; } = occurrence;
    private string _whereText = occurrence is null ? "Not tied to a text occurrence" : "Text location not loaded";
    public string WhereText => _whereText;

    internal void SetWhereText(string whereText)
    {
        if (_whereText == whereText) return;
        _whereText = whereText;
        OnPropertyChanged(nameof(WhereText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(HasDetailText));
        OnPropertyChanged(nameof(Presentation));
    }

    /// <summary>
    /// Whether this choice addresses the token's exact wordform and, for an occurrence-specific choice, its
    /// exact Text location. A choice without either identity cannot be associated with a token by spelling.
    /// </summary>
    public bool Addresses(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!token.IsWord) return false;
        return Addresses(token.WordformId, token.Occurrence);
    }

    /// <summary>Joins a pending change to compact identities without constructing a displayed token.</summary>
    internal bool Addresses(Guid? wordformId, OccurrenceAnchor? occurrence)
    {
        if (WordformId is not null)
        {
            if (wordformId is not { } id || WordformId != CanonicalId.FromGuid(id).Value) return false;
        }
        else if (Occurrence is null) return false;
        return Occurrence is null || Occurrence == occurrence;
    }

    public string? StoredAnalysisId { get; } = storedAnalysisId;
    public int? ReadingIndex { get; } = readingIndex;
    public string FitStatus => Fit?.Status switch
    {
        null => string.Empty,
        ChangeFitStatus.Fits => "Still fits the project.",
        ChangeFitStatus.Uncertain => "Needs another look",
        _ => "No longer fits the current project. Remove this change before review.",
    };
    public bool IsUncertain => Fit?.Status == ChangeFitStatus.Uncertain;
    public bool StillFits => Fit?.StillFits == true;

    /// <summary>The first line of the row's staged note: whether the change waits as staged or needs a look.</summary>
    public string NoteTitle => IsUncertain ? "Needs another look" : "Staged";

    /// <summary>
    /// The row's one line of context: what FieldWorks changed for a change that no longer fits, why an Uncertain
    /// change needs a look, else where the change was made and where an added analysis came from.
    /// </summary>
    public string DetailText => IsNoLongerFits ? NoLongerFitsDetail()
        : IsUncertain ? Fit?.Uncertainty?.Reason == WordsChangedReason ? string.Empty : UncertaintyReason
        : string.Join(" · ", new[] { Occurrence is null ? string.Empty : WhereText, SourceText }
            .Where(part => part.Length > 0));

    public bool HasDetailText => DetailText.Length > 0;

    /// <summary>The morphs of the one analysis this change is about, as the row's strip shows them.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> RowMorphs => RowAnalysis?.Morphs ?? [];

    public bool HasRowMorphs => RowMorphs.Count > 0;

    public IReadOnlyList<ParserReadingMorph> FieldWorksMorphs =>
        (Analyses.FirstOrDefault(analysis => analysis.Touched && analysis.Stored) ??
         Analyses.FirstOrDefault(analysis => analysis.Stored))?.SourceMorphs ?? [];

    /// <summary>Whether the row's analysis comes from the parser, so FieldWorks holds nothing like it yet.</summary>
    public bool RowAnalysisIsParserBuilt => RowAnalysis?.ParserBuilt == true;

    private ReviewAnalysisViewModel? RowAnalysis =>
        Analyses.FirstOrDefault(analysis => analysis.Touched) ?? Analyses.FirstOrDefault();

    // The "Now reads" sentence and the group's note already say that the words changed.
    private const string WordsChangedReason = "The words in the source sentence have changed.";

    // The fit reasons name internal ids, so the window words each by the kind the fit check gives it.
    private string NoLongerFitsDetail()
    {
        var forms = string.Concat(RowMorphs.Select(morph => morph.Form));
        var analysis = forms.Length > 0 ? forms : "this analysis";
        return ChangeFitReasons.KindOf(Fit?.Reasons.FirstOrDefault()) switch
        {
            ChangeFitReasonKind.WordformDeleted => $"the word {Word} was deleted in FieldWorks",
            ChangeFitReasonKind.WordformChangedForm => $"the spelling of {Word} was changed in FieldWorks",
            ChangeFitReasonKind.WordformSpellingChanged => $"the spelling status of {Word} was changed in FieldWorks",
            ChangeFitReasonKind.AnalysisMissing => $"the analysis {analysis} was deleted or moved in FieldWorks",
            ChangeFitReasonKind.AnalysisReadingChanged => $"the analysis {analysis} was edited in FieldWorks",
            ChangeFitReasonKind.AnalysisOpinionChanged => $"the opinion on {analysis} was changed in FieldWorks",
            ChangeFitReasonKind.MorphReferenceMissing => $"a morpheme in {analysis} was deleted or changed in FieldWorks",
            ChangeFitReasonKind.ReadingAlreadyExists => "FieldWorks already has this analysis",
            ChangeFitReasonKind.BaselineNotCurrent => "FieldWorks saved the project since you decided; check again",
            ChangeFitReasonKind.CannotCheck => "Motif can no longer check this change; undo it and make it again",
            _ => "FieldWorks changed this word since you decided",
        };
    }
    public bool IsNoLongerFits => Fit?.Status == ChangeFitStatus.NoLongerFits;
    public bool HasUncertainty => Fit?.Uncertainty is not null;
    public bool HasContext => Occurrence is not null || HasUncertainty;
    public string ShowContextAutomationName => $"Show context: {Word}";
    public string GoToTextAutomationName => $"Go to text: {Word}";
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

    /// <summary>The word as Review changes lists it; its Open in text goes to this change's place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presentation), nameof(PresentationState))]
    private ListedWordViewModel? _listed;

    /// <summary>The typed row facts and pending-change notes shown by the shared word-presentation module.</summary>
    public WordPresentation Presentation => new(PresentationKey, _presentationRevision,
        Listed?.Row ?? throw new InvalidOperationException("A Review change needs its listed word."),
        WordListOwner.Review, TransitionText, DetailText,
        PendingChange: new WordPendingChange(IsUncertain, StillFits,
            AfterWords.Select(token => new WordCardSentenceToken(token.Form, token.FormWritingSystem, token.IsChanged)).ToArray(),
            CheckAgainAutomationName, UndoAutomationName));

    /// <summary>The card-open state retained on the pending change while its list row is recycled.</summary>
    public WordInteractionState PresentationState
    {
        get => new(PresentationKey, Listed?.IsOpen == true);
        set
        {
            if (value.Key != PresentationKey)
                throw new InvalidOperationException("Review interaction state belongs to another change.");
            if (Listed is { } listed) listed.IsOpen = value.IsOpen;
        }
    }

    private ReviewSentenceContext? _contextSource;

    /// <summary>The currently leased plain sentence strip surrounding this change's exact source occurrence.</summary>
    public IReadOnlyList<ReviewSentenceToken> ContextTokens => _contextSource?.Tokens ?? [];

    /// <summary>The independently paged sentence context, without references to the Analyze page.</summary>
    public SIL.Motif.App.Services.IProgressivePageSource? ContextSource => _contextSource;

    public bool HasContextSource => _contextSource is not null;

    public bool HasUnavailableContext => IsContextExpanded && Occurrence is not null && ContextTokens.Count == 0;

    internal void SetContextSource(ReviewSentenceContext? source)
    {
        if (_contextSource is not null) _contextSource.PropertyChanged -= OnContextSourceChanged;
        _contextSource = source;
        if (_contextSource is not null) _contextSource.PropertyChanged += OnContextSourceChanged;
        OnPropertyChanged(nameof(ContextSource));
        OnPropertyChanged(nameof(HasContextSource));
        OnPropertyChanged(nameof(ContextTokens));
        OnPropertyChanged(nameof(HasUnavailableContext));
    }

    private void OnContextSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReviewSentenceContext.Tokens)) return;
        OnPropertyChanged(nameof(ContextTokens));
        OnPropertyChanged(nameof(HasUnavailableContext));
    }

    public void ToggleContext() => IsContextExpanded = !IsContextExpanded;

    partial void OnIsContextExpandedChanged(bool value)
    {
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
    public string UndoAutomationName => $"Undo: {Word}";

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
            : touched is not null || storedAnalysisId is null ? StagedMarkingTransition.NotInFieldWorks
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
    public string? FormWritingSystem { get; init; }

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
            token.Index, token.WordformId, token.Form, changed.Contains(index))
        {
            FormWritingSystem = token.FormWritingSystem,
        }).ToArray();
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

/// <summary>An analysis as a Review row shows it: its morphs only, since the row's staged note carries the opinion.</summary>
public sealed class ReviewAnalysisViewModel
{
    public ReviewAnalysisViewModel(ReviewAnalysis analysis, string changeKind)
    {
        SourceMorphs = analysis.Reading.Morphs;
        Morphs = analysis.Reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Touched = analysis.Touched;
        Stored = analysis.Stored;
        ParserBuilt = !Stored;
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public IReadOnlyList<ParserReadingMorph> SourceMorphs { get; }
    public bool Touched { get; }
    public bool Stored { get; }
    public bool ParserBuilt { get; }
}
