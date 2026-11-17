using System.ComponentModel;
using System.Windows.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

// round2-proposal: the one status block at the top of a Proposal's page.
public sealed partial class ProposalDocumentViewModel
{
    private string _statusSentence = string.Empty;
    private string? _statusActionText;
    private ICommand? _statusActionCommand;

    /// <summary>What state the Proposal is in and what to do next, said once; empty when nothing needs saying.</summary>
    public string StatusSentence => _statusSentence;

    /// <summary>Whether the status block has a sentence.</summary>
    public bool HasStatusSentence => _statusSentence.Length > 0;

    /// <summary>The label of the button that does the next step, or null when no button does it.</summary>
    public string? StatusActionText => _statusActionText;

    /// <summary>The command of that button.</summary>
    public ICommand? StatusActionCommand => _statusActionCommand;

    /// <summary>Whether the status block has a button.</summary>
    public bool HasStatusAction => _statusActionText is not null && _statusActionCommand is not null;

    /// <summary>Whether the status block is shown: it has a sentence, a button, or the controls that make a Difference.</summary>
    public bool ShowsStatus => HasStatusSentence || HasStatusAction || (CanAddComment && !IsLoading && Difference.ShowsMakeControls);

    /// <summary>Whether the page still needs its own line saying the evidence is out of date.</summary>
    public bool ShowsOutdatedNotice => IsOutdated && !ShowsStatus;

    /// <summary>The automation id of the status block's Try It or Try Again button.</summary>
    public string TryAutomationId => Card?.InspectorTryAgainAutomationId ?? AutomationIds.ForProposalTryAgain(ProposalId) + "-inspector";

    private void OnDifferenceStatusChanged(object? sender, PropertyChangedEventArgs e) => RaiseApply();

    private void RaiseStatus()
    {
        (_statusSentence, _statusActionText, _statusActionCommand) = ComputeStatus();
        Evidence.StatusSaysUnavailable = HasStatusSentence && !IsStatusReady;
        foreach (var name in new[] { nameof(StatusSentence), nameof(HasStatusSentence), nameof(StatusActionText),
                     nameof(StatusActionCommand), nameof(HasStatusAction), nameof(ShowsStatus), nameof(ShowsOutdatedNotice) })
            OnPropertyChanged(name);
    }

    private bool IsStatusReady => _statusSentence == MotifAppStrings.ProposalStatusReady;

    private (string Sentence, string? Action, ICommand? Command) ComputeStatus()
    {
        if (_stopped || IsLoading || !CanAddComment) return (string.Empty, null, null);
        if (IsPending)
            return (_context.PendingMeasurement.EvidenceComplete ? string.Empty : MotifAppStrings.ProposalStatusDraft,
                MotifAppStrings.ApplyCheckChanges, MeasurePendingCommand);
        var standing = Card?.Standing ?? (_review?.Trial is { Freshness: TrialFreshness.Current }
            ? TrialStanding.Current : TrialStanding.NotTried);
        if (standing.Kind != TrialStandingKind.Current)
        {
            var words = TrialStandingWords.For(standing);
            return (words.OldNumbers ?? words.Sentence, standing.CanTryAgain ? words.Action : null,
                standing.CanTryAgain ? TryCommand : null);
        }
        if (IsOutdated) return (MotifAppStrings.ProposalDocumentOutdated, MotifAppStrings.Refresh, RefreshCommand);
        if (Difference.State == ProposalDifferenceState.Absent && Difference.ShowsMakeControls)
            return (MotifAppStrings.ProposalEvidenceNotMadeYet, null, null);
        return Difference.IsShown && PrepareBlockReason is null ? (MotifAppStrings.ProposalStatusReady, null, null)
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
