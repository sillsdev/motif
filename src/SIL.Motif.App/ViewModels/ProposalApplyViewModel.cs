using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns Apply, its recorded outcome, and the following Baseline refresh.</summary>
public sealed class ProposalApplyViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private CancellationTokenSource? _applyCancellation;
    private CancellationTokenSource? _baselineCancellation;
    private Task? _runningApply;
    private int _generation;
    private bool _isRecordingBaseline;
    private WindowRefusal? _baselineRefusal;
    private bool _isApplying;
    private ApplyProjection? _receipt;
    private WindowRefusal? _applyRefusal;
    private int _wroteCount;

    public ProposalApplyViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Whether the Refresh following a successful Apply is running.</summary>
    public bool IsRecordingBaseline
    {
        get => _isRecordingBaseline;
        private set => SetProperty(ref _isRecordingBaseline, value);
    }

    /// <summary>Why the Refresh failed, or null when it has not failed.</summary>
    public WindowRefusal? BaselineRefusal
    {
        get => _baselineRefusal;
        private set => SetProperty(ref _baselineRefusal, value);
    }

    /// <summary>How long Apply waits before leaving Refresh available to the person.</summary>
    internal TimeSpan BaselineWaitLimit { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether a write is in progress.</summary>
    public bool IsApplying
    {
        get => _isApplying;
        internal set => SetProperty(ref _isApplying, value);
    }

    /// <summary>The last Apply receipt, if a write produced one.</summary>
    public ApplyProjection? Receipt
    {
        get => _receipt;
        internal set => SetProperty(ref _receipt, value);
    }

    /// <summary>The last Apply refusal, including a write requiring reconciliation.</summary>
    public WindowRefusal? ApplyRefusal
    {
        get => _applyRefusal;
        internal set
        {
            if (SetProperty(ref _applyRefusal, value)) OnPropertyChanged(nameof(NeedsReconciliation));
        }
    }

    /// <summary>The operation count captured before the last successful write.</summary>
    public int WroteCount
    {
        get => _wroteCount;
        private set => SetProperty(ref _wroteCount, value);
    }

    internal Task ApplyAsync(Func<bool> canApply, Func<string?> revision, Func<int> operationCount,
        Func<int> entryCount, Action stopConfirming, Func<Task> loadReview, Action<string> notify,
        Action raiseApplyState, Action notifyCanExecute)
    {
        if (IsApplying || _runningApply is { IsCompleted: false }) return Task.CompletedTask;
        var running = ApplyCoreAsync(canApply, revision, operationCount, entryCount, stopConfirming,
            loadReview, notify, raiseApplyState, notifyCanExecute);
        _runningApply = running;
        return running;
    }

    private async Task ApplyCoreAsync(Func<bool> canApply, Func<string?> revision, Func<int> operationCount,
        Func<int> entryCount, Action stopConfirming, Func<Task> loadReview, Action<string> notify,
        Action raiseApplyState, Action notifyCanExecute)
    {
        var changes = _context.Changes;
        var proposal = changes.TargetProposalId;
        if (!canApply() || _context.ProjectPath is not { } project ||
            (proposal is null && changes.Snapshot.DraftId is not { })) return;
        var draft = changes.Snapshot.DraftId ?? string.Empty;
        var generation = _generation;
        var wroteCount = operationCount();
        stopConfirming();
        using var usageAction = _context.Commands.BeginUsageAction(
            proposal is null ? "apply --all-pending" : "apply --proposal",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("changes", entryCount()));
        IsApplying = true;
        _applyCancellation?.Dispose();
        _applyCancellation = new CancellationTokenSource();
        notify("IsApplying");
        notify("CanApply");
        notify("ApplyBlockReason");
        CommandOutcome<ApplyPendingResult> result;
        try
        {
            result = proposal is null
                ? await _context.Commands.ApplyPendingAsync(new ApplyPendingRequest(
                    project, draft, changes.Snapshot.Revision), _applyCancellation.Token).ConfigureAwait(true)
                : await _context.Commands.ApplyProposalAsync(new ApplyProposalRequest(
                    project, proposal, revision()), _applyCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            var cancellation = _applyCancellation;
            _applyCancellation = null;
            cancellation?.Dispose();
            IsApplying = false;
            notify("IsApplying");
            notify("CanApply");
            notify("ApplyBlockReason");
            notifyCanExecute();
        }
        if (generation != _generation || _context.ProjectPath != project) return;
        if (!result.Succeeded)
        {
            ApplyRefusal = WindowRefusal.From(result.Refusal!);
            notify("ApplyRefusal");
            raiseApplyState();
            await changes.ReloadAsync().ConfigureAwait(true);
            return;
        }
        changes.ClearStagingHistory();
        Receipt = result.Value!.Receipt;
        WroteCount = wroteCount;
        ApplyRefusal = null;
        if (result.Value.Applied) _context.RecordApplied();
        if (proposal is not null) await _context.LeaveProposalAsync().ConfigureAwait(true);
        await changes.ReloadAsync().ConfigureAwait(true);
        await loadReview().ConfigureAwait(true);
        notify("Receipt");
        notify("HasReceipt");
        notify("ReceiptText");
        notify("ApplyResultWroteText");
        notify("ApplyResultBaselineText");
        notify("ApplyRefusal");
        if (generation != _generation || _context.ProjectPath != project) return;
        if (result.Value.Applied) await RecordBaselineAsync(project).ConfigureAwait(true);
    }

    internal void Cancel() => _applyCancellation?.Cancel();

    /// <summary>Whether an earlier Apply may have saved the project without recording its receipt.</summary>
    public bool NeedsReconciliation => ApplyRefusal?.Code == RefusalCodes.ApplyReconciliationNeeded;

    internal IReadOnlyList<ApplyBlocker> FitBlockers(ICommand? removeNonFitting, ICommand? refresh)
    {
        var blockers = new List<ApplyBlocker>();
        var changes = _context.Changes.Items;
        var noLongerFits = changes.Count(item => item.IsNoLongerFits);
        if (noLongerFits > 0)
            blockers.Add(new(ApplyBlockerKind.NoLongerFits,
                PluralText.Format(MotifAppStrings.ApplyNoLongerFitsPattern, noLongerFits),
                MotifAppStrings.ApplyRemoveNonFitting, removeNonFitting));
        var uncertain = changes.Count(item => item.IsUncertain);
        if (uncertain > 0)
            blockers.Add(new(ApplyBlockerKind.Uncertain,
                PluralText.Format(MotifAppStrings.ApplyUncertainPattern, uncertain)));
        if (changes.Any(item => item.Fit is null))
            blockers.Add(new(ApplyBlockerKind.FitNotChecked,
                MotifAppStrings.ApplyFitNotCheckedSentence, MotifAppStrings.Refresh, refresh));
        return blockers;
    }

    /// <summary>Returns the reasons the current Draft or Proposal cannot be applied.</summary>
    public IReadOnlyList<ApplyBlocker> Blockers(ProposalReviewResponse? review, ICommand? removeNonFitting,
        ICommand? refresh, ICommand? tryAgain)
    {
        var blockers = new List<ApplyBlocker>();
        if (NeedsReconciliation)
            blockers.Add(new(ApplyBlockerKind.ReconciliationNeeded,
                MotifAppStrings.ApplyReconciliationSentence, MotifAppStrings.Refresh, refresh));
        var proposal = _context.Changes.TargetProposalId;
        var hasEntries = proposal is null ? _context.Changes.HasItems : review is { Entries.Count: > 0 } &&
            review.ProposalId == proposal;
        if (!hasEntries)
        {
            blockers.Add(new(ApplyBlockerKind.NothingToApply, MotifAppStrings.ApplyNothingSentence));
            return blockers;
        }
        blockers.AddRange(FitBlockers(removeNonFitting, refresh));
        if (_context.Baseline?.FieldWorksHeldProject == true)
            blockers.Add(new(ApplyBlockerKind.FieldWorksHoldsProject,
                MotifAppStrings.ApplyFieldWorksHeldSentence));
        if (_context.Evidence.IsStale)
            blockers.Add(new(ApplyBlockerKind.FieldWorksSavedSince,
                MotifAppStrings.ApplyStaleSentence, MotifAppStrings.Refresh, refresh));
        if (proposal is not null)
        {
            var standing = _context.Lens?.ProposalId == proposal ? _context.ProposalStanding : TrialStanding.NotTried;
            if (standing.Kind != TrialStandingKind.Current)
            {
                var words = TrialStandingWords.For(standing);
                blockers.Add(standing.CanTryAgain
                    ? new(ApplyBlockerKind.TrialNeeded, words.Sentence, words.Action, tryAgain)
                    : new(ApplyBlockerKind.TrialNeeded, words.Sentence));
            }
            else if (review?.Trial is null || review.Trial.JobId != _context.Lens?.Trial?.JobId ||
                     review.IntentDigest != _context.Lens?.IntentDigest || review.Readiness is null)
                blockers.Add(new(ApplyBlockerKind.NotReady, MotifAppStrings.ApplyReadinessUnavailable));
            else if (review.Readiness is { IsReady: false } readiness)
                blockers.Add(new(ApplyBlockerKind.NotReady, ProposalReadinessWords.For(readiness)));
        }
        else
        {
            var measurement = _context.PendingMeasurement;
            var losing = measurement.WordsLosingApprovedAnalysis;
            if (losing.Count > 0)
                blockers.Add(new(ApplyBlockerKind.LosesApprovedAnalysis,
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ApplyLostSentenceFormat,
                        PluralText.Format(MotifAppStrings.ApplyLostCountPattern, losing.Count), string.Join(", ", losing))));
            if (!measurement.EvidenceComplete && !measurement.IsMeasuring)
                blockers.Add(new(ApplyBlockerKind.NotMeasured,
                    MotifAppStrings.ApplyNotMeasuredSentence, MotifAppStrings.ApplyCheckChanges, measurement.MeasureCommand));
        }
        return blockers;
    }

    /// <summary>Whether the current measured changes meet every Apply condition.</summary>
    public bool CanApply(ProposalReviewResponse? review, ICommand? removeNonFitting, ICommand? refresh, ICommand? tryAgain) =>
        _context.HasProject && !IsApplying && !_context.PendingMeasurement.IsMeasuring &&
        _context.Changes.Items.All(item => item.Fit is { StillFits: true }) &&
        Blockers(review, removeNonFitting, refresh, tryAgain).Count == 0;

    /// <summary>Writes the current changes after the person has confirmed Apply.</summary>
    public Task ApplyCurrentAsync(ProposalReviewResponse? review, Func<Task>? reload = null) => ApplyAsync(
        () => CanApply(review, null, _context.RefreshProjectCommand, null), () => review?.Revision,
        () => review?.Entries.Sum(entry => entry.OperationIds.Count) ?? _context.Changes.Count,
        () => review?.Entries.Count ?? _context.Changes.Count, () => { }, reload ?? (() => Task.CompletedTask),
        _ => { }, () => { }, () => { });

    internal void ShowReconciliationNeeded()
    {
        if (NeedsReconciliation) return;
        ApplyRefusal = WindowRefusal.From(new Refusal(
            RefusalCodes.ApplyReconciliationNeeded,
            FailureReason.StoreInconsistent,
            "A previous Apply may have saved changes to the FieldWorks project, but Motif could not record its Receipt. " +
            "Refresh the project after checking it before trying Apply again.",
            new Dictionary<string, string>()));
    }

    internal void ClearReconciliationNeeded()
    {
        if (NeedsReconciliation) ApplyRefusal = null;
    }

    internal async Task RecordBaselineAsync(string project)
    {
        var generation = ++_generation;
        _baselineCancellation?.Cancel();
        var cancellation = _baselineCancellation = new CancellationTokenSource(BaselineWaitLimit);
        IsRecordingBaseline = true;
        BaselineRefusal = null;
        Refusal? stopped;
        try
        {
            stopped = await _context.RefreshBeforeCheckAsync(cancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_baselineCancellation, cancellation)) _baselineCancellation = null;
            cancellation.Dispose();
            if (generation == _generation) IsRecordingBaseline = false;
        }
        if (generation != _generation || _context.ProjectPath != project) return;
        BaselineRefusal = stopped is null ? null : WindowRefusal.From(stopped);
    }

    internal void Reset()
    {
        _generation++;
        Cancel();
        _baselineCancellation?.Cancel();
        IsRecordingBaseline = false;
        BaselineRefusal = null;
    }

    internal async Task StopAsync()
    {
        Reset();
        try
        {
            if (_runningApply is { } running) await running.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        _runningApply = null;
    }
}
