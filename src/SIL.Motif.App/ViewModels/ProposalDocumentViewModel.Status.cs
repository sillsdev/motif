using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    private string _statusSentence = string.Empty;
    private string? _statusActionText;
    private ICommand? _statusActionCommand;

    public string StatusSentence => _statusSentence;
    public bool HasStatusSentence => _statusSentence.Length > 0;
    public bool ShowsReadySentence => _statusSentence == MotifAppStrings.ProposalStatusReady;
    public bool ShowsTrialStatusSentence => HasStatusSentence && !ShowsReadySentence;
    public bool ShowsLifecycleStatus => StatusText.Length > 0 && StatusText != MotifAppStrings.ProposalWaiting;
    public string? StatusActionText => _statusActionText;
    public ICommand? StatusActionCommand => _statusActionCommand;
    public bool HasStatusAction => _statusActionText is not null && _statusActionCommand is not null;
    public bool ShowsStatus => ShowsReadiness || HasStatusSentence || HasStatusAction;
    public bool ShowsReadiness => !_stopped && !IsLoading && CanAddComment && !IsPending;
    public bool ShowsOutdatedNotice => IsOutdated && !ShowsStatus;

    /// <summary>The automation id of the status block's Try It or Try Again button.</summary>
    public string TryAutomationId => Card?.InspectorTryAgainAutomationId ?? AutomationIds.ForProposalTryAgain(ProposalId) + "-inspector";

    private TrialStanding CurrentStanding => Card?.Standing ?? TrialStanding.Of(_review?.Trial);

    public bool TriedChecked => CurrentStanding.Kind == TrialStandingKind.Current && !IsOutdated;
    public bool DifferenceChecked => TriedChecked && Difference.IsShown && Difference.Summary is { } summary &&
        summary.TrialJobId == (_review?.Trial?.JobId ?? Card?.Card.Trial?.JobId);
    public bool DryRunChecked => !IsOutdated && _review?.Readiness is { MissingBoundDryRun: false };
    public string TriedCheckText => TriedChecked &&
        (Card?.Card.Trial?.CompletedUtc ?? _review?.Trial?.CompletedUtc) is { } time
        ? FormatCheckDetail(MotifAppStrings.ProposalCheckTried, time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : CheckText(MotifAppStrings.ProposalCheckTried, TriedChecked);
    public string DifferenceCheckText => DifferenceChecked && Difference.Summary is { } summary
        ? FormatCheckDetail(MotifAppStrings.ProposalCheckDifference, summary.TextTitle)
        : CheckText(MotifAppStrings.ProposalCheckDifference, DifferenceChecked);
    public string DryRunCheckText => CheckText(MotifAppStrings.ProposalCheckDryRun, DryRunChecked);
    public bool ShowsDifferenceAction => ShowsReadiness && TriedChecked && Difference.ShowsMakeControls;
    public bool ShowsDryRunAction => ShowsReadiness && !DryRunChecked;
    public bool ShowsDryRunAdvice => ShowsDryRunAction && DifferenceChecked &&
        _review?.Readiness is { MissingBoundDryRun: true } && !IsDryRunActive;
    public bool ShowsDifferenceAdvice => ShowsReadiness && TriedChecked && !DifferenceChecked && Difference.ShowsMakeControls;
    public string DifferenceAdvice => Difference.IsShown
        ? MotifAppStrings.ProposalDifferenceEarlierTry : MotifAppStrings.ProposalEvidenceNotMadeYet;

    private static string CheckText(string label, bool complete) => string.Format(CultureInfo.CurrentCulture,
        complete ? MotifAppStrings.ProposalCheckComplete : MotifAppStrings.ProposalCheckNeeded, label);

    private static string FormatCheckDetail(string label, string detail) =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalCheckCompleteDetail, label, detail);

    private void OnDifferenceStatusChanged(object? sender, PropertyChangedEventArgs e) => RaiseApply();

    private void RaiseStatus()
    {
        (_statusSentence, _statusActionText, _statusActionCommand) = ComputeStatus();
        Evidence.StatusSaysUnavailable = IsLoading || ShowsDifferenceAdvice ||
            HasStatusSentence && _statusSentence != MotifAppStrings.ProposalStatusReady;
        foreach (var name in new[] { nameof(StatusSentence), nameof(HasStatusSentence), nameof(StatusActionText),
                     nameof(StatusActionCommand), nameof(HasStatusAction), nameof(ShowsStatus), nameof(ShowsOutdatedNotice),
                     nameof(ShowsReadiness), nameof(ShowsReadySentence), nameof(ShowsTrialStatusSentence), nameof(ShowsLifecycleStatus), nameof(TriedChecked), nameof(DifferenceChecked), nameof(DryRunChecked),
                     nameof(TriedCheckText), nameof(DifferenceCheckText), nameof(DryRunCheckText), nameof(ShowsDifferenceAction),
                     nameof(ShowsDryRunAction), nameof(ShowsDryRunAdvice), nameof(ShowsDifferenceAdvice), nameof(DifferenceAdvice) })
            OnPropertyChanged(name);
    }

    private (string Sentence, string? Action, ICommand? Command) ComputeStatus()
    {
        if (_stopped || IsLoading || !CanAddComment) return (string.Empty, null, null);
        if (IsPending)
            return (_context.PendingMeasurement.EvidenceComplete ? string.Empty : MotifAppStrings.ProposalStatusDraft,
                MotifAppStrings.ApplyCheckChanges, MeasurePendingCommand);
        var standing = CurrentStanding;
        if (standing.Kind != TrialStandingKind.Current)
        {
            var words = TrialStandingWords.For(standing);
            return (words.OldNumbers ?? words.Sentence, standing.CanTryAgain ? words.Action : null,
                standing.CanTryAgain ? TryCommand : null);
        }
        if (IsOutdated) return (MotifAppStrings.ProposalDocumentOutdated, MotifAppStrings.Refresh, RefreshCommand);
        return DifferenceChecked && PrepareBlockReason is null ? (MotifAppStrings.ProposalStatusReady, null, null)
            : (string.Empty, null, null);
    }

    private async Task TryAsync()
    {
        if (_stopped) return;
        var outcome = await _context.TryAgainAsync(ProposalId, CancellationToken.None).ConfigureAwait(true);
        if (_stopped) return;
        DetailRefusal = outcome.Succeeded || outcome.Refusal is null ||
            _context.TrialRun.StateFor(ProposalId).Refusal is not null ? null : WindowRefusal.From(outcome.Refusal);
    }
}
