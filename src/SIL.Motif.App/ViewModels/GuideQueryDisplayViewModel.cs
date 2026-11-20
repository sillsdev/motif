using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.App.ViewModels;

/// <summary>A column a display sorts by.</summary>
public enum GuideQueryColumn
{
    BaselineWord,
    BaselineAnalysis,
    BaselineState,
    ProposalWord,
    ProposalAnalysis,
    ProposalState,
}

/// <summary>Which side's cells a filter reads.</summary>
public enum GuideQueryFilterScope
{
    BothSides,
    Baseline,
    Proposal,
}

/// <summary>One row of a display as its cells show it: each side's word, its analyses and its parse state.</summary>
public sealed class GuideQueryRowViewModel
{
    internal GuideQueryRowViewModel(string id, bool isGroup, string baselineWord, string baselineAnalysis, string baselineState,
        string proposalWord, string proposalAnalysis, string proposalState, string baselinePending, string proposalPending,
        Action<string>? openWord, bool showsDetails, bool showsProposal)
    {
        ShowsDetails = showsDetails;
        ShowsProposal = showsProposal;
        Id = id;
        IsGroup = isGroup;
        BaselineWord = baselineWord;
        BaselineAnalysis = baselineAnalysis;
        BaselineState = baselineState;
        ProposalWord = proposalWord;
        ProposalAnalysis = proposalAnalysis;
        ProposalState = proposalState;
        BaselinePending = baselinePending;
        ProposalPending = proposalPending;
        OpenBaselineWordCommand = new RelayCommand(() => openWord?.Invoke(BaselineWord), () => !IsGroup && BaselineWord.Length > 0 && openWord is not null);
        OpenProposalWordCommand = new RelayCommand(() => openWord?.Invoke(ProposalWord), () => !IsGroup && ProposalWord.Length > 0 && openWord is not null);
    }

    /// <summary>The row's id, unique in the query's answer.</summary>
    public string Id { get; }

    /// <summary>Whether the row shows each word's analyses and parse state beside it.</summary>
    public bool ShowsDetails { get; }

    /// <summary>Whether the row shows the Proposal side.</summary>
    public bool ShowsProposal { get; }

    /// <summary>Whether the row is a group of a grouped query: its first two cells are the key and the count.</summary>
    public bool IsGroup { get; }

    /// <summary>The Baseline word, or empty when that side lacks it.</summary>
    public string BaselineWord { get; }

    /// <summary>The Baseline analyses written as morph cuts, or empty.</summary>
    public string BaselineAnalysis { get; }

    /// <summary>The Baseline parse state in words, or empty.</summary>
    public string BaselineState { get; }

    /// <summary>The Proposal word, or empty when that side lacks it or is pending.</summary>
    public string ProposalWord { get; }

    /// <summary>The Proposal analyses written as morph cuts, or empty.</summary>
    public string ProposalAnalysis { get; }

    /// <summary>The Proposal parse state in words, or empty.</summary>
    public string ProposalState { get; }

    /// <summary>The pending message of the Baseline side, or empty when it was read.</summary>
    public string BaselinePending { get; }

    /// <summary>The pending message of the Proposal side, or empty when it was read.</summary>
    public string ProposalPending { get; }

    /// <summary>Whether the Baseline word opens its analysis.</summary>
    public bool BaselineIsWord => !IsGroup && BaselineWord.Length > 0;

    /// <summary>Whether the Baseline side shows plain text in the word cell: a group key, or a missing word.</summary>
    public bool BaselineIsPlain => !BaselineIsWord && BaselinePending.Length == 0;

    /// <summary>Whether the Baseline side is pending.</summary>
    public bool BaselineIsPending => BaselinePending.Length > 0;

    /// <summary>Whether the Proposal word opens its analysis.</summary>
    public bool ProposalIsWord => !IsGroup && ProposalWord.Length > 0;

    /// <summary>Whether the Proposal side shows plain text in the word cell: a missing word.</summary>
    public bool ProposalIsPlain => !ProposalIsWord && ProposalPending.Length == 0;

    /// <summary>Whether the Proposal side is pending.</summary>
    public bool ProposalIsPending => ProposalPending.Length > 0;

    /// <summary>Opens the Baseline word's analysis.</summary>
    public IRelayCommand OpenBaselineWordCommand { get; }

    /// <summary>Opens the Proposal word's analysis.</summary>
    public IRelayCommand OpenProposalWordCommand { get; }

    /// <summary>What the Baseline word's button tells assistive technology.</summary>
    public string BaselineOpenName => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryOpenWordFormat, BaselineWord);

    /// <summary>What the Proposal word's button tells assistive technology.</summary>
    public string ProposalOpenName => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryOpenWordFormat, ProposalWord);
}

/// <summary>
/// One <c>motif-show</c> of a query: its rows as a table, as counts or as a word list, sorted, filtered and paged on either
/// side. Rows come from a run and are never stored; showing a new run replaces them.
/// </summary>
public sealed partial class GuideQueryDisplayViewModel : ObservableObject
{
    /// <summary>How many rows a page shows.</summary>
    public const int PageSize = 25;

    private readonly Action<string>? _openWord;
    private readonly int? _limit;
    private readonly bool _bothSides;
    private GuideQueryRun? _run;
    private IReadOnlyList<GuideQueryRowViewModel> _matching = [];

    public GuideQueryDisplayViewModel(GuideShowKind kind, int? limit, bool bothSides, Action<string>? openWord)
    {
        Kind = kind;
        _limit = limit is > 0 ? limit : null;
        _bothSides = bothSides;
        _openWord = openWord;
        SortCommand = new RelayCommand<GuideQueryColumn>(Sort);
        PreviousPageCommand = new RelayCommand(() => PageIndex--, () => PageIndex > 0);
        NextPageCommand = new RelayCommand(() => PageIndex++, () => PageIndex < PageCount - 1);
        FilterScopeCommand = new RelayCommand<GuideQueryFilterScope>(scope => FilterScope = scope);
    }

    /// <summary>How the directive shows the query.</summary>
    public GuideShowKind Kind { get; }

    /// <summary>Whether the display is a table: words, analyses and parse states.</summary>
    public bool IsTable => Kind == GuideShowKind.Table;

    /// <summary>Whether the display is a word list: the words alone.</summary>
    public bool IsList => Kind == GuideShowKind.List;

    /// <summary>Whether the display is counts only.</summary>
    public bool IsCounts => Kind == GuideShowKind.Counts;

    /// <summary>Whether the display shows rows, as a table or a list.</summary>
    public bool ShowsRows => Kind != GuideShowKind.Counts;

    /// <summary>Whether the analysis and parse state columns show.</summary>
    public bool ShowsDetails => Kind == GuideShowKind.Table;

    /// <summary>Whether the Baseline side's columns show.</summary>
    public bool ShowsBaseline => true;

    /// <summary>Whether the Proposal side's columns show: only in a Proposal's context.</summary>
    public bool ShowsProposal => _bothSides;

    /// <summary>Whether the sort and filter buttons for both sides show.</summary>
    public bool CanChooseSide => _bothSides;

    /// <summary>Whether the display shows grouped rows rather than words.</summary>
    public bool IsGrouped => _run?.Groups.Count > 0 && _run.Rows.Count == 0;

    /// <summary>The Baseline words' header, or the group key's when the query grouped.</summary>
    public string BaselineWordHeader => IsGrouped ? MotifAppStrings.GuideQueryGroupHeader : MotifAppStrings.GuideQueryBaselineWord;

    /// <summary>The Baseline analyses' header, or the group count's when the query grouped.</summary>
    public string BaselineAnalysisHeader => IsGrouped ? MotifAppStrings.GuideQueryGroupCountHeader : MotifAppStrings.GuideQueryBaselineAnalysis;

    /// <summary>The rows of the current page.</summary>
    public ObservableCollection<GuideQueryRowViewModel> Rows { get; } = [];

    /// <summary>The Baseline side's pending message, or empty when it was read or not yet run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBaselinePending))]
    private string _baselinePending = "";

    /// <summary>Whether the Baseline side is pending.</summary>
    public bool HasBaselinePending => BaselinePending.Length > 0;

    /// <summary>The Proposal side's pending message, or empty when it was read or not yet run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProposalPending))]
    private string _proposalPending = "";

    /// <summary>Whether the Proposal side is pending.</summary>
    public bool HasProposalPending => ProposalPending.Length > 0;

    /// <summary>The counts a counts display shows, one line per fact.</summary>
    [ObservableProperty]
    private string _countsText = "";

    /// <summary>Text the filter keeps rows by, in either side's word or analyses.</summary>
    [ObservableProperty]
    private string _filterText = "";

    /// <summary>Which side the filter reads.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterOnBothSides), nameof(FilterOnBaseline), nameof(FilterOnProposal))]
    private GuideQueryFilterScope _filterScope = GuideQueryFilterScope.BothSides;

    /// <summary>Whether the filter reads both sides.</summary>
    public bool FilterOnBothSides => FilterScope == GuideQueryFilterScope.BothSides;

    /// <summary>Whether the filter reads the Baseline side only.</summary>
    public bool FilterOnBaseline => FilterScope == GuideQueryFilterScope.Baseline;

    /// <summary>Whether the filter reads the Proposal side only.</summary>
    public bool FilterOnProposal => FilterScope == GuideQueryFilterScope.Proposal;

    /// <summary>The column rows are sorted by, or null for the order the run returned.</summary>
    [ObservableProperty]
    private GuideQueryColumn? _sortColumn;

    /// <summary>Whether the sort runs from the largest to the smallest.</summary>
    [ObservableProperty]
    private bool _sortDescending;

    /// <summary>The zero-based page shown.</summary>
    [ObservableProperty]
    private int _pageIndex;

    /// <summary>The page position, such as "1 to 25 of 134", or what is missing when there are no rows.</summary>
    [ObservableProperty]
    private string _pageText = "";

    /// <summary>The number of pages the matching rows fill.</summary>
    public int PageCount => Math.Max(1, (_matching.Count + PageSize - 1) / PageSize);

    /// <summary>Whether there is more than one page.</summary>
    public bool HasPages => PageCount > 1;

    /// <summary>Sorts by a column; the same column again reverses the sort.</summary>
    public IRelayCommand<GuideQueryColumn> SortCommand { get; }

    /// <summary>Shows the page before.</summary>
    public IRelayCommand PreviousPageCommand { get; }

    /// <summary>Shows the page after.</summary>
    public IRelayCommand NextPageCommand { get; }

    /// <summary>Chooses which side the filter reads.</summary>
    public IRelayCommand<GuideQueryFilterScope> FilterScopeCommand { get; }

    /// <summary>Replaces what the display shows with <paramref name="run"/>, or with nothing.</summary>
    public void Show(GuideQueryRun? run)
    {
        _run = run;
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(BaselineWordHeader));
        OnPropertyChanged(nameof(BaselineAnalysisHeader));
        BaselinePending = SideText(run, "baseline", MotifAppStrings.GuideQueryBaselineSide);
        ProposalPending = _bothSides ? SideText(run, "proposal", MotifAppStrings.GuideQueryProposalSide) : "";
        Rebuild(resetPage: true);
    }

    partial void OnFilterTextChanged(string value) => Rebuild(resetPage: true);

    partial void OnFilterScopeChanged(GuideQueryFilterScope value) => Rebuild(resetPage: true);

    partial void OnPageIndexChanged(int value)
    {
        FillPage();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void Sort(GuideQueryColumn column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        Rebuild(resetPage: true);
    }

    /// <summary>Applies the sort a query definition names, when it names a column this display has.</summary>
    public void SortBy(GuideSort? sort)
    {
        if (sort is null) return;
        var column = sort.Column.ToLowerInvariant() switch
        {
            "form" or "word" or "baseline.form" or "baseline.word" => GuideQueryColumn.BaselineWord,
            "proposal.form" or "proposal.word" => GuideQueryColumn.ProposalWord,
            "analysis" or "baseline.analysis" => GuideQueryColumn.BaselineAnalysis,
            "proposal.analysis" => GuideQueryColumn.ProposalAnalysis,
            "parse-state" or "baseline.parse-state" => GuideQueryColumn.BaselineState,
            "proposal.parse-state" => GuideQueryColumn.ProposalState,
            "count" => GuideQueryColumn.BaselineAnalysis,
            "key" => GuideQueryColumn.BaselineWord,
            _ => (GuideQueryColumn?)null,
        };
        if (column is null) return;
        SortColumn = column;
        SortDescending = sort.Descending;
        Rebuild(resetPage: true);
    }

    private string SideText(GuideQueryRun? run, string side, string label)
    {
        if (run?.StateOf(side) is not { IsPending: true } state) return "";
        var reason = state.Reason switch
        {
            "no-trial" => MotifAppStrings.GuideQueryPendingNoTrial,
            "trial-not-completed" => MotifAppStrings.GuideQueryPendingTrialRunning,
            "trial-cleared" => MotifAppStrings.GuideQueryPendingTrialCleared,
            "no-baseline" => MotifAppStrings.GuideQueryPendingNoBaseline,
            _ => MotifAppStrings.GuideQueryPendingUnknown,
        };
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryPendingFormat, label, reason);
    }

    private void Rebuild(bool resetPage)
    {
        var all = BuildRows();
        IEnumerable<GuideQueryRowViewModel> rows = all.Where(Matches);
        if (SortColumn is { } column) rows = Sorted(rows, column);
        var matching = rows.ToList();
        if (_limit is { } limit && matching.Count > limit) matching = matching.Take(limit).ToList();
        _matching = matching;
        CountsText = CountsOf(all, matching);
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasPages));
        if (resetPage && PageIndex != 0) PageIndex = 0;
        else FillPage();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void FillPage()
    {
        Rows.Clear();
        foreach (var row in _matching.Skip(PageIndex * PageSize).Take(PageSize)) Rows.Add(row);
        PageText = _run is null ? MotifAppStrings.GuideQueryNotRun : _matching.Count == 0 ? MotifAppStrings.GuideQueryNoRows
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryPageFormat, PageIndex * PageSize + 1,
                Math.Min((PageIndex + 1) * PageSize, _matching.Count), _matching.Count);
    }

    private List<GuideQueryRowViewModel> BuildRows()
    {
        var rows = new List<GuideQueryRowViewModel>();
        if (_run is null) return rows;
        foreach (var row in _run.Rows)
            rows.Add(new GuideQueryRowViewModel(row.Id, false, row.Baseline?.Form ?? "", AnalysisText(row.Baseline),
                row.Baseline is null ? "" : StateText(row.Baseline.ParseState), row.Proposal?.Form ?? "", AnalysisText(row.Proposal),
                row.Proposal is null ? "" : StateText(row.Proposal.ParseState), BaselinePending, ProposalPending, _openWord, ShowsDetails, _bothSides));
        foreach (var group in _run.Groups)
            if (_run.Rows.Count == 0)
                rows.Add(new GuideQueryRowViewModel("group:" + group.Key, true, group.Key,
                    group.Count.ToString(CultureInfo.CurrentCulture), "", "", "", "", "", "", null, ShowsDetails, _bothSides));
        return rows;
    }

    private bool Matches(GuideQueryRowViewModel row)
    {
        if (FilterText.Length == 0) return true;
        bool Has(string text) => text.Contains(FilterText, StringComparison.CurrentCultureIgnoreCase);
        var baseline = Has(row.BaselineWord) || Has(row.BaselineAnalysis);
        var proposal = Has(row.ProposalWord) || Has(row.ProposalAnalysis);
        return FilterScope switch
        {
            GuideQueryFilterScope.Baseline => baseline,
            GuideQueryFilterScope.Proposal => proposal,
            _ => baseline || proposal,
        };
    }

    private IEnumerable<GuideQueryRowViewModel> Sorted(IEnumerable<GuideQueryRowViewModel> rows, GuideQueryColumn column)
    {
        string Key(GuideQueryRowViewModel row) => column switch
        {
            GuideQueryColumn.BaselineWord => row.BaselineWord,
            GuideQueryColumn.BaselineAnalysis => row.BaselineAnalysis,
            GuideQueryColumn.BaselineState => row.BaselineState,
            GuideQueryColumn.ProposalWord => row.ProposalWord,
            GuideQueryColumn.ProposalAnalysis => row.ProposalAnalysis,
            _ => row.ProposalState,
        };
        var comparer = Comparer<string>.Create((left, right) =>
            int.TryParse(left, NumberStyles.None, CultureInfo.CurrentCulture, out var a) &&
            int.TryParse(right, NumberStyles.None, CultureInfo.CurrentCulture, out var b)
                ? a.CompareTo(b) : string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase));
        // A row that lacks the side sorts last whichever way the sort runs.
        var present = rows.Where(row => Key(row).Length > 0);
        var absent = rows.Where(row => Key(row).Length == 0);
        var ordered = SortDescending ? present.OrderByDescending(Key, comparer) : present.OrderBy(Key, comparer);
        return ordered.Concat(absent);
    }

    private string CountsOf(IReadOnlyList<GuideQueryRowViewModel> all, IReadOnlyList<GuideQueryRowViewModel> matching)
    {
        if (_run is null) return "";
        var lines = new List<string>();
        if (IsGrouped)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryGroupsCountFormat, matching.Count));
            return string.Join("\n", lines);
        }
        lines.Add(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GuideQueryWordsCountFormat, matching.Count));
        AddStates(lines, MotifAppStrings.GuideQueryBaselineSide, matching.Select(row => row.BaselineState), BaselinePending);
        if (_bothSides) AddStates(lines, MotifAppStrings.GuideQueryProposalSide, matching.Select(row => row.ProposalState), ProposalPending);
        return string.Join("\n", lines);
    }

    private static void AddStates(List<string> lines, string side, IEnumerable<string> states, string pending)
    {
        if (pending.Length > 0) { lines.Add(side + ": " + pending); return; }
        var tallies = states.Where(state => state.Length > 0).GroupBy(state => state).OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.Key + " " + group.Count().ToString(CultureInfo.CurrentCulture));
        lines.Add(side + ": " + string.Join(", ", tallies));
    }

    private static string AnalysisText(GuideQueryWord? word)
    {
        if (word is null || word.Analyses.Count == 0) return "";
        return string.Join("; ", word.Analyses.Select(reading => reading.Glosses.Length > 0 ? reading.Cut + " (" + reading.Glosses + ")" : reading.Cut));
    }

    private static string StateText(string state) => state switch
    {
        "approved" => MotifAppStrings.GuideQueryStateApproved,
        "parsed" => MotifAppStrings.GuideQueryStateParsed,
        "unparsed" => MotifAppStrings.GuideQueryStateUnparsed,
        "failed" => MotifAppStrings.GuideQueryStateFailed,
        "absent" => MotifAppStrings.GuideQueryStateAbsent,
        _ => state,
    };
}
