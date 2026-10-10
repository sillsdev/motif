using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
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

/// <summary>One finding row in a measure's group, with its kept or deferred state beside it.</summary>
/// <param name="Description">The finding's counted numbers in words, such as "2 of 3 prohibitions".</param>
/// <param name="Items">The grammar items the finding concerns, or a plain statement that the Report names none.</param>
/// <param name="Disposition">Kept or Deferred when the person decided it, or empty while it is open.</param>
/// <param name="SelectCommand">Shows this finding's evidence in the detail pane.</param>
public sealed record ParsimonyFindingRow(
    string Description, string Items, string Disposition, ParsimonyFindingLink Link, bool IsSelected,
    IRelayCommand<ParsimonyFindingLink> SelectCommand);

/// <summary>The findings of one measure, under the measure's title and its axis.</summary>
/// <param name="Title">The measure's recipe title, or a plain fallback when no recipe exists.</param>
/// <param name="AxisLabel">Restrictiveness, Parsimony or both, kept apart from every other group.</param>
public sealed record ParsimonyMeasureGroup(string Title, string AxisLabel, IReadOnlyList<ParsimonyFindingRow> Items);

/// <summary>
/// One line of the findings list: a measure's header, or one finding under it. Flat, so the page draws one list.
/// </summary>
public sealed record ParsimonyListRow(
    bool IsHeader, string Title, string Detail, string Description, string Items, string Disposition,
    ParsimonyFindingLink? Link, bool IsSelected, IRelayCommand<ParsimonyFindingLink>? SelectCommand, string AutomationId);

/// <summary>The selected finding's evidence: what was counted, how strong the evidence is, and its limitations.</summary>
public sealed record ParsimonyEvidence(
    string Title, IReadOnlyList<InspectorDetail> Details, IReadOnlyList<string> Limitations);

/// <summary>
/// The Parsimony page: the findings of the latest stored Parsimony Report, with the evidence for the one selected.
/// Opening the page and reopening it read the stored Report and its Baseline; nothing here starts a parser run or
/// writes to the project.
/// </summary>
public sealed partial class ParsimonyPageModel : PageModel
{
    private const string NoReportCode = "parsimony.no-report";
    private const string NoReportMessage = "No Parsimony Report yet.";

    private static readonly ParsimonyRecipeCatalog RecipeCatalog = ParsimonyRecipeCatalog.Load();

    private int _readGeneration;
    private ParsimonyReportResponse? _report;
    private string? _reportProjectPath;
    private string? _selectedFindingId;

    public ParsimonyPageModel(WorkspaceContext context) : base(context)
    {
        ReloadCommand = new AsyncRelayCommand(() => ReloadAsync(), () => Context.HasProject);
        SelectFindingCommand = new RelayCommand<ParsimonyFindingLink>(link => { if (link is not null) SelectFinding(link); });
        State = context.AdvancedAiModeEnabled ? ParsimonyPageState.NoProject : ParsimonyPageState.Off;
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

    /// <summary>The findings per axis, each counted on its own; there is no combined score.</summary>
    [ObservableProperty]
    private string _axisSummary = string.Empty;

    /// <summary>The listed findings, one group per measure.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private IReadOnlyList<ParsimonyMeasureGroup> _groups = [];

    /// <summary>The listed findings as one flat list: each measure's header, then its findings.</summary>
    public IReadOnlyList<ParsimonyListRow> Rows =>
    [
        .. Groups.SelectMany(group => new[] { new ParsimonyListRow(true, group.Title, group.AxisLabel, string.Empty,
                string.Empty, string.Empty, null, false, null, string.Empty) }
            .Concat(group.Items.Select(item => new ParsimonyListRow(false, string.Empty, string.Empty, item.Description,
                item.Items, item.Disposition, item.Link, item.IsSelected, item.SelectCommand,
                AutomationIds.ForParsimonyFinding(item.Link.FindingId))))),
    ];

    /// <summary>The selected finding's evidence, or null while none is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidence))]
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

    /// <summary>Whether a finding's evidence is shown in the detail pane.</summary>
    public bool HasEvidence => Evidence is not null;

    /// <summary>Whether the last selection was refused.</summary>
    public bool HasSelectionMessage => SelectionMessage.Length > 0;

    /// <summary>Selects the finding a row names.</summary>
    public IRelayCommand<ParsimonyFindingLink> SelectFindingCommand { get; }

    /// <summary>Reads the stored Report again; it never starts a parser run.</summary>
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

    /// <summary>Selects one finding and shows its evidence, unless the link belongs to another project or Report.</summary>
    public bool SelectFinding(ParsimonyFindingLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var current = _report;
        if (current is null || !string.Equals(link.ProjectPath, Context.ProjectPath, StringComparison.Ordinal) ||
            !string.Equals(link.ProjectPath, _reportProjectPath, StringComparison.Ordinal))
        {
            Refuse("This finding belongs to a project that is no longer open. Open the Parsimony page for this project.");
            return false;
        }
        var finding = current.Findings.FirstOrDefault(item => string.Equals(item.FindingId, link.FindingId, StringComparison.Ordinal) &&
            string.Equals(current.ReportId, link.ReportId, StringComparison.Ordinal));
        if (finding is null)
        {
            Refuse("That finding is no longer in this Report.");
            return false;
        }
        SelectionMessage = string.Empty;
        _selectedFindingId = finding.FindingId;
        Groups = BuildGroups(current, link.ProjectPath);
        Evidence = BuildEvidence(current, finding);
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

        var current = await Context.Commands.GetCurrentBaselineAsync(new CurrentBaselineRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrent(generation, path)) return;
        _report = shown.Value!;
        _reportProjectPath = path;
        IsHistorical = !(current.Succeeded && current.Value?.Token == _report.Inputs.BaselineToken);
        ShowReport(_report, path);
    }

    private void ShowRefusal(Refusal refusal)
    {
        if (string.Equals(refusal.Code, NoReportCode, StringComparison.Ordinal))
        {
            State = ParsimonyPageState.NoReport;
            Message = NoReportMessage;
            NextStep = "Start a Parsimony check from the Advanced AI tools, then come back here. This page only reads what that check stored.";
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
        Groups = BuildGroups(report, path);
        AxisSummary = SummariseAxes(report.Findings);
        State = ParsimonyPageState.Listed;
        Badge = report.Findings.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Message = string.Empty;
        NextStep = string.Empty;
    }

    private IReadOnlyList<ParsimonyMeasureGroup> BuildGroups(ParsimonyReportResponse report, string path)
    {
        var decided = (report.DispositionProjection?.Findings ?? [])
            .Where(row => row.Disposition is not null)
            .ToDictionary(row => row.Finding.FindingId, row => DispositionLabel(row.Disposition!), StringComparer.Ordinal);
        return
        [
            .. report.Findings.GroupBy(finding => finding.MeasureId, StringComparer.Ordinal).Select(group => new ParsimonyMeasureGroup(
                TitleOf(group.Key), AxisLabel(group.First().Axis),
                [.. group.Select(finding => new ParsimonyFindingRow(
                    Describe(finding),
                    ItemsText(finding),
                    decided.GetValueOrDefault(finding.FindingId, string.Empty),
                    new ParsimonyFindingLink(path, report.ReportId, finding.FindingId),
                    finding.FindingId == _selectedFindingId, SelectFindingCommand))])),
        ];
    }

    private static ParsimonyEvidence BuildEvidence(ParsimonyReportResponse report, ParsimonyFinding finding) => new(
        TitleOf(finding.MeasureId),
        InspectorDetail.Recorded(
            ("Counted", Describe(finding)),
            ("Items", ItemsText(finding)),
            ("Why it was reported", $"Reported because {Describe(finding)} match {ShortTitleOf(finding.MeasureId)}."),
            ("Strength of evidence", TierLabel(finding.Tier)),
            ("Paired parser check", VerificationLabel(finding.Verification))),
        finding.Limitations);

    private static string ItemsText(ParsimonyFinding finding) => finding.ItemNames.Count == 0
        ? "The Report does not name the items for this finding."
        : string.Join(", ", finding.ItemNames);

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

    private static string SummariseAxes(IReadOnlyList<ParsimonyFinding> findings)
    {
        if (findings.Count == 0) return "No findings";
        var parts = new List<string>();
        foreach (var axis in new[] { ParsimonyAxis.Parsimony, ParsimonyAxis.Restrictiveness, ParsimonyAxis.Both })
        {
            var count = findings.Count(finding => finding.Axis == axis);
            if (count > 0) parts.Add($"{AxisLabel(axis)}: {count}");
        }
        return string.Join(" · ", parts);
    }

    private static string DispositionLabel(string disposition) => disposition switch
    {
        "keep" => "Kept",
        "defer" => "Deferred",
        "ask" => "Question open",
        "fix" => "Fix planned",
        _ => string.Empty,
    };

    private static string TierLabel(ParsimonyTier tier) => tier switch
    {
        ParsimonyTier.Static => "The grammar's own statements",
        ParsimonyTier.Text => "The grammar with attested forms",
        _ => "Completed parser outcomes",
    };

    private static string TriggerLabel(ParsimonyMeasureThreshold threshold) => threshold.Operator switch
    {
        ParsimonyThresholdOperator.GreaterThan => $"More than {threshold.Value}",
        ParsimonyThresholdOperator.GreaterThanOrEqual => $"At least {threshold.Value}",
        ParsimonyThresholdOperator.Equal => $"Exactly {threshold.Value}",
        _ => "Ranked, with no numeric trigger",
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
        ClearShown();
        State = Context.AdvancedAiModeEnabled ? ParsimonyPageState.NoProject : ParsimonyPageState.Off;
    }

    private void ClearShown()
    {
        _selectedFindingId = null;
        _report = null;
        _reportProjectPath = null;
        IsIncomplete = false;
        IsHistorical = false;
        MissingInputs = [];
        IncompleteNote = string.Empty;
        AxisSummary = string.Empty;
        Groups = [];
        Evidence = null;
        SelectionMessage = string.Empty;
        Message = string.Empty;
        NextStep = string.Empty;
        Badge = string.Empty;
    }

    private void Refuse(string message)
    {
        _selectedFindingId = null;
        Evidence = null;
        Groups = _report is null || _reportProjectPath is null ? [] : BuildGroups(_report, _reportProjectPath);
        SelectionMessage = message;
    }

    private bool IsCurrent(int generation, string path) =>
        generation == _readGeneration && string.Equals(path, Context.ProjectPath, StringComparison.Ordinal);
}
