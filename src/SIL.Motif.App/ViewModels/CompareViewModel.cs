using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What the parser did with a word, as one column of the Compare matrix. A word that stopped at a limit is a
/// Timeout even if it found readings first, exactly as the outcome bar counts it, so the column totals and the bar
/// always agree.
/// </summary>
public enum CompareColumn
{
    /// <summary>The parser rebuilt what the project holds: every approved analysis, or a candidate or rejected one.</summary>
    Match,

    /// <summary>The parser produced readings, but not the ones the project holds.</summary>
    NoMatch,

    /// <summary>The parser finished and found no reading at all.</summary>
    NoParse,

    /// <summary>A time or step limit stopped the search, so nothing can be said either way.</summary>
    Timeout,

    /// <summary>The word was never tried.</summary>
    Skipped,
}

/// <summary>What a matrix cell means for the grammar, which decides its colour.</summary>
public enum CompareFamily
{
    /// <summary>The grammar keeps what a person decided.</summary>
    Good,

    /// <summary>Nothing to do: the grammar agrees with a rejection or a misspelling.</summary>
    Fine,

    /// <summary>A person's decision is broken: an approved analysis lost or replaced, or a rejected one built.</summary>
    Violation,

    /// <summary>The grammar and the project disagree about something no person has ruled on.</summary>
    Review,

    /// <summary>The grammar offers an analysis the project does not have yet.</summary>
    New,

    /// <summary>Neither the project nor the grammar can analyse the word.</summary>
    Nobody,

    /// <summary>A timeout or an untried word: unknown, never a violation.</summary>
    Unknown,

    /// <summary>A combination that the data never produces, shown empty.</summary>
    None,
}

/// <summary>
/// The Results stage's Compare view: every assessed word placed in a five-by-five matrix by what the project held
/// for it and what the parser did, with the matrix as the filter for the word list beneath it. The matrix always
/// counts the whole Assessment; choosing cells, searching and sorting only change which words are listed.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    private readonly List<CompareWordViewModel> _all = [];

    public CompareViewModel()
    {
        Rows = Enum.GetValues<WordProjectStatus>().OrderBy(RowOrder)
            .Select(status => new CompareRowViewModel(status, Enum.GetValues<CompareColumn>()
                .Select(column => new CompareCellViewModel(status, column)).ToArray()))
            .ToArray();
        Columns = Enum.GetValues<CompareColumn>().Select(column => new CompareColumnViewModel(column)).ToArray();
        Presets =
        [
            new ComparePresetViewModel("Violations", CompareFamily.Violation),
            new ComparePresetViewModel("Review", CompareFamily.Review),
            new ComparePresetViewModel("New", CompareFamily.New),
            new ComparePresetViewModel("Nobody can analyse", CompareFamily.Nobody),
            new ComparePresetViewModel("Unknown", CompareFamily.Unknown),
        ];
        ClearSelectionCommand = new RelayCommand(() => Select([], additive: false));
        SelectPresetCommand = new RelayCommand<ComparePresetViewModel>(preset =>
        {
            if (preset is not null) Select(Cells.Where(cell => cell.Family == preset.Family), additive: false);
        });
        SelectRowCommand = new RelayCommand<CompareRowViewModel>(row =>
        {
            if (row is not null) Select(row.Cells, additive: false);
        });
        SelectColumnCommand = new RelayCommand<CompareColumnViewModel>(column =>
        {
            if (column is not null) Select(Cells.Where(cell => cell.Column == column.Column), additive: false);
        });
        OpenWordCommand = new RelayCommand<CompareWordViewModel>(word =>
        {
            if (word is not null) OpenWord?.Invoke(word.Word);
        });
    }

    /// <summary>The five rows, in reading order: nothing stored, candidate, approved, rejected, incorrect spelling.</summary>
    public IReadOnlyList<CompareRowViewModel> Rows { get; }

    public IReadOnlyList<CompareColumnViewModel> Columns { get; }

    public IEnumerable<CompareCellViewModel> Cells => Rows.SelectMany(row => row.Cells);

    /// <summary>Shortcuts that choose every cell of one meaning at once.</summary>
    public IReadOnlyList<ComparePresetViewModel> Presets { get; }

    /// <summary>The words in the chosen cells, or every word when none is chosen, searched and sorted.</summary>
    public ObservableCollection<CompareWordViewModel> Words { get; } = [];

    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand<ComparePresetViewModel> SelectPresetCommand { get; }
    public IRelayCommand<CompareRowViewModel> SelectRowCommand { get; }
    public IRelayCommand<CompareColumnViewModel> SelectColumnCommand { get; }
    public IRelayCommand<CompareWordViewModel> OpenWordCommand { get; }

    /// <summary>Opens a word in the Words view; set by the workspace.</summary>
    public Action<string>? OpenWord { get; set; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CompareSort _sort = CompareSort.MostFrequent;

    public IReadOnlyList<CompareSort> SortChoices { get; } = Enum.GetValues<CompareSort>();

    public int TotalCount => _all.Count;
    public bool HasWords => _all.Count > 0;

    /// <summary>Whether the Assessment recorded what the project held; one that did not cannot fill the rows.</summary>
    public bool HasStandings => _all.Count > 0 && _all.All(word => word.Standing is not null);

    /// <summary>Whether to say the rows are unknown, rather than let every word look as if nothing were stored.</summary>
    public bool ShowsNoStandingsNotice => HasWords && !HasStandings;

    public bool AnySelected => Cells.Any(cell => cell.IsSelected);

    public string SelectionText
    {
        get
        {
            var chosen = Cells.Where(cell => cell.IsSelected).ToList();
            return chosen.Count switch
            {
                0 => "No cells chosen: every word is listed. Click a cell to list only its words; Ctrl-click adds cells.",
                1 => $"{chosen[0].RowLabel} × {chosen[0].ColumnLabel}: {chosen[0].Label}",
                _ => $"{chosen.Count} cells chosen",
            };
        }
    }

    public string ListSummary => Words.Count == TotalCount
        ? $"{TotalCount:N0} word{(TotalCount == 1 ? string.Empty : "s")}"
        : $"{Words.Count:N0} of {TotalCount:N0} words";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSortChanged(CompareSort value) => ApplyFilter();

    /// <summary>Places every word in its cell and clears the selection; <see langword="null"/> empties the matrix.</summary>
    public void Load(IEnumerable<AssessWordRowViewModel>? rows)
    {
        _all.Clear();
        if (rows is not null)
            _all.AddRange(rows.Select(row => new CompareWordViewModel(row, Place(row))));
        foreach (var cell in Cells)
        {
            cell.Count = _all.Count(word => word.Row == cell.Row && word.Column == cell.Column);
            cell.IsSelected = false;
        }
        foreach (var row in Rows) row.Count = row.Cells.Sum(cell => cell.Count);
        foreach (var column in Columns) column.Count = Cells.Where(cell => cell.Column == column.Column).Sum(cell => cell.Count);
        foreach (var preset in Presets) preset.Count = Cells.Where(cell => cell.Family == preset.Family).Sum(cell => cell.Count);
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasWords));
        OnPropertyChanged(nameof(HasStandings));
        OnPropertyChanged(nameof(ShowsNoStandingsNotice));
        SelectionChanged();
    }

    /// <summary>
    /// Chooses <paramref name="cell"/>: alone, or added to or removed from the current choice when
    /// <paramref name="additive"/>, as a Ctrl- or Shift-click does.
    /// </summary>
    public void Toggle(CompareCellViewModel cell, bool additive)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (additive)
        {
            cell.IsSelected = !cell.IsSelected;
            SelectionChanged();
            return;
        }
        var onlyThis = cell.IsSelected && Cells.Count(candidate => candidate.IsSelected) == 1;
        Select(onlyThis ? [] : [cell], additive: false);
    }

    private void Select(IEnumerable<CompareCellViewModel> cells, bool additive)
    {
        var chosen = cells.ToHashSet();
        foreach (var cell in Cells)
            cell.IsSelected = chosen.Contains(cell) || (additive && cell.IsSelected);
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        foreach (var preset in Presets)
            preset.IsActive = preset.Count > 0 && Cells.Where(cell => cell.Count > 0)
                .All(cell => cell.IsSelected == (cell.Family == preset.Family));
        OnPropertyChanged(nameof(AnySelected));
        OnPropertyChanged(nameof(SelectionText));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var chosen = Cells.Where(cell => cell.IsSelected).Select(cell => (cell.Row, cell.Column)).ToHashSet();
        var matches = _all.AsEnumerable();
        if (chosen.Count > 0) matches = matches.Where(word => chosen.Contains((word.Row, word.Column)));
        if (!string.IsNullOrWhiteSpace(SearchText))
            matches = matches.Where(word => word.Word.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        matches = Sort switch
        {
            CompareSort.Alphabetical => matches.OrderBy(word => word.Word, StringComparer.CurrentCulture),
            CompareSort.Slowest => matches.OrderByDescending(word => word.ElapsedMs ?? -1),
            _ => matches.OrderByDescending(word => word.Occurrences ?? 0).ThenBy(word => word.Word, StringComparer.CurrentCulture),
        };
        Words.Clear();
        foreach (var word in matches) Words.Add(word);
        OnPropertyChanged(nameof(ListSummary));
    }

    /// <summary>
    /// Which cell a word belongs in. The column follows the outcome bar exactly; within the parsed words, Match means
    /// the parser rebuilt what the project holds — for an approved word, every approved analysis, as the Approved
    /// expectation requires, so a word that kept only some of them is not Kept.
    /// </summary>
    public static (WordProjectStatus Row, CompareColumn Column) Place(AssessWordRowViewModel word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var row = word.Standing ?? WordProjectStatus.NotPresent;
        if (word.StoppedAtALimit) return (row, CompareColumn.Timeout);
        if (word.Result == "Skipped") return (row, CompareColumn.Skipped);
        if (!word.IsParsed) return (row, CompareColumn.NoParse);

        bool Built(string grade) => word.Readings.Any(reading => reading.Grade == grade);
        var matched = row switch
        {
            WordProjectStatus.Approved => Built("approved") && word.MissedApproved.Count == 0,
            WordProjectStatus.Candidate => Built("candidate"),
            WordProjectStatus.Rejected => Built("disapproved"),
            _ => Built("approved") || Built("candidate") || Built("disapproved"),
        };
        return (row, matched ? CompareColumn.Match : CompareColumn.NoMatch);
    }

    /// <summary>What a cell means and how it is coloured, one entry per combination.</summary>
    public static (string Label, CompareFamily Family) MeaningOf(WordProjectStatus row, CompareColumn column) => column switch
    {
        CompareColumn.Timeout => ("Unknown", CompareFamily.Unknown),
        CompareColumn.Skipped => ("Not tested", CompareFamily.Unknown),
        _ => (row, column) switch
        {
            (WordProjectStatus.NotPresent, CompareColumn.Match) => ("Cannot happen", CompareFamily.None),
            (WordProjectStatus.NotPresent, CompareColumn.NoMatch) => ("New: the parser proposes", CompareFamily.New),
            (WordProjectStatus.NotPresent, _) => ("Nobody can analyse it", CompareFamily.Nobody),
            (WordProjectStatus.Candidate, CompareColumn.Match) => ("Confirms the candidate", CompareFamily.Good),
            (WordProjectStatus.Candidate, CompareColumn.NoMatch) => ("Differs: review", CompareFamily.Review),
            (WordProjectStatus.Candidate, _) => ("Grammar can't build it", CompareFamily.Review),
            (WordProjectStatus.Approved, CompareColumn.Match) => ("Kept", CompareFamily.Good),
            (WordProjectStatus.Approved, CompareColumn.NoMatch) => ("Violation: built other", CompareFamily.Violation),
            (WordProjectStatus.Approved, _) => ("Violation: lost", CompareFamily.Violation),
            (WordProjectStatus.Rejected, CompareColumn.Match) => ("Violation: built anyway", CompareFamily.Violation),
            (WordProjectStatus.Rejected, _) => ("Fine", CompareFamily.Fine),
            (_, CompareColumn.Match) => ("Builds a misspelling", CompareFamily.Review),
            (_, CompareColumn.NoMatch) => ("Over-generates", CompareFamily.Review),
            _ => ("Correct", CompareFamily.Fine),
        },
    };

    /// <summary>The label a row header shows, in the same words as the Texts stage.</summary>
    public static string RowLabelOf(WordProjectStatus row) => row switch
    {
        WordProjectStatus.NotPresent => "Not present",
        _ => WordProjectStatuses.LabelOf(row),
    };

    public static string ColumnLabelOf(CompareColumn column) => column switch
    {
        CompareColumn.Match => "Match",
        CompareColumn.NoMatch => "No match",
        CompareColumn.NoParse => "No parse",
        CompareColumn.Timeout => "Timeout",
        _ => "Skipped",
    };

    /// <summary>The verdict a parse column is drawn with in the word list, so it reads like every other stage.</summary>
    public static Verdict VerdictOf(CompareColumn column) => column switch
    {
        CompareColumn.Match => Verdict.Agrees,
        CompareColumn.NoMatch => Verdict.Differs,
        CompareColumn.NoParse => Verdict.NoResult,
        _ => Verdict.Limit,
    };

    private static int RowOrder(WordProjectStatus row) => row switch
    {
        WordProjectStatus.NotPresent => 0,
        WordProjectStatus.Candidate => 1,
        WordProjectStatus.Approved => 2,
        WordProjectStatus.Rejected => 3,
        _ => 4,
    };
}

/// <summary>How the Compare word list is ordered.</summary>
public enum CompareSort
{
    /// <summary>Most occurrences in the chosen Texts first: a fix there helps the most text.</summary>
    MostFrequent,

    Alphabetical,

    /// <summary>Longest parse first.</summary>
    Slowest,
}

/// <summary>One row of the matrix: what the project held, and its five cells.</summary>
public sealed partial class CompareRowViewModel(WordProjectStatus row, IReadOnlyList<CompareCellViewModel> cells) : ObservableObject
{
    public WordProjectStatus Row { get; } = row;
    public string Label { get; } = CompareViewModel.RowLabelOf(row);
    public Verdict Verdict { get; } = WordProjectStatuses.VerdictOf(row);
    public IReadOnlyList<CompareCellViewModel> Cells { get; } = cells;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _count;

    public string CountText => Count == 1 ? "1 word" : $"{Count:N0} words";
}

/// <summary>One column header of the matrix, with how many words the parser handled that way.</summary>
public sealed partial class CompareColumnViewModel(CompareColumn column) : ObservableObject
{
    public CompareColumn Column { get; } = column;
    public string Label { get; } = CompareViewModel.ColumnLabelOf(column);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _count;

    public string CountText => Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>One combination of what the project held and what the parser did, and how many words fell there.</summary>
public sealed partial class CompareCellViewModel : ObservableObject
{
    public CompareCellViewModel(WordProjectStatus row, CompareColumn column)
    {
        Row = row;
        Column = column;
        (Label, Family) = CompareViewModel.MeaningOf(row, column);
        RowLabel = CompareViewModel.RowLabelOf(row);
        ColumnLabel = CompareViewModel.ColumnLabelOf(column);
    }

    public WordProjectStatus Row { get; }
    public CompareColumn Column { get; }
    public string Label { get; }
    public CompareFamily Family { get; }
    public string RowLabel { get; }
    public string ColumnLabel { get; }

    public bool IsGood => Family == CompareFamily.Good;
    public bool IsFine => Family == CompareFamily.Fine;
    public bool IsViolation => Family == CompareFamily.Violation;
    public bool IsReview => Family == CompareFamily.Review;
    public bool IsNew => Family == CompareFamily.New;
    public bool IsNobody => Family == CompareFamily.Nobody;
    public bool IsNone => Family == CompareFamily.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsEmptyImpossible))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _count;

    /// <summary>No word fell here, so the cell is drawn faintly: still there to read, but not asking for attention.</summary>
    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    private bool _isSelected;

    public string CountText => Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A combination the data cannot produce, and did not: drawn hatched rather than as a zero.</summary>
    public bool IsEmptyImpossible => Family == CompareFamily.None && Count == 0;

    public string AccessibleName => $"{RowLabel}, {ColumnLabel}: {Count} words, {Label}";
}

/// <summary>A shortcut that chooses every cell of one meaning.</summary>
public sealed partial class ComparePresetViewModel(string label, CompareFamily family) : ObservableObject
{
    public string Label { get; } = label;
    public CompareFamily Family { get; } = family;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One word in the Compare list, with the cell it sits in.</summary>
public sealed class CompareWordViewModel
{
    public CompareWordViewModel(AssessWordRowViewModel word, (WordProjectStatus Row, CompareColumn Column) place)
    {
        ArgumentNullException.ThrowIfNull(word);
        Word = word.Word;
        Standing = word.Standing;
        Row = place.Row;
        Column = place.Column;
        Occurrences = word.OccurrenceCount;
        ElapsedMs = word.ElapsedMs;
        (Meaning, Family) = CompareViewModel.MeaningOf(Row, Column);
        RowLabel = CompareViewModel.RowLabelOf(Row);
        RowVerdict = WordProjectStatuses.VerdictOf(Row);
        ColumnLabel = CompareViewModel.ColumnLabelOf(Column);
        ColumnVerdict = CompareViewModel.VerdictOf(Column);
        FirstReading = word.Readings.FirstOrDefault()?.Text ?? string.Empty;
    }

    public string Word { get; }
    public WordProjectStatus? Standing { get; }
    public WordProjectStatus Row { get; }
    public CompareColumn Column { get; }
    public int? Occurrences { get; }
    public string OccurrenceText => Occurrences is { } count ? $"×{count}" : "—";
    public int? ElapsedMs { get; }
    public string Meaning { get; }
    public CompareFamily Family { get; }
    public bool IsViolation => Family == CompareFamily.Violation;
    public string RowLabel { get; }
    public Verdict RowVerdict { get; }
    public string ColumnLabel { get; }
    public Verdict ColumnVerdict { get; }

    /// <summary>The parser's first reading, so a listed word shows what was just calculated for it.</summary>
    public string FirstReading { get; }
}
