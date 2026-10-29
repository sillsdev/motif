using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Review changes page's model: the changes in the Draft <see cref="WorkspaceContext.Changes"/> holds, walked as
/// one list, before any is written to FieldWorks, and counted beside the page's sidebar label. Outside Proposal mode
/// that Draft is the pending changes; with a Proposal open it is that Proposal (ADR 0061), and nothing else differs.
/// </summary>
public sealed partial class ReviewPageModel : PageModel
{
    private const string NumbersPrompt = "Check how many changed words keep their approved analyses.";
    private CancellationTokenSource? _measurementCancellation;
    private CancellationTokenSource? _applyCancellation;
    private IReadOnlyList<ReviewListEntryViewModel>? _reviewEntries;
    private ReviewSentenceContext? _sentenceContext;
    private ChangeViewModel? _contextChange;
    private Task _contextStop = Task.CompletedTask;
    private CancellationTokenSource? _retirementReviewCancellation;
    private Task? _retirementReviewTask;
    private int _retirementReviewGeneration;
    private ProposalReviewResponse? _review;
    private IReadOnlyList<ReviewEntryViewModel> _entries = [];
    private int _currentIndex;
    private bool _walked;
    private bool _entriesStaleWhileHidden;
    private int _reviewGeneration;
    private Task? _reviewTask;
    private int _reviewEntryCountGeneration;
    private int? _hiddenReviewEntryCount;

    public ReviewPageModel(WorkspaceContext context) : base(context)
    {
        InitializeFilterMemory();
        ParseProgress = new ParseProgressViewModel(context.Clock);
        Changes.PropertyChanged += OnChangesChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        context.Evidence.PropertyChanged += OnEvidencePropertyChanged;
        context.SelectionReads.Replacing += ReleaseContext;
        // A change's row comes from the parse on screen, so a new parse rebuilds the rows.
        context.Assess.Words.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AssessWordsViewModel.AllRows)) return;
            // A hidden Review rebuilds when it is shown, and only if the parse moved while it was away.
            if (Context.CurrentPage == WorkspacePage.Review) RefreshReviewEntries();
            else _entriesStaleWhileHidden = true;
        };
        RemoveNonFittingCommand = new AsyncRelayCommand(RemoveNonFittingAsync,
            () => Changes.Items.Any(item => item.IsNoLongerFits));
        CheckAgainCommand = new AsyncRelayCommand(() => Changes.RecheckAsync(),
            () => HasNonFittingChanges && !Context.Evidence.IsStale);
        ReconfirmChangeCommand = new AsyncRelayCommand<ChangeViewModel>(ReconfirmChangeAsync,
            change => change is { IsUncertain: true });
        ToggleContextCommand = new AsyncRelayCommand<ChangeViewModel>(ToggleContextAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions | AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);
        ShowContextCommand = new AsyncRelayCommand<ChangeViewModel>(ShowContextAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions | AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);
        GoToTextCommand = new RelayCommand<ChangeViewModel>(change =>
        {
            if (change is not null) Context.OpenOccurrence(change.Occurrence, change.Word, change.WordformId);
        });
        MeasureCommand = new AsyncRelayCommand(MeasureAsync,
            () => Changes.HasItems && Context.HasProject && !IsMeasuring);
        CancelMeasureCommand = new RelayCommand(() => _measurementCancellation?.Cancel(), () => IsMeasuring);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        KeepEditingCommand = new RelayCommand(() => Context.OpenPage(OriginPage));
        OpenAnalyzeTextsCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.AnalyzeTexts));
        PreviousCommand = new RelayCommand(Previous, () => AdjacentVisibleIndex(-1) >= 0);
        NextCommand = new AsyncRelayCommand(NextAsync, () => AdjacentVisibleIndex(1) >= 0);
        KeepCommand = new AsyncRelayCommand(() => KeepAsync(Current), () => Current is not null);
        UndoCommand = new AsyncRelayCommand(() => UndoAsync(Current), () => Current is not null);
        KeepAllCommand = new AsyncRelayCommand(KeepAllAsync, () => _entries.Count > 0);
        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync, () => _entries.Count > 0);
        TryAgainCommand = new AsyncRelayCommand(TryAgainAsync,
            () => InProposalMode && !IsApplying && Context.ProposalStanding.CanTryAgain);
        DeclineCommand = new AsyncRelayCommand(DeclineAsync, () => ShowsDecline && !IsApplying);
        ShowNeedsAttentionCommand = new RelayCommand(() => SelectedFilterIndex = 3);
        context.PropertyChanged += OnLensPropertyChanged;
    }

    /// <summary>The Proposal list's entries in the order Previous and Next walk them.</summary>
    public IReadOnlyList<ReviewEntryViewModel> Entries => _entries;

    /// <summary>The entry Previous and Next are standing on, or <see langword="null"/> when the list is empty.</summary>
    public ReviewEntryViewModel? Current => _currentIndex >= 0 && _currentIndex < _entries.Count
        ? _entries[_currentIndex] : null;

    /// <summary>Moves to the previous entry and marks nothing.</summary>
    public IRelayCommand PreviousCommand { get; }

    /// <summary>Marks the entry it leaves as looked at and moves to the next one.</summary>
    public IAsyncRelayCommand NextCommand { get; }

    /// <summary>Marks the current entry as looked at and moves on.</summary>
    public IAsyncRelayCommand KeepCommand { get; }

    /// <summary>Takes the current entry out of the Proposal.</summary>
    public IAsyncRelayCommand UndoCommand { get; }

    /// <summary>Marks every entry as looked at.</summary>
    public IAsyncRelayCommand KeepAllCommand { get; }

    /// <summary>Takes every entry out of the Proposal.</summary>
    public IAsyncRelayCommand UndoAllCommand { get; }

    /// <summary>Queues a fresh try of the open Proposal; nothing re-runs until the person asks.</summary>
    public IAsyncRelayCommand TryAgainCommand { get; }

    /// <summary>Declines the open Proposal: it is kept with its Trials, and the window leaves it.</summary>
    public IAsyncRelayCommand DeclineCommand { get; }

    /// <summary>Whether Decline is offered: on an open Proposal, never on the pending changes.</summary>
    public bool ShowsDecline => InProposalMode && !ReviewsPendingChanges;

    /// <summary>Why the last Decline was refused, in the window's words, or null.</summary>
    public WindowRefusal? DeclineRefusal { get; private set; }

    /// <summary>How many entries have been looked at, in the window's words.</summary>
    public string LookedAtText => $"{_entries.Count(entry => entry.IsLookedAt)} of {_entries.Count} looked at";

    /// <summary>The toolbar's Keep all, with the count it marks so it cannot be read as one group's.</summary>
    public string KeepAllText => $"Keep all {_entries.Count:N0}";

    /// <summary>The toolbar's Undo all, with the count it takes out.</summary>
    public string UndoAllText => $"Undo all {_entries.Count:N0}";

    /// <summary>Whether the toolbar offers Undo all when the review list has entries.</summary>
    public bool ShowsUndoAll => HasEntries;

    /// <summary>The question shown when an Undo would also take out dependent changes, or null.</summary>
    public UndoPromptViewModel? UndoPrompt { get; private set; }

    /// <summary>Why the last Undo could not finish, in the window's words.</summary>
    public WindowRefusal? UndoRefusal { get; private set; }

    /// <summary>
    /// Whether the Draft under review is an open Proposal rather than the pending changes, so Apply works from its
    /// try rather than a check.
    /// </summary>
    public bool InProposalMode => Changes.TargetProposalId is not null;

    /// <summary>Whether the page has changes to act on.</summary>
    public bool HasReviewItems => _entries.Count > 0;

    /// <summary>The open Proposal's name as the page heading shows it, or null outside Proposal mode.</summary>
    public string? ProposalLabel => !InProposalMode ? null
        : _review?.Label is { Length: > 0 } label ? label
        : Context.Lens is { } lens && lens.Label != lens.ProposalId ? lens.Label : "This Proposal";

    // The pending changes read the same opened as a Proposal or not: still "not applied yet".
    private bool ReviewsPendingChanges => !InProposalMode ||
        _review?.IsPendingChanges == true;

    /// <summary>Whether the list has entries to walk, which shows the toolbar.</summary>
    public bool HasEntries => _entries.Count > 0;

    public ChangesViewModel Changes => Context.Changes;
    public ParseProgressViewModel ParseProgress { get; }

    /// <summary>The open project's file name, which the apply card names.</summary>
    public string ProjectName => Context.ProjectName;

    /// <summary>The Apply heading: the project's file name without its folder or extension.</summary>
    public string ApplyTitle
    {
        get
        {
            var file = ProjectName[(ProjectName.LastIndexOfAny(['\\', '/']) + 1)..];
            var name = file.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase) ? file[..^".fwdata".Length] : file;
            return $"Apply to {(name.Length == 0 ? ProjectName : name)}";
        }
    }

    /// <summary>Removes only the changes that no longer fit the FieldWorks project.</summary>
    public IAsyncRelayCommand RemoveNonFittingCommand { get; }

    /// <summary>Checks unchanged identities against the refreshed Baseline and renews their evidence.</summary>
    public IAsyncRelayCommand CheckAgainCommand { get; }

    public IAsyncRelayCommand<ChangeViewModel> ReconfirmChangeCommand { get; }

    public IAsyncRelayCommand<ChangeViewModel> ToggleContextCommand { get; }

    public IRelayCommand<ChangeViewModel> GoToTextCommand { get; }

    /// <summary>Shows the sentence a change was made in, as its row's card opens.</summary>
    public IAsyncRelayCommand<ChangeViewModel> ShowContextCommand { get; }

    /// <summary>Starts a Trial of the touched words only when the person asks for one.</summary>
    public IAsyncRelayCommand MeasureCommand { get; }

    /// <summary>Stops the active check of the touched words.</summary>
    public IRelayCommand CancelMeasureCommand { get; }

    /// <summary>Writes the measured changes as one atomic Apply.</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>Returns to the page where the first pending change was collected.</summary>
    public IRelayCommand KeepEditingCommand { get; }

    /// <summary>Whether the requested measurement is still running.</summary>
    public bool IsMeasuring { get; private set; }

    /// <summary>Whether Apply is writing the pending changes to FieldWorks.</summary>
    public bool IsApplying { get; private set; }

    /// <summary>Completed words out of the words requested, while measuring.</summary>
    public string MeasurementProgressText { get; private set; } = string.Empty;

    /// <summary>The result recorded when Apply wrote the changes.</summary>
    public ApplyProjection? Receipt { get; private set; }

    /// <summary>The exact linked review data for one rule-retirement Proposal, when opened here.</summary>
    public RetirementProposalReviewViewModel? RetirementReview { get; private set; }

    /// <summary>Whether the page has a linked rule-retirement review to show.</summary>
    public bool HasRetirementReview => RetirementReview is not null;

    public bool HasReceipt => Receipt is not null;

    public bool HasNonFittingChanges => Changes.Items.Any(item => item.IsNoLongerFits);

    public IReadOnlyList<ChangeViewModel> UncertainChanges => Changes.Items.Where(item => item.IsUncertain).ToArray();

    /// <summary>The pending changes grouped by their effect on FieldWorks.</summary>
    public IReadOnlyList<ReviewChangeGroupViewModel> ReviewGroups => Changes.Items
        .Select(change => (Change: change, Group: GroupFor(change)))
        .GroupBy(item => item.Group)
        .OrderBy(group => group.Key.Order)
        .Select(group => new ReviewChangeGroupViewModel(group.Key.Title,
            group.Select(item => item.Change).Select(change =>
            {
                var location = change.Occurrence is { } occurrence ? Context.OccurrenceLocation(occurrence) : null;
                change.SetWhereText(location?.Description ?? (change.Occurrence is null
                    ? "Not tied to a text occurrence" : "Text location not loaded"));
                ListWord(change);
                return (Change: change, Location: location);
            }).OrderBy(item => item.Location?.TextOrder ?? int.MaxValue)
                .ThenBy(item => item.Location?.LineOrder ?? int.MaxValue)
                .ThenBy(item => item.Location?.WordIndex ?? int.MaxValue)
                .ThenBy(item => item.Change.Word, StringComparer.Ordinal)
                .ThenBy(item => item.Change.ChangeId, StringComparer.Ordinal)
                .Select(item => item.Change).ToArray(), Changes, group.Key.Kind))
        .ToArray();

    /// <summary>The page heading, group headings and change rows shown on the Review page.</summary>
    public IReadOnlyList<ReviewListEntryViewModel> ReviewEntries => _reviewEntries ??= BuildReviewEntries();

    private void OnLensPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceContext.ProposalStanding))
        {
            RefreshWordCardState();
            RaiseApplyState();
            OnPropertyChanged(nameof(WorkNotice));
            TryAgainCommand.NotifyCanExecuteChanged();
            return;
        }
        if (e.PropertyName is not (nameof(WorkspaceContext.Lens) or nameof(WorkspaceContext.EvidenceLens))) return;
        RefreshWordCardState();
        RaiseModeState();
        OnPropertyChanged(nameof(WorkNotice));
        // A new Trial changes the evidence a looked-at mark was judged against, so the marks are read again.
        if (e.PropertyName == nameof(WorkspaceContext.Lens) && Context.CurrentPage == WorkspacePage.Review)
            _reviewTask = LoadReviewAsync();
    }

    private void RaiseModeState()
    {
        OnPropertyChanged(nameof(InProposalMode));
        OnPropertyChanged(nameof(ProposalLabel));
        OnPropertyChanged(nameof(ShowsUndoAll));
        OnPropertyChanged(nameof(ShowsDecline));
        DeclineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsBackButton));
        OnPropertyChanged(nameof(ShowsNumbersText));
        OnPropertyChanged(nameof(ApplyChecks));
        OnPropertyChanged(nameof(HasApplyChecks));
        OnPropertyChanged(nameof(HeaderNoteText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowsSideCards));
        RaiseApplyState();
        TryAgainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Reads the list of the Draft <see cref="Changes"/> holds: the open Proposal's, or the pending one.</summary>
    internal async Task LoadReviewAsync()
    {
        using var span = MotifTrace.Begin("review.load");
        MotifTrace.Count("review-load");
        var generation = ++_reviewGeneration;
        if (Context.ProjectPath is not { } project)
        {
            SetReview(null);
            return;
        }
        var proposalId = Changes.TargetProposalId;
        var outcome = await Context.Commands.ReadProposalReviewAsync(
            new ProposalReviewRequest(project, proposalId), CancellationToken.None).ConfigureAwait(true);
        if (generation != _reviewGeneration || !string.Equals(Context.ProjectPath, project, StringComparison.Ordinal) ||
            !string.Equals(Changes.TargetProposalId, proposalId, StringComparison.Ordinal))
            return;
        SetReview(outcome.Succeeded ? outcome.Value : null);
    }

    private void SetReview(ProposalReviewResponse? review)
    {
        _review = review;
        UndoPrompt = null;
        RebuildEntries(changesMoved: false);
    }

    // One list: the review's grammar entries, then one entry per word change in Changes, in the groups' order.
    private void RebuildEntries(bool changesMoved)
    {
        using var span = MotifTrace.Begin("review.rebuild-entries");
        MotifTrace.Count("review-rebuild");
        var previousKey = Current?.Key;
        var reviewed = (_review?.Entries ?? []).ToArray();
        var words = reviewed.OfType<WordChangeEntry>()
            .ToDictionary(entry => entry.Change.ChangeId, entry => (ReviewEntry)entry, StringComparer.Ordinal);
        var grammar = reviewed.Where(entry => entry is not WordChangeEntry)
            .Select(entry => new ReviewEntryViewModel(this, entry, null));
        var wordEntries = ReviewGroups.SelectMany(group => group.Items).Select(change =>
            new ReviewEntryViewModel(this, words.GetValueOrDefault(change.ChangeId), change));
        _entries = grammar.Concat(wordEntries).ToArray();
        var keep = previousKey is null ? -1 : Array.FindIndex(_entries.ToArray(), entry => entry.Key == previousKey);
        _currentIndex = keep >= 0 ? keep : 0;
        EnsureVisibleCurrent();
        MarkCurrent();
        _reviewEntries = null;
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasReviewItems));
        OnPropertyChanged(nameof(ShowsEmptyState));
        OnPropertyChanged(nameof(ShowsHeaderNote));
        OnPropertyChanged(nameof(ShowsUndoAll));
        OnPropertyChanged(nameof(KeepAllText));
        OnPropertyChanged(nameof(UndoAllText));
        OnPropertyChanged(nameof(LookedAtText));
        OnPropertyChanged(nameof(UndoPrompt));
        if (changesMoved) OnPropertyChanged(nameof(ReviewGroups));
        OnPropertyChanged(nameof(ReviewEntries));
        var badgeCount = Context.CurrentPage != WorkspacePage.Review && _hiddenReviewEntryCount is { } hiddenCount
            ? Math.Max(hiddenCount, Changes.Items.Count) : _entries.Count;
        BadgeCount = BadgeOf(badgeCount);
        RaiseModeState();
        RaiseWalkState();
    }

    private static int? BadgeOf(int count) => count > 0 ? count : null;

    private async Task RefreshHiddenReviewBadgeAsync()
    {
        var generation = ++_reviewEntryCountGeneration;
        _hiddenReviewEntryCount = null;
        if (Context.ProjectPath is not { } project)
        {
            BadgeCount = null;
            return;
        }
        var revision = Changes.Snapshot.Revision;
        var proposalId = Changes.TargetProposalId;
        var outcome = await Context.Commands.ReadProposalReviewEntryCountAsync(
            new ProposalReviewRequest(project, proposalId), CancellationToken.None).ConfigureAwait(true);
        if (generation != _reviewEntryCountGeneration || Context.CurrentPage == WorkspacePage.Review ||
            !string.Equals(Context.ProjectPath, project, StringComparison.Ordinal) ||
            !string.Equals(Changes.Snapshot.Revision, revision, StringComparison.Ordinal) ||
            !string.Equals(Changes.TargetProposalId, proposalId, StringComparison.Ordinal) ||
            !outcome.Succeeded || outcome.Value is not { } result) return;
        _hiddenReviewEntryCount = result.EntryCount;
        BadgeCount = BadgeOf(result.EntryCount);
    }

    // The pending changes look as they always have until the person walks them; a Proposal starts walked.
    private void MarkCurrent()
    {
        var shows = InProposalMode || _walked;
        for (var index = 0; index < _entries.Count; index++)
            _entries[index].IsCurrent = shows && index == _currentIndex;
    }

    private void RaiseWalkState()
    {
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        KeepAllCommand.NotifyCanExecuteChanged();
        UndoAllCommand.NotifyCanExecuteChanged();
        TryAgainCommand.NotifyCanExecuteChanged();
    }

    private void Previous()
    {
        var previous = AdjacentVisibleIndex(-1);
        if (previous < 0) return;
        _currentIndex = previous;
        AfterMove();
    }

    private async Task NextAsync()
    {
        var next = AdjacentVisibleIndex(1);
        if (Current is { } leaving) await MarkAsync(leaving, true).ConfigureAwait(true);
        if (next >= 0) _currentIndex = next;
        AfterMove();
    }

    private void AfterMove()
    {
        _walked = true;
        EnsureVisibleCurrent();
        MarkCurrent();
        OnPropertyChanged(nameof(Current));
        RaiseWalkState();
    }

    internal async Task KeepAsync(ReviewEntryViewModel? entry)
    {
        if (entry is null) return;
        await MarkAsync(entry, true).ConfigureAwait(true);
        var index = _entries.ToList().IndexOf(entry);
        if (index >= 0)
        {
            _currentIndex = index;
            var next = AdjacentVisibleIndex(1);
            if (next >= 0) _currentIndex = next;
        }
        AfterMove();
    }

    private async Task KeepAllAsync()
    {
        foreach (var entry in _entries.ToArray()) await MarkAsync(entry, true).ConfigureAwait(true);
        if (_selectedFilterIndex == 1) AfterMove();
    }

    private async Task MarkAsync(ReviewEntryViewModel entry, bool lookedAt)
    {
        if (_review is not { } review || Context.ProjectPath is not { } project || entry.Entry is not { } read) return;
        if (entry.IsLookedAt == lookedAt) return;
        var outcome = await Context.Commands.MarkLookedAtAsync(new ReviewMarkRequest(project, review.ProposalId,
            entry.Key, read.ContentDigest, review.EvidenceDigest, lookedAt), CancellationToken.None)
            .ConfigureAwait(true);
        if (!outcome.Succeeded) return;
        entry.IsLookedAt = lookedAt;
        OnPropertyChanged(nameof(LookedAtText));
        if (_selectedFilterIndex == 1) InvalidateVisibleEntries();
    }

    private async Task UndoAllAsync()
    {
        if (InProposalMode)
        {
            await UndoEntriesAsync(_entries.ToArray(), confirmed: true, removeTogether: true).ConfigureAwait(true);
            return;
        }
        UndoRefusal = null;
        UndoPrompt = null;
        OnPropertyChanged(nameof(UndoRefusal));
        OnPropertyChanged(nameof(UndoPrompt));
        await Changes.DiscardAllAsync().ConfigureAwait(true);
    }

    internal Task UndoAsync(ReviewEntryViewModel? entry) =>
        entry is null ? Task.CompletedTask : UndoEntriesAsync([entry], confirmed: false);

    private async Task UndoEntriesAsync(IReadOnlyList<ReviewEntryViewModel> targets, bool confirmed,
        bool removeTogether = false)
    {
        if (Context.ProjectPath is not { } project || targets.Count == 0) return;
        UndoRefusal = null;
        UndoPrompt = null;
        OnPropertyChanged(nameof(UndoRefusal));
        OnPropertyChanged(nameof(UndoPrompt));

        // A word change goes back through the staging commands, which keep their Undo history, in either mode.
        if (!removeTogether && targets.All(target => target.Change is not null) && DependentsOf(targets).Count == 0)
        {
            using (Changes.BeginStagingAction())
            {
                foreach (var target in targets)
                {
                    if (target.Change!.GroupId is not null)
                        await Changes.RemoveGroupAsync(target.Change).ConfigureAwait(true);
                    else await Changes.RemoveCommand.ExecuteAsync(target.Change).ConfigureAwait(true);
                }
            }
            if (_review is { } read) await ClearMarksAsync(project, read, targets).ConfigureAwait(true);
            await RefreshProposalTrialAsync().ConfigureAwait(true);
            return;
        }
        if (_review is not { } review) return;

        var everyone = targets.ToList();
        if (!confirmed)
        {
            var dependents = DependentsOf(targets);
            if (dependents.Count > 0)
            {
                var all = targets.Concat(dependents).ToArray();
                UndoPrompt = new UndoPromptViewModel(targets.Select(entry => entry.Title).ToArray(),
                    dependents.Select(entry => entry.Title).ToArray(), InProposalMode,
                    new AsyncRelayCommand(() => UndoEntriesAsync(all, confirmed: true)),
                    new RelayCommand(() =>
                    {
                        UndoPrompt = null;
                        OnPropertyChanged(nameof(UndoPrompt));
                    }));
                OnPropertyChanged(nameof(UndoPrompt));
                return;
            }
        }
        else everyone = targets.Concat(DependentsOf(targets)).Distinct().ToList();

        var draftName = review.DraftName;
        if (!review.IsDraft || draftName is null)
        {
            draftName = "review/" + review.ProposalId;
            var reopened = await Context.Commands.ReopenAsync(new ReopenRequest(project,
                MotifProductVersion.CurrentText, draftName, review.ProposalId), CancellationToken.None)
                .ConfigureAwait(true);
            if (!reopened.Succeeded)
            {
                UndoRefusal = WindowRefusal.From(reopened.Refusal!);
                OnPropertyChanged(nameof(UndoRefusal));
                return;
            }
            // The Proposal is a Draft from here on, so a refused removal must not make the next Undo reopen it again.
            review = _review = review with { IsDraft = true, DraftName = draftName };
        }
        var ids = everyone.Where(entry => entry.Entry is not null).OrderByDescending(entry => entry.Entry!.Ordinal)
            .SelectMany(entry => entry.Entry!.OperationIds).Distinct(StringComparer.Ordinal).ToArray();
        var removed = await Context.Commands.RemoveOperationsAsync(new RemoveOperationsRequest(project,
            MotifProductVersion.CurrentText, draftName, ids, Force: false), CancellationToken.None)
            .ConfigureAwait(true);
        if (!removed.Succeeded)
        {
            UndoRefusal = WindowRefusal.From(removed.Refusal!);
            OnPropertyChanged(nameof(UndoRefusal));
            return;
        }
        await ClearMarksAsync(project, review, everyone).ConfigureAwait(true);
        await RefreshProposalTrialAsync().ConfigureAwait(true);
        await Changes.ReloadAsync().ConfigureAwait(true);
        await LoadReviewAsync().ConfigureAwait(true);
    }

    // Undoing changes what a try measured; the open Proposal reads its stale try again, and nothing re-runs.
    private async Task RefreshProposalTrialAsync()
    {
        if (Context.Lens is { } lens && lens.ProposalId == Changes.TargetProposalId)
            await Context.OpenProposalAsync(lens.ProposalId, CancellationToken.None).ConfigureAwait(true);
    }

    private IReadOnlyList<ReviewEntryViewModel> DependentsOf(IReadOnlyList<ReviewEntryViewModel> targets)
    {
        var gone = targets.Select(target => target.Key).ToHashSet(StringComparer.Ordinal);
        var found = new List<ReviewEntryViewModel>();
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var entry in _entries)
            {
                if (gone.Contains(entry.Key) || entry.Entry is not { } read ||
                    !read.DependsOnEntries.Any(gone.Contains)) continue;
                gone.Add(entry.Key);
                found.Add(entry);
                grew = true;
            }
        }
        return found;
    }

    private async Task ClearMarksAsync(string project, ProposalReviewResponse review,
        IEnumerable<ReviewEntryViewModel> removed)
    {
        foreach (var entry in removed.Where(entry => entry.IsLookedAt && entry.Entry is not null))
            await Context.Commands.MarkLookedAtAsync(new ReviewMarkRequest(project, review.ProposalId, entry.Key,
                entry.Entry!.ContentDigest, review.EvidenceDigest, false), CancellationToken.None).ConfigureAwait(true);
    }

    private async Task DeclineAsync()
    {
        if (Changes.TargetProposalId is not { } proposalId || Context.ProjectPath is not { } project) return;
        var declined = await Context.Commands.DeclineProposalAsync(new DeclineProposalRequest(project, proposalId),
            CancellationToken.None).ConfigureAwait(true);
        DeclineRefusal = declined.Succeeded ? null : WindowRefusal.From(declined.Refusal!);
        OnPropertyChanged(nameof(DeclineRefusal));
        if (!declined.Succeeded || Context.ProjectPath != project) return;
        await Context.LeaveProposalAsync().ConfigureAwait(true);
        Context.OpenPage(WorkspacePage.Proposals);
    }

    private async Task TryAgainAsync()
    {
        if (Changes.TargetProposalId is not { } proposalId) return;
        MeasurementRefusal = null;
        OnPropertyChanged(nameof(MeasurementRefusal));
        BaselineNotice = null;
        OnPropertyChanged(nameof(BaselineNotice));
        var outcome = await Context.TryAgainAsync(proposalId, CancellationToken.None).ConfigureAwait(true);
        BaselineNotice = Context.TrialRun.StateFor(proposalId).BaselineNotice;
        OnPropertyChanged(nameof(BaselineNotice));
        if (outcome.Refusal is { } refusal)
        {
            MeasurementRefusal = WindowRefusal.From(refusal);
            OnPropertyChanged(nameof(MeasurementRefusal));
        }
        await LoadReviewAsync().ConfigureAwait(true);
        RaiseApplyState();
    }

    private IReadOnlyList<ReviewListEntryViewModel> BuildReviewEntries()
    {
        var entries = new List<ReviewListEntryViewModel> { new(this) };
        var visible = _entries.Where(MatchesFilter).ToArray();
        var grammar = visible.Where(entry => entry.IsGrammar).ToArray();
        if (grammar.Length > 0) entries.Add(new ReviewListEntryViewModel(new ReviewGrammarGroupViewModel(grammar.Length)));
        for (var index = 0; index < grammar.Length; index++)
            entries.Add(new ReviewListEntryViewModel(grammar[index], index == grammar.Length - 1));
        var byChange = visible.Where(entry => entry.Change is not null)
            .ToDictionary(entry => entry.Change!.ChangeId, StringComparer.Ordinal);
        foreach (var group in ReviewGroups)
        {
            var changes = group.Items.Where(change => byChange.ContainsKey(change.ChangeId)).ToArray();
            if (changes.Length == 0) continue;
            var visibleGroup = new ReviewChangeGroupViewModel(group.Title, changes, Changes,
                group.IsNoLongerFits ? ReviewGroupKind.NoLongerFits
                : group.IsUncertain ? ReviewGroupKind.Uncertain : ReviewGroupKind.Ordinary);
            entries.Add(new ReviewListEntryViewModel(visibleGroup, null, isLastChange: false));
            for (var index = 0; index < changes.Length; index++)
                entries.Add(new ReviewListEntryViewModel(visibleGroup, changes[index], index == changes.Length - 1,
                    byChange.GetValueOrDefault(changes[index].ChangeId)));
        }

        return entries;
    }

    private void RefreshReviewEntries() => RebuildEntries(changesMoved: true);

    public bool HasUncertainChanges => UncertainChanges.Count > 0;

    /// <summary>The Apply result in words without internal command vocabulary.</summary>
    public string ReceiptText => Receipt is null ? string.Empty :
        $"Changes applied to {ProjectName}. Receipt recorded at {Receipt.AppliedLogEntry.TimestampUtc}.";

    /// <summary>Why the last Apply could not finish, in the window's words.</summary>
    public WindowRefusal? ApplyRefusal { get; private set; }

    public bool NeedsReconciliation => ApplyRefusal?.Code == RefusalCodes.ApplyReconciliationNeeded;

    /// <summary>
    /// Every reason Apply is blocked, in <see cref="ApplyBlockerKind"/> order, so the page names them all rather
    /// than the first one found.
    /// </summary>
    public IReadOnlyList<ApplyBlocker> ApplyBlockers
    {
        get
        {
            var blockers = new List<ApplyBlocker>();
            if (NeedsReconciliation)
                blockers.Add(new(ApplyBlockerKind.ReconciliationNeeded, ReconciliationNotice, "Refresh", RefreshCommand));
            if (!HasReviewItems)
            {
                if (InProposalMode && _review is not null)
                    blockers.Add(new(ApplyBlockerKind.NothingToApply, "Nothing to apply."));
                return blockers;
            }
            var noLongerFits = Changes.Items.Count(item => item.IsNoLongerFits);
            if (noLongerFits > 0)
                blockers.Add(new(ApplyBlockerKind.NoLongerFits, NoLongerFitsSentence(noLongerFits),
                    "Remove the ones that no longer fit", RemoveNonFittingCommand));
            var uncertain = UncertainChanges.Count;
            if (uncertain > 0)
                blockers.Add(new(ApplyBlockerKind.Uncertain, UncertainSentence(uncertain)));
            if (Changes.Items.Any(item => item.Fit is null))
                blockers.Add(new(ApplyBlockerKind.FitNotChecked,
                    "Refresh to check whether every change still fits the project.", "Refresh", RefreshCommand));
            if (Context.Baseline?.FieldWorksHeldProject == true)
                blockers.Add(new(ApplyBlockerKind.FieldWorksHoldsProject,
                    "FieldWorks has this project open. Close it before applying changes."));
            if (Context.Evidence.IsStale)
                blockers.Add(new(ApplyBlockerKind.FieldWorksSavedSince,
                    "FieldWorks saved since these numbers were measured. Refresh before applying.",
                    "Refresh", RefreshCommand));
            if (!InProposalMode && WordsLosingApprovedAnalysis.Count > 0)
                blockers.Add(new(ApplyBlockerKind.LosesApprovedAnalysis,
                    LostAnalysisSentence(WordsLosingApprovedAnalysis)));
            if (InProposalMode)
            {
                var standing = Context.ProposalStanding;
                if (standing.Kind != TrialStandingKind.Current)
                {
                    var words = TrialStandingWords.For(standing);
                    blockers.Add(standing.CanTryAgain
                        ? new(ApplyBlockerKind.TrialNeeded, words.Sentence, words.Action, TryAgainCommand)
                        : new(ApplyBlockerKind.TrialNeeded, words.Sentence));
                }
                else if (_review?.Readiness is { IsReady: false } readiness)
                    blockers.Add(new(ApplyBlockerKind.NotReady, string.Join(" ", readiness.Reasons)));
            }
            else if (!EvidenceComplete && !IsMeasuring)
                blockers.Add(new(ApplyBlockerKind.NotMeasured,
                    "See what applying does to the numbers before applying.", "Check these changes", MeasureCommand));
            return blockers;
        }
    }

    public bool IsApplyBlocked => ApplyBlockers.Count > 0;

    /// <summary>The number of changes that need attention before Apply can proceed.</summary>
    public string ApplyBlockedTitle
    {
        get
        {
            if (ApplyBlockers.Count == 0) return string.Empty;
            var count = BlockingChangeCount;
            return count switch
            {
                0 => "Apply is blocked",
                1 => "Apply is blocked by 1 change",
                _ => $"Apply is blocked by {count} changes",
            };
        }
    }

    private int BlockingChangeCount
    {
        get
        {
            var blockingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in Changes.Items.Where(item => item.Fit is null || item.IsNoLongerFits || item.IsUncertain ||
                         WordsLosingApprovedAnalysis.Contains(item.Word, StringComparer.Ordinal)))
                blockingIds.Add(change.ChangeId);
            return blockingIds.Count > 0 ? blockingIds.Count : _entries.Count;
        }
    }

    /// <summary>
    /// The page's count of pending changes, which after an unconfirmed Apply cannot honestly say they are unapplied.
    /// </summary>
    public string CountText => NeedsReconciliation
        ? _entries.Count > 0 ? PluralText.Format(MotifAppStrings.ChangesMayAlreadyBeAppliedPattern, _entries.Count)
        : MotifAppStrings.ChangesMayAlreadyBeApplied
        : ReviewsPendingChanges
        ? _entries.Count > 0 ? PluralText.Format(MotifAppStrings.StatusChangesPattern, _entries.Count) : MotifAppStrings.NoChangesYet
        : string.Format(MotifAppStrings.ProposalChangeCountFormat, ProposalLabel,
            _entries.Count == 0 ? MotifAppStrings.NoChanges : PluralText.Format(MotifAppStrings.ChangeCountPattern, _entries.Count));

    /// <summary>The line under the heading, saying how the list is ordered and that nothing is written yet.</summary>
    public string HeaderNoteText => _entries.Any(entry => entry.IsGrammar)
        ? "Grammar changes first, then word changes grouped by what they do. Nothing reaches FieldWorks until you apply."
        : "Grouped by what they do. Nothing reaches FieldWorks until you apply.";

    /// <summary>What the page leads with after an Apply whose result could not be confirmed.</summary>
    public string ReconciliationNotice => "FieldWorks may already have these changes. Refresh to check.";

    /// <summary>The shell's Refresh, which settles an unconfirmed Apply and renews stale numbers.</summary>
    public IAsyncRelayCommand? RefreshCommand => Context.RefreshProjectCommand;

    /// <summary>The last Apply's refusal, unless it is the unconfirmed Apply the page already leads with.</summary>
    public WindowRefusal? ShownApplyRefusal => NeedsReconciliation ? null : ApplyRefusal;

    /// <summary>Whether the page has nothing to review, and no unconfirmed Apply to settle.</summary>
    /// <summary>
    /// Whether the header adds that nothing reaches FieldWorks until Apply, which an unconfirmed Apply makes untrue.
    /// </summary>
    public bool ShowsHeaderNote => HasEntries && !NeedsReconciliation;

    public bool ShowsEmptyState => !HasEntries && !NeedsReconciliation;

    /// <summary>Whether the navigator and Apply area explain the open project or show a result.</summary>
    public bool ShowsSideCards => Context.HasProject || HasEntries || (InProposalMode && _review is not null) ||
        HasReceipt || ApplyRefusal is not null;

    /// <summary>
    /// What Apply checks for an open Proposal, each passed or not, so the card says what is already settled as well
    /// as what blocks it; empty outside Proposal mode, where the numbers card speaks instead.
    /// </summary>
    public IReadOnlyList<ApplyCheck> ApplyChecks
    {
        get
        {
            if (!InProposalMode || !HasEntries) return [];
            var standing = Context.ProposalStanding;
            var checks = new List<ApplyCheck>
            {
                standing.Kind == TrialStandingKind.Current
                    ? new("Tried as it is now", true)
                    : new(TrialStandingWords.For(standing).Status, false),
                Changes.Items.Any(item => item.IsNoLongerFits || item.IsUncertain)
                    ? new("A change needs a look", false)
                    : new("Every change still fits", true),
            };
            if (_review?.Readiness is { } readiness)
                checks.Add(readiness.IsReady ? new("Ready to apply", true) : new("Not ready to apply", false));
            return checks;
        }
    }

    public bool HasApplyChecks => ApplyChecks.Count > 0;

    /// <summary>
    /// Whether the card offers its check of the changed words: only for the pending changes, since a Proposal is
    /// checked by trying it.
    /// </summary>
    public bool ShowsNumbersText => !InProposalMode;

    /// <summary>Whether the card offers the way back to where the pending changes were made.</summary>
    public bool ShowsBackButton => !InProposalMode && Changes.HasItems;

    private WorkspacePage OriginPage => Changes.Items.FirstOrDefault()?.OriginPage ?? WorkspacePage.Texts;

    /// <summary>The way back's words, naming the page the first pending change came from.</summary>
    public string BackText => OriginPage switch
    {
        WorkspacePage.TryAWord => "Back to Try a Word",
        WorkspacePage.Texts => "Back to Analyze texts",
        _ => "Go back",
    };

    /// <summary>Whether the page lists word changes, which gives the word column labels something to label.</summary>
    public bool HasWordRows => _entries.Any(entry => entry.Change is not null);

    /// <summary>Opens Analyze texts, where most changes are made, from an empty Review.</summary>
    public IRelayCommand OpenAnalyzeTextsCommand { get; }

    /// <summary>The words on the action that writes the measured changes.</summary>
    public string ApplyButtonText => "Apply to FieldWorks project";

    /// <summary>The measured word counts for this exact pending revision, worded for a linguist.</summary>
    public string NumbersText { get; private set; } = NumbersPrompt;

    /// <summary>Why the last requested measurement could not complete, in the window's words.</summary>
    public WindowRefusal? MeasurementRefusal { get; private set; }

    /// <summary>
    /// Why the latest Try again was not compared against a Baseline, in the window's words, or
    /// <see langword="null"/> when it was.
    /// </summary>
    public string? BaselineNotice { get; private set; }

    /// <summary>
    /// The note in the window's words that the Trial from the latest Try again makes parsing more work
    /// than the Baseline, or <see langword="null"/> when it does not.
    /// </summary>
    public string? WorkNotice => Context.Lens is { } lens && lens.ProposalId == Changes.TargetProposalId &&
        !Context.TrialRun.IsRunning(lens.ProposalId) ? TrialWorkNotice.For(lens.Trial?.Work) : null;

    /// <summary>Whether the measured changes can be applied to the FieldWorks project.</summary>
    public bool CanApply => HasReviewItems && Changes.Items.All(item => item.Fit is { StillFits: true }) &&
        ApplyBlockers.Count == 0 && !IsMeasuring && !IsApplying;

    /// <summary>Every blocker's sentence in order, or what Apply is doing, for a reader of one line.</summary>
    public string ApplyBlockReason => IsApplying ? "Applying changes to FieldWorks..." :
        IsMeasuring ? "Checking these changes…" :
        !HasEntries && !NeedsReconciliation ? "Choose a change in Texts to begin." :
        string.Join(" ", ApplyBlockers.Select(blocker => blocker.Sentence));

    /// <summary>
    /// Why Apply cannot be pressed, shown beside it and under the page's blocked title; null while it can. A disabled
    /// button shows no tooltip of its own, so the reason has to be written where the button is.
    /// </summary>
    public string? ApplyDisabledReason => CanApply ? null : ApplyBlockReason;

    /// <summary>Shows the response already bound to the exact Proposal and Dry Run being reviewed.</summary>
    public void ShowRetirementReview(RetirementProposalReviewProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (Changes.Snapshot.DraftId is { } draftId && draftId != projection.ProposalId)
            throw new InvalidOperationException("The review response belongs to another Proposal.");
        RetirementReview = new RetirementProposalReviewViewModel(projection);
        OnPropertyChanged(nameof(RetirementReview));
        OnPropertyChanged(nameof(HasRetirementReview));
    }

    private void ShowRetirementReview(RetirementReviewQueryResponse response)
    {
        if (!response.Applicable) return;
        if (Changes.Snapshot.DraftId is { } draftId && draftId != response.DraftId)
            throw new InvalidOperationException("The review response belongs to another Proposal.");
        RetirementReview = new RetirementProposalReviewViewModel(response);
        OnPropertyChanged(nameof(RetirementReview));
        OnPropertyChanged(nameof(HasRetirementReview));
    }

    /// <summary>Clears the rule-retirement review when its source snapshot changes.</summary>
    public void ClearRetirementReview()
    {
        RetirementReview = null;
        OnPropertyChanged(nameof(RetirementReview));
        OnPropertyChanged(nameof(HasRetirementReview));
    }

    private async Task LoadRetirementReviewAsync()
    {
        _retirementReviewCancellation?.Cancel();
        _retirementReviewCancellation?.Dispose();
        _retirementReviewCancellation = null;
        var generation = ++_retirementReviewGeneration;
        if (Context.ProjectPath is not { } project || Changes.Snapshot.DraftId is not { } draftId) return;

        var cancellation = new CancellationTokenSource();
        _retirementReviewCancellation = cancellation;
        try
        {
            var outcome = await Context.Commands.ReadRetirementReviewAsync(new ReadRetirementReviewRequest(
                project, MotifProductVersion.CurrentText, draftId), cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested || generation != _retirementReviewGeneration ||
                !string.Equals(Context.ProjectPath, project, StringComparison.Ordinal) ||
                Changes.Snapshot.DraftId != draftId)
                return;
            if (outcome.Value is { Applicable: true } response) ShowRetirementReview(response);
            else ClearRetirementReview();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation == _retirementReviewGeneration) ClearRetirementReview();
        }
        finally
        {
            if (ReferenceEquals(_retirementReviewCancellation, cancellation))
            {
                _retirementReviewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    /// <summary>Shows that a saved project change could not be matched to its recorded Receipt.</summary>
    internal void ShowReconciliationNeeded()
    {
        if (!NeedsReconciliation)
        {
            ApplyRefusal = WindowRefusal.From(new Refusal(
                RefusalCodes.ApplyReconciliationNeeded,
                FailureReason.StoreInconsistent,
                "A previous Apply may have saved changes to the FieldWorks project, but Motif could not record its Receipt. " +
                "Refresh the project after checking it before trying Apply again.",
                new Dictionary<string, string>()));
        }

        OnPropertyChanged(nameof(ApplyRefusal));
        RaiseApplyState();
    }

    /// <summary>Clears the reconciliation warning after a successful project refresh.</summary>
    internal void ClearReconciliationNeeded() => ShowReconciliationNeeded(false);

    private void ShowReconciliationNeeded(bool needed)
    {
        if (!needed && NeedsReconciliation) ApplyRefusal = null;
        OnPropertyChanged(nameof(ApplyRefusal));
        RaiseApplyState();
    }

    private static string NoLongerFitsSentence(int count) =>
        $"{count} {(count == 1 ? "change no longer fits: FieldWorks changed its word"
            : "changes no longer fit: FieldWorks changed their words")} since you decided.";

    private static string UncertainSentence(int count) =>
        $"{count} {(count == 1 ? "change needs" : "changes need")} another look because " +
        $"{(count == 1 ? "its sentence changed" : "their sentence changed")}. " +
        $"Check {(count == 1 ? "it again or undo it." : "them again or undo them.")}";

    private async Task ReconfirmChangeAsync(ChangeViewModel? change)
    {
        if (change is null) return;
        await Changes.ReconfirmAsync(change).ConfigureAwait(true);
    }

    private bool EvidenceComplete { get; set; }

    private IReadOnlyList<string> WordsLosingApprovedAnalysis { get; set; } = [];

    private static string LostAnalysisSentence(IReadOnlyList<string> words) =>
        $"Applying these changes would lose an approved analysis for {words.Count} " +
        $"{(words.Count == 1 ? "word" : "words")}: {string.Join(", ", words)}. " +
        "Change or remove the changes that cause it before applying.";

    private async Task MeasureAsync()
    {
        if (Context.ProjectPath is not { } project || Changes.Snapshot.DraftId is not { } draft) return;
        using var usageAction = Context.Commands.BeginUsageAction("trial --pending",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("words",
                Changes.Snapshot.Changes.Select(change => change.Word).Distinct(StringComparer.Ordinal).Count()));
        _measurementCancellation?.Dispose();
        _measurementCancellation = new CancellationTokenSource();
        IsMeasuring = true;
        EvidenceComplete = false;
        WordsLosingApprovedAnalysis = [];
        MeasurementRefusal = null;
        OnPropertyChanged(nameof(IsMeasuring));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(MeasurementRefusal));
        MeasureCommand.NotifyCanExecuteChanged();
        CancelMeasureCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        var revision = Changes.Snapshot.Revision;
        var words = Changes.Snapshot.Changes.Select(change => change.Word)
            .Distinct(StringComparer.Ordinal).ToArray();
        CommandOutcome<MeasurePendingResult> result;
        try
        {
            result = await ParseProgress.TrackAsync(() => Context.Commands.MeasurePendingAsync(
                new MeasurePendingRequest(project, draft, revision, words,
                    Context.Evidence.CorrectnessAssessmentId),
                new Progress<MeasureProgress>(OnMeasurementProgress),
                _measurementCancellation.Token)).ConfigureAwait(true);
        }
        finally
        {
            IsMeasuring = false;
            OnPropertyChanged(nameof(IsMeasuring));
            MeasureCommand.NotifyCanExecuteChanged();
            CancelMeasureCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
        if (revision != Changes.Snapshot.Revision) return;
        EvidenceComplete = result.Succeeded && result.Value is { EvidenceComplete: true } measured &&
            measured.Revision == revision;
        WordsLosingApprovedAnalysis = result.Value is { } checkedChanges && checkedChanges.Revision == revision
            ? checkedChanges.Numbers.WordsLosingApprovedAnalysis : [];
        MeasurementRefusal = result.Refusal is { } measureRefusal
            ? WindowRefusal.From(measureRefusal)
            : !EvidenceComplete ? WindowRefusal.Plain("Some words did not finish or their analysis could not be checked.") : null;
        if (result.Value is { } evidence) NumbersText = WindowSentence(evidence.Numbers);
        OnPropertyChanged(nameof(NumbersText));
        OnPropertyChanged(nameof(MeasurementRefusal));
        if (result.Refusal?.Code == RefusalCodes.TrialChangesChanged) await Changes.ReloadAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private static string WindowSentence(ReviewNumbersResponse numbers)
    {
        var kept = $"{numbers.TouchedWordsCovered} of {numbers.TouchedWordCount} " +
            $"{(numbers.TouchedWordCount == 1 ? "word" : "words")} you changed kept their approved analyses. ";
        return numbers.Comparability switch
        {
            ReviewComparability.NoEarlierAssessment => kept + "Nothing was checked before to compare with.",
            ReviewComparability.DifferentAssessor => kept +
                "The earlier numbers came from a different parser, so they cannot be compared with these.",
            ReviewComparability.NoSharedWords => kept +
                "None of these words were checked before, so there is nothing to compare them with.",
            ReviewComparability.Compared =>
                $"Among {numbers.SharedWordCount} {(numbers.SharedWordCount == 1 ? "word" : "words")} also " +
                $"checked before, {numbers.ApprovedKeptBefore} → {numbers.ApprovedKeptAfter} kept their approved " +
                "analyses.",
            _ => throw new ArgumentOutOfRangeException(nameof(numbers), numbers.Comparability, null),
        };
    }

    private void OnMeasurementProgress(MeasureProgress progress)
    {
        if (progress.WordProgress is { } word)
            ParseProgress.Report(new AssessmentProgress(AssessmentStage.Parsing, word.Completed, word.Total,
                "Checking the changed words...")
            {
                CurrentWord = word.CurrentWord, PerWordLimitMs = word.PerWordLimitMs,
                StoppedWords = word.StoppedWords, SlowestWord = word.SlowestWord,
            });
        else
            ParseProgress.Report(new AssessmentProgress(AssessmentStage.SelectingWords, progress.Completed,
                progress.Total, "Preparing to check the changed words..."));
        MeasurementProgressText = $"{progress.Completed} of {progress.Total} words checked";
        OnPropertyChanged(nameof(MeasurementProgressText));
    }

    private async Task ApplyAsync()
    {
        var proposal = Changes.TargetProposalId;
        if (!CanApply || Context.ProjectPath is not { } project ||
            (proposal is null && Changes.Snapshot.DraftId is not { }))
            return;
        var draft = Changes.Snapshot.DraftId ?? string.Empty;
        using var usageAction = Context.Commands.BeginUsageAction(
            proposal is null ? "apply --all-pending" : "apply --proposal",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("changes", _entries.Count));
        IsApplying = true;
        _applyCancellation?.Dispose();
        _applyCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsApplying));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        CommandOutcome<ApplyPendingResult> result;
        try
        {
            result = proposal is null
                ? await Context.Commands.ApplyPendingAsync(new ApplyPendingRequest(
                    project, draft, Changes.Snapshot.Revision),
                    _applyCancellation.Token).ConfigureAwait(true)
                : await Context.Commands.ApplyProposalAsync(new ApplyProposalRequest(
                    project, proposal, _review?.Revision), _applyCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            var cancellation = _applyCancellation;
            _applyCancellation = null;
            cancellation?.Dispose();
            IsApplying = false;
            OnPropertyChanged(nameof(IsApplying));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            ApplyCommand.NotifyCanExecuteChanged();
        }
        if (Context.ProjectPath != project) return;
        if (!result.Succeeded)
        {
            ApplyRefusal = WindowRefusal.From(result.Refusal!);
            OnPropertyChanged(nameof(ApplyRefusal));
            RaiseApplyState();
            await Changes.ReloadAsync().ConfigureAwait(true);
            return;
        }
        Changes.ClearStagingHistory();
        Receipt = result.Value!.Receipt;
        ApplyRefusal = null;
        if (result.Value.Applied) Context.RecordApplied();
        if (proposal is not null) await Context.LeaveProposalAsync().ConfigureAwait(true);
        await Changes.ReloadAsync().ConfigureAwait(true);
        await LoadReviewAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyRefusal));
    }

    private async Task RemoveNonFittingAsync()
    {
        using var historyAction = Changes.BeginStagingAction();
        var changes = Changes.Items.Where(item => item.IsNoLongerFits).ToArray();
        using var usageAction = Context.Commands.BeginUsageAction("remove-pending-change",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("changes", changes.Length));
        foreach (var change in changes)
            await Changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }

    private void OnChangesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangesViewModel.TargetProposalId))
        {
            RaiseModeState();
            OnPropertyChanged(nameof(WorkNotice));
        }
        if (e.PropertyName == nameof(ChangesViewModel.Snapshot))
        {
            if (Context.CurrentPage == WorkspacePage.Review)
            {
                _reviewEntryCountGeneration++;
                _hiddenReviewEntryCount = null;
                if (_entriesStaleWhileHidden)
                {
                    _entriesStaleWhileHidden = false;
                    RefreshReviewEntries();
                }
                _reviewTask = LoadReviewAsync();
            }
            else
            {
                _reviewGeneration++;
                _ = RefreshHiddenReviewBadgeAsync();
            }
            ClearRetirementReview();
            if (Context.CurrentPage == WorkspacePage.Review)
                _retirementReviewTask = LoadRetirementReviewAsync();
            else
            {
                _retirementReviewGeneration++;
                _retirementReviewCancellation?.Cancel();
            }
            EvidenceComplete = false;
            WordsLosingApprovedAnalysis = [];
            NumbersText = NumbersPrompt;
            OnPropertyChanged(nameof(NumbersText));
            RaiseApplyState();
        }
        if (e.PropertyName == nameof(ChangesViewModel.Count))
        {
            if (_contextChange is not null && !Changes.Items.Contains(_contextChange)) ReleaseContext();
            OnPropertyChanged(nameof(HasWordRows));
            OnPropertyChanged(nameof(ShowsBackButton));
            OnPropertyChanged(nameof(BackText));
            EvidenceComplete = false;
            WordsLosingApprovedAnalysis = [];
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            OnPropertyChanged(nameof(HasNonFittingChanges));
            OnPropertyChanged(nameof(UncertainChanges));
            OnPropertyChanged(nameof(HasUncertainChanges));
            RefreshReviewEntries();
            ReconfirmChangeCommand.NotifyCanExecuteChanged();
            RemoveNonFittingCommand.NotifyCanExecuteChanged();
            CheckAgainCommand.NotifyCanExecuteChanged();
            MeasureCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    // Open in text goes to where the change was made, as Go to text did; an opened card stays open.
    private void ListWord(ChangeViewModel change)
    {
        var wasOpen = change.Listed?.IsOpen == true;
        var listed = Context.Assess.Words.Listed(change.Word, new WordRowRoutes
        {
            OpenInText = word => Context.OpenOccurrence(change.Occurrence, word, change.WordformId),
            TryWord = Context.TryWord,
        });
        if (change.FieldWorksMorphs.Count > 0)
            listed = new ListedWordViewModel(listed.Row.WithFieldWorksMorphemes(change.FieldWorksMorphs), listed.Card);
        if (change.NowOpinionMark is { } opinion)
            listed = new ListedWordViewModel(listed.Row.WithOpinionMark(opinion), listed.Card);
        change.Listed = listed;
        change.Listed.IsOpen = wasOpen;
    }

    private async Task ShowContextAsync(ChangeViewModel? change)
    {
        if (change is null) return;
        if (ReferenceEquals(change, _contextChange) && _sentenceContext is not null)
        {
            change.IsContextExpanded = true;
            return;
        }
        ReleaseContext();
        change.IsContextExpanded = true;
        if (change.Occurrence is not { } occurrence || Context.SelectionReads.Reader is not { } reader) return;
        var source = new ReviewSentenceContext(reader, occurrence);
        _sentenceContext = source;
        _contextChange = change;
        change.PropertyChanged += OnContextChangeChanged;
        if (await source.OpenAsync().ConfigureAwait(true) && ReferenceEquals(source, _sentenceContext))
            change.SetContextSource(source);
    }

    private Task ToggleContextAsync(ChangeViewModel? change)
    {
        if (change is null) return Task.CompletedTask;
        if (!change.IsContextExpanded) return ShowContextAsync(change);
        if (ReferenceEquals(change, _contextChange)) ReleaseContext();
        else change.IsContextExpanded = false;
        return Task.CompletedTask;
    }

    private void ReleaseContext()
    {
        ReleaseWordCard();
        var change = _contextChange;
        var source = _sentenceContext;
        _contextChange = null;
        _sentenceContext = null;
        if (change is not null)
        {
            change.PropertyChanged -= OnContextChangeChanged;
            change.SetContextSource(null);
            change.IsContextExpanded = false;
            if (change.Listed is { } listed) listed.IsOpen = false;
        }
        if (source is not null)
        {
            source.ReleasePage();
            _contextStop = Task.WhenAll(_contextStop, source.StopAsync());
        }
    }

    private void OnContextChangeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangeViewModel.IsContextExpanded) && _contextChange?.IsContextExpanded == false)
            ReleaseContext();
    }

    private static ReviewChangeGroupDefinition GroupFor(ChangeViewModel change)
    {
        if (change.IsNoLongerFits) return new(-1, "No longer fits", ReviewGroupKind.NoLongerFits);
        if (change.IsUncertain) return Uncertain;
        return change.Kind switch
        {
            ChangeKinds.AddCandidate => new(6, "Added"),
            ChangeKinds.Approve or ChangeKinds.Reject when change.StoredAnalysisId is null => new(6, "Added"),
            ChangeKinds.RemoveAnalysis => new(7, "Removed"),
            ChangeKinds.IncorrectSpelling => new(8, "Spelling → Incorrect"),
            _ => change.StagedTransition.Now switch
            {
                "Unknown" when change.StagedTransition.AfterApply == "Approved" => new(0, "Unknown → Approved"),
                "Unknown" when change.StagedTransition.AfterApply == "Disapproved" => new(1, "Unknown → Disapproved"),
                "Approved" when change.StagedTransition.AfterApply == "Disapproved" => new(2, "Approved → Disapproved"),
                "Approved" when change.StagedTransition.AfterApply == "Unknown" => new(3, "Approved → Unknown"),
                "Disapproved" when change.StagedTransition.AfterApply == "Approved" => new(4, "Disapproved → Approved"),
                "Disapproved" when change.StagedTransition.AfterApply == "Unknown" => new(5, "Disapproved → Unknown"),
                _ => Uncertain,
            },
        };
    }

    private static readonly ReviewChangeGroupDefinition Uncertain =
        new(9, "Needs another look", ReviewGroupKind.Uncertain);

    private sealed record ReviewChangeGroupDefinition(int Order, string Title,
        ReviewGroupKind Kind = ReviewGroupKind.Ordinary);

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.CurrentPage))
        {
            if (Context.CurrentPage == WorkspacePage.Review)
            {
                _reviewEntryCountGeneration++;
                _hiddenReviewEntryCount = null;
                if (_entriesStaleWhileHidden)
                {
                    _entriesStaleWhileHidden = false;
                    RefreshReviewEntries();
                }
                _reviewTask = LoadReviewAsync();
                _retirementReviewTask = LoadRetirementReviewAsync();
            }
            else
            {
                ReleaseContext();
                _reviewGeneration++;
                _reviewEntryCountGeneration++;
                _retirementReviewGeneration++;
                _retirementReviewCancellation?.Cancel();
            }
        }
        if (e.PropertyName == nameof(WorkspaceContext.ProjectName))
        {
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(ApplyTitle));
        }
        if (e.PropertyName == nameof(WorkspaceContext.ProjectPath))
        {
            _measurementCancellation?.Cancel();
            _applyCancellation?.Cancel();
            OnPropertyChanged(nameof(WorkNotice));
        }
        if (e.PropertyName is nameof(WorkspaceContext.Baseline)) RaiseApplyState();
    }

    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectEvidence.IsStale)) RaiseApplyState();
    }

    // Many paths announce CanApply or ApplyBlockReason; everything derived from the blockers follows those two.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Current)) ReleaseWordCard();
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(ReviewEntries):
                _tableEntries = null;
                OnPropertyChanged(nameof(TableEntries));
                OnPropertyChanged(nameof(SelectedTableEntry));
                break;
            case nameof(Current):
                OnPropertyChanged(nameof(SelectedTableEntry));
                break;
            case nameof(CanApply):
                // The Apply button follows its command, so every change to CanApply must re-ask the command.
                ApplyCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ApplyBlockReason));
                break;
            case nameof(ApplyBlockReason):
                OnPropertyChanged(nameof(ApplyDisabledReason));
                OnPropertyChanged(nameof(ApplyBlockers));
                OnPropertyChanged(nameof(IsApplyBlocked));
                OnPropertyChanged(nameof(ApplyBlockedTitle));
                OnPropertyChanged(nameof(HasChangesNeedingAttention));
                break;
            case nameof(ApplyRefusal):
                OnPropertyChanged(nameof(NeedsReconciliation));
                OnPropertyChanged(nameof(ShownApplyRefusal));
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(ShowsSideCards));
                OnPropertyChanged(nameof(ShowsEmptyState));
                OnPropertyChanged(nameof(ShowsHeaderNote));
                break;
            case nameof(HasReceipt):
                OnPropertyChanged(nameof(ShowsSideCards));
                break;
        }
    }

    private void RaiseApplyState()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        CheckAgainCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        _filterMemory?.Open(projectPath);
        return Task.CompletedTask;
    }

    protected override void OnProjectCleared()
    {
        _filterMemory?.Open(null);
        _walked = false;
        _selectedFilterIndex = 0;
        OnPropertyChanged(nameof(SelectedFilterIndex));
        ReleaseContext();
        _reviewGeneration++;
        _reviewEntryCountGeneration++;
        _hiddenReviewEntryCount = null;
        SetReview(null);
        _retirementReviewGeneration++;
        _retirementReviewCancellation?.Cancel();
        _retirementReviewCancellation?.Dispose();
        _retirementReviewCancellation = null;
        ClearRetirementReview();
        _measurementCancellation?.Cancel();
        _applyCancellation?.Cancel();
        Receipt = null;
        ApplyRefusal = null;
        MeasurementRefusal = null;
        NumbersText = NumbersPrompt;
        EvidenceComplete = false;
        WordsLosingApprovedAnalysis = [];
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyRefusal));
        OnPropertyChanged(nameof(MeasurementRefusal));
        OnPropertyChanged(nameof(NumbersText));
        OnPropertyChanged(nameof(CanApply));
    }

    protected override async Task OnStopWorkAsync()
    {
        ReleaseContext();
        await _contextStop.ConfigureAwait(true);
        await _wordCardStop.ConfigureAwait(true);
        _retirementReviewCancellation?.Cancel();
        _measurementCancellation?.Cancel();
        _applyCancellation?.Cancel();
        if (_retirementReviewTask is { } retirementReview) await retirementReview.ConfigureAwait(true);
        _reviewGeneration++;
        if (_reviewTask is { } review) await review.ConfigureAwait(true);
        if (MeasureCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        if (ApplyCommand.ExecutionTask is { } applying) await applying.ConfigureAwait(true);
    }
}
