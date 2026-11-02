using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One of a card's numbers, as "before → after" when the Trial measured it.</summary>
/// <param name="Label">What the number is.</param>
/// <param name="Value">The number, or today's number alone when the Proposal has no Trial values yet.</param>
/// <param name="Moved">Whether the Trial moved the number enough to highlight it.</param>
/// <param name="Note">Why the value is blank, for a tooltip and accessible name; null when it has a value.</param>
public sealed record ProposalNumber(string Label, string Value, bool Moved, string? Note = null);

/// <summary>
/// The one place a card's typed <see cref="Paired{T}"/> values become text, so the swap to the shared paired-number
/// renderer is a single edit.
/// </summary>
internal static class ProposalNumberText
{
    public static IReadOnlyList<ProposalNumber> From(TrialSummary trial) =>
    [
        Number("Coverage", Show(trial.TextCoveragePercent, Percent), trial.TextCoveragePercent.Moved,
            side: Side(trial.TextCoveragePercent, Percent)),
        new("Approved kept", ShowKept(trial), trial.ApprovedKept.Moved || trial.ApprovedTotal.Moved),
        Number("Work", ShowWork(trial.Work), trial.Work is { } work && PairedMoves.Work(work.BaselineWork, work.TrialParserWork),
            MotifAppStrings.ProposalWorkNotComparable),
        Number("Warnings", Show(trial.WarningCount, Count), trial.WarningCount.Moved,
            side: Side(trial.WarningCount, Count)),
        Number("Time on this computer", Show(trial.WordTimeMs, Time), trial.WordTimeMs.Moved,
            side: Side(trial.WordTimeMs, Time)),
    ];

    // A blank number keeps its meaning in a tooltip and an accessible name, never in visible text.
    private static ProposalNumber Number(string label, string value, bool moved, string? blankNote = null,
        PairedSide side = PairedSide.Both) =>
        new(label, value, moved, value.Length == 0 ? blankNote ?? MotifAppStrings.TextsWordNotMeasured
            : PairedSideNote.For(side, value));

    // Parser work is the cost; with no comparison the cell is blank, never a time in their place.
    private static string ShowWork(TrialWork? work) => HasWorkComparison(work)
        ? $"{work!.BaselineWork!.Value.ToString("N0", CultureInfo.CurrentCulture)} → " +
            work.TrialParserWork!.Value.ToString("N0", CultureInfo.CurrentCulture)
        : string.Empty;

    // TrialWorkComparison leaves both totals empty when two tries cannot be compared; the Overview asks the same.
    public static bool HasWorkComparison(TrialWork? work) => work is { BaselineWork: not null, TrialParserWork: not null };

    private static string ShowKept(TrialSummary trial)
    {
        var before = $"{trial.ApprovedKept.Before.ToString(CultureInfo.CurrentCulture)} of " +
            trial.ApprovedTotal.Before.ToString(CultureInfo.CurrentCulture);
        if (!trial.ApprovedKept.HasAfter || !trial.ApprovedTotal.HasAfter) return before;
        return $"{before} → {trial.ApprovedKept.After.ToString(CultureInfo.CurrentCulture)} of " +
            trial.ApprovedTotal.After.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Formats the stored kept counts without their totals for the dense table.</summary>
    public static string ApprovedKeptCounts(TrialSummary trial)
    {
        var before = trial.ApprovedKept.Before.ToString(CultureInfo.CurrentCulture);
        return trial.ApprovedKept.HasAfter
            ? $"{before} → {trial.ApprovedKept.After.ToString(CultureInfo.CurrentCulture)}"
            : before;
    }

    private static string Show<T>(Paired<T> pair, Func<T?, string> format)
    {
        var before = format(pair.Before);
        if (!pair.HasAfter) return before;
        var after = format(pair.After);
        if (before.Length == 0 && after.Length > 0)
            return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PairedProposalOnly, after);
        return after.Length == 0 ? before : $"{before} → {after}";
    }

    private static PairedSide Side<T>(Paired<T> pair, Func<T?, string> format)
    {
        if (!pair.HasAfter) return PairedSide.Both;
        var today = format(pair.Before).Length > 0;
        var proposal = format(pair.After).Length > 0;
        return today == proposal ? PairedSide.Both : today ? PairedSide.TodayOnly : PairedSide.ProposalOnly;
    }

    private static string Percent(double? value) =>
        value is { } percent ? percent.ToString("0.#", CultureInfo.CurrentCulture) + "%" : string.Empty;

    private static string Time(double? milliseconds) => milliseconds is { } ms
        ? ms >= 1000 ? (ms / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " s"
            : ms.ToString("0", CultureInfo.CurrentCulture) + " ms"
        : string.Empty;

    private static string Count(int? value) => value is { } count ? count.ToString(CultureInfo.CurrentCulture) : string.Empty;
}

/// <summary>One Proposal row: its label, what it changes, its numbers and what a person can do with it.</summary>
public sealed class ProposalCardViewModel : ObservableObject
{
    private bool _isSelected;

    public ProposalCardViewModel(ProposalCard card, IReadOnlyDictionary<string, string>? labels = null,
        TrialStanding? standing = null, WindowRefusal? refusal = null, string? baselineNotice = null,
        string? workNotice = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Standing = standing ?? card.Standing;
        ShownRefusal = refusal;
        BaselineNotice = baselineNotice;
        WorkNotice = workNotice;
        Words = TrialStandingWords.For(Standing);
        OpenAutomationId = AutomationIds.ForProposalOpen(card.ProposalId);
        TryAgainAutomationId = AutomationIds.ForProposalTryAgain(card.ProposalId);
        DeleteAutomationId = AutomationIds.ForProposalDelete(card.ProposalId);
        ReopenAutomationId = AutomationIds.ForProposalReopen(card.ProposalId);
        ActionsAutomationId = AutomationIds.ForProposalActions(card.ProposalId);
        DetailsAutomationId = OpenAutomationId + "-details";
        InspectorOpenAutomationId = OpenAutomationId + "-inspector";
        InspectorTryAgainAutomationId = TryAgainAutomationId + "-inspector";
        InspectorDeleteAutomationId = DeleteAutomationId + "-inspector";
        InspectorReopenAutomationId = ReopenAutomationId + "-inspector";
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

    /// <summary>
    /// Why the latest Try again was not compared against a Baseline, in the window's words, or
    /// <see langword="null"/> when it was; shown next to the Trial's result.
    /// </summary>
    public string? BaselineNotice { get; }

    /// <summary>
    /// The note in the window's words that the Trial makes parsing more work than the Baseline, or
    /// <see langword="null"/> when it does not; shown next to the Trial's result.
    /// </summary>
    public string? WorkNotice { get; }

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

    /// <summary>The automation id of this card's Reopen button.</summary>
    public string ReopenAutomationId { get; }

    /// <summary>The automation id of this Proposal row's action menu.</summary>
    public string ActionsAutomationId { get; }

    /// <summary>The automation id of the row's action that opens its page Inspector details.</summary>
    public string DetailsAutomationId { get; }

    /// <summary>The automation id of this Proposal's inspector Open button.</summary>
    public string InspectorOpenAutomationId { get; }

    /// <summary>The automation id of this Proposal's inspector Try again button.</summary>
    public string InspectorTryAgainAutomationId { get; }

    /// <summary>The automation id of this Proposal's inspector Delete button.</summary>
    public string InspectorDeleteAutomationId { get; }

    /// <summary>The automation id of this Proposal's inspector Reopen button.</summary>
    public string InspectorReopenAutomationId { get; }

    /// <summary>Whether the card offers Reopen: a declined or withdrawn Proposal opens again as a Draft.</summary>
    public bool CanReopen => !Card.IsDraft && Card.Status is "rejected" or "withdrawn";

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

    /// <summary>The short status displayed in the dense table.</summary>
    public string TableStatusText => Card.IsPendingDraft ? "Draft"
        : Card.Group == ProposalGroup.SetAside || Standing.Kind == TrialStandingKind.Current
            ? StatusWord
            : Words.Status;

    public bool HasTableStatus => TableStatusText.Length > 0;

    public bool TableStatusIsLook => TableStatusText is "Ready" or "Applied";

    public bool TableStatusIsProblem => TableStatusText is "Needs another try" or "Did not finish";

    /// <summary>Whether the card offers Delete: a declined, superseded or withdrawn Proposal that never applied.</summary>
    public bool CanDelete => !Card.IsDraft && Card.Status is "rejected" or "superseded" or "withdrawn";

    /// <summary>What the Proposal changes in one line, such as "2 rules · 1 allomorph · 3 parses approved".</summary>
    public string KindsText => KindsIn(Card.ChangeKinds);

    /// <summary>The card's numbers in order, the time on this computer last, or empty when the Proposal was never tried.</summary>
    public IReadOnlyList<ProposalNumber> Numbers { get; } = [];

    public bool HasNumbers => Numbers.Count > 0;

    /// <summary>Text Coverage before and after, or null when the Proposal was never tried.</summary>
    public ProposalNumber? Coverage => Numbers.Count > 0 ? Numbers[0] : null;

    /// <summary>Approved words kept before and after.</summary>
    public ProposalNumber? ApprovedKept => Numbers.Count > 1 ? Numbers[1] : null;

    /// <summary>Parser work, in parser work, in the Baseline and in the Trial.</summary>
    public ProposalNumber? Work => Numbers.Count > 2 ? Numbers[2] : null;

    /// <summary>Warning count before and after.</summary>
    public ProposalNumber? Warnings => Numbers.Count > 3 ? Numbers[3] : null;

    /// <summary>The short Approved kept counts for the table, or the existing no-Trial wording.</summary>
    public string TableApprovedKeptText => Card.Trial is { } trial
        ? ProposalNumberText.ApprovedKeptCounts(trial)
        : TrialStandingWords.NotTried.Status;

    /// <summary>The Coverage value for the table, or the existing no-Trial wording.</summary>
    public string TableCoverageText => Coverage?.Value ?? TrialStandingWords.NotTried.Status;

    /// <summary>The Warnings value for the table, or the existing no-Trial wording.</summary>
    public string TableWarningsText => Warnings?.Value ?? TrialStandingWords.NotTried.Status;

    /// <summary>The Work value for the table, or the existing no-Trial wording.</summary>
    public string TableWorkText => Work?.Value ?? TrialStandingWords.NotTried.Status;

    /// <summary>Whether the row's main action should open the Proposal.</summary>
    public bool ShowsOpenAction => !CanReopen;

    /// <summary>Whether the numbers come from an earlier try and should read as old.</summary>
    public bool NumbersAreOld => HasNumbers && Standing is
        { Kind: TrialStandingKind.NeedsAnotherTry, Reason: not TrialStaleReason.DroppedGrammarNotChecked };

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
        TrialStandingKind.NeedsAnotherTry => Words.OldNumbers ?? Words.Sentence,
        TrialStandingKind.NotTried when Card.Group != ProposalGroup.NotTried => Words.Status,
        _ => string.Empty,
    };

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Whether this Proposal supplies the details currently shown beside the list.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(AccessibleRowName));
        }
    }

    /// <summary>The Proposal's accessible name, including its selection state.</summary>
    public string AccessibleRowName => IsSelected ? $"{Label}, selected" : Label;

    /// <summary>The person shown as the author of this row.</summary>
    public string AuthorText => Card.IsPendingDraft ? "You" : "An assistant";

    /// <summary>The status shown beside the Proposal details.</summary>
    public string DetailsStatusText => Card.IsPendingDraft ? "Not applied yet" : StatusWord;

    /// <summary>The number of changes listed for this Proposal.</summary>
    public string ChangeCountText
    {
        get
        {
            var count = Card.ChangeKinds.Sum(kind => kind.Count);
            return PluralText.Format(MotifAppStrings.ChangeCountPattern, count);
        }
    }

    internal void SetSelected(bool selected) => IsSelected = selected;

    /// <summary>Joins change kinds into the one-line wording the page and tests share.</summary>
    public static string KindsIn(IReadOnlyList<ChangeKindCount> kinds) =>
        string.Join(" · ", kinds.Select(kind => kind.Text));
}

/// <summary>One titled group of Proposal rows.</summary>
/// <param name="Group">Which group this is.</param>
/// <param name="Title">The group's heading.</param>
/// <param name="Cards">The group's Proposals.</param>
public sealed record ProposalGroupViewModel(ProposalGroup Group, string Title, IReadOnlyList<ProposalCardViewModel> Cards);

/// <summary>One of the three groups available in the Proposals page filter.</summary>
public enum ProposalListFilter
{
    /// <summary>The person's own changes that have not been applied to FieldWorks.</summary>
    YourChangesNotAppliedYet,

    /// <summary>Proposals waiting for a person, including those not tried or needing another try.</summary>
    WaitingForYou,

    /// <summary>Applied, declined, superseded or withdrawn Proposals.</summary>
    Closed,
}

/// <summary>A top-level Proposals page group and its rows.</summary>
/// <param name="Filter">The group identity used by the page's filters.</param>
/// <param name="Title">The heading shown above the rows.</param>
/// <param name="Cards">The Proposals in this group, in standings order.</param>
public sealed record ProposalListGroupViewModel(
    ProposalListFilter Filter, string Title, IReadOnlyList<ProposalCardViewModel> Cards);

/// <summary>The label, count and action for one Proposals page filter.</summary>
/// <param name="Filter">The group to show.</param>
/// <param name="Label">The words shown for the group.</param>
/// <param name="Count">How many Proposals the group contains.</param>
/// <param name="IsActive">Whether this filter currently limits the list.</param>
/// <param name="SelectCommand">The action that selects or clears this filter.</param>
public sealed record ProposalGroupFilterViewModel(
    ProposalListFilter Filter, string Label, int Count, bool IsActive, ICommand SelectCommand)
{
    /// <summary>Whether the filter can show any rows or is active and can be cleared.</summary>
    public bool CanSelect => Count > 0 || IsActive;
}

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
    private bool _rereading;
    private string? _differenceProposalId;
    private string? _differenceProject;
    private bool _includeSetAside;
    private ProposalStandings _standings = new([], 0);

    public ProposalsPageModel(WorkspaceContext context) : this(context, null)
    {
    }

    // Tests pass their own wait between two Parsimony reads so no clock decides a result.
    internal ProposalsPageModel(WorkspaceContext context, Func<TimeSpan, CancellationToken, Task>? parsimonyDelay)
        : base(context)
    {
        OpenCommand = new AsyncRelayCommand<ProposalCardViewModel>(OpenAsync);
        OpenInReviewCommand = new AsyncRelayCommand<ProposalCardViewModel>(OpenInReviewAsync);
        TryAgainCommand = new AsyncRelayCommand<ProposalCardViewModel>(TryAgainAsync);
        ShowSetAsideCommand = new AsyncRelayCommand(ShowSetAsideAsync);
        ClearGroupFilterCommand = new RelayCommand(ClearGroupFilter);
        DeleteCommand = new AsyncRelayCommand<ProposalCardViewModel>(DeleteAsync);
        AskToDeleteCommand = new RelayCommand<ProposalCardViewModel>(AskToDelete);
        KeepProposalCommand = new RelayCommand(() => SetDeleteQuestion(null));
        ReopenCommand = new AsyncRelayCommand<ProposalCardViewModel>(ReopenAsync);
        SelectGroupFilterCommand = new AsyncRelayCommand<ProposalListFilter>(SelectGroupFilterAsync);
        SelectProposalCommand = new RelayCommand<ProposalCardViewModel>(SelectProposal);
        Difference = new ProposalDifferenceViewModel(
            (request, cancellationToken) => Context.Commands.ReadProposalDifferenceAsync(request, cancellationToken),
            word => Context.OpenWord(word),
            (request, progress, cancellationToken) =>
                Context.Commands.ProduceProposalDifferenceAsync(request, progress, cancellationToken),
            () => [.. Context.Selection.AllTexts.Select(text => new DifferenceTextChoice(text.Id, text.Title))]);
        Parsimony = new ProposalParsimonyViewModel(
            (request, cancellationToken) => Context.Commands.ReadProposalParsimonyStatusAsync(request, cancellationToken),
            parsimonyDelay);
        Context.Selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectionViewModel.TextCount)) Difference.RefreshTexts();
        };
        Context.TrialRun.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrialRunViewModel.States) && State == ProposalsPageState.Listed)
                Apply(_standings);
        };
    }

    /// <summary>The groups that have Proposals, in display order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupFilters), nameof(ListGroups), nameof(TotalProposalCountText))]
    private IReadOnlyList<ProposalGroupViewModel> _groups = [];

    /// <summary>The page's dense table: each shared heading row followed by its Proposals.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilteredEmpty), nameof(IsNoProposalsToShow), nameof(ShowSelectProposalPrompt))]
    private IReadOnlyList<object> _rows = [];

    /// <summary>The selected Proposal whose details and actions appear beside the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProposal), nameof(ShowSelectProposalPrompt),
        nameof(SelectedOpenAutomationId), nameof(SelectedTryAgainAutomationId), nameof(SelectedReopenAutomationId),
        nameof(SelectedDeleteAutomationId))]
    private ProposalCardViewModel? _selectedProposal;

    /// <summary>The selected Proposal's saved Difference, or the offer to make one.</summary>
    public ProposalDifferenceViewModel Difference { get; }

    /// <summary>The selected Proposal's automatic Parsimony advice, kept current while its job runs.</summary>
    public ProposalParsimonyViewModel Parsimony { get; }

    /// <summary>The watch of the selected Proposal's Parsimony status that is under way, or the last one that ended.</summary>
    public Task ParsimonyWatching { get; private set; } = Task.CompletedTask;

    /// <summary>The read of the selected Proposal's Difference that is under way, or the last one that finished.</summary>
    public Task DifferenceReading { get; private set; } = Task.CompletedTask;

    /// <summary>The Inspector's Open in Review changes id for the selected Proposal.</summary>
    public string? SelectedOpenAutomationId => SelectedProposal?.InspectorOpenAutomationId;

    /// <summary>The Inspector's try-again id for the selected Proposal.</summary>
    public string? SelectedTryAgainAutomationId => SelectedProposal?.InspectorTryAgainAutomationId;

    /// <summary>The Inspector's Reopen id for the selected Proposal.</summary>
    public string? SelectedReopenAutomationId => SelectedProposal?.InspectorReopenAutomationId;

    /// <summary>The Inspector's Delete id for the selected Proposal.</summary>
    public string? SelectedDeleteAutomationId => SelectedProposal?.InspectorDeleteAutomationId;

    /// <summary>The group limiting the list, or null when all available groups are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupFilters), nameof(IsFilteredEmpty), nameof(IsNoProposalsToShow),
        nameof(FilterEmptyText), nameof(ListGroups))]
    private ProposalListFilter? _selectedGroupFilter;

    /// <summary>How many stored Proposals could not be read, as a sentence; empty when all could be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadable))]
    private string _unreadableText = string.Empty;

    /// <summary>The Proposals that could not be read, by name and id; empty when all could be read.</summary>
    [ObservableProperty]
    private string _unreadableNamesText = string.Empty;

    /// <summary>Whether some stored Proposals could not be read.</summary>
    public bool HasUnreadable => IsListed && UnreadableText.Length > 0;

    /// <summary>The words of the link that loads applied and set-aside Proposals.</summary>
    [ObservableProperty]
    private string _setAsideLinkText = string.Empty;

    /// <summary>What the page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsEmpty), nameof(IsListed), nameof(IsRefused),
        nameof(TotalProposalCountText), nameof(IsFilteredEmpty), nameof(IsNoProposalsToShow), nameof(ShowSelectProposalPrompt),
        nameof(HasSetAsideLink), nameof(HasSelectedProposal), nameof(ListGroups), nameof(HasUnreadable))]
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

    /// <summary>Whether the read was refused.</summary>
    public bool IsRefused => State == ProposalsPageState.Refused;

    /// <summary>Whether a selected Proposal's details and actions are available.</summary>
    public bool HasSelectedProposal => IsListed && SelectedProposal is not null;

    /// <summary>Whether the current group filter leaves no Proposals in the list.</summary>
    public bool IsFilteredEmpty => IsListed && SelectedGroupFilter is not null &&
        !Rows.OfType<ProposalCardViewModel>().Any();

    /// <summary>Whether the listed page has no open Proposals and no filter is limiting the list.</summary>
    public bool IsNoProposalsToShow => IsListed && SelectedGroupFilter is null &&
        !Rows.OfType<ProposalCardViewModel>().Any();

    /// <summary>Whether the inspector should ask the person to select a Proposal.</summary>
    public bool ShowSelectProposalPrompt => IsListed && SelectedProposal is null &&
        !IsFilteredEmpty && !IsNoProposalsToShow;

    /// <summary>The empty-result message for the selected group.</summary>
    public string FilterEmptyText => SelectedGroupFilter switch
    {
        ProposalListFilter.YourChangesNotAppliedYet => "Nothing in Your changes not applied yet.",
        ProposalListFilter.WaitingForYou => "Nothing in Waiting for you.",
        ProposalListFilter.Closed => "Nothing in Closed.",
        _ => "Nothing in this group.",
    };

    /// <summary>Whether the link to applied and set-aside Proposals shows.</summary>
    public bool HasSetAsideLink => IsListed && SetAsideLinkText.Length > 0;

    public IAsyncRelayCommand<ProposalCardViewModel> OpenCommand { get; }

    /// <summary>Opens the selected Proposal in Review changes.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> OpenInReviewCommand { get; }

    public IAsyncRelayCommand<ProposalCardViewModel> TryAgainCommand { get; }

    /// <summary>Selects a group or clears it when it is already selected.</summary>
    public IAsyncRelayCommand<ProposalListFilter> SelectGroupFilterCommand { get; }

    /// <summary>Shows the selected Proposal's details and actions.</summary>
    public IRelayCommand<ProposalCardViewModel> SelectProposalCommand { get; }

    /// <summary>Shows or hides the closed Proposals: applied, declined, superseded and withdrawn.</summary>
    public IAsyncRelayCommand ShowSetAsideCommand { get; }

    /// <summary>Clears the active group filter and shows every available Proposal.</summary>
    public IRelayCommand ClearGroupFilterCommand { get; }

    /// <summary>Deletes a closed Proposal that never applied, with its Trials, and reads the list again.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> DeleteCommand { get; }

    /// <summary>Asks whether to delete a Proposal; nothing is deleted until <see cref="DeleteCommand"/> runs.</summary>
    public IRelayCommand<ProposalCardViewModel> AskToDeleteCommand { get; }

    /// <summary>Withdraws the question about deleting the selected Proposal and keeps it.</summary>
    public IRelayCommand KeepProposalCommand { get; }

    /// <summary>Whether the selected Proposal's deletion is waiting for an answer.</summary>
    public bool IsConfirmingDelete => SelectedProposal is { } selected && selected.ProposalId == _deleteQuestionId;

    /// <summary>The question about deleting the selected Proposal, which names it.</summary>
    public string DeleteQuestion => SelectedProposal is { } selected
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DeleteProposalQuestionFormat, selected.Label)
        : "";

    /// <summary>Opens a declined or withdrawn Proposal again as a Draft and reads the list again.</summary>
    public IAsyncRelayCommand<ProposalCardViewModel> ReopenCommand { get; }

    /// <summary>The three filter groups and their current counts.</summary>
    public IReadOnlyList<ProposalGroupFilterViewModel> GroupFilters =>
    [
        Filter(ProposalListFilter.YourChangesNotAppliedYet, "Your changes not applied yet",
            CountFor(ProposalListFilter.YourChangesNotAppliedYet)),
        Filter(ProposalListFilter.WaitingForYou, "Waiting for you",
            CountFor(ProposalListFilter.WaitingForYou)),
        Filter(ProposalListFilter.Closed, "Closed", CountFor(ProposalListFilter.Closed)),
    ];

    /// <summary>The three filter groups with their rows, in standings order.</summary>
    public IReadOnlyList<ProposalListGroupViewModel> ListGroups
    {
        get
        {
            if (!IsListed) return [];
            var cards = Groups.SelectMany(group => group.Cards)
                .ToDictionary(card => card.ProposalId, StringComparer.Ordinal);
            return new[]
                {
                    ListGroup(ProposalListFilter.YourChangesNotAppliedYet, "Your changes not applied yet", cards),
                    ListGroup(ProposalListFilter.WaitingForYou, "Waiting for you", cards),
                    ListGroup(ProposalListFilter.Closed, "Closed", cards),
                }
                .Where(group => group.Cards.Count > 0 &&
                    (SelectedGroupFilter is null || SelectedGroupFilter == group.Filter))
                .ToArray();
        }
    }

    /// <summary>The number of listed and closed Proposals, in the page's words.</summary>
    public string TotalProposalCountText
    {
        get
        {
            if (!IsListed) return string.Empty;
            var count = Groups.Sum(group => group.Cards.Count) + (_includeSetAside ? 0 : _standings.SetAsideCount);
            return PluralText.Format(MotifAppStrings.ProposalCountPattern, count);
        }
    }

    private ProposalGroupFilterViewModel Filter(ProposalListFilter filter, string label, int count) =>
        new(filter, label, count, SelectedGroupFilter == filter, SelectGroupFilterCommand);

    private int CountFor(ProposalListFilter filter) => filter switch
    {
        ProposalListFilter.YourChangesNotAppliedYet =>
            _standings.Proposals.Count(card => card.Group == ProposalGroup.NotAppliedYet),
        ProposalListFilter.WaitingForYou => _standings.Proposals.Count(card => card.Group is ProposalGroup.WaitingForYou or
            ProposalGroup.NeedsAnotherTry or ProposalGroup.NotTried),
        ProposalListFilter.Closed => _standings.SetAsideCount,
        _ => 0,
    };

    private ProposalListGroupViewModel ListGroup(ProposalListFilter filter, string title,
        IReadOnlyDictionary<string, ProposalCardViewModel> cards)
    {
        var groupCards = _standings.Proposals.Where(card => IsInFilter(card.Group, filter))
            .Select(card => cards[card.ProposalId]).ToArray();
        return new ProposalListGroupViewModel(filter, title, groupCards);
    }

    private static bool IsInFilter(ProposalGroup group, ProposalListFilter filter) => filter switch
    {
        ProposalListFilter.YourChangesNotAppliedYet => group == ProposalGroup.NotAppliedYet,
        ProposalListFilter.WaitingForYou => group is ProposalGroup.WaitingForYou or
            ProposalGroup.NeedsAnotherTry or ProposalGroup.NotTried,
        ProposalListFilter.Closed => group == ProposalGroup.SetAside,
        _ => false,
    };

    partial void OnSetAsideLinkTextChanged(string value) => OnPropertyChanged(nameof(HasSetAsideLink));

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        _rereading = false;
        _includeSetAside = false;
        SelectedGroupFilter = null;
        SetSelectedProposal(null);
        ClearDifference();
        Parsimony.Stop();
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
        State = ProposalsPageState.Loading;
        ShownRefusal = null;
        var keepSelected = SelectedProposal?.ProposalId;
        _rereading = true;
        SetSelectedProposal(null);
        Rows = [];
        try
        {
            var read = await Context.Commands.ProposalStandingsAsync(
                new ProposalStandingsRequest(projectPath, _includeSetAside), cancellationToken).ConfigureAwait(true);
            if (generation != _readGeneration || projectPath != Context.ProjectPath) return;
            if (!read.Succeeded)
            {
                _includeSetAside = false;
                SelectedGroupFilter = null;
                Apply(new ProposalStandings([], 0));
                ShownRefusal = WindowRefusal.From(read.Refusal!, "The Proposals could not be read.");
                State = ProposalsPageState.Refused;
                return;
            }
            ShownRefusal = null;
            Apply(read.Value!, keepSelected);
        }
        finally
        {
            if (generation == _readGeneration) EndReread();
        }
    }

    // A re-read deselects first, so it names the Proposal to keep selected when the list comes back.
    private void Apply(ProposalStandings standings, string? keepSelectedId = null)
    {
        var selectedId = SelectedProposal?.ProposalId ?? keepSelectedId;
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
        State = cards.Length == 0 && standings.SetAsideCount == 0 && standings.Unreadable.Count == 0
            ? ProposalsPageState.Empty : ProposalsPageState.Listed;
        UnreadableText = standings.Unreadable.Count == 0 ? string.Empty
            : PluralText.Format(MotifAppStrings.UnreadableProposalsPattern, standings.Unreadable.Count);
        UnreadableNamesText = standings.Unreadable.Count == 0 ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.UnreadableProposalsNamesFormat,
                string.Join(", ", standings.Unreadable.Select(item =>
                    item.Label == item.ProposalId ? item.ProposalId : $"{item.Label} ({item.ProposalId})")));
        UpdateRows(selectedId);
        SetAsideLinkText = _includeSetAside ? "Hide closed"
            : standings.SetAsideCount == 0 ? string.Empty
            : $"Show closed ({standings.SetAsideCount.ToString(CultureInfo.CurrentCulture)})";
        var waiting = cards.Count(card => card.Card.Group == ProposalGroup.WaitingForYou);
        BadgeCount = waiting == 0 ? null : waiting;
    }

    private ProposalCardViewModel CardFor(ProposalCard card, IReadOnlyDictionary<string, string> labels)
    {
        var run = Context.TrialRun.StateFor(card.ProposalId);
        var standing = run.IsRunning ? TrialStanding.Trying : card.Standing;
        return new ProposalCardViewModel(card, labels, standing, run.IsRunning ? null : run.ShownRefusal,
            run.IsRunning ? null : run.BaselineNotice,
            run.IsRunning ? null : run.JobId is not null && run.JobId == card.Trial?.JobId
                ? run.WorkNotice : TrialWorkNotice.For(card.Trial?.Work));
    }

    private async Task SelectGroupFilterAsync(ProposalListFilter filter)
    {
        ProposalListFilter? nextFilter = SelectedGroupFilter == filter ? null : filter;
        if (nextFilter is { } requested && CountFor(requested) == 0) return;
        SelectedGroupFilter = nextFilter;
        if (SelectedGroupFilter == ProposalListFilter.Closed && !_includeSetAside)
        {
            _includeSetAside = true;
            if (Context.ProjectPath is { } path)
                await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
            return;
        }

        UpdateRows(SelectedProposal?.ProposalId);
    }

    private void ClearGroupFilter()
    {
        SelectedGroupFilter = null;
        UpdateRows(SelectedProposal?.ProposalId);
    }

    private void SelectProposal(ProposalCardViewModel? card)
    {
        if (card is null || !Groups.Any(group => group.Cards.Contains(card))) return;
        SetSelectedProposal(card);
    }

    private void UpdateRows(string? selectedId)
    {
        var visibleGroups = ListGroups;
        Rows = visibleGroups.SelectMany(group => new object[]
            {
                new DenseTableGroup(group.Title, ProposalCountText(group.Cards.Count)),
            }
            .Concat(group.Cards)).ToArray();
        var visibleCards = visibleGroups.SelectMany(group => group.Cards).ToArray();
        var selected = visibleCards.FirstOrDefault(card => card.ProposalId == selectedId) ??
            visibleCards.FirstOrDefault(card => !card.Card.IsPendingDraft) ?? visibleCards.FirstOrDefault();
        SetSelectedProposal(selected);
    }

    private static string ProposalCountText(int count) =>
        PluralText.Format(MotifAppStrings.ProposalCountPattern, count);

    private void SetSelectedProposal(ProposalCardViewModel? proposal)
    {
        if (ReferenceEquals(SelectedProposal, proposal)) return;
        SelectedProposal = proposal;
    }

    partial void OnSelectedProposalChanging(ProposalCardViewModel? value) => _selectedProposal?.SetSelected(false);

    partial void OnSelectedProposalChanged(ProposalCardViewModel? value)
    {
        value?.SetSelected(true);
        if (value?.ProposalId != _deleteQuestionId) SetDeleteQuestion(null);
        else OnPropertyChanged(nameof(IsConfirmingDelete));
        OnPropertyChanged(nameof(DeleteQuestion));
        if (value is null || value.Card.IsPendingDraft || Context.ProjectPath is not { } path)
        {
            // A re-read of the list deselects for a moment; it settles the Difference when it finishes.
            if (!_rereading) ClearDifference();
            // Only the brief empty selection of a re-read keeps a watch; any other end closes it.
            if (value is not null || !_rereading) Parsimony.Stop();
            return;
        }
        ParsimonyWatching = Parsimony.Watch(path, value.ProposalId);
        // The same Proposal chosen again, as when the list was re-read, only refreshes what its card offers.
        if (value.ProposalId == _differenceProposalId && path == _differenceProject)
        {
            var offer = DifferenceOfferFor(value.Card);
            // Its word detail goes when it is decided, so a change of that state reads again, never mid-run.
            if (Difference.IsDecidedChangedBy(offer) && !Difference.Maker.IsActive)
                DifferenceReading = Difference.LoadAsync(path, value.ProposalId, offer: offer);
            else
                Difference.SetOffer(offer);
            return;
        }
        _differenceProposalId = value.ProposalId;
        _differenceProject = path;
        DifferenceReading = Difference.LoadAsync(path, value.ProposalId, offer: DifferenceOfferFor(value.Card));
    }

    private void ClearDifference()
    {
        _differenceProposalId = _differenceProject = null;
        Difference.Clear();
        DifferenceReading = Task.CompletedTask;
    }

    // Ends a re-read of the list: a Proposal no longer there leaves no Difference to show.
    private void EndReread()
    {
        _rereading = false;
        if (SelectedProposal is null)
        {
            ClearDifference();
            Parsimony.Stop();
        }
    }

    protected override Task OnStopWorkAsync()
    {
        Parsimony.Stop();
        return Difference.StopAsync();
    }

    // Decided keeps no new Difference, untried has no Trial, and deferred can make its words again.
    private static DifferenceOffer DifferenceOfferFor(ProposalCard card) =>
        card.Group == ProposalGroup.SetAside ? DifferenceOffer.Decided
        : card.Standing.Kind == TrialStandingKind.NotTried ? DifferenceOffer.NotTried
        : card.Status == "deferred" ? DifferenceOffer.Deferred
        : DifferenceOffer.Available;

    private Task OpenAsync(ProposalCardViewModel? card) => OpenProposalAsync(card, WorkspacePage.Overview);

    private Task OpenInReviewAsync(ProposalCardViewModel? card) => OpenProposalAsync(card, WorkspacePage.Review);

    private async Task OpenProposalAsync(ProposalCardViewModel? card, WorkspacePage page)
    {
        if (card is null) return;
        if (Context.ProjectPath is null)
        {
            ShownRefusal = WindowRefusal.Plain("Open a project before opening a Proposal.");
            return;
        }
        try
        {
            await Context.OpenProposalAsync(card.ProposalId, CancellationToken.None, card.Label).ConfigureAwait(true);
            Context.OpenPage(page);
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
        SelectedGroupFilter = null;
        if (Context.ProjectPath is { } path) await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task ReopenAsync(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanReopen || Context.ProjectPath is not { } path) return;
        var reopened = await Context.Commands.ReopenProposalAsync(new ReopenProposalRequest(path, card.ProposalId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = reopened.Succeeded ? null : WindowRefusal.From(reopened.Refusal!);
        await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }

    private string? _deleteQuestionId;

    private void AskToDelete(ProposalCardViewModel? card)
    {
        if (card is null || !card.CanDelete) return;
        SetSelectedProposal(card);
        SetDeleteQuestion(card.ProposalId);
    }

    private void SetDeleteQuestion(string? proposalId)
    {
        _deleteQuestionId = proposalId;
        OnPropertyChanged(nameof(IsConfirmingDelete));
        OnPropertyChanged(nameof(DeleteQuestion));
    }

    private async Task DeleteAsync(ProposalCardViewModel? card)
    {
        SetDeleteQuestion(null);
        if (card is null || !card.CanDelete || Context.ProjectPath is not { } path) return;
        var deleted = await Context.Commands.DeleteProposalAsync(new DeleteProposalRequest(path, card.ProposalId),
            CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        ShownRefusal = deleted.Succeeded ? null : WindowRefusal.From(deleted.Refusal!);
        await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }
}
