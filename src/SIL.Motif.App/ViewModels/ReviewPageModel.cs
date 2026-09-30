using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Review changes page's model: the pending changes every page adds to, before any is written to FieldWorks,
/// counted beside the page's sidebar label.
/// </summary>
public sealed class ReviewPageModel : PageModel
{
    private const string NumbersPrompt = "See what applying does to the numbers.";
    private CancellationTokenSource? _measurementCancellation;
    private CancellationTokenSource? _applyCancellation;

    public ReviewPageModel(WorkspaceContext context) : base(context)
    {
        Changes.PropertyChanged += OnChangesChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        context.Evidence.PropertyChanged += OnEvidencePropertyChanged;
        RemoveNonFittingCommand = new AsyncRelayCommand(RemoveNonFittingAsync,
            () => Changes.Items.Any(item => item.IsNoLongerFits));
        CheckAgainCommand = new AsyncRelayCommand(() => Changes.RecheckAsync(),
            () => HasNonFittingChanges && !Context.Evidence.IsStale);
        ReconfirmChangeCommand = new AsyncRelayCommand<ChangeViewModel>(ReconfirmChangeAsync,
            change => change is { IsUncertain: true });
        ToggleContextCommand = new RelayCommand<ChangeViewModel>(ToggleContext);
        GoToTextCommand = new RelayCommand<ChangeViewModel>(change =>
        {
            if (change is not null) Context.OpenOccurrence(change.Occurrence, change.Word);
        });
        MeasureCommand = new AsyncRelayCommand(MeasureAsync,
            () => Changes.HasItems && Context.HasProject && !IsMeasuring);
        CancelMeasureCommand = new RelayCommand(() => _measurementCancellation?.Cancel(), () => IsMeasuring);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        KeepEditingCommand = new RelayCommand(() => Context.OpenPage(
            Changes.Items.FirstOrDefault()?.OriginPage ?? WorkspacePage.Texts));
        OpenAnalyzeTextsCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.AnalyzeTexts));
    }

    public ChangesViewModel Changes => Context.Changes;

    /// <summary>The open project's file name, which the apply card names.</summary>
    public string ProjectName => Context.ProjectName;

    /// <summary>Removes only the changes that no longer fit the FieldWorks project.</summary>
    public IAsyncRelayCommand RemoveNonFittingCommand { get; }

    /// <summary>Checks unchanged identities against the refreshed Baseline and renews their evidence.</summary>
    public IAsyncRelayCommand CheckAgainCommand { get; }

    public IAsyncRelayCommand<ChangeViewModel> ReconfirmChangeCommand { get; }

    public IRelayCommand<ChangeViewModel> ToggleContextCommand { get; }

    public IRelayCommand<ChangeViewModel> GoToTextCommand { get; }

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
                return (Change: change, Location: location);
            }).OrderBy(item => item.Location?.TextOrder ?? int.MaxValue)
                .ThenBy(item => item.Location?.LineOrder ?? int.MaxValue)
                .ThenBy(item => item.Location?.WordIndex ?? int.MaxValue)
                .ThenBy(item => item.Change.Word, StringComparer.Ordinal)
                .ThenBy(item => item.Change.ChangeId, StringComparer.Ordinal)
                .Select(item => item.Change).ToArray(), Changes, group.Key.Kind))
        .ToArray();

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
            if (!Changes.HasItems) return blockers;
            var noLongerFits = Changes.Items.Count(item => item.IsNoLongerFits);
            if (noLongerFits > 0)
                blockers.Add(new(ApplyBlockerKind.NoLongerFits, NoLongerFitsSentence(noLongerFits),
                    "Remove the ones that no longer fit", RemoveNonFittingCommand));
            if (HasUncertainChanges)
                blockers.Add(new(ApplyBlockerKind.Uncertain, UncertainSentence(UncertainChanges.Count)));
            if (Context.Baseline?.FieldWorksHeldProject == true)
                blockers.Add(new(ApplyBlockerKind.FieldWorksHoldsProject,
                    "FieldWorks has this project open. Close it before applying changes."));
            if (Context.Evidence.IsStale)
                blockers.Add(new(ApplyBlockerKind.FieldWorksSavedSince,
                    "FieldWorks saved since these numbers were measured. Refresh before applying.",
                    "Refresh", RefreshCommand));
            if (WordsLosingApprovedAnalysis.Count > 0)
                blockers.Add(new(ApplyBlockerKind.LosesApprovedAnalysis,
                    LostAnalysisSentence(WordsLosingApprovedAnalysis)));
            if (!EvidenceComplete && !IsMeasuring)
                blockers.Add(new(ApplyBlockerKind.NotMeasured,
                    "See what applying does to the numbers before applying.", "Check these changes", MeasureCommand));
            return blockers;
        }
    }

    public bool IsApplyBlocked => ApplyBlockers.Count > 0;

    /// <summary>How many things block Apply, named at the top of the page.</summary>
    public string ApplyBlockedTitle => ApplyBlockers.Count switch
    {
        0 => string.Empty,
        1 => "Apply is blocked by 1 thing",
        var count => $"Apply is blocked by {count} things",
    };

    /// <summary>
    /// The page's count of pending changes, which after an unconfirmed Apply cannot honestly say they are unapplied.
    /// </summary>
    public string CountText => !NeedsReconciliation ? Changes.HasItems ? Changes.CountText : "No changes yet"
        : Changes.Count == 1 ? "1 change may already be applied"
        : Changes.Count > 1 ? $"{Changes.Count:N0} changes may already be applied"
        : "Changes may already be applied";

    /// <summary>What the page leads with after an Apply whose result could not be confirmed.</summary>
    public string ReconciliationNotice => "FieldWorks may already have these changes. Refresh to check.";

    /// <summary>The shell's Refresh, which settles an unconfirmed Apply and renews stale numbers.</summary>
    public IAsyncRelayCommand? RefreshCommand => Context.RefreshProjectCommand;

    /// <summary>The last Apply's refusal, unless it is the unconfirmed Apply the page already leads with.</summary>
    public WindowRefusal? ShownApplyRefusal => NeedsReconciliation ? null : ApplyRefusal;

    /// <summary>Whether the page has nothing to review, and no unconfirmed Apply to settle.</summary>
    public bool ShowsEmptyState => !Changes.HasItems && !NeedsReconciliation;

    /// <summary>Whether the numbers and Apply cards have anything to act on or report.</summary>
    public bool ShowsSideCards => Changes.HasItems || HasReceipt || ApplyRefusal is not null;

    /// <summary>Opens Analyze texts, where most changes are made, from an empty Review.</summary>
    public IRelayCommand OpenAnalyzeTextsCommand { get; }

    /// <summary>The words on the action that writes the measured changes.</summary>
    public string ApplyButtonText => "Apply to FieldWorks project";

    /// <summary>The measured word counts for this exact pending revision, worded for a linguist.</summary>
    public string NumbersText { get; private set; } = NumbersPrompt;

    /// <summary>Why the last requested measurement could not complete, in the window's words.</summary>
    public WindowRefusal? MeasurementRefusal { get; private set; }

    /// <summary>Whether the measured changes can be applied to the FieldWorks project.</summary>
    public bool CanApply => Changes.HasItems && Changes.Items.All(item => item.Fit is { StillFits: true }) &&
        ApplyBlockers.Count == 0 && !IsMeasuring && !IsApplying;

    /// <summary>Every blocker's sentence in order, or what Apply is doing, for a reader of one line.</summary>
    public string ApplyBlockReason => IsApplying ? "Applying changes to FieldWorks..." :
        !Changes.HasItems && !NeedsReconciliation ? "Choose a change in Texts to begin." :
        string.Join(" ", ApplyBlockers.Select(blocker => blocker.Sentence));

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
            result = await Context.Commands.MeasurePendingAsync(
                new MeasurePendingRequest(project, draft, revision, words,
                    Context.Evidence.CorrectnessAssessmentId),
                new Progress<MeasureProgress>(OnMeasurementProgress),
                _measurementCancellation.Token).ConfigureAwait(true);
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
        MeasurementProgressText = $"{progress.Completed} of {progress.Total} words checked";
        OnPropertyChanged(nameof(MeasurementProgressText));
    }

    private async Task ApplyAsync()
    {
        if (!CanApply || Context.ProjectPath is not { } project || Changes.Snapshot.DraftId is not { } draft)
            return;
        IsApplying = true;
        _applyCancellation?.Dispose();
        _applyCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsApplying));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        CommandOutcome<ApplyPendingResult> result;
        try
        {
            result = await Context.Commands.ApplyPendingAsync(new ApplyPendingRequest(
                project, draft, Changes.Snapshot.Revision, Environment.UserName),
                _applyCancellation.Token).ConfigureAwait(true);
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
        Receipt = result.Value!.Receipt;
        ApplyRefusal = null;
        if (result.Value.Applied) Context.RecordApplied();
        await Changes.ReloadAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyRefusal));
    }

    private async Task RemoveNonFittingAsync()
    {
        foreach (var change in Changes.Items.Where(item => item.IsNoLongerFits).ToArray())
            await Changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }

    private void OnChangesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangesViewModel.Snapshot))
        {
            EvidenceComplete = false;
            WordsLosingApprovedAnalysis = [];
            NumbersText = NumbersPrompt;
            OnPropertyChanged(nameof(NumbersText));
            RaiseApplyState();
        }
        if (e.PropertyName == nameof(ChangesViewModel.Count))
        {
            Badge = Changes.Count > 0 ? Changes.Count.ToString(CultureInfo.CurrentCulture) : string.Empty;
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(ShowsSideCards));
            OnPropertyChanged(nameof(ShowsEmptyState));
            EvidenceComplete = false;
            WordsLosingApprovedAnalysis = [];
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            OnPropertyChanged(nameof(HasNonFittingChanges));
            OnPropertyChanged(nameof(UncertainChanges));
            OnPropertyChanged(nameof(HasUncertainChanges));
            OnPropertyChanged(nameof(ReviewGroups));
            ReconfirmChangeCommand.NotifyCanExecuteChanged();
            RemoveNonFittingCommand.NotifyCanExecuteChanged();
            CheckAgainCommand.NotifyCanExecuteChanged();
            MeasureCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private void ToggleContext(ChangeViewModel? change)
    {
        if (change is null) return;
        change.ToggleContext();
        if (change.IsContextExpanded)
            change.SetContextTokens(change.Occurrence is { } occurrence
                ? Context.OccurrenceContext(occurrence) ?? [] : []);
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
        new(9, "Uncertain — check again", ReviewGroupKind.Uncertain);

    private sealed record ReviewChangeGroupDefinition(int Order, string Title,
        ReviewGroupKind Kind = ReviewGroupKind.Ordinary);

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.ProjectName)) OnPropertyChanged(nameof(ProjectName));
        if (e.PropertyName == nameof(WorkspaceContext.ProjectPath))
        {
            _measurementCancellation?.Cancel();
            _applyCancellation?.Cancel();
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
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(CanApply):
                OnPropertyChanged(nameof(ApplyBlockReason));
                break;
            case nameof(ApplyBlockReason):
                OnPropertyChanged(nameof(ApplyBlockers));
                OnPropertyChanged(nameof(IsApplyBlocked));
                OnPropertyChanged(nameof(ApplyBlockedTitle));
                break;
            case nameof(ApplyRefusal):
                OnPropertyChanged(nameof(NeedsReconciliation));
                OnPropertyChanged(nameof(ShownApplyRefusal));
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(ShowsSideCards));
                OnPropertyChanged(nameof(ShowsEmptyState));
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

    protected override void OnProjectCleared()
    {
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
        _measurementCancellation?.Cancel();
        _applyCancellation?.Cancel();
        if (MeasureCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        if (ApplyCommand.ExecutionTask is { } applying) await applying.ConfigureAwait(true);
    }
}
