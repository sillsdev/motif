using System.Globalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>How a finding stands between today's grammar check and the open Proposal's.</summary>
public enum ProposalFindingTag
{
    /// <summary>The finding has no standing against the Proposal: both sides have it, or no Proposal is paired.</summary>
    None,

    /// <summary>Today's grammar has the finding and the Proposal's does not.</summary>
    Gone,

    /// <summary>The Proposal's grammar has the finding and today's does not.</summary>
    New,
}

public sealed partial class GrammarWarningsViewModel
{
    private IReadOnlyList<GrammarWarning>? _today;
    private IReadOnlyList<GrammarWarning>? _proposal;
    private MarkupView _proposalView = MarkupView.Today;
    private List<GrammarWarningRowViewModel> _proposalRows = [];
    private List<GrammarWarningRowViewModel> _goneRows = [];

    /// <summary>Whether today's and the Proposal's checks are both held and the view pairs them.</summary>
    public bool Pairs => _proposalView == MarkupView.TodayToProposal && _proposal is not null && _today is not null;

    /// <summary>Whether the rows shown are the Proposal's own check rather than today's.</summary>
    public bool ShowsProposalCheck => _proposal is not null && _proposalView != MarkupView.Today &&
        (_proposalView == MarkupView.Proposal || _today is null);

    /// <summary>How many findings today's check reported, whatever the view shows.</summary>
    public int TodayTotalCount => _proposalView == MarkupView.Today ? TotalCount : _today?.Count ?? 0;

    /// <summary>Whether today's check reported anything, whatever the view shows.</summary>
    public bool TodayHasAny => _proposalView == MarkupView.Today ? HasAny : _today is { Count: > 0 };

    /// <summary>Today's error, warning and information totals, whatever the view shows.</summary>
    public string TodayBreakdownText => _proposalView == MarkupView.Today ? BreakdownText : BreakdownOf(_today ?? []);

    /// <summary>The pair "today → Proposal" while paired, else the shown count.</summary>
    public string AllCountText => CountText(TotalCount, _ => true);

    /// <summary>The error chip's count, paired while the view pairs.</summary>
    public string ErrorCountText => CountText(ErrorCount, GrammarDiagnosticLevel.Error);

    /// <summary>The warning chip's count, paired while the view pairs.</summary>
    public string WarningCountText => CountText(WarningCount, GrammarDiagnosticLevel.Warning);

    /// <summary>The information chip's count, paired while the view pairs.</summary>
    public string InformationCountText => CountText(InformationCount, GrammarDiagnosticLevel.Information);

    /// <summary>How many findings only today's check has, while the view pairs.</summary>
    public int GoneCount => _all.Where(row => row.ProposalTag == ProposalFindingTag.Gone).Sum(row => row.RepeatCount);

    /// <summary>How many findings only the Proposal's check has, while the view pairs.</summary>
    public int NewCount => _all.Where(row => row.ProposalTag == ProposalFindingTag.New).Sum(row => row.RepeatCount);

    /// <summary>Whether the gone and new filters have something to list.</summary>
    public bool ShowsGone => Pairs && GoneCount > 0;

    /// <summary>Whether the new filter has something to list.</summary>
    public bool ShowsNew => Pairs && NewCount > 0;

    /// <summary>
    /// Holds the open Proposal's findings (null when it has no check) and the view to draw them in, and redraws the
    /// rows from the findings already held; nothing is read.
    /// </summary>
    public void ShowProposal(IReadOnlyList<GrammarWarning>? proposal, MarkupView view)
    {
        var newProposal = !ReferenceEquals(_proposal, proposal);
        if (!newProposal && _proposalView == view) return;
        _proposal = proposal;
        _proposalView = view;
        if (newProposal) BuildProposalRows();
        if (Bucket is GrammarFindingBucket.GoneWithProposal or GrammarFindingBucket.NewWithProposal && !Pairs)
            Bucket = GrammarFindingBucket.All;
        Present();
    }

    /// <summary>The text for today's object timing, which only today's rows have.</summary>
    public string ShownObjectTimingDetailText => ShowsProposalCheck || Pairs ? string.Empty : ObjectTimingDetailText;

    // Builds the Proposal's rows, and the gone rows today's findings the Proposal lacks would add to the pair.
    private void BuildProposalRows()
    {
        _proposalRows = RowsOf(_proposal);
        _goneRows = [];
        if (_proposal is null || _today is null) return;
        var proposalKinds = KindsOf(_proposal);
        var todayRows = _today.Select((warning, index) =>
            (Warning: warning, Index: index, Identity: WarningFindingIdentity.ForGrouping(warning)));
        foreach (var group in todayRows.Where(item => item.Identity is not null && !proposalKinds.Contains(item.Identity))
                     .GroupBy(item => item.Identity!))
        {
            var row = new GrammarWarningRowViewModel(group.Select(item => item.Warning).ToArray(), _wordRoutes)
            {
                SourceOrder = _proposalRows.Count + _goneRows.Count,
                ProposalTag = ProposalFindingTag.Gone,
            };
            row.MarkGone();
            _goneRows.Add(row);
        }
    }

    // The rows of the chosen view: the live ones and the ones struck out beside them.
    private (List<GrammarWarningRowViewModel> Live, List<GrammarWarningRowViewModel> Extra) RowsInView()
    {
        List<GrammarWarningRowViewModel> none = [];
        TagProposalRows();
        if (_proposalView == MarkupView.Proposal) return (_proposalRows, none);
        if (_proposalView != MarkupView.TodayToProposal || _proposal is null) return (_todayRows, _todayResolved);
        return (_proposalRows, Pairs ? _goneRows : none);
    }

    // In the pair, the Proposal's findings today's check lacks are new.
    private void TagProposalRows()
    {
        var todayKinds = Pairs ? KindsOf(_today!) : null;
        foreach (var row in _proposalRows)
        {
            var isNew = todayKinds is not null && row.FindingIdentity is not null && !todayKinds.Contains(row.FindingIdentity);
            row.ProposalTag = isNew ? ProposalFindingTag.New : ProposalFindingTag.None;
        }
    }

    // The findings behind the rows in view; today's own and the Proposal's rows stay apart.
    private IReadOnlyList<GrammarWarning> ShownFindings() => _all
        .Where(row => !row.IsGone || row.ProposalTag == ProposalFindingTag.Gone)
        .SelectMany(row => row.Findings).ToArray();

    private static HashSet<string> KindsOf(IReadOnlyList<GrammarWarning> findings) => findings
        .Select(WarningFindingIdentity.ForGrouping).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private bool Reported(GrammarDiagnosticLevel level) =>
        VisibleFindings().Any(row => row.Level == level) || Pairs && _today!.Any(finding => finding.Severity == level);

    // Gone rows are not counted, except under the filter that lists only them.
    private IEnumerable<GrammarWarningRowViewModel> Countable() => Bucket == GrammarFindingBucket.GoneWithProposal
        ? _all.Where(row => row.ProposalTag == ProposalFindingTag.Gone) : VisibleFindings();

    private string CountText(int shown, GrammarDiagnosticLevel level) =>
        CountText(shown, finding => finding.Severity == level);

    private string CountText(int shown, Func<GrammarWarning, bool> today) => Pairs
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewPairFormat,
            _today!.Count(today).ToString("N0", CultureInfo.CurrentCulture), shown.ToString("N0", CultureInfo.CurrentCulture))
        : shown.ToString("N0", CultureInfo.CurrentCulture);

    private string PairedCount(string shown) => Pairs
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewPairFormat,
            _today!.Count.ToString("N0", CultureInfo.CurrentCulture), shown)
        : shown;

    private static string BreakdownOf(IReadOnlyList<GrammarWarning> findings) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.WarningsBreakdownFormat,
        PluralText.Format(MotifAppStrings.WarningsErrorsPattern, findings.Count(f => f.Severity == GrammarDiagnosticLevel.Error)),
        PluralText.Format(MotifAppStrings.WarningsWarningsPattern, findings.Count(f => f.Severity == GrammarDiagnosticLevel.Warning)),
        PluralText.Format(MotifAppStrings.WarningsInformationPattern, findings.Count(f => f.Severity == GrammarDiagnosticLevel.Information)));

    private void RaiseProposalCounts()
    {
        foreach (var name in new[]
        {
            nameof(Pairs), nameof(ShowsProposalCheck), nameof(TodayTotalCount), nameof(TodayHasAny),
            nameof(TodayBreakdownText), nameof(AllCountText), nameof(ErrorCountText), nameof(WarningCountText),
            nameof(InformationCountText), nameof(GoneCount), nameof(NewCount), nameof(ShowsGone), nameof(ShowsNew),
            nameof(CountSummary),
        }) OnPropertyChanged(name);
    }
}

public sealed partial class GrammarWarningRowViewModel
{
    private ProposalFindingTag _proposalTag;

    /// <summary>Whether only today's check has this finding, so the Proposal view draws it struck through.</summary>
    public bool IsGoneWithProposal => _proposalTag == ProposalFindingTag.Gone;

    /// <summary>The short word that marks the row as gone or new beside the Proposal, or empty when it is neither.</summary>
    public string ProposalMarkText => _proposalTag switch
    {
        ProposalFindingTag.Gone => MotifAppStrings.WarningsGoneMark,
        ProposalFindingTag.New => MotifAppStrings.WarningsNewMark,
        _ => string.Empty,
    };

    /// <summary>Whether the row carries a visible gone or new mark.</summary>
    public bool HasProposalMark => _proposalTag != ProposalFindingTag.None;

    /// <summary>The Details button's name for assistive technology, carrying the row's mark.</summary>
    public string DetailsAccessibleName => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.WarningsDetailsForFormat, MarkedTitle());

    /// <summary>The actions button's name for assistive technology, carrying the row's mark.</summary>
    public string ActionsAccessibleName => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.WarningsActionsForFormat, MarkedTitle());

    private string MarkedTitle() => HasProposalMark
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WarningsMarkedRowFormat, RowTitleText, ProposalMarkText)
        : RowTitleText;

    /// <summary>How this finding stands against the open Proposal's check; set before the row is shown.</summary>
    public ProposalFindingTag ProposalTag
    {
        get => _proposalTag;
        internal set
        {
            if (_proposalTag == value) return;
            _proposalTag = value;
            OnPropertyChanged(nameof(ProposalTag));
            OnPropertyChanged(nameof(IsGoneWithProposal));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasRefreshStatus));
            OnPropertyChanged(nameof(ProposalMarkText));
            OnPropertyChanged(nameof(HasProposalMark));
            OnPropertyChanged(nameof(DetailsAccessibleName));
            OnPropertyChanged(nameof(ActionsAccessibleName));
        }
    }
}
