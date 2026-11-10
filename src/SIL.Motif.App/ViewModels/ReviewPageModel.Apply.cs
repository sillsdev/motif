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

// Apply as a deliberate step: what it writes, what it needs, a question before it, and what it did.
public sealed partial class ReviewPageModel
{
    private bool _isConfirming;
    private int _wroteCount;

    /// <summary>The operations the open Proposal writes, which is what Apply counts as changes.</summary>
    private int OperationCount => _review is { } review ? review.Entries.Sum(entry => entry.OperationIds.Count) : _entries.Count;

    private string CountedChanges(int count) => PluralText.Format(MotifAppStrings.ChangeCountPattern, count);

    private string ApplyProjectLabel
    {
        get
        {
            var name = ProjectName;
            return name.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase) ? name[..^".fwdata".Length] : name;
        }
    }

    /// <summary>Above Apply: how many changes it writes and to which project file.</summary>
    public string ApplySummaryText =>
        string.Format(MotifAppStrings.ApplySummaryFormat, CountedChanges(OperationCount), ProjectName);

    /// <summary>The card's count line: what Apply writes while there is something to write, else the page's count.</summary>
    public string ApplyCardCountText => HasEntries && !NeedsReconciliation ? ApplySummaryText : CountText;

    /// <summary>The confirmation's question, naming the project.</summary>
    public string ApplyQuestionText =>
        string.Format(MotifAppStrings.ApplyQuestionFormat, CountedChanges(OperationCount), ApplyProjectLabel);

    /// <summary>Whether the page asks for confirmation instead of offering the Apply button.</summary>
    public bool IsConfirmingApply => _isConfirming;

    /// <summary>Whether the Apply button is offered rather than the confirmation question.</summary>
    public bool OffersApply => !IsConfirmingApply;

    /// <summary>Opens the confirmation; nothing is written until it is confirmed.</summary>
    public IRelayCommand AskToApplyCommand => _askToApply ??= new RelayCommand(
        () => SetConfirmingApply(true), () => CanApply && !_isConfirming);

    private IRelayCommand? _askToApply;

    /// <summary>Closes the confirmation and writes nothing.</summary>
    public IRelayCommand CancelApplyCommand => _cancelApply ??= new RelayCommand(StopConfirmingApply);

    private IRelayCommand? _cancelApply;

    private void StopConfirmingApply() => SetConfirmingApply(false);

    private void SetConfirmingApply(bool confirming)
    {
        _isConfirming = confirming;
        OnPropertyChanged(nameof(IsConfirmingApply));
        OnPropertyChanged(nameof(OffersApply));
        AskToApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What the last Apply wrote, from the count taken before the page reloaded.</summary>
    public string ApplyResultWroteText => string.Format(MotifAppStrings.ApplyResultWroteFormat,
        CountedChanges(_wroteCount), ProjectName);

    /// <summary>Whether the window has captured a new Baseline since that Apply; it says so only then.</summary>
    public string ApplyResultBaselineText => Context.Evidence.AppliedSinceRefresh
        ? MotifAppStrings.ApplyResultBaselinePending : MotifAppStrings.ApplyResultBaselineRecorded;

    /// <summary>Opens FieldWorks' own help on backing up a project.</summary>
    public IAsyncRelayCommand OpenBackupHelpCommand => _openBackupHelp ??= new AsyncRelayCommand(async () =>
    {
        if (Context.UriLauncher is { } launcher)
            await launcher.LaunchAsync(new Uri(AppLinks.FieldWorksBackupHelp)).ConfigureAwait(true);
    });

    private IAsyncRelayCommand? _openBackupHelp;

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
                Context.Baseline?.FieldWorksHeldProject == true
                    ? new(MotifAppStrings.ApplyCheckFieldWorksOpen, false)
                    : new(MotifAppStrings.ApplyCheckFieldWorksClosed, true),
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

    private async Task ApplyAsync()
    {
        var proposal = Changes.TargetProposalId;
        if (!CanApply || Context.ProjectPath is not { } project ||
            (proposal is null && Changes.Snapshot.DraftId is not { }))
            return;
        var draft = Changes.Snapshot.DraftId ?? string.Empty;
        var wroteCount = OperationCount;
        StopConfirmingApply();
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
        _wroteCount = wroteCount;
        ApplyRefusal = null;
        if (result.Value.Applied) Context.RecordApplied();
        if (proposal is not null) await Context.LeaveProposalAsync().ConfigureAwait(true);
        await Changes.ReloadAsync().ConfigureAwait(true);
        await LoadReviewAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyResultWroteText));
        OnPropertyChanged(nameof(ApplyResultBaselineText));
        OnPropertyChanged(nameof(ApplyRefusal));
    }
}
