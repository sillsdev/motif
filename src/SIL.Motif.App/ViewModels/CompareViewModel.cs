using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Compare matrix on the Texts page places each assessed word in a five-by-five grid according to what the
/// project held and what the parser built. The matrix always counts the whole Assessment; choosing cells, searching
/// and sorting only change which words are listed.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    private readonly List<CompareWordViewModel> _all = [];
    private ChangesViewModel? _changes;
    private string? _focusedWordSearch;

    public CompareViewModel()
    {
        Rows = Enum.GetValues<WordProjectStatus>().OrderBy(RowOrder)
            .Select(status => new CompareRowViewModel(status, Enum.GetValues<CompareColumnKind>()
                .Select(column => new CompareCellViewModel(status, column)).ToArray()))
            .ToArray();
        Columns = Enum.GetValues<CompareColumnKind>().Select(column => new CompareColumnViewModel(column)).ToArray();
        Presets =
        [
            new ComparePresetViewModel("Violations", CompareFamilyKind.Violation),
            new ComparePresetViewModel("Review", CompareFamilyKind.Review),
            new ComparePresetViewModel("New", CompareFamilyKind.New),
            new ComparePresetViewModel("Nobody can analyze", CompareFamilyKind.Nobody),
            new ComparePresetViewModel("Unknown", CompareFamilyKind.Unknown),
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
        RerunCommand = new AsyncRelayCommand(RerunUnknownAsync, () => Rerun is not null && RerunWords.Count > 0);
        HandOffCommand = new RelayCommand(() => HandOff?.Invoke(Words.Select(word => word.Word).ToArray()),
            () => HandOff is not null && Words.Count > 0);
        FocusFixFirstCommand = new RelayCommand<CompareFixFirstViewModel>(item =>
        {
            if (item is not null) FocusWord(item.Word);
        });
        ProposeCommand = new AsyncRelayCommand<string>(async kind =>
        {
            if (kind is null || !CanPropose(kind)) return;
            var chosen = Words.Where(word => word.IsChecked).ToList();
            Changes.BeginCollection();
            foreach (var word in chosen)
            {
                await Changes.AddAsync(kind, word);
                if (Changes.LastRefusal is null && Changes.Snapshot.SkippedWord != word.Word)
                    word.IsChecked = false;
            }
        }, CanPropose);
    }

    /// <summary>
    /// The changes collected so far, to become one Proposal, supplied by the workspace.
    /// </summary>
    public ChangesViewModel Changes
    {
        get => _changes ?? throw new InvalidOperationException("The shared change list has not been assigned.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_changes, value)) return;
            if (_changes is not null) _changes.Items.CollectionChanged -= OnChangesChanged;
            _changes = value;
            _changes.Items.CollectionChanged += OnChangesChanged;
            UpdatePendingMarkers();
            OnPropertyChanged();
        }
    }

    /// <summary>Adds the chosen change for checked words, with opinions restricted to one selected analysis.</summary>
    public IAsyncRelayCommand<string> ProposeCommand { get; }

    private bool CanPropose(string? kind)
    {
        var chosen = Words.Where(word => word.IsChecked).ToArray();
        return kind switch
        {
            ChangeKinds.IncorrectSpelling => chosen.Length > 0,
            ChangeKinds.AddCandidate => chosen.Length > 0 && chosen.All(word => word.ReadingCount > 0),
            ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate =>
                chosen.Length == 1 && chosen[0].SelectedReading is not null,
            _ => false,
        };
    }

    private void OnWordPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompareWordViewModel.IsChecked))
        {
            OnPropertyChanged(nameof(CheckedWordCount));
            OnPropertyChanged(nameof(CheckedWordText));
            CheckedWordsChanged?.Invoke(this, EventArgs.Empty);
        }
        if (e.PropertyName is nameof(CompareWordViewModel.IsChecked) or
            nameof(CompareWordViewModel.SelectedReading)) ProposeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Runs words again with a longer limit and folds the answers into this Assessment; set by its owner.</summary>
    public Func<IReadOnlyList<string>, int, Task>? Rerun { get; set; }

    /// <summary>Writes a Handoff of just these words; set by the workspace.</summary>
    public Action<IReadOnlyList<string>>? HandOff { get; set; }

    /// <summary>Raised whenever the chosen cells change, so other views of the same words can follow them.</summary>
    public event EventHandler? ChosenCellsChanged;

    /// <summary>Raised whenever a word's check mark changes.</summary>
    public event EventHandler? CheckedWordsChanged;

    /// <summary>The words in the chosen cells, or <see langword="null"/> when no cell is chosen.</summary>
    public IReadOnlySet<string>? ChosenWords { get; private set; }

    public IAsyncRelayCommand RerunCommand { get; }
    public IRelayCommand HandOffCommand { get; }

    /// <summary>Seconds each word gets when run again; the project's own limit is what put it here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RerunText))]
    private decimal _rerunSeconds = 30;

    /// <summary>
    /// The timed-out and untried words to run again: those in the chosen cells when any Unknown cell is chosen,
    /// otherwise every one of them.
    /// </summary>
    public IReadOnlyList<string> RerunWords
    {
        get
        {
            var unknown = _all.Where(word => word.Family == CompareFamilyKind.Unknown).ToList();
            var chosen = Cells.Where(cell => cell.IsSelected && cell.Family == CompareFamilyKind.Unknown)
                .Select(cell => (cell.Row, cell.Column)).ToHashSet();
            if (chosen.Count > 0) unknown = unknown.Where(word => chosen.Contains((word.Row, word.Column))).ToList();
            return unknown.Select(word => word.Word).ToArray();
        }
    }

    public bool CanRerun => RerunWords.Count > 0;

    public string RerunText => RerunWords.Count switch
    {
        0 => "Every word finished.",
        1 => "1 word timed out or was not tried.",
        var count => $"{count:N0} words timed out or were not tried.",
    };

    private Task RerunUnknownAsync() =>
        Rerun?.Invoke(RerunWords, (int)Math.Round(RerunSeconds * 1000)) ?? Task.CompletedTask;

    /// <summary>The five rows, in reading order: nothing stored, candidate, approved, rejected, incorrect spelling.</summary>
    public IReadOnlyList<CompareRowViewModel> Rows { get; }

    public IReadOnlyList<CompareColumnViewModel> Columns { get; }

    public IEnumerable<CompareCellViewModel> Cells => Rows.SelectMany(row => row.Cells);

    /// <summary>Shortcuts that choose every cell of one meaning at once.</summary>
    public IReadOnlyList<ComparePresetViewModel> Presets { get; }

    /// <summary>The words in the chosen cells, or every word when none is chosen, searched and sorted.</summary>
    public ObservableCollection<CompareWordViewModel> Words { get; } = [];

    /// <summary>Every ticked word, including rows hidden by the current list filter.</summary>
    public IReadOnlyList<string> CheckedWords => _all.Where(word => word.IsChecked)
        .Select(word => word.Word).ToArray();

    /// <summary>Gets every assessed word placed in any of the given matrix cells, in Assessment order.</summary>
    public IReadOnlyList<string> WordsInCells(IReadOnlyList<TextsListCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var chosen = cells.ToHashSet();
        return _all.Where(word => chosen.Contains(new TextsListCell(word.Row, word.Column)))
            .Select(word => word.Word).ToArray();
    }

    /// <summary>Gets checked words placed in any of the given matrix cells, in Assessment order.</summary>
    public IReadOnlyList<string> CheckedWordsInCells(IReadOnlyList<TextsListCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var chosen = cells.ToHashSet();
        return _all.Where(word => word.IsChecked && chosen.Contains(new TextsListCell(word.Row, word.Column)))
            .Select(word => word.Word).ToArray();
    }

    /// <summary>Every word in a Texts list, regardless of the current search or matrix filter.</summary>
    public IReadOnlyList<string> WordsInFamily(CompareFamilyKind family) => _all
        .Where(word => word.Family == family).Select(word => word.Word).ToArray();

    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand<ComparePresetViewModel> SelectPresetCommand { get; }
    public IRelayCommand<CompareRowViewModel> SelectRowCommand { get; }
    public IRelayCommand<CompareColumnViewModel> SelectColumnCommand { get; }
    public IRelayCommand<CompareWordViewModel> OpenWordCommand { get; }

    public IRelayCommand<CompareFixFirstViewModel> FocusFixFirstCommand { get; }

    /// <summary>Opens a word in the Words view; set by the workspace.</summary>
    public Action<string>? OpenWord { get; set; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CompareSort _sort = CompareSort.MostFrequent;

    public IReadOnlyList<CompareSort> SortChoices { get; } = Enum.GetValues<CompareSort>();

    [ObservableProperty]
    private CompareCountMode _countMode = CompareCountMode.Words;

    public int TotalCount => _all.Count;
    public bool HasWords => _all.Count > 0;

    /// <summary>Whether the Assessment recorded what the project held; one that did not cannot fill the rows.</summary>
    public bool HasStandings => _all.Count > 0 && _all.All(word => word.Standing is not null);

    /// <summary>Whether to say the rows are unknown, rather than let every word look as if nothing were stored.</summary>
    public bool ShowsNoStandingsNotice => HasWords && !HasStandings;

    public IReadOnlyList<CompareFixFirstViewModel> FixFirstRows => _all
        .Where(word => word.FixFirst is not null)
        .OrderBy(word => word.FixFirst!.Rank)
        .ThenByDescending(word => word.Occurrences ?? 0)
        .ThenBy(word => word.Word, StringComparer.CurrentCulture)
        .Select(word => new CompareFixFirstViewModel(word, word.FixFirst!))
        .ToArray();

    public string FixFirstSummary => FixFirstRows.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

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

    public int CheckedWordCount => Words.Count(word => word.IsChecked);

    public string CheckedWordText => CheckedWordCount switch
    {
        0 => "No words selected",
        1 => "1 word selected",
        var count => $"{count:N0} words selected",
    };

    partial void OnSearchTextChanged(string value)
    {
        if (!string.Equals(value, _focusedWordSearch, StringComparison.Ordinal)) _focusedWordSearch = null;
        ApplyFilter();
    }
    partial void OnSortChanged(CompareSort value) => ApplyFilter();
    partial void OnCountModeChanged(CompareCountMode value)
    {
        RefreshCounts();
        OnPropertyChanged(nameof(CountWords));
        OnPropertyChanged(nameof(CountOccurrences));
    }

    public bool CountWords
    {
        get => CountMode == CompareCountMode.Words;
        set { if (value) CountMode = CompareCountMode.Words; }
    }

    public bool CountOccurrences
    {
        get => CountMode == CompareCountMode.Occurrences;
        set { if (value) CountMode = CompareCountMode.Occurrences; }
    }

    /// <summary>Places every word in its cell and clears the selection; <see langword="null"/> empties the matrix.</summary>
    public void Load(IEnumerable<AssessWordRowViewModel>? rows)
    {
        foreach (var word in _all) word.PropertyChanged -= OnWordPropertyChanged;
        _all.Clear();
        if (rows is not null)
            _all.AddRange(rows.Select(row => new CompareWordViewModel(row, Place(row))));
        foreach (var word in _all) word.PropertyChanged += OnWordPropertyChanged;
        foreach (var cell in Cells)
        {
            var words = _all.Where(word => word.Row == cell.Row && word.Column == cell.Column).ToArray();
            cell.SetCounts(words.Length, words.Sum(word => word.Occurrences ?? 0));
            cell.IsSelected = false;
        }
        RefreshCounts();
        UpdatePendingMarkers();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasWords));
        OnPropertyChanged(nameof(HasStandings));
        OnPropertyChanged(nameof(ShowsNoStandingsNotice));
        OnPropertyChanged(nameof(FixFirstRows));
        OnPropertyChanged(nameof(FixFirstSummary));
        SelectionChanged();
    }

    public void FocusWord(CompareWordViewModel word)
    {
        ArgumentNullException.ThrowIfNull(word);
        foreach (var item in _all) item.IsFocused = ReferenceEquals(item, word);
        word.IsExpanded = true;
        _focusedWordSearch = word.Word;
        SearchText = word.Word;
        Select([Cells.Single(cell => cell.Row == word.Row && cell.Column == word.Column)], additive: false);
    }

    public void SelectCells(IEnumerable<TextsListCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _focusedWordSearch = null;
        SearchText = string.Empty;
        var keys = cells.ToHashSet();
        Select(Cells.Where(cell => keys.Contains(new TextsListCell(cell.Row, cell.Column))), additive: false);
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
        var chosen = Cells.Where(cell => cell.IsSelected).Select(cell => (cell.Row, cell.Column)).ToHashSet();
        ChosenWords = chosen.Count == 0 ? null
            : _all.Where(word => chosen.Contains((word.Row, word.Column))).Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
        OnPropertyChanged(nameof(RerunWords));
        OnPropertyChanged(nameof(CanRerun));
        OnPropertyChanged(nameof(RerunText));
        RerunCommand.NotifyCanExecuteChanged();
        foreach (var preset in Presets)
            preset.IsActive = preset.Count > 0 && Cells.Where(cell => cell.Count > 0)
                .All(cell => cell.IsSelected == (cell.Family == preset.Family));
        OnPropertyChanged(nameof(AnySelected));
        OnPropertyChanged(nameof(SelectionText));
        ApplyFilter();
    }

    private void RefreshCounts()
    {
        foreach (var cell in Cells)
            cell.SetDisplayedCount(CountMode == CompareCountMode.Words ? cell.WordCount : cell.OccurrenceCount,
                CountMode == CompareCountMode.Words ? "word" : "occurrence");
        foreach (var row in Rows) row.Count = row.Cells.Sum(cell => cell.Count);
        foreach (var column in Columns) column.Count = Cells.Where(cell => cell.Column == column.Column).Sum(cell => cell.Count);
        foreach (var preset in Presets) preset.Count = Cells.Where(cell => cell.Family == preset.Family).Sum(cell => cell.Count);
    }

    private void OnChangesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        UpdatePendingMarkers();

    private void UpdatePendingMarkers()
    {
        var pending = (_changes?.Items ?? []).GroupBy(change => change.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var word in _all)
        {
            pending.TryGetValue(word.Word, out var changes);
            word.PendingState = PendingChangeStates.FromChanges(changes);
            word.HasPendingChange = word.PendingState != PendingChangeState.None;
        }
        foreach (var cell in Cells)
        {
            var statuses = _all.Where(word => word.Row == cell.Row && word.Column == cell.Column)
                .Select(word => word.PendingState).ToArray();
            cell.PendingState = statuses.Contains(PendingChangeState.NoLongerFits)
                ? PendingChangeState.NoLongerFits
                : statuses.FirstOrDefault(status => status != PendingChangeState.None);
        }
    }

    private void ApplyFilter()
    {
        var chosen = Cells.Where(cell => cell.IsSelected).Select(cell => (cell.Row, cell.Column)).ToHashSet();
        var matches = _all.AsEnumerable();
        if (chosen.Count > 0) matches = matches.Where(word => chosen.Contains((word.Row, word.Column)));
        if (_focusedWordSearch is { } focusedWord)
            matches = matches.Where(word => word.Word == focusedWord);
        else if (!string.IsNullOrWhiteSpace(SearchText))
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
        OnPropertyChanged(nameof(CheckedWordCount));
        OnPropertyChanged(nameof(CheckedWordText));
        HandOffCommand.NotifyCanExecuteChanged();
        ProposeCommand.NotifyCanExecuteChanged();
        ChosenCellsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Which cell a word belongs in. The column follows the outcome bar exactly; within the parsed words, Match means
    /// the parser rebuilt what the project holds — for an approved word, every approved analysis, as the Approved
    /// expectation requires, so a word that kept only some of them is not Kept.
    /// </summary>
    public static (WordProjectStatus Row, CompareColumnKind Column) Place(AssessWordRowViewModel word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var result = CompareSemantics.Place(new CompareWordFacts(
            StandingWire(word.Standing),
            word.Outcome, word.IsIncomplete, word.Morphology,
            word.Readings.Select(reading => reading.Grade).Where(grade => grade is not null).Cast<string>().ToArray(),
            word.MissedApproved.Count));
        var row = WordProjectStatuses.FromStanding(result.Standing);
        return (row, result.Column);
    }

    /// <summary>What a cell means and how it is coloured, one entry per combination.</summary>
    public static (string Label, CompareFamilyKind Family) MeaningOf(WordProjectStatus row, CompareColumnKind column)
    {
        return CompareSemantics.MeaningOf(StandingWire(row), column);
    }

    private static string StandingWire(WordProjectStatus? status) => status switch
    {
        WordProjectStatus.Approved => SIL.Motif.Contract.Responses.ProjectStanding.Approved,
        WordProjectStatus.Candidate => SIL.Motif.Contract.Responses.ProjectStanding.Candidate,
        WordProjectStatus.Rejected => SIL.Motif.Contract.Responses.ProjectStanding.Rejected,
        WordProjectStatus.IncorrectSpelling => SIL.Motif.Contract.Responses.ProjectStanding.IncorrectSpelling,
        _ => SIL.Motif.Contract.Responses.ProjectStanding.NotPresent,
    };

    /// <summary>The label a row header shows, in the same wording as the Words view.</summary>
    public static string RowLabelOf(WordProjectStatus row) => row switch
    {
        WordProjectStatus.NotPresent => "Not stored yet",
        _ => WordProjectStatuses.LabelOf(row),
    };

    public static string ColumnLabelOf(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => "Match",
        CompareColumnKind.NoMatch => "No match",
        CompareColumnKind.NoParse => "No parse",
        CompareColumnKind.Timeout => "Timeout",
        _ => "Skipped",
    };

    /// <summary>The verdict used to mark each parser outcome in a word list.</summary>
    public static Verdict VerdictOf(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => Verdict.Agrees,
        CompareColumnKind.NoMatch => Verdict.Differs,
        CompareColumnKind.NoParse => Verdict.NoResult,
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

/// <summary>What the matrix counts in its cells and summaries.</summary>
public enum CompareCountMode
{
    Words,
    Occurrences,
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
public sealed partial class CompareColumnViewModel(CompareColumnKind column) : ObservableObject
{
    public CompareColumnKind Column { get; } = column;
    public string Label { get; } = CompareViewModel.ColumnLabelOf(column);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _count;

    public string CountText => Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>One combination of what the project held and what the parser did, and how many words fell there.</summary>
public sealed partial class CompareCellViewModel : ObservableObject
{
    public CompareCellViewModel(WordProjectStatus row, CompareColumnKind column)
    {
        Row = row;
        Column = column;
        (Label, Family) = CompareViewModel.MeaningOf(row, column);
        RowLabel = CompareViewModel.RowLabelOf(row);
        ColumnLabel = CompareViewModel.ColumnLabelOf(column);
    }

    public WordProjectStatus Row { get; }
    public CompareColumnKind Column { get; }
    public string Label { get; }
    public CompareFamilyKind Family { get; }
    public string RowLabel { get; }
    public string ColumnLabel { get; }

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _occurrenceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyPropertyChangedFor(nameof(PendingChangeStatus))]
    private PendingChangeState _pendingState;

    public string? PendingChangeStatus => PendingChangeStates.Label(PendingState);

    public bool HasPendingChanges => PendingState != PendingChangeState.None;

    public bool IsGood => Family == CompareFamilyKind.Good;
    public bool IsFine => Family == CompareFamilyKind.Fine;
    public bool IsViolation => Family == CompareFamilyKind.Violation;
    public bool IsReview => Family == CompareFamilyKind.Review;
    public bool IsNew => Family == CompareFamilyKind.New;
    public bool IsNobody => Family == CompareFamilyKind.Nobody;
    public bool IsNone => Family == CompareFamilyKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CountUnit))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsEmptyImpossible))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountUnit))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string _unit = "word";

    public string CountUnit => Count == 1 ? Unit : $"{Unit}s";

    public void SetCounts(int wordCount, int occurrenceCount)
    {
        WordCount = wordCount;
        OccurrenceCount = occurrenceCount;
    }

    public void SetDisplayedCount(int count, string unit)
    {
        Count = count;
        Unit = unit;
    }

    /// <summary>No word fell here, so the cell is drawn faintly: still there to read, but not asking for attention.</summary>
    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    private bool _isSelected;

    public string CountText => Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A combination the data cannot produce, and did not: drawn hatched rather than as a zero.</summary>
    public bool IsEmptyImpossible => Family == CompareFamilyKind.None && Count == 0;

    public string AccessibleName => $"{RowLabel}, {ColumnLabel}: {Count} {CountUnit}, {Label}" +
        (PendingChangeStatus is { } status ? $", {status}" : string.Empty);
}

/// <summary>A shortcut that chooses every cell of one meaning.</summary>
public sealed partial class ComparePresetViewModel(string label, CompareFamilyKind family) : ObservableObject
{
    public string Label { get; } = label;
    public CompareFamilyKind Family { get; } = family;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One word in the Compare list, with the cell it sits in.</summary>
public sealed partial class CompareWordViewModel : ObservableObject
{
    public CompareWordViewModel(AssessWordRowViewModel word, (WordProjectStatus Row, CompareColumnKind Column) place)
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
        MissedApproved = word.MissedApproved;
        FixFirst = word.FixFirst;
        ReadingCount = word.Morphology?.Analyses.Count ?? 0;
        ReadingChoices = word.Morphology?.Analyses.Select((reading, index) =>
            new CompareReadingChoice(index, reading,
                $"Reading {index + 1}: {(index < word.Readings.Count ? word.Readings[index].Text : "Unresolved")}"))
            .ToArray() ?? [];
    }

    public string Word { get; }
    public WordProjectStatus? Standing { get; }
    public WordProjectStatus Row { get; }
    public CompareColumnKind Column { get; }
    public int? Occurrences { get; }
    public string OccurrenceText => Occurrences is { } count ? $"×{count}" : "—";
    public IReadOnlyList<ParserReadingViewModel> MissedApproved { get; }
    public FixFirstPriority? FixFirst { get; }
    public int? ElapsedMs { get; }
    public string Meaning { get; }
    public CompareFamilyKind Family { get; }
    public bool IsViolation => Family == CompareFamilyKind.Violation;
    public string RowLabel { get; }
    public Verdict RowVerdict { get; }
    public string ColumnLabel { get; }
    public Verdict ColumnVerdict { get; }

    /// <summary>The parser's first reading, so a listed word shows what was just calculated for it.</summary>
    public string FirstReading { get; }

    public IReadOnlyList<CompareReadingChoice> ReadingChoices { get; }

    [ObservableProperty]
    private CompareReadingChoice? _selectedReading;

    public int ReadingCount { get; }

    /// <summary>Whether the word is ticked, to receive the next change chosen for ticked words.</summary>
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingChangeStatus))]
    private bool _hasPendingChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingChangeStatus))]
    private PendingChangeState _pendingState;

    public string? PendingChangeStatus => PendingChangeStates.Label(PendingState);

    [ObservableProperty]
    private bool _isFocused;

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>A parser reading chosen by its position in one recorded Assessment word.</summary>
public sealed record CompareReadingChoice(int Index, ParseAnalysis Reading, string Label);

/// <summary>One word in the ranked fix-first list, with the reason it needs attention.</summary>
public sealed record CompareFixFirstViewModel(CompareWordViewModel Word, FixFirstPriority Priority)
{
    public string Category => Priority.Label;

    public string Explanation => Priority.Explanation;

    public string OccurrenceText => Word.Occurrences is { } count ? $"×{count}" : "—";

    public IReadOnlyList<ParserReadingViewModel> MissedApproved => Word.MissedApproved;

    public IReadOnlyList<CompareReadingChoice> ParserReadings => Word.ReadingChoices;
}
