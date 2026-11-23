using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    private IAsyncRelayCommand? _bringBack;
    private bool _isBringingBack;

    [ObservableProperty] private WindowRefusal? _bringBackRefusal;

    /// <summary>Raised after a Deferred Proposal went back to Waiting, so the list can show it there.</summary>
    internal event Action? BroughtBackToWaiting;

    /// <summary>Whether this Deferred Proposal can go back to Waiting.</summary>
    public bool CanBringBack => !_stopped && IsDeferred && !_isBringingBack && !IsDryRunActive &&
        !_context.ProposalApply.IsApplying && _context.ProjectPath == ProjectPath;

    /// <summary>Whether the Bring Back button shows beside the status; the Apply confirmation carries its own.</summary>
    public bool ShowsBringBack => CanBringBack && !IsConfirmingApply;

    /// <summary>Returns the Deferred Proposal to Waiting, where it reads like any other open Proposal.</summary>
    public IAsyncRelayCommand BringBackCommand => _bringBack ??= new AsyncRelayCommand(BringBackAsync, () => CanBringBack);

    private async Task BringBackAsync()
    {
        if (!CanBringBack) return;
        SetBringingBack(true);
        try
        {
            var outcome = await _context.Commands.UndeferProposalAsync(new UndeferRequest(ProjectPath,
                MotifProductVersion.CurrentText, ProposalId), CancellationToken.None).ConfigureAwait(true);
            if (_stopped || _context.ProjectPath != ProjectPath) return;
            if (!outcome.Succeeded)
            {
                BringBackRefusal = WindowRefusal.From(outcome.Refusal!);
                if (outcome.Refusal!.Code is RefusalCodes.ProposalInvalidStatus or RefusalCodes.ProposalNotFound)
                {
                    // The status changed under the reader, so the page and the list read it again.
                    await RefreshAsync().ConfigureAwait(true);
                    if (Detail is { } read && Card is { } held)
                        Card = new ProposalCardViewModel(held.Card with { Status = read.Status });
                    BroughtBackToWaiting?.Invoke();
                }
                return;
            }
            BringBackRefusal = null;
            IsConfirmingApply = false;
            if (Card is { } card) Card = new ProposalCardViewModel(card.Card with { Status = "proposed" });
            await RefreshAsync().ConfigureAwait(true);
            BroughtBackToWaiting?.Invoke();
        }
        finally
        {
            SetBringingBack(false);
        }
    }

    private void SetBringingBack(bool value)
    {
        _isBringingBack = value;
        RaiseBringBack();
    }

    private void RaiseBringBack()
    {
        OnPropertyChanged(nameof(CanBringBack));
        OnPropertyChanged(nameof(ShowsBringBack));
        _bringBack?.NotifyCanExecuteChanged();
    }
}
