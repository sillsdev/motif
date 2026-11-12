using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    private string? _declineProposalId;
    private int _noticeGeneration;

    /// <summary>Shows the decline confirmation.</summary>
    public IRelayCommand<ProposalCardViewModel> AskToDeclineCommand { get; }

    /// <summary>Closes the decline confirmation without deciding.</summary>
    public IRelayCommand CancelDeclineCommand { get; }

    /// <summary>Saves an optional reason and declines the selected Proposal.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> DeclineCommand { get; }

    /// <summary>Leaves the selected Proposal open for a later decision.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> DecideLaterCommand { get; }

    /// <summary>Whether a decline is waiting for confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingDecline;

    /// <summary>The person's optional reason for declining.</summary>
    [ObservableProperty]
    private string _declineReason = string.Empty;

    /// <summary>The temporary result of a decision.</summary>
    [ObservableProperty]
    private string _decisionNotice = string.Empty;

    private void AskToDecline(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanDecide || !SelectByUser(card)) return;
        _declineProposalId = card.ProposalId;
        DeclineReason = string.Empty;
        IsConfirmingDecline = true;
    }

    private void CancelDecline()
    {
        _declineProposalId = null;
        DeclineReason = string.Empty;
        IsConfirmingDecline = false;
    }

    private async Task DeclineAsync(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanDecide || _declineProposalId != card.ProposalId ||
            Context.ProjectPath is not { } path) return;
        var result = await Context.Commands.DeclineProposalAsync(
            new DeclineProposalRequest(path, card.ProposalId, DeclineReason.Trim()), CancellationToken.None)
            .ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        if (!result.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(result.Refusal!);
            return;
        }
        CancelDecline();
        await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShowDecisionNotice(MotifAppStrings.ProposalDeclinedNotice);
    }

    private async Task DecideLaterAsync(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanDecideLater || Context.ProjectPath is not { } path) return;
        var result = await Context.Commands.DeferProposalAsync(new DeferRequest(path,
            MotifProductVersion.CurrentText, card.ProposalId), CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = result.Succeeded ? null : WindowRefusal.From(result.Refusal!);
        if (result.Succeeded) await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }

    private void ShowDecisionNotice(string notice)
    {
        var generation = ++_noticeGeneration;
        DecisionNotice = notice;
        _ = ClearDecisionNoticeAsync(generation);
    }

    private async Task ClearDecisionNoticeAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        if (generation == _noticeGeneration) DecisionNotice = string.Empty;
    }
}
