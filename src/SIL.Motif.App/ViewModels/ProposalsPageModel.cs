using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One of a card's four numbers, as "before → after" when the Trial measured it.</summary>
/// <param name="Label">What the number is.</param>
/// <param name="Value">The number, or today's number alone when the Proposal has no Trial values yet.</param>
/// <param name="Moved">Whether the Trial moved the number enough to highlight it.</param>
public sealed record ProposalNumber(string Label, string Value, bool Moved);

/// <summary>
/// The one place a card's typed <see cref="Paired{T}"/> values become text, so the swap to the shared paired-number
/// renderer is a single edit.
/// </summary>
internal static class ProposalNumberText
{
    public static IReadOnlyList<ProposalNumber> From(TrialSummary trial) =>
    [
        new("Coverage", Show(trial.TextCoveragePercent, Percent), trial.TextCoveragePercent.Moved),
        new("Approved kept", ShowKept(trial), trial.ApprovedKept.Moved || trial.ApprovedTotal.Moved),
        new("Word time", Show(trial.WordTimeMs, Time), trial.WordTimeMs.Moved),
        new("Warnings", Show(trial.WarningCount, Count), trial.WarningCount.Moved),
    ];

    private static string ShowKept(TrialSummary trial)
    {
        var before = $"{trial.ApprovedKept.Before.ToString(CultureInfo.CurrentCulture)} of " +
            trial.ApprovedTotal.Before.ToString(CultureInfo.CurrentCulture);
        if (!trial.ApprovedKept.HasAfter || !trial.ApprovedTotal.HasAfter) return before;
        return $"{before} → {trial.ApprovedKept.After.ToString(CultureInfo.CurrentCulture)} of " +
            trial.ApprovedTotal.After.ToString(CultureInfo.CurrentCulture);
    }

    private static string Show<T>(Paired<T> pair, Func<T?, string> format)
    {
        var before = format(pair.Before);
        return pair.HasAfter ? $"{before} → {format(pair.After)}" : before;
    }

    private static string Percent(double? value) =>
        value is { } percent ? percent.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "—";

    private static string Time(double? milliseconds) => milliseconds is { } ms
        ? ms >= 1000 ? (ms / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " s"
            : ms.ToString("0", CultureInfo.CurrentCulture) + " ms"
        : "—";

    private static string Count(int? value) => value is { } count ? count.ToString(CultureInfo.CurrentCulture) : "—";
}

/// <summary>One Proposal row: its label, what it changes, its numbers and what a person can do with it.</summary>
public sealed class ProposalCardViewModel
{
    public ProposalCardViewModel(ProposalCard card, IReadOnlyDictionary<string, string>? labels = null,
        TrialStanding? standing = null, WindowRefusal? refusal = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Standing = standing ?? card.Standing;
        ShownRefusal = refusal;
        Words = TrialStandingWords.For(Standing);
        OpenAutomationId = AutomationIds.ForProposalOpen(card.ProposalId);
        TryAgainAutomationId = AutomationIds.ForProposalTryAgain(card.ProposalId);
        DeleteAutomationId = AutomationIds.ForProposalDelete(card.ProposalId);
        if (card.Trial is { } trial)
        {
            Numbers = ProposalNumberText.From(trial);
            if (trial.LostApproved > 0)
                LostChip = $"{trial.LostApproved.ToString(CultureInfo.CurrentCulture)} lost";
            if (trial.NoLongerFits > 0)
                FitChip = $"{trial.NoLongerFits.ToString(CultureInfo.CurrentCulture)} no longer fits";
        }
        if (card.AlsoChangedBy.Count > 0)
            AlsoChip = "also changes words in " + string.Join(", ",
                card.AlsoChangedBy.Select(id => labels is not null && labels.TryGetValue(id, out var label) ? label : id));
    }

    /// <summary>The standings row this card shows.</summary>
    public ProposalCard Card { get; }

    /// <summary>What the window shows: the open Try again while it runs, else the stored Trial's standing.</summary>
    public TrialStanding Standing { get; }

    /// <summary>The window's words for <see cref="Standing"/>.</summary>
    public TrialStandingText Words { get; }

    /// <summary>Why the last Try again of this Proposal did not produce a result, in the window's words.</summary>
    public WindowRefusal? ShownRefusal { get; }

    public string ProposalId => Card.ProposalId;

    public string Label => Card.Label;

    /// <summary>The automation id of this row.</summary>
    public string RowAutomationId => AutomationIds.ForProposalRow(Card.ProposalId);

    /// <summary>The automation id of this card's Open button.</summary>
    public string OpenAutomationId { get; }

    /// <summary>The automation id of this card's Try again button.</summary>
    public string TryAgainAutomationId { get; }

    /// <summary>The automation id of this card's Delete button.</summary>
    public string DeleteAutomationId { get; }

    /// <summary>
    /// The Proposal's status in the window's words: Draft, Ready, Deferred, Applied, Declined, Superseded or
    /// Withdrawn. Empty for the pending changes, which the window never names as a Proposal.
    /// </summary>
    public string StatusWord => Card.IsPendingDraft ? string.Empty : Card.IsDraft ? "Draft" : Card.Status switch
    {
        "proposed" => "Ready",
        "deferred" => "Deferred",
        "applied" => "Applied",
        "rejected" => "Declined",
        "superseded" => "Superseded",
        "withdrawn" => "Withdrawn",
        _ => string.Empty,
    };

    public bool HasStatusWord => StatusWord.Length > 0;

    /// <summary>Whether the card offers Delete: a declined, superseded or withdrawn Proposal that never applied.</summary>
    public bool CanDelete => !Card.IsDraft && Card.Status is "rejected" or "superseded" or "withdrawn";

    /// <summary>What the Proposal changes in one line, such as "2 rules · 1 allomorph · 3 parses approved".</summary>
    public string KindsText => KindsIn(Card.ChangeKinds);

    /// <summary>The card's four numbers in order, or empty when the Proposal was never tried.</summary>
    public IReadOnlyList<ProposalNumber> Numbers { get; } = [];

    public bool HasNumbers => Numbers.Count > 0;

    /// <summary>Text Coverage before and after, or null when the Proposal was never tried.</summary>
    public ProposalNumber? Coverage => Numbers.Count > 0 ? Numbers[0] : null;

    /// <summary>Approved words kept before and after.</summary>
    public ProposalNumber? ApprovedKept => Numbers.Count > 1 ? Numbers[1] : null;

    /// <summary>Parse time before and after.</summary>
    public ProposalNumber? WordTime => Numbers.Count > 2 ? Numbers[2] : null;

    /// <summary>Warning count before and after.</summary>
    public ProposalNumber? Warnings => Numbers.Count > 3 ? Numbers[3] : null;

    /// <summary>Whether the numbers come from an earlier try and should read as old.</summary>
    public bool NumbersAreOld => HasNumbers && Standing.Kind == TrialStandingKind.NeedsAnotherTry;

    /// <summary>The chip naming approved words the Trial lost, or null.</summary>
    public string? LostChip { get; }

    public bool HasLostChip => LostChip is not null;

    /// <summary>The chip naming changes that no longer fit, or null.</summary>
    public string? FitChip { get; }

    public bool HasFitChip => FitChip is not null;

    /// <summary>The chip naming other Proposals that change some of the same words, or null.</summary>
    public string? AlsoChip { get; }

    public bool HasAlsoChip => AlsoChip is not null;

    /// <summary>Whether the card offers a button that tries the Proposal now.</summary>
    public bool CanTry => Card.Group is not (ProposalGroup.SetAside or ProposalGroup.WaitingForYou) &&
        Standing.CanTryAgain && Standing.Kind != TrialStandingKind.Current;

    /// <summary>The button's label: "Try it" before any Trial, "Try again" after.</summary>
    public string TryText => Words.Action;

    /// <summary>
    /// What the card says beside its numbers. The group's own heading is not repeated, and numbers from an earlier
    /// try say which change made them old.
    /// </summary>
    public string StatusText => Standing.Kind switch
    {
        TrialStandingKind.Trying or TrialStandingKind.Failed => Words.Sentence,
        TrialStandingKind.NeedsAnotherTry => Words.OldNumbers ?? string.Empty,
        TrialStandingKind.NotTried when Card.Group != ProposalGroup.NotTried => Words.Status,
        _ => string.Empty,
    };

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Joins change kinds into the one-line wording the page and tests share.</summary>
    public static string KindsIn(IReadOnlyList<ChangeKindCount> kinds) =>
        string.Join(" · ", kinds.Select(kind => kind.Text));
}

/// <summary>One titled group of Proposal rows.</summary>
/// <param name="Group">Which group this is.</param>
/// <param name="Title">The group's heading.</param>
/// <param name="Cards">The group's Proposals.</param>
public sealed record ProposalGroupViewModel(ProposalGroup Group, string Title, IReadOnlyList<ProposalCardViewModel> Cards);

/// <summary>The heading above one group's cards, as a row of the page's one list.</summary>
/// <param name="Title">The group's heading.</param>
public sealed record ProposalGroupHeader(string Title);

/// <summary>What the Proposals page is showing, so loading, empty and refused cannot overlap.</summary>
public enum ProposalsPageState
{
    /// <summary>The first read is under way.</summary>
    Loading,

    /// <summary>At least one Proposal is listed, or applied ones wait behind the link.</summary>
    Listed,

    /// <summary>The read succeeded and there are no Proposals.</summary>
    Empty,

    /// <summary>The read was refused; the refusal is shown instead of a list.</summary>
    Refused,
}

/// <summary>
/// The Proposals page's model: every Proposal grouped by what to do next, with its numbers before and after its
/// Trial. Opening a row enters Proposal mode; Try again goes through the window's one Try again, and nothing else
/// runs on its own.
/// </summary>
public sealed partial class ProposalsPageModel : PageModel
{
    private int _readGeneration;
    private bool _includeSetAside;
    private ProposalStandings _standings = new([], 0);

    public ProposalsPageModel(WorkspaceContext context) : base(context)
    {
        OpenCommand = new AsyncRelayCommand<ProposalCardViewModel>(OpenAsync);
        TryAgainCommand = new AsyncRelayCommand<ProposalCardViewModel>(TryAgainAsync);
        ShowSetAsideCommand = new AsyncRelayCommand(ShowSetAsideAsync);
        DeleteCommand = new AsyncRelayCommand<ProposalCardViewModel>(DeleteAsync);
        Context.TrialRun.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrialRunViewModel.States) && State == ProposalsPageState.Listed)
                Apply(_standings);
        };
    }

    /// <summary>The groups that have Proposals, in display order.</summary>
    [ObservableProperty]
    private IReadOnlyList<ProposalGroupViewModel> _groups = [];

    /// <summary>The page's one list: each group's heading followed by its cards.</summary>
    [ObservableProperty]
    private IReadOnlyList<object> _rows = [];

    /// <summary>The words of the link that loads applied and set-aside Proposals.</summary>
    [ObservableProperty]
    private string _setAsideLinkText = string.Empty;

    /// <summary>What the page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsEmpty), nameof(IsListed))]
    private ProposalsPageState _state = ProposalsPageState.Loading;

    /// <summary>The refusal from the last read or action, in the window's words, or null.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    /// <summary>Whether the first read is still under way.</summary>
    public bool IsLoading => State == ProposalsPageState.Loading;

    /// <summary>Whether the read succeeded and found no Proposals.</summary>
    public bool IsEmpty => State == ProposalsPageState.Empty;

    /// <summary>Whether the page lists Proposals.</summary>
    public bool IsListed => State == ProposalsPageState.Listed;

    /// <summary>Whether the link to applied and set-aside Proposals shows.</summary>
    public bool HasSetAsideLink => SetAsideLinkText.Length > 0;

    public IAsyncRelayCommand<ProposalCardViewModel> OpenCommand { get; }

    public IAsyncRelayCommand<ProposalCardViewModel> TryAgainCommand { get; }

    /// <summary>Shows or hides the closed Proposals: applied, declined, superseded and withdrawn.</summary>
    public IAsyncRelayCommand ShowSetAsideCommand { get; }

    /// <summary>Deletes a closed Proposal that never applied, with its Trials, and reads the list again.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> DeleteCommand { get; }

    partial void OnSetAsideLinkTextChanged(string value) => OnPropertyChanged(nameof(HasSetAsideLink));

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        _includeSetAside = false;
        ShownRefusal = null;
        Apply(new ProposalStandings([], 0));
        State = ProposalsPageState.Loading;
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        LoadAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } path ? LoadAsync(path, cancellationToken) : Task.CompletedTask;

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken) =>
        Context.ProjectPath is { } path ? LoadAsync(path, cancellationToken) : Task.CompletedTask;

    private async Task LoadAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_readGeneration;
        var read = await Context.Commands.ProposalStandingsAsync(
            new ProposalStandingsRequest(projectPath, _includeSetAside), cancellationToken).ConfigureAwait(true);
        if (generation != _readGeneration || projectPath != Context.ProjectPath) return;
        if (!read.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(read.Refusal!, "The Proposals could not be read.");
            State = ProposalsPageState.Refused;
            return;
        }
        ShownRefusal = null;
        Apply(read.Value!);
    }

    private void Apply(ProposalStandings standings)
    {
        _standings = standings;
        var labels = standings.Proposals.ToDictionary(card => card.ProposalId, card => card.Label,
            StringComparer.Ordinal);
        var cards = standings.Proposals.Select(card => CardFor(card, labels)).ToArray();
        Groups = new (ProposalGroup, string)[]
            {
                (ProposalGroup.WaitingForYou, "Waiting for you"),
                (ProposalGroup.NeedsAnotherTry, "Needs another try"),
                (ProposalGroup.NotTried, "Not tried yet"),
                (ProposalGroup.NotAppliedYet, "Your changes not applied yet"),
                (ProposalGroup.SetAside, "Closed"),
            }
            .Select(entry => new ProposalGroupViewModel(entry.Item1, entry.Item2,
                cards.Where(card => card.Card.Group == entry.Item1).ToArray()))
            .Where(group => group.Cards.Count > 0).ToArray();
        Rows = Groups.SelectMany(group => new object[] { new ProposalGroupHeader(group.Title) }
            .Concat(group.Cards)).ToArray();
        State = cards.Length == 0 && standings.SetAsideCount == 0 ? ProposalsPageState.Empty : ProposalsPageState.Listed;
        SetAsideLinkText = _includeSetAside ? "Hide closed"
            : standings.SetAsideCount == 0 ? string.Empty
            : $"Show closed ({standings.SetAsideCount.ToString(CultureInfo.CurrentCulture)})";
        var waiting = cards.Count(card => card.Card.Group == ProposalGroup.WaitingForYou);
        Badge = waiting == 0 ? string.Empty : waiting.ToString(CultureInfo.CurrentCulture);
    }

    private ProposalCardViewModel CardFor(ProposalCard card, IReadOnlyDictionary<string, string> labels)
    {
        var run = Context.TrialRun.StateFor(card.ProposalId);
        var standing = run.IsRunning ? TrialStanding.Trying : card.Standing;
        return new ProposalCardViewModel(card, labels, standing, run.IsRunning ? null : run.ShownRefusal);
    }

    private async Task OpenAsync(ProposalCardViewModel? card)
    {
        if (card is null) return;
        if (Context.ProjectPath is null)
        {
            ShownRefusal = WindowRefusal.Plain("Open a project before opening a Proposal.");
            return;
        }
        try
        {
            await Context.OpenProposalAsync(card.ProposalId, CancellationToken.None).ConfigureAwait(true);
            Context.OpenPage(WorkspacePage.Overview);
        }
        catch (InvalidOperationException exception)
        {
            ShownRefusal = WindowRefusal.Failure("window.open-proposal-failed",
                "The Proposal could not be opened. Try again.", exception);
        }
    }

    private async Task TryAgainAsync(ProposalCardViewModel? card)
    {
        if (card is null) return;
        var outcome = await Context.TryAgainAsync(card.ProposalId, CancellationToken.None).ConfigureAwait(true);
        ShownRefusal = outcome.Succeeded || outcome.Refusal is null || Context.TrialRun.StateFor(card.ProposalId).Refusal is not null
            ? null
            : WindowRefusal.From(outcome.Refusal);
    }

    private async Task ShowSetAsideAsync()
    {
        _includeSetAside = !_includeSetAside;
        if (Context.ProjectPath is { } path) await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task DeleteAsync(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanDelete || Context.ProjectPath is not { } path) return;
        var deleted = await Context.Commands.DeleteProposalAsync(new DeleteProposalRequest(path, card.ProposalId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = deleted.Succeeded ? null : WindowRefusal.From(deleted.Refusal!);
        await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }
}
