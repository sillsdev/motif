using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The bar across the window while a Proposal is open: it names the Proposal on every page, says what its Trial
/// stands at in the window's words, and offers Try Again and a way back to today. It adds no evidence of its own.
/// </summary>
public sealed partial class ProposalBarViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private WindowRefusal? _tryRefusal;
    private string? _shownProposalId;

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
            var standing = TrialStandingWords.For(_context.ProposalStanding).Sentence;
            return TriedText(lens) is { } when
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBarSentenceFormat, when, standing)
                : standing;
        }
    }

    // A Proposal that was never tried, or whose try did not finish, has no time to give.
    private string? TriedText(ProposalLens lens)
    {
        if (lens.Trial is not { CompletedUtc: { } completed } trial || _context.ProposalStanding.Kind is not
            (TrialStandingKind.Current or TrialStandingKind.NeedsAnotherTry)) return null;
        var tried = WindowTimeText.Format(completed, _context.Clock);
        return DateTimeOffset.TryParse(trial.Baseline.CapturedUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var saved) && saved.Year > 1
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBarTriedFormat, tried,
                WindowTimeText.Format(saved, _context.Clock))
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBarTriedNoSaveFormat, tried);
    }

    /// <summary>
    /// The place of the open Proposal's view among the switch's three choices, which are in <see cref="MarkupView"/>'s
    /// order; choosing one changes every page that draws the view, and no choice is shown when no Proposal is open.
    /// </summary>
    public int SelectedViewIndex
    {
        get => _context.Lens is { } lens ? (int)lens.View : -1;
        set
        {
            if (Enum.IsDefined(typeof(MarkupView), value)) _context.ShowProposalView((MarkupView)value);
            // Anything that clears the choice puts the shown choice back once the clearing has finished.
            else if (_context.Lens is not null) Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedViewIndex)));
        }
    }

    /// <summary>Whether the switch shows: only on a page that follows it, so no page claims a view it ignores.</summary>
    public bool ShowsViewSwitch => _context.Lens is not null && ProposalViewScope.Pages.Contains(_context.CurrentPage);

    /// <summary>Says which pages follow the switch and that the rest show today's numbers.</summary>
    public string ViewTooltip => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalViewTooltipFormat,
        string.Join(", ", ProposalViewScope.Pages.Select(PageName)));

    private static string PageName(WorkspacePage page) => page switch
    {
        WorkspacePage.Overview => MotifAppStrings.Overview,
        WorkspacePage.Warnings => MotifAppStrings.Warnings,
        WorkspacePage.Timing => MotifAppStrings.PageTiming,
        _ => page.ToString(),
    };

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
        if (e.PropertyName == nameof(WorkspaceContext.ProposalView)) OnPropertyChanged(nameof(SelectedViewIndex));
        if (e.PropertyName == nameof(WorkspaceContext.CurrentPage)) OnPropertyChanged(nameof(ShowsViewSwitch));
        if (e.PropertyName is not (nameof(WorkspaceContext.Lens) or nameof(WorkspaceContext.ProposalStanding))) return;
        if (_context.Lens?.ProposalId != _shownProposalId || e.PropertyName == nameof(WorkspaceContext.ProposalStanding))
            _tryRefusal = null;
        _shownProposalId = _context.Lens?.ProposalId;
        OnPropertyChanged(nameof(SelectedViewIndex));
        OnPropertyChanged(nameof(ShowsViewSwitch));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(TryAgainText));
        OnPropertyChanged(nameof(TryAgainAccessibleName));
        TryAgainCommand.NotifyCanExecuteChanged();
    }
}
