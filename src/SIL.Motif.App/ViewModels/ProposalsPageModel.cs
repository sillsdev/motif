using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

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
        Number(MotifAppStrings.ProposalWork, ShowWork(trial.Work), trial.Work is { } work && PairedMoves.Work(work.BaselineWork, work.TrialParserWork),
            MotifAppStrings.ProposalWorkNotComparable),
        Number("Warnings", Show(trial.WarningCount, Count), trial.WarningCount.Moved,
            side: Side(trial.WarningCount, Count)),
    ];

    // A blank number keeps its meaning in a tooltip and an accessible name, never in visible text.
    private static ProposalNumber Number(string label, string value, bool moved, string? blankNote = null,
        PairedSide side = PairedSide.Both) =>
        new(label, value, moved, value.Length == 0 ? blankNote ?? MotifAppStrings.TextsWordNotMeasured
            : PairedSideNote.For(side, value));

    // Parser work reads as a percent against the Baseline, never two totals; blank with no comparison.
    private static string ShowWork(TrialWork? work)
    {
        if (!HasWorkComparison(work)) return string.Empty;
        var text = ProposalParsimonyText.ParserWork(work);
        return text == MotifAppStrings.ProposalWorkNotComparable ? string.Empty : text;
    }

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

    private static string Count(int? value) => value is { } count ? count.ToString(CultureInfo.CurrentCulture) : string.Empty;
}

/// <summary>One Proposal row: its label, what it changes, its numbers and what a person can do with it.</summary>
public sealed class ProposalCardViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isUnread;
    private bool _isCheckedForComparison;
    internal Func<ProposalCardViewModel, bool, bool>? CheckComparison { get; set; }

    public bool IsCheckedForComparison
    {
        get => _isCheckedForComparison;
        set
        {
            if (_isCheckedForComparison == value) return;
            if (CheckComparison?.Invoke(this, value) == false)
            {
                OnPropertyChanged();
                return;
            }
            SetProperty(ref _isCheckedForComparison, value);
        }
    }
    internal void SetComparisonChecked(bool value) =>
        SetProperty(ref _isCheckedForComparison, value, nameof(IsCheckedForComparison));
    public string ComparisonAccessibleName => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.ProposalCheckForComparison, Label);
    private readonly TimeProvider _clock;

    public ProposalCardViewModel(ProposalCard card, IReadOnlyDictionary<string, string>? labels = null,
        TrialStanding? standing = null, WindowRefusal? refusal = null, string? baselineNotice = null,
        string? workNotice = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        _clock = clock ?? TimeProvider.System;
        Card = card;
        _isUnread = card.IsUnread;
        Standing = standing ?? card.Standing;
        ShownRefusal = refusal;
        BaselineNotice = baselineNotice;
        WorkNotice = workNotice;
        Words = TrialStandingWords.For(Standing);
        OpenAutomationId = AutomationIds.ForProposalOpen(card.ProposalId);
        TryAgainAutomationId = AutomationIds.ForProposalTryAgain(card.ProposalId);
        DeleteAutomationId = AutomationIds.ForProposalDelete(card.ProposalId);
        ReopenAutomationId = AutomationIds.ForProposalReopen(card.ProposalId);
        AskAgainAutomationId = AutomationIds.ForProposalAskAgain(card.ProposalId);
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

    /// <summary>The title shown for this Proposal; an unnamed one reads as Untitled, never as its raw id.</summary>
    public string Label => ProposalTitle.For(Card.Label, Card.ProposalId, Card.IsDraft || Card.IsPendingDraft);

    /// <summary>Whether this Proposal has not been viewed on the Proposals page.</summary>
    public bool IsUnread
    {
        get => _isUnread;
        private set => SetProperty(ref _isUnread, value);
    }

    internal void MarkRead() => IsUnread = false;

    /// <summary>The automation id of this row.</summary>
    public string RowAutomationId => AutomationIds.ForProposalRow(Card.ProposalId);

    /// <summary>The automation id of this card's Open button.</summary>
    public string OpenAutomationId { get; }

    /// <summary>The identifier of the Proposal lens action in the row menu.</summary>
    public string MenuOpenAutomationId => OpenAutomationId + "-menu";

    /// <summary>The automation id of this card's Try again button.</summary>
    public string TryAgainAutomationId { get; }

    /// <summary>The automation id of this card's Delete button.</summary>
    public string DeleteAutomationId { get; }

    /// <summary>The automation id of this card's Reopen button.</summary>
    public string ReopenAutomationId { get; }

    public string AskAgainAutomationId { get; }

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

    /// <summary>Whether this open Proposal can be declined.</summary>
    public bool CanDecide => !Card.IsPendingDraft && (Card.IsDraft || Card.Status is "proposed" or "deferred");

    /// <summary>Whether a Ready Proposal can be left for a later decision.</summary>
    public bool CanDecideLater => !Card.IsPendingDraft && !Card.IsDraft && Card.Status == "proposed";
    /// <summary>Whether the card offers Ask Again: a declined Proposal that answered a Request.</summary>
    public bool CanAskAgain => !Card.IsDraft && Card.Status == "rejected" && Card.RequestId is not null;

    /// <summary>
    /// The Proposal's status in the window's words: Draft, Ready, Deferred, Applied, Declined, Superseded or Withdrawn.
    /// </summary>
    public string StatusWord => (Card.IsPendingDraft || Card.IsDraft) &&
        Card.Status is not ("applied" or "rejected" or "superseded" or "withdrawn")
        ? MotifAppStrings.ProposalDraft : Card.Status switch
    {
        "proposed" => MotifAppStrings.ProposalReady,
        "deferred" => MotifAppStrings.ProposalDeferred,
        "applied" => MotifAppStrings.ProposalApplied,
        "rejected" => MotifAppStrings.ProposalDeclined,
        "superseded" => MotifAppStrings.ProposalSuperseded,
        "withdrawn" => MotifAppStrings.ProposalWithdrawn,
        _ => string.Empty,
    };

    public bool HasStatusWord => StatusWord.Length > 0;

    /// <summary>The short status displayed in the dense table.</summary>
    public string TableStatusText => Card.IsPendingDraft ? "Draft"
        : Card.Group == ProposalGroup.SetAside || Standing.Kind == TrialStandingKind.Current
            ? StatusWord
            : Words.Status;

    public bool HasTableStatus => TableStatusText.Length > 0;

    public bool TableStatusIsLook => TableStatusText is "Applied";

    public bool TableStatusIsProblem => TableStatusText is "Needs another try" or "Did not finish";

    /// <summary>Whether the card offers Delete: a declined, superseded or withdrawn Proposal that never applied.</summary>
    public bool CanDelete => !Card.IsDraft && Card.Status is "rejected" or "superseded" or "withdrawn";

    /// <summary>What the Proposal changes in one line, such as "2 rules · 1 allomorph · 3 parses approved".</summary>
    public string KindsText => KindsIn(Card.ChangeKinds) is { } kinds && kinds != ChangeCountText ? kinds : string.Empty;

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

    /// <summary>
    /// Whether opening the Proposal is the primary action when no Trial or Reopen action is available.
    /// </summary>
    public bool IsOpenPrimary => !CanTry && !CanReopen;

    /// <summary>Whether Reopen is the one Primary button: a closed Proposal's next step is to work on it again.</summary>
    public bool IsReopenPrimary => !CanTry && CanReopen;

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

    /// <summary>The latest Trial's verdict, or its standing when no comparable result exists.</summary>
    public string VerdictText => Card.Trial is
        { ApprovedKept.HasAfter: true, ApprovedTotal.HasBefore: true, LostApprovedKnown: true } trial
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalVerdictFormat,
            trial.ApprovedKept.After, trial.ApprovedTotal.Before,
            trial.LostApproved == 0 ? MotifAppStrings.ProposalLosesNone : trial.LostApproved.ToString(CultureInfo.CurrentCulture))
        : Words.Sentence;

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
    public string AccessibleRowName => IsSelected ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalSelectedNameFormat, Label) : Label;

    /// <summary>The person shown as the author of this row.</summary>
    public string AuthorText => Card.IsPendingDraft ? MotifAppStrings.ProposalBuilderYou :
        string.IsNullOrWhiteSpace(Card.Builder) ? MotifAppStrings.ProposalBuilderUnknown : Card.Builder;

    /// <summary>Labels the recorded builder in the Proposal header.</summary>
    public string BuiltByText => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalBuiltByFormat, AuthorText);

    /// <summary>The time since the earliest available finalized revision.</summary>
    public string AgeText
    {
        get
        {
            if (Card.FirstFinalizedUtc is not { } created) return MotifAppStrings.ProposalAgeUnknown;
            var days = Math.Max(0, (int)(_clock.GetUtcNow() - created).TotalDays);
            return days == 0 ? MotifAppStrings.ProposalAgeToday :
                PluralText.Format(MotifAppStrings.ProposalAgeDaysPattern, days);
        }
    }

    /// <summary>The decision facts shown in a Proposal row.</summary>
    public string DecisionFacts => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDecisionFactsFormat,
        AuthorText, ChangeCountText, VerdictText, AgeText);

    /// <summary>The earliest finalized revision date, when one exists.</summary>
    public string FinalizedText => Card.FirstFinalizedUtc is { } created
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalFinalizedFormat,
            WindowTimeText.FormatWithDate(created, _clock))
        : MotifAppStrings.ProposalAgeUnknown;

    /// <summary>Whether the Proposal names who built it; a line with no value is not shown.</summary>
    public bool HasBuilder => Card.IsPendingDraft || !string.IsNullOrWhiteSpace(Card.Builder);

    /// <summary>Whether the builder recorded a model.</summary>
    public bool HasModel => !string.IsNullOrWhiteSpace(Card.Model);

    /// <summary>Whether the builder recorded a question.</summary>
    public bool HasQuestion => !string.IsNullOrWhiteSpace(Card.Question);

    /// <summary>The model the builder recorded, or a clear absence.</summary>
    public string ModelText => string.IsNullOrWhiteSpace(Card.Model) ? MotifAppStrings.ProposalModelUnknown :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalModelFormat, Card.Model);

    /// <summary>The question the builder recorded, or a clear absence.</summary>
    public string QuestionText => string.IsNullOrWhiteSpace(Card.Question) ? MotifAppStrings.ProposalQuestionUnknown :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalQuestionFormat, Card.Question);

    /// <summary>The saved reading state of the Rationale.</summary>
    public string RationaleStatus => !Card.HasRationale ? MotifAppStrings.ProposalRationaleMissing :
        Card.RationaleNew ? MotifAppStrings.ProposalUnread : MotifAppStrings.ProposalReadState;

    /// <summary>Whether the Proposal has been applied, without predicting Apply readiness.</summary>
    public string AppliedStatus => Card.Status == "applied" ? MotifAppStrings.ProposalApplied :
        MotifAppStrings.ProposalNotApplied;

    /// <summary>The status shown beside the Proposal details.</summary>
    public string DetailsStatusText => Card.IsPendingDraft ? "Not applied yet" : StatusWord;

    /// <summary>Explains a Ready Proposal's status and any reason its numbers need another try.</summary>
    public string DetailsStatusSentence => StatusWord != "Ready" ? string.Empty :
        Standing.Kind == TrialStandingKind.Current ? MotifAppStrings.ProposalReadyExplanation : Words.Sentence;

    public bool HasDetailsStatusSentence => DetailsStatusSentence.Length > 0;

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
/// Groups Proposal standings and retains independently open documents. Apply preparation activates a Proposal
/// through the workspace, and Try Again uses the workspace's shared Trial command.
/// </summary>
public sealed partial class ProposalsPageModel : PageModel
{
    private int _readGeneration;
    private string? _seenChangesSignature;
    private bool _rereading;
    private ProposalCard? _rereadCard;
    private bool _includeSetAside;
    private bool _retainedClosedCard;
    private string? _retainedRowId;
    private ProposalListFilter? _pausedGroupFilter;
    private readonly HashSet<RationaleEditorModel> _pausedEditors = [];
    private ProposalStandings _standings = new([], 0);

    public ProposalsPageModel(WorkspaceContext context) : this(context, null)
    {
    }

    // Tests pass their own wait between two Parsimony reads so no clock decides a result.
    internal ProposalsPageModel(WorkspaceContext context, Func<TimeSpan, CancellationToken, Task>? parsimonyDelay)
        : base(context)
    {
        Documents = new OpenProposalDocuments(context, parsimonyDelay);
        Documents.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedDocument));
        OpenCommand = new AsyncRelayCommand<ProposalCardViewModel>(OpenAsync);
        OpenInReviewCommand = new AsyncRelayCommand<ProposalCardViewModel>(OpenInReviewAsync);
        TryAgainCommand = new AsyncRelayCommand<ProposalCardViewModel>(TryAgainAsync);
        ShowSetAsideCommand = new AsyncRelayCommand(ShowSetAsideAsync);
        ClearGroupFilterCommand = new RelayCommand(ClearGroupFilter);
        DeleteCommand = new AsyncRelayCommand<ProposalCardViewModel>(DeleteAsync);
        AskToDeleteCommand = new RelayCommand<ProposalCardViewModel>(AskToDelete);
        KeepProposalCommand = new RelayCommand(() => SetDeleteQuestion(null));
        ReopenCommand = new AsyncRelayCommand<ProposalCardViewModel>(ReopenAsync);
        AskToDeclineCommand = new RelayCommand<ProposalCardViewModel>(AskToDecline);
        CancelDeclineCommand = new RelayCommand(CancelDecline);
        DeclineCommand = new AsyncRelayCommand<ProposalCardViewModel>(DeclineAsync);
        DecideLaterCommand = new AsyncRelayCommand<ProposalCardViewModel>(DecideLaterAsync);
        SelectGroupFilterCommand = new AsyncRelayCommand<ProposalListFilter>(SelectGroupFilterAsync);
        SelectProposalCommand = new RelayCommand<ProposalCardViewModel>(SelectProposal);
        _emptyDocument = new ProposalDocumentViewModel(context, string.Empty, string.Empty, parsimonyDelay);
        CloseDocumentCommand = new AsyncRelayCommand<ProposalDocumentViewModel>(CloseDocumentAsync);
        SelectDocumentCommand = new RelayCommand<ProposalDocumentViewModel>(SelectDocument);
        CompareCommand = new RelayCommand(ComparePair,
            () => Documents.CanCompare && HasComparisonHost);
        Documents.PropertyChanged += (_, _) =>
        {
            CompareCommand.NotifyCanExecuteChanged();
            foreach (var card in Groups.SelectMany(group => group.Cards))
                card.SetComparisonChecked(Documents.ComparisonPair.Any(document => document.ProposalId == card.ProposalId));
        };
        Context.Selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectionViewModel.TextCount)) Difference.RefreshTexts();
        };
        Context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceContext.CurrentPage) && Context.CurrentPage == WorkspacePage.Proposals)
                MarkSelectedRead();
        };
        // Staging, unstaging or applying changes the pending Draft, so the list is read again.
        Context.Changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ChangesViewModel.Snapshot) || Context.ProjectPath is not { } path) return;
            var signature = Context.Changes.Snapshot.DraftId + "|" + Context.Changes.Snapshot.Revision;
            if (signature == _seenChangesSignature) return;
            _seenChangesSignature = signature;
            if (State is ProposalsPageState.Loading) return;
            _ = LoadAsync(path, CancellationToken.None);
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

    /// <summary>The grouped list of Proposals.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilteredEmpty), nameof(IsNoProposalsToShow), nameof(ShowSelectProposalPrompt))]
    private IReadOnlyList<object> _rows = [];

    private ProposalCardViewModel? _selectedProposal;

    /// <summary>
    /// The selected row; selecting another Proposal retains the earlier document and its unsaved edits.
    /// </summary>
    public ProposalCardViewModel? SelectedProposal
    {
        get => _selectedProposal;
        set
        {
            if (ReferenceEquals(_selectedProposal, value)) return;
            if (!_rereading) ComparisonContent = null;
            OnSelectedProposalChanging(value);
            OnPropertyChanging();
            _selectedProposal = value;
            OnSelectedProposalChanged(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProposal));
            OnPropertyChanged(nameof(ShowSelectProposalPrompt));
            OnPropertyChanged(nameof(SelectedOpenAutomationId));
            OnPropertyChanged(nameof(SelectedReopenAutomationId));
            OnPropertyChanged(nameof(SelectedDeleteAutomationId));
        }
    }

    // A refused selection leaves the unsaved field visible so the person can save or cancel.
    private bool KeepsUnsavedEdit(string? proposalId)
    {
        if (UnsavedEditor is not { } editing || editing.ProposalId == proposalId) return false;
        editing.MarkSwitchRefused();
        return true;
    }

    internal bool RefuseLeavingWithUnsavedEdit()
    {
        if (UnsavedEditor is not { } editor) return false;
        editor.MarkSwitchRefused();
        return true;
    }

    private RationaleEditorModel? UnsavedEditor => Documents.UnsavedEditor;

    /// <summary>The selected Proposal's saved Difference, or the offer to make one.</summary>
    public ProposalDifferenceViewModel Difference => ActiveDocument.Difference;

    /// <summary>The selected Proposal's Diagnostics helper, activated by the Diagnostics host.</summary>
    public ProposalParsimonyViewModel Parsimony => ActiveDocument.Parsimony;

    /// <summary>The selected Proposal's pictures, which a person adds to and removes while it is open.</summary>
    public ProposalPicturesViewModel Pictures => ActiveDocument.Pictures;

    /// <summary>The read of the selected Proposal's pictures that is under way, or the last one that ended.</summary>
    public Task PicturesLoading { get; private set; } = Task.CompletedTask;

    /// <summary>The watch of the selected Proposal's Parsimony status that is under way, or the last one that ended.</summary>
    public Task ParsimonyWatching { get; private set; } = Task.CompletedTask;

    /// <summary>The read of the selected Proposal's Difference that is under way, or the last one that finished.</summary>
    public Task DifferenceReading { get; private set; } = Task.CompletedTask;

    /// <summary>The Inspector's Open in Review changes id for the selected Proposal.</summary>
    public string? SelectedOpenAutomationId => SelectedProposal?.InspectorOpenAutomationId;

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

    /// <summary>Whether a group filter is temporarily paused to keep an edited Proposal visible.</summary>
    public bool IsGroupFilterPaused => _pausedGroupFilter is not null;

    /// <summary>The three filter groups and their current counts.</summary>
    public IReadOnlyList<ProposalGroupFilterViewModel> GroupFilters =>
    [
        Filter(ProposalListFilter.YourChangesNotAppliedYet, MotifAppStrings.ProposalsPendingChanges,
            CountFor(ProposalListFilter.YourChangesNotAppliedYet)),
        Filter(ProposalListFilter.WaitingForYou, MotifAppStrings.ProposalsWaitingForYou,
            CountFor(ProposalListFilter.WaitingForYou)),
        Filter(ProposalListFilter.Closed, MotifAppStrings.ProposalsClosed, CountFor(ProposalListFilter.Closed)),
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
                    ListGroup(ProposalListFilter.YourChangesNotAppliedYet, MotifAppStrings.ProposalsPendingChanges, cards),
                    ListGroup(ProposalListFilter.WaitingForYou, MotifAppStrings.ProposalsWaitingForYou, cards),
                    ListGroup(ProposalListFilter.Closed, MotifAppStrings.ProposalsClosed, cards),
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
            var count = Groups.Sum(group => group.Cards.Count) + (_includeSetAside ? 0 : _standings.SetAsideCount) -
                (_retainedClosedCard && !_includeSetAside ? 1 : 0);
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
        _requestGeneration++;
        Requests.Show([], Context.Clock.GetUtcNow());
        _rereading = false;
        _rereadCard = null;
        UnwatchDocuments();
        _ = Documents.StopAsync();
        _includeSetAside = false;
        _retainedClosedCard = false;
        _retainedRowId = null;
        ClearPausedFilter();
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
        var selectedCard = SelectedProposal?.Card ?? (_rereading ? _rereadCard : null);
        _rereadCard = selectedCard;
        var keepSelected = selectedCard?.ProposalId;
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
                _retainedRowId = null;
                ClearPausedFilter();
                SelectedGroupFilter = null;
                Apply(new ProposalStandings([], 0));
                ShownRefusal = WindowRefusal.From(read.Refusal!, "The Proposals could not be read.");
                State = ProposalsPageState.Refused;
                return;
            }
            ShownRefusal = null;
            var standings = read.Value!;
            _retainedClosedCard = false;
            _retainedRowId = null;
            var selectedDocument = Documents.Items.FirstOrDefault(document => document.ProposalId == keepSelected);
            if (selectedCard is not null && selectedDocument is not null &&
                standings.Proposals.All(card => card.ProposalId != selectedCard.ProposalId))
            {
                var detail = await Context.Commands.ShowProposalAsync(
                    new ShowProposalRequest(projectPath, MotifProductVersion.CurrentText, selectedCard.ProposalId),
                    cancellationToken).ConfigureAwait(true);
                if (generation != _readGeneration || projectPath != Context.ProjectPath) return;
                if (detail.Succeeded && detail.Value!.ProposalId == selectedCard.ProposalId &&
                    (PicturesFrozen(detail.Value.Status) || selectedDocument.UnsavedEditor is not null))
                {
                    var decided = PicturesFrozen(detail.Value.Status);
                    var retained = selectedCard with
                    {
                        Status = detail.Value.Status,
                        Group = decided ? ProposalGroup.SetAside : selectedCard.Group,
                        IsDraft = detail.Value.DraftName is not null,
                        IsPendingDraft = decided ? false : selectedCard.IsPendingDraft,
                    };
                    _retainedRowId = retained.ProposalId;
                    _retainedClosedCard = decided && standings.SetAsideCount > 0;
                    standings = standings with { Proposals = [.. standings.Proposals, retained] };
                }
            }
            if (UnsavedEditor is { } unsaved &&
                standings.Proposals.FirstOrDefault(card => card.ProposalId == unsaved.ProposalId) is { } edited &&
                SelectedGroupFilter is { } filter && !IsInFilter(edited.Group, filter))
            {
                _pausedGroupFilter = filter;
                OnPropertyChanged(nameof(IsGroupFilterPaused));
                TrackPausedEditors();
                SelectedGroupFilter = null;
            }
            Apply(standings, keepSelected);
            await LoadRequestsAsync(projectPath, cancellationToken).ConfigureAwait(true);
            _ = Task.WhenAll(Documents.Items.Where(document => document != SelectedDocument)
                .Select(document => document.RefreshAsync(cancellationToken)));
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
        foreach (var document in Documents.Items)
            if (cards.FirstOrDefault(card => card.ProposalId == document.ProposalId) is { } currentCard)
                document.Card = currentCard;
            else document.InvalidateEvidence();
        Groups = new (ProposalGroup, string)[]
            {
                (ProposalGroup.WaitingForYou, MotifAppStrings.ProposalsWaitingForYou),
                (ProposalGroup.NeedsAnotherTry, MotifAppStrings.ProposalsNeedsAnotherTry),
                (ProposalGroup.NotTried, MotifAppStrings.ProposalsNotTried),
                (ProposalGroup.NotAppliedYet, MotifAppStrings.ProposalsPendingChanges),
                (ProposalGroup.SetAside, MotifAppStrings.ProposalsClosed),
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
        SetAsideLinkText = _includeSetAside ? MotifAppStrings.ProposalsHideClosed
            : standings.SetAsideCount == 0 ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalsShowClosedFormat, standings.SetAsideCount);
        var waiting = cards.Count(card => !card.Card.IsDraft && card.Card.Status == "proposed");
        BadgeCount = waiting == 0 ? null : waiting;
    }

    private ProposalCardViewModel CardFor(ProposalCard card, IReadOnlyDictionary<string, string> labels)
    {
        var run = Context.TrialRun.StateFor(card.ProposalId);
        var standing = run.IsRunning ? TrialStanding.Trying : card.Standing;
        var row = new ProposalCardViewModel(card, labels, standing, run.IsRunning ? null : run.ShownRefusal,
            run.IsRunning ? null : run.BaselineNotice,
            run.IsRunning ? null : run.JobId is not null && run.JobId == card.Trial?.JobId
                ? run.WorkNotice : TrialWorkNotice.For(card.Trial?.Work), Context.Clock);
        row.IsCheckedForComparison = Documents.Items.Any(document => document.ProposalId == card.ProposalId &&
            document.IsCheckedForComparison);
        row.CheckComparison = CheckComparison;
        return row;
    }

    private async Task SelectGroupFilterAsync(ProposalListFilter filter)
    {
        ProposalListFilter? nextFilter = SelectedGroupFilter == filter ? null : filter;
        if (nextFilter is { } requested && CountFor(requested) == 0) return;
        // A filter cannot hide the field holding an unsaved edit.
        if (nextFilter is { } hiding && UnsavedEditor is { } editing &&
            _standings.Proposals.FirstOrDefault(card => card.ProposalId == editing.ProposalId) is { } edited &&
            !IsInFilter(edited.Group, hiding) && KeepsUnsavedEdit(null)) return;
        ClearPausedFilter();
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
        ClearPausedFilter();
        SelectedGroupFilter = null;
        UpdateRows(SelectedProposal?.ProposalId);
    }

    private void SelectProposal(ProposalCardViewModel? card)
    {
        if (card is null || !Groups.Any(group => group.Cards.Contains(card))) return;
        SelectByUser(card);
    }

    // Only an explicit choice marks the Proposal read; opening another document preserves unsaved fields.
    private bool SelectByUser(ProposalCardViewModel card)
    {
        _pendingUserSelectionId = card.ProposalId;
        SetSelectedProposal(card);
        _pendingUserSelectionId = null;
        if (!_selectionByUser && ReferenceEquals(SelectedProposal, card))
        {
            _selectionByUser = true;
            MarkSelectedRead();
        }
        return true;
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

    // Selection by the page itself: a re-read, Finalize, Delete, or a person's choice SelectByUser has let through.
    private void SetSelectedProposal(ProposalCardViewModel? proposal)
    {
        if (ReferenceEquals(SelectedProposal, proposal)) return;
        SelectedProposal = proposal;
    }

    private void OnSelectedProposalChanging(ProposalCardViewModel? value) => _selectedProposal?.SetSelected(false);

    private void OnSelectedProposalChanged(ProposalCardViewModel? value)
    {
        _selectionByUser = value is not null && value.ProposalId == _pendingUserSelectionId;
        value?.SetSelected(true);
        if (value?.ProposalId != _declineProposalId) CancelDecline();
        ReadDetail(value);
        if (value?.ProposalId != _deleteQuestionId) SetDeleteQuestion(null);
        else OnPropertyChanged(nameof(IsConfirmingDelete));
        OnPropertyChanged(nameof(DeleteQuestion));
    }

    private void ClearDifference()
    {
        Difference.Clear();
        DifferenceReading = Task.CompletedTask;
    }

    // Ends a re-read of the list: a Proposal no longer there leaves no Difference to show.
    private void EndReread()
    {
        _rereading = false;
        _rereadCard = null;
        ResumePausedFilter();
        if (SelectedProposal is null)
        {
            ClearDifference();
            Parsimony.Stop();
            Pictures.Clear();
        }
    }

    private void TrackPausedEditors()
    {
        if (_pausedGroupFilter is null) return;
        if (RationaleEditor is { } rationale) TrackPausedEditor(rationale);
        if (Comments is { } comments)
            foreach (var editor in comments) TrackPausedEditor(editor);
    }

    private void TrackPausedEditor(RationaleEditorModel editor)
    {
        if (_pausedGroupFilter is not null && _pausedEditors.Add(editor))
            editor.EditFinished += ResumePausedFilter;
    }

    private void ClearPausedFilter()
    {
        if (_pausedGroupFilter is null) return;
        _pausedGroupFilter = null;
        foreach (var editor in _pausedEditors) editor.EditFinished -= ResumePausedFilter;
        _pausedEditors.Clear();
        OnPropertyChanged(nameof(IsGroupFilterPaused));
    }

    private void ResumePausedFilter()
    {
        if (_rereading || _pausedGroupFilter is not { } filter || _pausedEditors.Any(editor => editor.IsEditing)) return;
        var retained = _retainedRowId;
        _retainedRowId = null;
        _retainedClosedCard = false;
        ClearPausedFilter();
        SelectedGroupFilter = filter;
        if (retained is null) UpdateRows(SelectedProposal?.ProposalId);
        else Apply(_standings with { Proposals = _standings.Proposals.Where(card => card.ProposalId != retained).ToArray() });
    }

    // A decided Proposal keeps its pictures as they were; Deferred is still open.
    private static bool PicturesFrozen(string status) => status is "applied" or "rejected" or "withdrawn" or "superseded";

    protected override async Task OnStopWorkAsync()
    {
        UnwatchDocuments();
        await Task.WhenAll(Documents.StopAsync(), _emptyDocument.StopAsync(), DetailReading).ConfigureAwait(true);
        NotifyDocument();
    }

    // Decided keeps no new Difference, untried has no Trial, and deferred can make its words again.
    private static DifferenceOffer DifferenceOfferFor(ProposalCard card) =>
        card.Group == ProposalGroup.SetAside ? DifferenceOffer.Decided
        : card.Standing.Kind == TrialStandingKind.NotTried ? DifferenceOffer.NotTried
        : card.Status == "deferred" ? DifferenceOffer.Deferred
        : DifferenceOffer.Available;

    private Task OpenAsync(ProposalCardViewModel? card) => OpenProposalAsync(card, WorkspacePage.Proposals);

    private Task OpenInReviewAsync(ProposalCardViewModel? card) => OpenProposalAsync(card, WorkspacePage.Proposals);

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
            SelectByUser(card);
            await Context.OpenProposalAsync(card.ProposalId, CancellationToken.None, card.Label).ConfigureAwait(true);
            Context.OpenPage(page);
        }
        catch (InvalidOperationException exception)
        {
            ShownRefusal = WindowRefusal.Failure(WindowRefusal.OpenProposalFailedCode,
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
        ClearPausedFilter();
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
        if (card is null || !card.CanDelete || !SelectByUser(card)) return;
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
        var request = new DeleteProposalRequest(path, card.ProposalId);
        var deleted = await Context.Commands.DeleteProposalAsync(request, CancellationToken.None).ConfigureAwait(true);
        if (path != Context.ProjectPath) return;
        if (!deleted.Succeeded && deleted.Refusal?.Code == RefusalCodes.ProjectBusy)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), Context.Clock, CancellationToken.None).ConfigureAwait(true);
            if (path != Context.ProjectPath) return;
            deleted = await Context.Commands.DeleteProposalAsync(request, CancellationToken.None).ConfigureAwait(true);
        }
        if (path != Context.ProjectPath) return;
        ShownRefusal = deleted.Succeeded ? null : WindowRefusal.From(deleted.Refusal!);
        if (deleted.Succeeded) await LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
    }
}
