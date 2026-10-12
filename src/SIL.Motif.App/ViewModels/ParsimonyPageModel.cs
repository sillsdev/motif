using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Help;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where the Parsimony page is in reading the stored Report.</summary>
public enum ParsimonyPageState
{
    /// <summary>Advanced AI mode is off, so the page does not exist in this window.</summary>
    Off,

    /// <summary>No project is open.</summary>
    NoProject,

    /// <summary>The stored Report is being read.</summary>
    Loading,

    /// <summary>A refusal from the stored Report's read; its message is shown.</summary>
    Refused,

    /// <summary>The project has no stored Parsimony Report yet.</summary>
    NoReport,

    /// <summary>The stored Report's findings are listed.</summary>
    Listed,
}

/// <summary>
/// One finding's address: the project it was read from, its Report and the finding in that Report. A link made for an
/// earlier project is refused once another project is open.
/// </summary>
public sealed record ParsimonyFindingLink(string ProjectPath, string ReportId, string FindingId);

/// <summary>The findings of one measure, under the measure's title and its axis.</summary>
/// <param name="Title">The measure's recipe title, or a plain fallback when no recipe exists.</param>
/// <param name="AxisLabel">Restrictiveness, Parsimony or both, kept apart from every other group.</param>
/// <param name="Items">The measure's finding rows, the same instances the flat list shows.</param>
public sealed record ParsimonyMeasureGroup(string Title, string AxisLabel, IReadOnlyList<ParsimonyListRow> Items)
{
    /// <summary>The group's count in words, such as "2 findings", shown on its header.</summary>
    public string CountLabel => FindingCount(Items.Count);

    /// <summary>The axis by name, such as "Parsimony"; the page's legend says once what each axis means.</summary>
    public string AxisText => ParsimonyPageModel.AxisName(AxisLabel);

    internal static string FindingCount(int count) =>
        count.ToString(System.Globalization.CultureInfo.CurrentCulture) + (count == 1 ? " finding" : " findings");
}

/// <summary>
/// One line of the findings list: a measure's header, or one finding under it. Flat, so the page draws one
/// virtualized list. A row is made once per finding and changes in place, so a selection or a staged decision
/// updates the rows it concerns and no others.
/// </summary>
public sealed partial class ParsimonyListRow(
    bool isHeader, string title, string detail, string count, string description, string items, string disposition,
    ParsimonyFindingLink? link, bool isSelected, IRelayCommand<ParsimonyFindingLink>? selectCommand, string automationId)
    : ObservableObject
{
    /// <summary>Whether this line is a measure's header and not a finding.</summary>
    public bool IsHeader { get; } = isHeader;

    /// <summary>A header's measure title.</summary>
    public string Title { get; } = title;

    /// <summary>A header's axis, or empty on a finding.</summary>
    public string Detail { get; } = detail;

    /// <summary>The finding's counted numbers in words, such as "2 of 3 prohibitions".</summary>
    public string Description { get; } = description;

    /// <summary>
    /// The grammar items the finding concerns, shortened to the first few names, or a plain statement that the
    /// Report names none. The inspector holds the full list.
    /// </summary>
    public string Items { get; } = items;

    /// <summary>The finding's address, or null on a header.</summary>
    public ParsimonyFindingLink? Link { get; } = link;

    /// <summary>Shows this finding's evidence and its actions in the detail pane.</summary>
    public IRelayCommand<ParsimonyFindingLink>? SelectCommand { get; } = selectCommand;

    /// <summary>The row's automation identifier.</summary>
    public string AutomationId { get; } = automationId;

    /// <summary>A header's count in words, such as "2 findings".</summary>
    [ObservableProperty]
    private string _count = count;

    /// <summary>The decision state beside the row, empty while the finding is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisposition))]
    private string _disposition = disposition;

    /// <summary>Whether this finding is the one shown in the detail pane.</summary>
    [ObservableProperty]
    private bool _isSelected = isSelected;

    /// <summary>Whether the finding has a decision state to show as a chip.</summary>
    public bool HasDisposition => Disposition.Length > 0;
}

/// <summary>One saved decision in the Suppressed list, read-only except for its return to Active.</summary>
/// <param name="key">The revision the row shows, which selects it.</param>
/// <param name="title">The measure and the subject it applies to.</param>
/// <param name="identity">The judgment ID and the Notebook record title, so the row matches its record.</param>
/// <param name="decision">Kept or Deferred.</param>
/// <param name="reason">The reason the person gave, or "No reason given".</param>
/// <param name="stateLabel">What the decision means now: suppressed, stale, or unavailable.</param>
/// <param name="returnLabel">Set while a return to Active is staged and not yet applied.</param>
/// <param name="isSelected">Whether the decision is the one shown in the detail pane.</param>
/// <param name="selectCommand">Shows this decision's detail.</param>
/// <param name="automationId">The row's automation identifier, from its revision.</param>
public sealed partial class ParsimonySuppressedItem(
    string key, string title, string identity, string decision, string reason, string stateLabel, string returnLabel,
    bool isSelected, IRelayCommand<string> selectCommand, string automationId) : ObservableObject
{
    /// <summary>The revision the row shows, which selects it.</summary>
    public string Key { get; } = key;

    /// <summary>The measure and the subject it applies to.</summary>
    public string Title { get; } = title;

    /// <summary>The judgment ID and the Notebook record title, so the row matches its record.</summary>
    public string Identity { get; } = identity;

    /// <summary>Kept or Deferred.</summary>
    public string Decision { get; } = decision;

    /// <summary>The reason the person gave, or "No reason given".</summary>
    public string Reason { get; } = reason;

    /// <summary>What the decision means now: suppressed, stale, or unavailable.</summary>
    public string StateLabel { get; } = stateLabel;

    /// <summary>Shows this decision's detail.</summary>
    public IRelayCommand<string> SelectCommand { get; } = selectCommand;

    /// <summary>The row's automation identifier, from its revision.</summary>
    public string AutomationId { get; } = automationId;

    /// <summary>Set while a return to Active is staged and not yet applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReturn))]
    private string _returnLabel = returnLabel;

    /// <summary>Whether this decision is the one shown in the detail pane.</summary>
    [ObservableProperty]
    private bool _isSelected = isSelected;

    /// <summary>Whether the decision is shown as a chip, which every Suppressed row has.</summary>
    public bool HasDecision => Decision.Length > 0;

    /// <summary>Whether a return to Active is staged, so the row says so.</summary>
    public bool HasReturn => ReturnLabel.Length > 0;
}

/// <summary>The selected finding's evidence: what was counted, how strong the evidence is, and its limitations.</summary>
public sealed record ParsimonyEvidence(
    string Title, IReadOnlyList<InspectorDetail> Details, IReadOnlyList<string> Limitations);

/// <summary>
/// The Parsimony page: the Active and Suppressed findings of the latest stored Parsimony Report, with the evidence for
/// the one selected. Opening the page and reopening it read the stored Report and the live decision views; nothing here
/// starts a parser run. A decision staged from the page goes into pending changes and is written only by Apply.
/// </summary>
public sealed partial class ParsimonyPageModel : PageModel
{
    private const string NoReportMessage = "No Parsimony Report yet.";
    private const string NoReportNextStep = "Ask an AI assistant with Motif tools to run every Parsimony measure for " +
        "this project, or run this in the CLI after replacing the project path:\n" +
        "motif parsimony --project \"<project.fwdata>\" --measure all --wait\n" +
        "Then come back here. Opening this page never starts a check; it reads the stored Report.";
    private const string NoRecordTypesMessage = "This project's Notebook has no record types, so a decision has nowhere to go. " +
        "Add one in FieldWorks, then reopen this page.";
    private const string StagedNote = "Staged in pending changes. Nothing is written to FieldWorks until Apply.";
    private const int ViewPageSize = 200;
    private const string ReturnStagedLabel = "Return to Active is staged. Apply writes it.";

    // A row names this many items and then counts the rest; the inspector lists them all.
    private const int RowItemNames = 12;
    private const string EvidenceNoteText = "The evidence this Report was measured from is no longer stored, so its " +
        "findings are listed from the Report itself. Decisions are matched to findings by their recorded evidence, " +
        "not checked against the grammar again.";

    private static readonly ParsimonyRecipeCatalog RecipeCatalog = ParsimonyRecipeCatalog.Load();

    private int _readGeneration;
    private bool _staging;
    private string? _stagedReportId;
    private ParsimonyReportResponse? _report;
    private string? _reportProjectPath;
    private string? _selectedFindingId;
    private string? _selectedSuppressedKey;
    private List<ParsimonyFindingDispositionViewRow> _activeRows = [];
    private List<ParsimonySuppressionHistoryViewRow> _suppressedRows = [];
    private int _activeTotal;
    private int _suppressedTotal;
    private CancellationTokenSource? _remainingCancellation;

    // The flat list and its rows are made once per finding; a page that arrives only inserts what is new.
    private readonly ObservableCollection<ParsimonyListRow> _rows = [];
    private readonly Dictionary<string, ParsimonyListRow> _rowsByFinding = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParsimonyListRow> _headersByMeasure = new(StringComparer.Ordinal);
    private readonly ObservableCollection<ParsimonySuppressedItem> _suppressedItems = [];
    private readonly Dictionary<string, ParsimonySuppressedItem> _suppressedByKey = new(StringComparer.Ordinal);
    private ParsimonyListRow? _selectedRow;
    private ParsimonySuppressedItem? _selectedSuppressedItem;

    // Staged here and not yet applied. They are cleared once Apply has written them or the draft is gone.
    private readonly Dictionary<string, StagedDecision> _stagedByFinding = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _stagedReturns = new(StringComparer.Ordinal);

    public ParsimonyPageModel(WorkspaceContext context) : base(context)
    {
        ReloadCommand = new AsyncRelayCommand(() => ReloadAsync(), () => Context.HasProject);
        SelectFindingCommand = new RelayCommand<ParsimonyFindingLink>(link => { if (link is not null) SelectFinding(link); });
        SelectSuppressedCommand = new RelayCommand<string>(key => { if (key is not null) SelectSuppressed(key); });
        ShowActiveCommand = new RelayCommand(() => IsShowingSuppressed = false);
        ShowSuppressedCommand = new RelayCommand(() => IsShowingSuppressed = true);
        KeepCommand = new AsyncRelayCommand(() => StageDecisionAsync("keep"), CanStageFinding);
        DeferCommand = new AsyncRelayCommand(() => StageDecisionAsync("defer"), CanStageFinding);
        AskCommand = new AsyncRelayCommand(() => StageDecisionAsync("ask"), CanStageFinding);
        ReturnToActiveCommand = new AsyncRelayCommand(ReturnToActiveAsync, CanReturnSelected);
        State = context.AdvancedAiModeEnabled ? ParsimonyPageState.NoProject : ParsimonyPageState.Off;
        _suppressedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSuppressedItems));
    }

    /// <summary>Where the page is in reading the stored Report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsListed), nameof(HasMessage))]
    private ParsimonyPageState _state;

    /// <summary>The refusal or the empty-state line the page shows, empty when the findings are listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    /// <summary>The one next step an empty page names.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextStep))]
    private string _nextStep = string.Empty;

    /// <summary>Whether some measures of the stored Report went unmeasured, so its findings are partial.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomplete))]
    private bool _isIncomplete;

    /// <summary>The titles of the measures the stored Report could not measure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomplete), nameof(MissingInputsText))]
    private IReadOnlyList<string> _missingInputs = [];

    /// <summary>Why some measures went unmeasured, in one sentence; empty when the Report is complete.</summary>
    [ObservableProperty]
    private string _incompleteNote = string.Empty;

    /// <summary>Whether the Report was measured from a Baseline other than the one the project has now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistorical))]
    private bool _isHistorical;

    /// <summary>Why the Active list comes from the Report itself, when its evidence is no longer stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidenceNote))]
    private string _evidenceNote = string.Empty;

    /// <summary>The judgment records that need a look: one line each with its ID, title and what is wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJudgmentIssues))]
    private string _judgmentIssuesText = string.Empty;

    /// <summary>How many Active findings there are to review; the axes are never summed.</summary>
    [ObservableProperty]
    private string _axisSummary = string.Empty;

    /// <summary>What each axis present among the Active findings means, in a few words.</summary>
    [ObservableProperty]
    private string _axisLegend = string.Empty;

    /// <summary>The Active findings loaded so far, one group per measure.</summary>
    [ObservableProperty]
    private IReadOnlyList<ParsimonyMeasureGroup> _groups = [];

    /// <summary>Says findings are still arriving, or why the rest are missing; empty when all are listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadingNote))]
    private string _loadingNote = string.Empty;

    /// <summary>Whether the Suppressed tab is showing; the Active tab is the default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTabLabel), nameof(SuppressedTabLabel), nameof(ShowingActive),
        nameof(ShowingSuppressed), nameof(HasSuppressedEmptyText))]
    private bool _isShowingSuppressed;

    /// <summary>The reason the person writes for a keep or a defer; empty means none is recorded.</summary>
    [ObservableProperty]
    private string _reasonText = string.Empty;

    /// <summary>The question an Ask records; required for an Ask.</summary>
    [ObservableProperty]
    private string _questionText = string.Empty;

    /// <summary>The result of the last action: a staged note or the refusal the command gave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionMessage))]
    private string _actionMessage = string.Empty;

    /// <summary>Whether the last action staged a change that can be reviewed before Apply.</summary>
    [ObservableProperty]
    private bool _hasStagedAction;

    /// <summary>The record type a decision is recorded under; the person picks one when there are several.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordTypes), nameof(CanActOnSelection))]
    private NotebookRecordType? _selectedRecordType;

    /// <summary>The Notebook's saved record types, read when the page lists findings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordTypes), nameof(CanActOnSelection))]
    private IReadOnlyList<NotebookRecordType> _recordTypes = [];

    /// <summary>What Fix offers: the recipe's supported update, or a plain statement that there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFixNote))]
    private string _fixNote = string.Empty;

    /// <summary>The selected finding's evidence, or null while none is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidence), nameof(ShowActiveActions), nameof(ShowSuppressedActions))]
    private ParsimonyEvidence? _evidence;

    /// <summary>Why the last selection was refused, empty when it was accepted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectionMessage))]
    private string _selectionMessage = string.Empty;

    /// <summary>Whether the page is reading the stored Report.</summary>
    public bool IsLoading => State == ParsimonyPageState.Loading;

    /// <summary>Whether the stored findings are listed.</summary>
    public bool IsListed => State == ParsimonyPageState.Listed;

    /// <summary>Whether the page has a refusal or an empty-state line to show.</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>Whether an empty page has its one next step to show.</summary>
    public bool HasNextStep => NextStep.Length > 0;

    /// <summary>The unmeasured checks in one line, such as "Not measured: A and B."; empty when there are none.</summary>
    public string MissingInputsText => MissingInputs.Count == 0
        ? string.Empty
        : "Not measured: " + JoinPlainly(MissingInputs) + ".";

    /// <summary>Whether the listed findings are a partial view of the grammar.</summary>
    public bool HasIncomplete => IsIncomplete || MissingInputs.Count > 0;

    /// <summary>Whether the Report predates the project's current Baseline.</summary>
    public bool HasHistorical => IsHistorical;

    /// <summary>Whether the Active list is listed from the Report because its evidence is no longer stored.</summary>
    public bool HasEvidenceNote => EvidenceNote.Length > 0;

    /// <summary>Whether a judgment record failed a check or carries a warning.</summary>
    public bool HasJudgmentIssues => JudgmentIssuesText.Length > 0;

    /// <summary>Whether a finding's evidence is shown in the detail pane.</summary>
    public bool HasEvidence => Evidence is not null;

    /// <summary>Whether the last selection was refused.</summary>
    public bool HasSelectionMessage => SelectionMessage.Length > 0;

    /// <summary>Whether the last action left a note or a refusal to show.</summary>
    public bool HasActionMessage => ActionMessage.Length > 0;

    /// <summary>Whether the Fix action has a note to show.</summary>
    public bool HasFixNote => FixNote.Length > 0;

    /// <summary>Whether the project's Notebook offers any record type.</summary>
    public bool HasRecordTypes => RecordTypes.Count > 0;

    /// <summary>The Suppressed decisions, each with its reason or the plain statement that none was given.</summary>
    public IReadOnlyList<ParsimonySuppressedItem> SuppressedItems => _suppressedItems;

    /// <summary>Whether the page has a note about findings still arriving or not loaded.</summary>
    public bool HasLoadingNote => LoadingNote.Length > 0;

    /// <summary>Completes when every page of findings has been read; already complete when the first page was all.</summary>
    public Task RemainingLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Whether a Suppressed decision is listed.</summary>
    public bool HasSuppressedItems => _suppressedItems.Count > 0;

    /// <summary>Whether the Suppressed tab is empty and should say what would put a finding there.</summary>
    public bool HasSuppressedEmptyText => IsShowingSuppressed && _suppressedItems.Count == 0 && IsListed;

    /// <summary>Whether the Active tab is the one showing.</summary>
    public bool ShowingActive => !IsShowingSuppressed;

    /// <summary>Whether the Suppressed tab is the one showing.</summary>
    public bool ShowingSuppressed => IsShowingSuppressed;

    /// <summary>The Active tab's label with its count.</summary>
    public string ActiveTabLabel => "Active (" + _activeTotal.ToString(System.Globalization.CultureInfo.CurrentCulture) + ")";

    /// <summary>The Suppressed tab's label with its count.</summary>
    public string SuppressedTabLabel =>
        "Suppressed (" + _suppressedTotal.ToString(System.Globalization.CultureInfo.CurrentCulture) + ")";

    /// <summary>Whether a selected Active finding has the decision actions to show.</summary>
    public bool ShowActiveActions => Evidence is not null && _selectedFindingId is not null;

    /// <summary>Whether a selected Suppressed decision shows its return to Active.</summary>
    public bool ShowSuppressedActions => Evidence is not null && _selectedSuppressedKey is not null;

    /// <summary>Whether a decision can be recorded against the current selection.</summary>
    public bool CanActOnSelection => SelectedRecordType is not null && _report is not null;

    /// <summary>
    /// The Active list as one flat list: each measure's header, then its findings. The page draws it virtualized.
    /// </summary>
    public IReadOnlyList<ParsimonyListRow> Rows => _rows;

    /// <summary>Selects one Active finding and shows its evidence and actions.</summary>
    public IRelayCommand<ParsimonyFindingLink> SelectFindingCommand { get; }

    /// <summary>Selects one Suppressed decision, read-only, and shows its detail.</summary>
    public IRelayCommand<string> SelectSuppressedCommand { get; }

    /// <summary>Shows the Active tab.</summary>
    public IRelayCommand ShowActiveCommand { get; }

    /// <summary>Shows the Suppressed tab.</summary>
    public IRelayCommand ShowSuppressedCommand { get; }

    /// <summary>Stages a keep, with the reason the person wrote, if any.</summary>
    public IAsyncRelayCommand KeepCommand { get; }

    /// <summary>Stages a defer, with the reason the person wrote, if any.</summary>
    public IAsyncRelayCommand DeferCommand { get; }

    /// <summary>Stages an ask, which needs the question the person wrote.</summary>
    public IAsyncRelayCommand AskCommand { get; }

    /// <summary>Stages the withdrawal of the selected Suppressed decision, so its finding returns to Active.</summary>
    public IAsyncRelayCommand ReturnToActiveCommand { get; }

    /// <summary>Reads the stored Report and the decision views again; it never starts a parser run.</summary>
    public IAsyncRelayCommand ReloadCommand { get; }

    /// <summary>Reads the stored Report again for the open project.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!Context.AdvancedAiModeEnabled) return;
        if (Context.ProjectPath is not { } path)
        {
            ClearReport();
            return;
        }
        await LoadAsync(path, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Selects one Active finding and shows its evidence, unless the link belongs to another project or Report.</summary>
    public bool SelectFinding(ParsimonyFindingLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var current = _report;
        if (current is null || !string.Equals(link.ProjectPath, Context.ProjectPath, StringComparison.Ordinal) ||
            !string.Equals(link.ProjectPath, _reportProjectPath, StringComparison.Ordinal) ||
            !string.Equals(link.ReportId, current.ReportId, StringComparison.Ordinal))
        {
            Refuse("This finding belongs to a project that is no longer open. Open the Parsimony page for this project.");
            return false;
        }
        var row = _activeRows.FirstOrDefault(item => string.Equals(item.Finding.FindingId, link.FindingId,
            StringComparison.Ordinal));
        if (row is null)
        {
            Refuse("That finding is no longer Active in this Report.");
            return false;
        }
        SelectionMessage = string.Empty;
        ActionMessage = string.Empty;
        HasStagedAction = false;
        _selectedSuppressedKey = null;
        _selectedFindingId = row.Finding.FindingId;
        MoveSelection();
        Evidence = BuildEvidence(row.Finding, row.Reason, row.PreviousEvidenceDigest is not null);
        ActionsChanged();
        FixNote = FixNoteFor(row.Finding.MeasureId);
        ReasonText = string.Empty;
        QuestionText = string.Empty;
        return true;
    }

    /// <summary>Shows one saved decision's detail. Selecting it writes nothing.</summary>
    public bool SelectSuppressed(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var row = _suppressedRows.FirstOrDefault(item => string.Equals(item.RevisionId, key, StringComparison.Ordinal));
        if (row is null)
        {
            SelectionMessage = "That decision is no longer in this Report's decisions.";
            return false;
        }
        SelectionMessage = string.Empty;
        ActionMessage = string.Empty;
        HasStagedAction = false;
        _selectedFindingId = null;
        _selectedSuppressedKey = key;
        MoveSelection();
        Evidence = BuildSuppressedDetail(row);
        ActionsChanged();
        return true;
    }

    protected override void OnProjectCleared() => ClearReport();

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (!Context.AdvancedAiModeEnabled) return Task.CompletedTask;
        return LoadAsync(projectPath, cancellationToken);
    }

    private async Task LoadAsync(string path, CancellationToken cancellationToken)
    {
        var generation = ++_readGeneration;
        ClearShown();
        State = ParsimonyPageState.Loading;
        var latest = await Context.Commands.ReadLatestParsimonyReportAsync(
            new ReadLatestParsimonyReportRequest(path, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        if (!latest.Succeeded)
        {
            ShowRefusal(latest.Refusal!);
            return;
        }

        var shown = await Context.Commands.ReadParsimonyReportAsync(new ShowParsimonyReportRequest(
            path, MotifProductVersion.CurrentText, latest.Value!.ReportId), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        if (!shown.Succeeded)
        {
            ShowRefusal(shown.Refusal!);
            return;
        }
        var report = shown.Value!;

        var active = await ReadViewPageAsync(path, report, ParsimonyViews.ParsimonyActiveFindings, null, cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        var evidenceMissing = !active.Succeeded &&
            active.Refusal!.Code is ParsimonyRefusalCodes.BundleNotFound or ParsimonyRefusalCodes.BundleInvalid;
        if (!active.Succeeded && !evidenceMissing)
        {
            ShowRefusal(active.Refusal!);
            return;
        }
        var suppressed = await ReadViewPageAsync(path, report, ParsimonyViews.ParsimonySuppressed, null, cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        if (!suppressed.Succeeded)
        {
            ShowRefusal(suppressed.Refusal!);
            return;
        }

        var current = await Context.Commands.GetCurrentBaselineAsync(new CurrentBaselineRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        var types = await Context.Commands.ListNotebookRecordTypesAsync(
            new ListNotebookRecordTypesRequest(path, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        var staged = await Context.Commands.LoadPendingChangesAsync(
            new PendingChangesRequest(path, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        var history = await ReadAllViewRowsAsync(path, report, ParsimonyViews.ParsimonySuppressionHistory, cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;

        _report = report;
        _reportProjectPath = path;
        _suppressedRows = [.. suppressed.Value!.Rows.OfType<ParsimonySuppressionHistoryViewRow>()];
        _suppressedTotal = TotalOf(suppressed.Value, _suppressedRows.Count);
        JudgmentIssuesText = history.Succeeded ? JudgmentIssueLines(history.Value!.Rows) : string.Empty;
        _activeRows = evidenceMissing
            ? [.. UndecidedFindings(report, _suppressedRows)]
            : [.. active.Value!.Rows.OfType<ParsimonyFindingDispositionViewRow>()];
        _activeTotal = evidenceMissing ? _activeRows.Count : TotalOf(active.Value!, _activeRows.Count);
        EvidenceNote = evidenceMissing ? EvidenceNoteText : string.Empty;
        IsHistorical = !(current.Succeeded && current.Value?.Token == report.Inputs.BaselineToken);
        RecordTypes = types.Succeeded ? types.Value!.RecordTypes : [];
        SelectedRecordType = RecordTypes.Count == 1 ? RecordTypes[0] : null;
        ForgetStagedWork(staged, report.ReportId);
        var activeCursor = evidenceMissing ? null : NextCursorOf(active.Value!);
        var suppressedCursor = NextCursorOf(suppressed.Value!);
        if (suppressedCursor is null) PruneAppliedStagedWork();
        var more = activeCursor is not null || suppressedCursor is not null;
        LoadingNote = more ? LoadingNoteText() : string.Empty;
        ShowReport(report, path);
        if (more)
        {
            var remaining = new CancellationTokenSource();
            _remainingCancellation = remaining;
            RemainingLoad = LoadRemainingAsync(generation, path, report, activeCursor, suppressedCursor, remaining.Token);
        }
    }

    private static int TotalOf(ParsimonyNamedViewResponse page, int loaded) =>
        (int)Math.Max(loaded, Math.Min(page.Total ?? loaded, int.MaxValue));

    private static string? NextCursorOf(ParsimonyNamedViewResponse page) => page.Truncated ? page.NextCursor : null;

    private string LoadingNoteText()
    {
        var loaded = _activeRows.Count + _suppressedRows.Count;
        var total = _activeTotal + _suppressedTotal;
        return $"Showing {loaded.ToString(System.Globalization.CultureInfo.CurrentCulture)} of " +
            $"{total.ToString(System.Globalization.CultureInfo.CurrentCulture)} findings; the rest are still loading.";
    }

    // The first page is already listed. Each later page is appended in place, and a newer read drops what is left.
    private async Task LoadRemainingAsync(int generation, string path, ParsimonyReportResponse report,
        string? activeCursor, string? suppressedCursor, CancellationToken cancellationToken)
    {
        try
        {
            while (activeCursor is not null)
            {
                var page = await ReadViewPageAsync(path, report, ParsimonyViews.ParsimonyActiveFindings, activeCursor, cancellationToken)
                    .ConfigureAwait(true);
                if (!IsCurrent(generation, path)) return;
                if (!page.Succeeded)
                {
                    LoadingNote = "Not every finding could be loaded: " + page.Refusal!.Message;
                    return;
                }
                _activeRows.AddRange(page.Value!.Rows.OfType<ParsimonyFindingDispositionViewRow>());
                activeCursor = NextCursorOf(page.Value);
                ShowArrivedPage(report);
            }
            while (suppressedCursor is not null)
            {
                var page = await ReadViewPageAsync(path, report, ParsimonyViews.ParsimonySuppressed, suppressedCursor, cancellationToken)
                    .ConfigureAwait(true);
                if (!IsCurrent(generation, path)) return;
                if (!page.Succeeded)
                {
                    LoadingNote = "Not every finding could be loaded: " + page.Refusal!.Message;
                    return;
                }
                _suppressedRows.AddRange(page.Value!.Rows.OfType<ParsimonySuppressionHistoryViewRow>());
                suppressedCursor = NextCursorOf(page.Value);
                ShowArrivedPage(report);
            }
            PruneAppliedStagedWork();
            LoadingNote = string.Empty;
        }
        catch (OperationCanceledException) when (!IsCurrent(generation, path))
        {
        }
        catch (Exception exception) when (IsCurrent(generation, path))
        {
            LoadingNote = "Not every finding could be loaded: " + exception.Message;
        }
    }

    private void ShowArrivedPage(ParsimonyReportResponse report)
    {
        _activeTotal = Math.Max(_activeTotal, _activeRows.Count);
        _suppressedTotal = Math.Max(_suppressedTotal, _suppressedRows.Count);
        SyncLists();
        ShowAxes();
        Badge = _activeTotal.ToString(System.Globalization.CultureInfo.CurrentCulture);
        LoadingNote = LoadingNoteText();
        OnPropertyChanged(nameof(ActiveTabLabel));
        OnPropertyChanged(nameof(SuppressedTabLabel));
    }

    // Without the evidence bundle, the Report's findings stand in for the Active view, less matched decisions.
    private static ParsimonyFindingDispositionViewRow[] UndecidedFindings(ParsimonyReportResponse report,
        IReadOnlyList<ParsimonySuppressionHistoryViewRow> decisions) =>
        [
            .. report.Findings
                .Where(finding => !decisions.Any(row => row.MeasureId == finding.MeasureId &&
                    row.EvidenceDigest == finding.EvidenceDigest))
                .Select(finding => new ParsimonyFindingDispositionViewRow(report.ReportId, report.Inputs.BundleId,
                    "active", finding, null, null, null, null, null, null)),
        ];

    private async Task<CommandOutcome<ParsimonyNamedViewResponse>> ReadViewPageAsync(string path,
        ParsimonyReportResponse report, string view, string? cursor, CancellationToken cancellationToken) =>
        await Context.Commands.ReadParsimonyViewAsync(new ReadParsimonyViewRequest(
            path, MotifProductVersion.CurrentText, new ParsimonyNamedViewRequest(report.Inputs.BundleId, view,
                new ParsimonyViewFilters(ReportId: report.ReportId), ViewPageSize, cursor)), cancellationToken)
            .ConfigureAwait(true);

    // Every record that failed a check or carries a warning, named by ID and title, one line each.
    private static string JudgmentIssueLines(IReadOnlyList<ParsimonyViewRow> rows) => string.Join("\n", rows
        .OfType<ParsimonyUnresolvedJudgmentViewRow>().Where(row => row.Check != "capability")
        .Select(row => ParsimonyJudgmentText.Line(row)));

    private async Task<CommandOutcome<ParsimonyNamedViewResponse>> ReadAllViewRowsAsync(string path, ParsimonyReportResponse report,
        string view, CancellationToken cancellationToken)
    {
        var rows = new List<ParsimonyViewRow>();
        string? cursor = null;
        ParsimonyNamedViewResponse? last = null;
        do
        {
            var outcome = await Context.Commands.ReadParsimonyViewAsync(new ReadParsimonyViewRequest(
                path, MotifProductVersion.CurrentText, new ParsimonyNamedViewRequest(report.Inputs.BundleId, view,
                    new ParsimonyViewFilters(ReportId: report.ReportId), ViewPageSize, cursor)), cancellationToken)
                .ConfigureAwait(true);
            if (!outcome.Succeeded) return outcome;
            last = outcome.Value!;
            rows.AddRange(last.Rows);
            cursor = last.NextCursor;
        } while (last.Truncated && cursor is not null);
        return CommandOutcome<ParsimonyNamedViewResponse>.Success(last! with { Rows = rows });
    }

    // A staged decision leaves the overlay once Apply writes it; the whole overlay goes when its draft does.
    private void ForgetStagedWork(CommandOutcome<PendingChangesSnapshot> staged, string reportId)
    {
        if (_stagedReportId is not null && _stagedReportId != reportId)
        {
            _stagedByFinding.Clear();
            _stagedReturns.Clear();
        }
        _stagedReportId = reportId;
        if (!staged.Succeeded || staged.Value?.DraftId is null)
        {
            _stagedByFinding.Clear();
            _stagedReturns.Clear();
        }
    }

    // Needs every Suppressed row, so it runs once the last page is in; a page short of it would drop live staging.
    private void PruneAppliedStagedWork()
    {
        foreach (var applied in _suppressedRows)
            foreach (var entry in _stagedByFinding.Where(entry => entry.Value.MeasureId == applied.MeasureId &&
                entry.Value.EvidenceDigest == applied.EvidenceDigest).Select(entry => entry.Key).ToArray())
                _stagedByFinding.Remove(entry);
        foreach (var recordId in _stagedReturns.Keys.ToArray())
            if (!_suppressedRows.Any(row => row.RecordId == recordId)) _stagedReturns.Remove(recordId);
    }

    private void ShowRefusal(Refusal refusal)
    {
        if (string.Equals(refusal.Code, ParsimonyRefusalCodes.NoReport, StringComparison.Ordinal))
        {
            State = ParsimonyPageState.NoReport;
            Message = NoReportMessage;
            NextStep = NoReportNextStep;
        }
        else
        {
            State = ParsimonyPageState.Refused;
            Message = refusal.Message;
        }
        Badge = string.Empty;
    }

    private void ShowReport(ParsimonyReportResponse report, string path)
    {
        var missing = report.MeasureRuns.Where(run => run.Status != ParsimonyMeasureStatus.Computed).ToArray();
        MissingInputs = [.. missing.Select(run => ShortTitleOf(run.MeasureId))];
        IsIncomplete = missing.Length > 0;
        IncompleteNote = IsIncomplete
            ? "Some checks did not run on the stored evidence, so their findings are not listed. " +
              "Measure the grammar again once the evidence is complete."
            : string.Empty;
        SyncLists();
        ShowAxes();
        State = ParsimonyPageState.Listed;
        Badge = _activeTotal.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Message = string.Empty;
        NextStep = string.Empty;
        FixNote = string.Empty;
        if (RecordTypes.Count == 0) ActionMessage = NoRecordTypesMessage;
        OnPropertyChanged(nameof(ActiveTabLabel));
        OnPropertyChanged(nameof(SuppressedTabLabel));
    }

    private void ShowAxes()
    {
        var findings = _activeRows.Select(row => row.Finding).ToArray();
        AxisSummary = _activeTotal == 0 ? "No findings to review" : ParsimonyMeasureGroup.FindingCount(_activeTotal) + " to review";
        AxisLegend = LegendOf(findings);
    }

    // Brings the flat lists up to the rows read so far. Rows already listed are kept and only what is new goes in.
    private void SyncLists()
    {
        var path = _reportProjectPath ?? string.Empty;
        var reportId = _report?.ReportId ?? string.Empty;
        var groups = new List<ParsimonyMeasureGroup>();
        var flat = new List<ParsimonyListRow>(_activeRows.Count + 8);
        foreach (var measure in _activeRows.GroupBy(row => row.Finding.MeasureId, StringComparer.Ordinal))
        {
            var items = measure.Select(row => RowFor(row, path, reportId)).ToArray();
            var group = new ParsimonyMeasureGroup(TitleOf(measure.Key), AxisLabel(measure.First().Finding.Axis), items);
            groups.Add(group);
            if (!_headersByMeasure.TryGetValue(measure.Key, out var header))
                _headersByMeasure[measure.Key] = header = new ParsimonyListRow(true, group.Title, group.AxisLabel,
                    group.CountLabel, string.Empty, string.Empty, string.Empty, null, false, null, string.Empty);
            header.Count = group.CountLabel;
            flat.Add(header);
            flat.AddRange(items);
        }
        Groups = groups;
        InsertNew(_rows, flat);
        InsertNew(_suppressedItems, [.. _suppressedRows.Select(SuppressedItemFor)]);
        OnPropertyChanged(nameof(ActiveTabLabel));
        OnPropertyChanged(nameof(SuppressedTabLabel));
        OnPropertyChanged(nameof(HasSuppressedEmptyText));
        OnPropertyChanged(nameof(CanActOnSelection));
        ActionsChanged();
    }

    // Pages only ever add rows and keep the order of the rows before them, so the new ones slot in by position.
    private static void InsertNew<T>(ObservableCollection<T> shown, IReadOnlyList<T> target) where T : class
    {
        for (var index = 0; index < target.Count; index++)
        {
            if (index < shown.Count && ReferenceEquals(shown[index], target[index])) continue;
            shown.Insert(index, target[index]);
        }
        while (shown.Count > target.Count) shown.RemoveAt(shown.Count - 1);
    }

    private ParsimonyListRow RowFor(ParsimonyFindingDispositionViewRow row, string path, string reportId)
    {
        if (_rowsByFinding.TryGetValue(row.Finding.FindingId, out var existing)) return existing;
        var made = new ParsimonyListRow(false, string.Empty, string.Empty, string.Empty, Describe(row.Finding),
            ItemsText(row.Finding, RowItemNames), StatusOf(row), new ParsimonyFindingLink(path, reportId, row.Finding.FindingId),
            row.Finding.FindingId == _selectedFindingId, SelectFindingCommand,
            AutomationIds.ForParsimonyFinding(row.Finding.FindingId));
        _rowsByFinding[row.Finding.FindingId] = made;
        if (made.IsSelected) _selectedRow = made;
        return made;
    }

    private ParsimonySuppressedItem SuppressedItemFor(ParsimonySuppressionHistoryViewRow row)
    {
        if (_suppressedByKey.TryGetValue(row.RevisionId, out var existing)) return existing;
        var made = new ParsimonySuppressedItem(
            row.RevisionId,
            row.MeasureCaption + " · " + row.SubjectCaption,
            row.VisibleId + " · " + row.RecordTitle,
            DecisionLabel(row.Disposition),
            string.IsNullOrWhiteSpace(row.Reason) ? "No reason given" : "Reason: " + row.Reason,
            SuppressedStateLabel(row.State),
            _stagedReturns.ContainsKey(row.RecordId) ? ReturnStagedLabel : string.Empty,
            row.RevisionId == _selectedSuppressedKey,
            SelectSuppressedCommand,
            AutomationIds.ForParsimonySuppressed(row.RevisionId));
        _suppressedByKey[row.RevisionId] = made;
        if (made.IsSelected) _selectedSuppressedItem = made;
        return made;
    }

    // A selection changes the flag on the row it leaves and the row it reaches, and on no other.
    private void MoveSelection()
    {
        var row = _selectedFindingId is null ? null : _rowsByFinding.GetValueOrDefault(_selectedFindingId);
        if (!ReferenceEquals(row, _selectedRow))
        {
            _selectedRow?.IsSelected = false;
            row?.IsSelected = true;
            _selectedRow = row;
        }
        var item = _selectedSuppressedKey is null ? null : _suppressedByKey.GetValueOrDefault(_selectedSuppressedKey);
        if (!ReferenceEquals(item, _selectedSuppressedItem))
        {
            _selectedSuppressedItem?.IsSelected = false;
            item?.IsSelected = true;
            _selectedSuppressedItem = item;
        }
        ActionsChanged();
    }

    private void ActionsChanged()
    {
        KeepCommand.NotifyCanExecuteChanged();
        DeferCommand.NotifyCanExecuteChanged();
        AskCommand.NotifyCanExecuteChanged();
        ReturnToActiveCommand.NotifyCanExecuteChanged();
    }

    // A staged decision shows over the applied state, so the person sees what Apply has not yet written.
    private string StatusOf(ParsimonyFindingDispositionViewRow row)
    {
        if (row.JudgmentState == "unresolved") return "Conflict: " + (row.Issue ?? "more than one decision names this finding");
        if (_stagedByFinding.TryGetValue(row.Finding.FindingId, out var staged))
            return staged.Label + " · staged, not yet applied";
        if (row.State == "resurfaced")
            return "Changed since it was " + DecisionLabel(row.Disposition ?? string.Empty).ToLowerInvariant() + ": back for review";
        return string.Empty;
    }

    private static ParsimonyEvidence BuildEvidence(ParsimonyFinding finding, string? priorReason, bool changed)
    {
        var details = InspectorDetail.Recorded(
            ("Counted", Describe(finding)),
            ("Items", ItemsText(finding)),
            ("Why it was reported", $"Reported because {Describe(finding)} match {ShortTitleOf(finding.MeasureId)}."),
            ("Strength of evidence", TierLabel(finding.Tier)),
            ("Paired parser check", VerificationLabel(finding.Verification))).ToList();
        if (changed && priorReason is not null) details.AddRange(InspectorDetail.Recorded(("Earlier reason", priorReason)));
        return new ParsimonyEvidence(TitleOf(finding.MeasureId), details, finding.Limitations);
    }

    private static ParsimonyEvidence BuildSuppressedDetail(ParsimonySuppressionHistoryViewRow row) => new(
        row.MeasureCaption,
        InspectorDetail.Recorded(
            ("Decision", DecisionLabel(row.Disposition)),
            ("Subject", row.SubjectCaption),
            ("Judgment ID", row.VisibleId),
            ("Notebook record", row.RecordTitle),
            ("Reason", string.IsNullOrWhiteSpace(row.Reason) ? "No reason given" : row.Reason),
            ("Current state", SuppressedStateLabel(row.State))),
        []);

    private bool CanStageFinding() => Context.HasProject && CanActOnSelection && ShowActiveActions && !_staging;

    private bool CanReturnSelected() =>
        Context.HasProject && CanActOnSelection && !_staging && SelectedSuppressedRow() is { State: "suppressed" };

    private ParsimonySuppressionHistoryViewRow? SelectedSuppressedRow() =>
        _suppressedRows.FirstOrDefault(row => string.Equals(row.RevisionId, _selectedSuppressedKey, StringComparison.Ordinal));

    private async Task StageDecisionAsync(string disposition)
    {
        if (_report is null || _reportProjectPath is null || SelectedRecordType is null || _selectedFindingId is null) return;
        var row = _activeRows.FirstOrDefault(item => string.Equals(item.Finding.FindingId, _selectedFindingId,
            StringComparison.Ordinal));
        if (row is null) return;
        var question = QuestionText.Trim();
        if (disposition == "ask" && question.Length == 0)
        {
            ActionMessage = "Write the question to ask first.";
            return;
        }
        var reason = ReasonText.Trim();
        var path = _reportProjectPath;
        var report = _report;
        _staging = true;
        ActionsChanged();
        try
        {
            var outcome = await Context.Commands.RecordParsimonyDispositionAsync(new RecordParsimonyDispositionFromFindingRequest(
                path, MotifProductVersion.CurrentText, report.ReportId, row.Finding.FindingId, disposition,
                SelectedRecordType.Id, PendingChanges.DraftName,
                disposition is "keep" or "defer" && reason.Length > 0 ? reason : null,
                disposition == "ask" ? question : null), CancellationToken.None).ConfigureAwait(true);
            if (!IsCurrent(_readGeneration, path)) return;
            if (!outcome.Succeeded)
            {
                HasStagedAction = false;
                ActionMessage = outcome.Refusal!.Message;
                return;
            }
            _stagedByFinding[row.Finding.FindingId] = new StagedDecision(row.Finding.MeasureId,
                row.Finding.EvidenceDigest, DecisionLabel(disposition));
            ReasonText = string.Empty;
            QuestionText = string.Empty;
            ActionMessage = StagedNote;
            HasStagedAction = true;
            if (_rowsByFinding.TryGetValue(row.Finding.FindingId, out var staged)) staged.Disposition = StatusOf(row);
            Evidence = BuildEvidence(row.Finding, row.Reason, row.PreviousEvidenceDigest is not null);
        }
        finally
        {
            _staging = false;
            ActionsChanged();
        }
    }

    private async Task ReturnToActiveAsync()
    {
        var row = SelectedSuppressedRow();
        if (row is null || _reportProjectPath is null) return;
        var path = _reportProjectPath;
        var intent = JsonSerializer.Serialize(new
        {
            recordId = row.RecordId,
            expectedHeads = new[] { new JudgmentPredecessor(row.RevisionId, row.ContentDigest) },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _staging = true;
        ActionsChanged();
        try
        {
            var outcome = await Context.Commands.RetractParsimonyDispositionAsync(new RetractParsimonyDispositionRequest(
                path, MotifProductVersion.CurrentText, PendingChanges.DraftName, intent), CancellationToken.None)
                .ConfigureAwait(true);
            if (!IsCurrent(_readGeneration, path)) return;
            if (!outcome.Succeeded)
            {
                HasStagedAction = false;
                ActionMessage = outcome.Refusal!.Message;
                return;
            }
            _stagedReturns[row.RecordId] = row.RevisionId;
            ActionMessage = "Return to Active is staged. " + StagedNote;
            HasStagedAction = true;
            if (_suppressedByKey.TryGetValue(row.RevisionId, out var shown)) shown.ReturnLabel = ReturnStagedLabel;
        }
        finally
        {
            _staging = false;
            ActionsChanged();
        }
    }

    private string FixNoteFor(string measureId)
    {
        var recipe = RecipeCatalog.Find(measureId);
        return recipe is { Metadata.UpdateIntents.Count: > 0 }
            ? $"The Guide page \u201c{recipe.Title}\u201d describes the update Motif supports for this check. You make the fix itself in FieldWorks."
            : "This check has no supported update in Motif yet, so there is nothing to link to. Ask about it or defer it instead.";
    }

    // A row names the first few items and counts the rest; the inspector passes no limit and lists every one.
    private static string ItemsText(ParsimonyFinding finding, int limit = int.MaxValue)
    {
        if (finding.ItemNames.Count == 0) return "The Report does not name the items for this finding.";
        if (finding.ItemNames.Count <= limit) return string.Join(", ", finding.ItemNames);
        return string.Join(", ", finding.ItemNames.Take(limit)) + $", and {finding.ItemNames.Count - limit} more";
    }

    // Two names read "A and B"; three or more read "A, B, and C".
    private static string JoinPlainly(IReadOnlyList<string> names) => names.Count switch
    {
        <= 1 => string.Join(string.Empty, names),
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1],
    };

    private static string Describe(ParsimonyFinding finding) =>
        $"{finding.Number.Numerator} of {finding.Number.Denominator} {finding.Number.Unit}";

    private static string TitleOf(string measureId) => RecipeCatalog.Find(measureId)?.Title ?? "Parsimony check";

    private static string ShortTitleOf(string measureId) => RecipeCatalog.Find(measureId)?.ShortTitle ?? "parsimony check";

    private static string AxisLabel(ParsimonyAxis axis) => axis switch
    {
        ParsimonyAxis.Restrictiveness => "Restrictiveness",
        ParsimonyAxis.Parsimony => "Parsimony",
        _ => "Both",
    };

    private static string LegendOf(IReadOnlyList<ParsimonyFinding> findings)
    {
        var axes = new[] { ParsimonyAxis.Parsimony, ParsimonyAxis.Restrictiveness }
            .Where(axis => findings.Any(finding => finding.Axis == axis || finding.Axis == ParsimonyAxis.Both));
        return string.Join(" ", axes.Select(axis => AxisWithMeaning(AxisLabel(axis)) + "."));
    }

    /// <summary>An axis as a group header names it, with "Both" spelled out.</summary>
    internal static string AxisName(string axisLabel) => axisLabel switch
    {
        "Parsimony" or "Restrictiveness" => axisLabel,
        _ => "Parsimony and restrictiveness",
    };

    /// <summary>An axis name with what it means in a few words; the two axes are never combined into one score.</summary>
    internal static string AxisWithMeaning(string axisLabel) => axisLabel switch
    {
        "Parsimony" => "Parsimony: a simpler grammar",
        "Restrictiveness" => "Restrictiveness: a grammar that accepts fewer wrong forms",
        _ => "Parsimony and restrictiveness: a simpler grammar that accepts fewer wrong forms",
    };

    private static string DecisionLabel(string disposition) => disposition switch
    {
        "keep" => "Kept",
        "defer" => "Deferred",
        "ask" => "Question open",
        "fix" => "Fix planned",
        _ => string.Empty,
    };

    private static string SuppressedStateLabel(string state) => state switch
    {
        "suppressed" => "Suppressed",
        "no-current-finding" => "Stale: no current finding matches it",
        "evidence-unavailable" => "Unavailable: the evidence cannot be read",
        "conflict" => "Conflict: needs a decision",
        "unavailable" => "Unavailable: the decision cannot be read",
        _ => state,
    };

    private static string TierLabel(ParsimonyTier tier) => tier switch
    {
        ParsimonyTier.Static => "The grammar's own statements",
        ParsimonyTier.Text => "The grammar with attested forms",
        _ => "Completed parser outcomes",
    };

    private static string VerificationLabel(ParsimonyVerification verification) => verification switch
    {
        ParsimonyVerification.Passed => "Passed",
        ParsimonyVerification.Failed => "Failed",
        ParsimonyVerification.Inconclusive => "Inconclusive",
        _ => "Not yet run",
    };

    private void ClearReport()
    {
        _readGeneration++;
        _report = null;
        _reportProjectPath = null;
        _stagedByFinding.Clear();
        _stagedReturns.Clear();
        ClearShown();
        State = Context.AdvancedAiModeEnabled ? ParsimonyPageState.NoProject : ParsimonyPageState.Off;
    }

    private void ClearShown()
    {
        _remainingCancellation?.Cancel();
        _remainingCancellation = null;
        RemainingLoad = Task.CompletedTask;
        LoadingNote = string.Empty;
        _selectedFindingId = null;
        _selectedSuppressedKey = null;
        _report = null;
        _reportProjectPath = null;
        _activeRows = [];
        _suppressedRows = [];
        _activeTotal = 0;
        _suppressedTotal = 0;
        _rows.Clear();
        _rowsByFinding.Clear();
        _headersByMeasure.Clear();
        _suppressedItems.Clear();
        _suppressedByKey.Clear();
        _selectedRow = null;
        _selectedSuppressedItem = null;
        IsIncomplete = false;
        IsHistorical = false;
        MissingInputs = [];
        IncompleteNote = string.Empty;
        AxisSummary = string.Empty;
        AxisLegend = string.Empty;
        Groups = [];
        EvidenceNote = string.Empty;
        JudgmentIssuesText = string.Empty;
        RecordTypes = [];
        SelectedRecordType = null;
        Evidence = null;
        SelectionMessage = string.Empty;
        ActionMessage = string.Empty;
        ReasonText = string.Empty;
        QuestionText = string.Empty;
        FixNote = string.Empty;
        Message = string.Empty;
        NextStep = string.Empty;
        Badge = string.Empty;
        ActionsChanged();
    }

    private void Refuse(string message)
    {
        _selectedFindingId = null;
        _selectedSuppressedKey = null;
        Evidence = null;
        MoveSelection();
        SelectionMessage = message;
    }

    private bool IsCurrent(int generation, string path) =>
        generation == _readGeneration && string.Equals(path, Context.ProjectPath, StringComparison.Ordinal);

    /// <summary>A decision the page staged and that Apply has not yet written.</summary>
    private sealed record StagedDecision(string MeasureId, string EvidenceDigest, string Label);
}
