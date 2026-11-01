using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The bar across the window while a Proposal is open: it names the Proposal on every page, says what its Trial
/// stands at in the window's words, and offers Try Again and a way back to today. It adds no evidence of its own.
/// </summary>
public sealed partial class ProposalBarViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private WindowRefusal? _tryRefusal;

    /// <summary>Creates the bar over the window's context, which holds the open Proposal.</summary>
    public ProposalBarViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
        TryAgainCommand = new AsyncRelayCommand(TryAgainAsync, () => IsVisible && _context.ProposalStanding.CanTryAgain);
        context.PropertyChanged += OnContextPropertyChanged;
    }

    /// <summary>Whether a Proposal is open, which is the only time the bar shows.</summary>
    public bool IsVisible => _context.Lens is not null;

    /// <summary>The open Proposal's name, such as "Proposal: Sound change", or just "Proposal" when it has no label.</summary>
    public string Name => _context.Lens is { } lens && !string.IsNullOrWhiteSpace(lens.Label) && lens.Label != lens.ProposalId
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBarLabelFormat, lens.Label)
        : MotifAppStrings.Proposal;

    /// <summary>
    /// What the open Proposal's Trial stands at: why its Trial could not be read or tried, else the window's sentence
    /// for its standing, such as "Not tried yet. Try it to see what it does to the numbers."
    /// </summary>
    public string Sentence
    {
        get
        {
            if (_context.Lens is not { } lens) return string.Empty;
            if (lens.ReadRefusal is { } unread) return WindowRefusal.From(unread).Sentence;
            if (_context.TrialRun.StateFor(lens.ProposalId).ShownRefusal is { } refused) return refused.Sentence;
            if (_tryRefusal is { } tried) return tried.Sentence;
            return TrialStandingWords.For(_context.ProposalStanding).Sentence;
        }
    }

    /// <summary>The label of the button that tries the Proposal: "Try It" before any Trial, else "Try Again".</summary>
    public string TryAgainText => TrialStandingWords.For(_context.ProposalStanding).Action;

    /// <summary>Returns every page to today's numbers.</summary>
    public IAsyncRelayCommand LeaveCommand { get; }

    /// <summary>Tries the open Proposal; it cannot run again while a Trial is already running.</summary>
    public IAsyncRelayCommand TryAgainCommand { get; }

    /// <summary>The visible text of the button that tries the Proposal, which its accessible name must contain.</summary>
    public string TryAgainAccessibleName =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBarTryAgainAccessibleNameFormat, TryAgainText);

    /// <summary>Raised after the Proposal has been left, so the window can place focus once the bar has gone.</summary>
    public event EventHandler? Left;

    private async Task LeaveAsync()
    {
        await _context.LeaveProposalAsync().ConfigureAwait(true);
        if (_context.Lens is null) Left?.Invoke(this, EventArgs.Empty);
    }

    // A refusal the Trial run keeps no state for, such as a project that has gone, would otherwise show nowhere.
    private async Task TryAgainAsync()
    {
        if (_context.Lens is not { } lens) return;
        var outcome = await _context.TryAgainAsync(lens.ProposalId, CancellationToken.None).ConfigureAwait(true);
        if (_context.Lens?.ProposalId != lens.ProposalId) return;
        _tryRefusal = outcome.Succeeded || outcome.Refusal is null ||
            _context.TrialRun.StateFor(lens.ProposalId).Refusal is not null ? null : WindowRefusal.From(outcome.Refusal);
        OnPropertyChanged(nameof(Sentence));
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(WorkspaceContext.Lens) or nameof(WorkspaceContext.ProposalStanding))) return;
        _tryRefusal = null;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(TryAgainText));
        OnPropertyChanged(nameof(TryAgainAccessibleName));
        TryAgainCommand.NotifyCanExecuteChanged();
    }
}
