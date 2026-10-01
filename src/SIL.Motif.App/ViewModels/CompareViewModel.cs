using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
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
    private static readonly IReadOnlyList<OpinionLegendItem> OpinionLegendItems =
    [
        new(OpinionMarkKind.Approved, WindowWords.Of(OpinionMarkKind.Approved)),
        new(OpinionMarkKind.Unknown, WindowWords.Of(OpinionMarkKind.Unknown)),
        new(OpinionMarkKind.Disapproved, WindowWords.Of(OpinionMarkKind.Disapproved)),
        new(OpinionMarkKind.None, WindowWords.Of(OpinionMarkKind.None)),
    ];

    private static readonly IReadOnlyList<PanGlossLegendItem> PanGlossLegendItems =
    [
        new(AnalysisMarkingClass.Same, WindowWords.Of(ParserOutcome.Same)),
        new(AnalysisMarkingClass.Different, WindowWords.Of(ParserOutcome.Different)),
        new(AnalysisMarkingClass.None, WindowWords.Of(ParserOutcome.NoParse)),
        new(AnalysisMarkingClass.Capped, WindowWords.Of(ParserOutcome.Stopped)),
        new(AnalysisMarkingClass.NotAssessed, WindowWords.Of(ParserOutcome.NotParsed)),
    ];

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
            Preset("Lost", cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoParse),
            Preset("Built something else",
                cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoMatch),
            Preset("Have a look", cell => cell.Family == CompareFamilyKind.Review),
            Preset("Built anyway", cell => cell.Row == WordProjectStatus.Rejected && cell.Column == CompareColumnKind.Match),
            Preset("New: PanGloss proposes", cell => cell.Family == CompareFamilyKind.New),
            Preset("Nobody can analyze", cell => cell.Family == CompareFamilyKind.Nobody),
            Preset("Stopped", cell => cell.Column == CompareColumnKind.Timeout),
            Preset("Not parsed", cell => cell.Column == CompareColumnKind.Skipped),
        ];
        ClearSelectionCommand = new RelayCommand(() => Select([], additive: false));
        SelectPresetCommand = new RelayCommand<ComparePresetViewModel>(preset =>
        {
            if (preset is not null) Select(preset.Cells, additive: false);
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
            using var usageAction = Changes.BeginMatrixStagingAction(kind, chosen);
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

    /// <summary>
    /// Adds a bulk change for the checked words: their parser readings as Unknown, or an Incorrect spelling. Opinions
    /// change one analysis at a time, in the text (ADR 0049), so no opinion kind is accepted here.
    /// </summary>
    public IAsyncRelayCommand<string> ProposeCommand { get; }

    private bool CanPropose(string? kind)
    {
        var chosen = Words.Where(word => word.IsChecked).ToArray();
        return kind switch
        {
            ChangeKinds.IncorrectSpelling => chosen.Length > 0,
            ChangeKinds.AddCandidate => chosen.Length > 0 && chosen.All(word => word.ReadingCount > 0),
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
        if (e.PropertyName == nameof(CompareWordViewModel.IsChecked)) ProposeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Runs words again with a longer limit and folds the answers into this Assessment; set by its owner.</summary>
    public Func<IReadOnlyList<string>, int, Task>? Rerun { get; set; }

    /// <summary>Writes a Handoff of just these words; set by the workspace.</summary>
    public Action<IReadOnlyList<string>>? HandOff { get; set; }

    /// <summary>Raised whenever the chosen cells change, so other views of the same words can follow them.</summary>
    public event EventHandler? ChosenCellsChanged;

    /// <summary>Raised when checked words change or a load replaces the comparison.</summary>
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

    public IReadOnlyList<OpinionLegendItem> OpinionLegend => OpinionLegendItems;

    public IReadOnlyList<PanGlossLegendItem> PanGlossLegend => PanGlossLegendItems;

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

    /// <summary>Every word a shortcut chooses, regardless of the current search or matrix filter.</summary>
    public IReadOnlyList<string> WordsInPreset(ComparePresetViewModel preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return WordsInCells(preset.Cells.Select(cell => new TextsListCell(cell.Row, cell.Column)).ToArray());
    }

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

    /// <summary>Whether any word needs fixing first; an empty list is hidden rather than shown open with 0.</summary>
    public bool HasFixFirst => _all.Any(word => word.FixFirst is not null);

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

    /// <summary>The one cell chosen, which the list's heading names with its marks; otherwise <see langword="null"/>.</summary>
    public CompareCellViewModel? ChosenCell => Cells.Where(cell => cell.IsSelected).Take(2).ToArray() is [var only] ? only : null;

    /// <summary>What the list holds, named for the chosen cells: the one cell's opinion and outcome, or how many.</summary>
    public string ListHeading
    {
        get
        {
            var chosen = Cells.Count(cell => cell.IsSelected);
            return chosen switch
            {
                0 => "All words",
                1 => $"{ChosenCell!.RowLabel} × {ChosenCell.ColumnLabel}",
                _ => $"{chosen:N0} cells chosen",
            };
        }
    }

    /// <summary>The one line under the heading: what the chosen cell means, or how to choose cells.</summary>
    public string ListExplanation => Cells.Count(cell => cell.IsSelected) switch
    {
        0 => "Choose a cell to list only its words; Ctrl-click adds cells.",
        1 => ChosenCell!.Explanation ?? string.Empty,
        _ => "Ctrl-click a cell to add it or take it away.",
    };

    /// <summary>
    /// How many words are listed and the places they occur, out of the chosen cells' words when a search narrows them.
    /// </summary>
    public string ListSummary
    {
        get
        {
            var chosen = Cells.Where(cell => cell.IsSelected).ToArray();
            var inCells = chosen.Length == 0 ? TotalCount : chosen.Sum(cell => cell.WordCount);
            var words = Words.Count == inCells
                ? CompareCellViewModel.WordsText(Words.Count) : $"{Words.Count:N0} of {inCells:N0} words";
            return Words.Any(word => word.Occurrences is not null)
                ? $"{words} · {CompareCellViewModel.PlacesTextOf(Words.Sum(word => word.Occurrences ?? 0))}" : words;
        }
    }

    /// <summary>The AI Handoff button's words, naming how many listed words it sends.</summary>
    public string HandOffLabel => Words.Count switch
    {
        0 => "AI Handoff for these words",
        1 => "AI Handoff for this word",
        var count => $"AI Handoff for these {count:N0} words",
    };

    /// <summary>
    /// The morphemes at least two listed words use, matched by identity, most words first. It stays empty until a cell
    /// is chosen, because what the whole Selection shares is no clue to one cause.
    /// </summary>
    public ObservableCollection<CompareSharedMorphemeViewModel> Shared { get; } = [];

    public bool HasShared => Shared.Count > 0;

    // A cell of many words can share hundreds of morphemes; the strip is a first clue, not an inventory.
    private const int SharedShown = 8;

    /// <summary>How many more shared morphemes there are than the strip shows; null for none.</summary>
    [ObservableProperty]
    private string? _sharedMoreText;

    public int CheckedWordCount => Words.Count(word => word.IsChecked);

    public string CheckedWordText => CheckedWordCount switch
    {
        0 => "No words ticked",
        1 => "1 word ticked",
        var count => $"{count:N0} words ticked",
    };

    partial void OnSearchTextChanged(string value)
    {
        if (!string.Equals(value, _focusedWordSearch, StringComparison.Ordinal)) _focusedWordSearch = null;
        ApplyFilter();
    }
    partial void OnSortChanged(CompareSort value) => ApplyFilter();

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
            cell.SetCounts(words.Length,
                words.Any(word => word.Occurrences is not null) ? words.Sum(word => word.Occurrences ?? 0) : null);
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
        OnPropertyChanged(nameof(HasFixFirst));
        SelectionChanged();
        CheckedWordsChanged?.Invoke(this, EventArgs.Empty);
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
                .All(cell => cell.IsSelected == preset.Cells.Contains(cell));
        OnPropertyChanged(nameof(AnySelected));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(ChosenCell));
        OnPropertyChanged(nameof(ListHeading));
        OnPropertyChanged(nameof(ListExplanation));
        ApplyFilter();
    }

    private void RefreshCounts()
    {
        foreach (var row in Rows) row.Count = row.Cells.Sum(cell => cell.Count);
        foreach (var column in Columns) column.Count = Cells.Where(cell => cell.Column == column.Column).Sum(cell => cell.Count);
        foreach (var preset in Presets) preset.Count = preset.Cells.Sum(cell => cell.Count);
    }

    private void RefreshShared(bool anyCellChosen)
    {
        Shared.Clear();
        // Assessment order, not the list's sort, so re-sorting the list never reorders what the words share.
        var listed = _all.Where(Words.ToHashSet().Contains).ToArray();
        var shared = anyCellChosen && listed.Length >= 2
            ? ObjectUsesQuery.SharedBy(listed.Select(word => word.Source).ToArray(), listed.Select(word => word.Word).ToArray())
            : [];
        foreach (var morpheme in shared.Take(SharedShown))
            Shared.Add(new CompareSharedMorphemeViewModel(morpheme.Morpheme.Form, morpheme.Morpheme.Gloss, morpheme.Count));
        SharedMoreText = shared.Count > SharedShown ? $"and {shared.Count - SharedShown:N0} more" : null;
        OnPropertyChanged(nameof(HasShared));
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
                : statuses.Contains(PendingChangeState.Uncertain) ? PendingChangeState.Uncertain
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
        RefreshShared(chosen.Count > 0);
        OnPropertyChanged(nameof(ListSummary));
        OnPropertyChanged(nameof(HandOffLabel));
        OnPropertyChanged(nameof(CheckedWordCount));
        OnPropertyChanged(nameof(CheckedWordText));
        HandOffCommand.NotifyCanExecuteChanged();
        ProposeCommand.NotifyCanExecuteChanged();
        ChosenCellsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Places the word from its morphology-backed marking class. Rebuilt rejected readings still occupy
    /// Match when every parser reading matches a stored analysis, so the rejected row names that conflict.
    /// </summary>
    public static (WordProjectStatus Row, CompareColumnKind Column) Place(AssessWordRowViewModel word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var row = word.Standing ?? WordProjectStatus.NotPresent;
        var column = word.StoppedAtALimit ? CompareColumnKind.Timeout
            : word.Outcome == "skipped" ? CompareColumnKind.Skipped
            : !word.IsParsed || word.ReadingCount == 0 ? CompareColumnKind.NoParse
            : word.Marking.PanGlossClass switch
        {
            AnalysisMarkingClass.Same => CompareColumnKind.Match,
            AnalysisMarkingClass.Conflict when word.Marking.PanGlossReadings.Count > 0 &&
                word.Marking.PanGlossReadings.All(reading => reading.MatchesStored) &&
                word.Marking.FieldWorksAnalyses.Where(analysis => analysis.Opinion == ReadingGrade.Approved)
                    .All(analysis => word.Marking.PanGlossReadings.Any(reading =>
                        reading.MatchingAnalysisIds.Contains(analysis.StoredAnalysisId, StringComparer.Ordinal)))
                => CompareColumnKind.Match,
            AnalysisMarkingClass.None => CompareColumnKind.NoParse,
            AnalysisMarkingClass.Capped => CompareColumnKind.Timeout,
            AnalysisMarkingClass.NotAssessed => CompareColumnKind.Skipped,
            _ => CompareColumnKind.NoMatch,
        };
        return (row, column);
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

    /// <summary>
    /// What a cell's words have in common, in one line of window words; <see langword="null"/> for the one cell no
    /// word can reach, a word FieldWorks lacks that PanGloss built the same as.
    /// </summary>
    public static string? ExplanationOf(WordProjectStatus row, CompareColumnKind column) => (row, column) switch
    {
        (_, CompareColumnKind.Timeout) => "PanGloss stopped at a limit before it finished these.",
        (_, CompareColumnKind.Skipped) => "PanGloss hasn't parsed these yet.",
        (WordProjectStatus.Approved, CompareColumnKind.Match) => "You approved these in FieldWorks; the grammar builds the same.",
        (WordProjectStatus.Approved, CompareColumnKind.NoMatch) =>
            "You approved these in FieldWorks; the grammar builds something else.",
        (WordProjectStatus.Approved, _) => "You approved these in FieldWorks; the grammar builds nothing for them.",
        (WordProjectStatus.Candidate, CompareColumnKind.Match) => "These are Unknown in FieldWorks; the grammar builds the same.",
        (WordProjectStatus.Candidate, CompareColumnKind.NoMatch) =>
            "These are Unknown in FieldWorks; the grammar builds something else.",
        (WordProjectStatus.Candidate, _) => "These are Unknown in FieldWorks; the grammar builds nothing for them.",
        (WordProjectStatus.Rejected, CompareColumnKind.Match) =>
            "You disapproved these in FieldWorks; the grammar still builds them.",
        (WordProjectStatus.Rejected, CompareColumnKind.NoMatch) =>
            "You disapproved these in FieldWorks; the grammar builds something else.",
        (WordProjectStatus.Rejected, _) => "You disapproved these in FieldWorks; the grammar doesn't build them.",
        (WordProjectStatus.IncorrectSpelling, CompareColumnKind.NoParse) =>
            "You marked these as incorrect spellings; the grammar doesn't build them.",
        (WordProjectStatus.IncorrectSpelling, _) =>
            "You marked these as incorrect spellings; the grammar still builds them.",
        (_, CompareColumnKind.Match) => null,
        (_, CompareColumnKind.NoMatch) => "FieldWorks has no analysis for these; the grammar proposes one.",
        _ => "Neither FieldWorks nor the grammar can analyze these.",
    };

    /// <summary>The label a row header shows, in FieldWorks' opinion words.</summary>
    public static string RowLabelOf(WordProjectStatus row) => OpinionLabelOf(row);

    /// <summary>The label a column header shows: the outcome's word.</summary>
    public static string ColumnLabelOf(CompareColumnKind column) => WindowWords.Of(WindowWords.OutcomeOf(column));

    public static OpinionMarkKind OpinionMarkFor(WordProjectStatus row) => WindowWords.OpinionOf(row);

    public static string OpinionLabelOf(WordProjectStatus row) => WindowWords.LabelOf(row);

    public static string ColumnSentenceOf(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => "PanGloss finds the same",
        CompareColumnKind.NoMatch => "PanGloss finds something different",
        CompareColumnKind.NoParse => "PanGloss found no parse",
        CompareColumnKind.Timeout => "PanGloss stopped at a limit",
        _ => "PanGloss has not parsed the word",
    };

    /// <summary>What FieldWorks holds, for an accessible name; a missing word never reads "in FieldWorks" twice.</summary>
    public static string HeldInFieldWorks(string opinions) =>
        opinions == OpinionLabelOf(WordProjectStatus.NotPresent) ? opinions : $"{opinions} in FieldWorks";

    /// <summary>The outcome's word for one occurrence; readings beyond the approved ones say so.</summary>
    public static string PanGlossClassLabel(AnalysisMarkingClass markingClass) =>
        markingClass == AnalysisMarkingClass.Extra
            ? "Different, and more"
            : WindowWords.Of(WindowWords.OutcomeOf(markingClass));

    private ComparePresetViewModel Preset(string label, Func<CompareCellViewModel, bool> chooses) =>
        new(label, Cells.Where(chooses).ToArray());

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
    public string OpinionLabel { get; } = CompareViewModel.OpinionLabelOf(row);
    public OpinionMarkKind OpinionMark { get; } = CompareViewModel.OpinionMarkFor(row);
    public bool IsOpinionMarkVisible => Row != WordProjectStatus.IncorrectSpelling;
    public string AccessibleName => Row == WordProjectStatus.NotPresent
        ? "Choose the words not in FieldWorks" : $"Choose the {OpinionLabel} row";
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

    /// <summary>What PanGloss built for the words in this column.</summary>
    public ParserOutcome Outcome { get; } = WindowWords.OutcomeOf(column);

    /// <summary>The outcome's mark, which the column's heading wears.</summary>
    public Mark OutcomeMark => Mark.Of(Outcome);
    public string AccessibleName => $"Choose the PanGloss {Label} column";
    public bool IsSame => Column == CompareColumnKind.Match;
    public bool IsDifferent => Column == CompareColumnKind.NoMatch;
    public bool IsNoParse => Column == CompareColumnKind.NoParse;
    public bool IsCapped => Column == CompareColumnKind.Timeout;
    public bool IsNotAssessed => Column == CompareColumnKind.Skipped;

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
        Explanation = CompareViewModel.ExplanationOf(row, column);
    }

    public WordProjectStatus Row { get; }
    public CompareColumnKind Column { get; }
    public string Label { get; }
    public CompareFamilyKind Family { get; }

    /// <summary>The tone the cell's meaning takes.</summary>
    public MeaningTone Tone => WindowWords.ToneOf(Family);

    /// <summary>The meaning's mark, for the list's heading when this cell is chosen.</summary>
    public Mark MeaningMark => Mark.Of(Tone);

    /// <summary>The row's opinion, for the list's heading; an incorrect spelling has no mark.</summary>
    public OpinionMarkKind OpinionMark => CompareViewModel.OpinionMarkFor(Row);
    public bool IsOpinionMarkVisible => Row != WordProjectStatus.IncorrectSpelling;

    /// <summary>The column's outcome, for the list's heading.</summary>
    public Mark OutcomeMark => Mark.Of(WindowWords.OutcomeOf(Column));
    public string RowLabel { get; }
    public string ColumnLabel { get; }

    /// <summary>What the cell's words have in common, in one line: the cell's tooltip and the list's explanation.</summary>
    public string? Explanation { get; }

    /// <summary>How many words fell here; <see cref="Count"/> follows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _wordCount;

    /// <summary>How many places in the chosen Texts those words occur.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlacesText))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _occurrenceCount;

    /// <summary>Whether the places were counted: until the chosen Texts load, they are unknown, not 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPlaces))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _hasPlaces;

    /// <summary>The places the cell's words occur, the small number beside the words.</summary>
    public string PlacesText => PlacesTextOf(OccurrenceCount);

    /// <summary>Whether to show the places; a cell no word fell in shows only its zero.</summary>
    public bool ShowsPlaces => Count > 0 && HasPlaces;

    /// <summary>A count of words with its noun.</summary>
    public static string WordsText(int count) => count == 1 ? "1 word" : $"{count:N0} words";

    /// <summary>A count of places with its noun.</summary>
    public static string PlacesTextOf(int count) => count == 1 ? "1 place" : $"{count:N0} places";

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

    /// <summary>The words the cell shows, its big number.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsEmptyImpossible))]
    [NotifyPropertyChangedFor(nameof(ShowsPlaces))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int _count;

    /// <summary>Sets the words that fell here and their places, <see langword="null"/> when nobody counted them.</summary>
    public void SetCounts(int wordCount, int? occurrenceCount)
    {
        WordCount = wordCount;
        OccurrenceCount = occurrenceCount ?? 0;
        HasPlaces = occurrenceCount is not null;
        Count = wordCount;
    }

    /// <summary>No word fell here, so the cell is drawn faintly: still there to read, but not asking for attention.</summary>
    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    private bool _isSelected;

    public string CountText => IsEmptyImpossible
        ? "—" : Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A combination the data cannot produce, and did not: drawn as a dash rather than as a zero.</summary>
    public bool IsEmptyImpossible => Family == CompareFamilyKind.None && Count == 0;

    /// <summary>Whether the cell names what happened; a word FieldWorks lacks has nothing PanGloss could match.</summary>
    public bool ShowsLabel => Family != CompareFamilyKind.None;

    public string AccessibleName => (HasPlaces ? $"{WordsText(WordCount)}, {PlacesText}: " : $"{WordsText(WordCount)}: ") +
        $"{CompareViewModel.HeldInFieldWorks(CompareViewModel.OpinionLabelOf(Row))}, " +
        CompareViewModel.ColumnSentenceOf(Column) +
        (PendingChangeStatus is { } status ? $". {status}" : string.Empty);
}

public sealed record OpinionLegendItem(OpinionMarkKind Kind, string Label);

public sealed record PanGlossLegendItem(AnalysisMarkingClass Kind, string Label)
{
    public bool IsSame => Kind == AnalysisMarkingClass.Same;
    public bool IsConflict => Kind == AnalysisMarkingClass.Conflict;
    public bool IsDifferent => Kind == AnalysisMarkingClass.Different;
    public bool IsExtra => Kind == AnalysisMarkingClass.Extra;
    public bool IsNoParse => Kind == AnalysisMarkingClass.None;
    public bool IsCapped => Kind == AnalysisMarkingClass.Capped;
    public bool IsNotAssessed => Kind == AnalysisMarkingClass.NotAssessed;
}

/// <summary>A morpheme some listed words share by identity, and how many of them use it.</summary>
/// <param name="Form">The morpheme's form, as the first word that uses it shows it.</param>
/// <param name="Gloss">Its gloss.</param>
/// <param name="Count">How many of the listed words use it.</param>
public sealed record CompareSharedMorphemeViewModel(string Form, string Gloss, int Count)
{
    public string CountText => $"in {Count:N0}";

    public string AccessibleName => (Gloss.Length == 0 ? Form : $"{Form} {Gloss}") +
        (Count == 1 ? ": 1 of these words uses it" : $": {Count:N0} of these words use it");
}

/// <summary>A shortcut that chooses a named set of cells at once, such as every word PanGloss stopped on.</summary>
public sealed partial class ComparePresetViewModel(string label, IReadOnlyList<CompareCellViewModel> cells) : ObservableObject
{
    public string Label { get; } = label;
    public IReadOnlyList<CompareCellViewModel> Cells { get; } = cells;

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
        Source = word.Source;
        WordRow = word.WordRow;
        Standing = word.Standing;
        Row = place.Row;
        Column = place.Column;
        Marking = word.Marking;
        OpinionMarks = Marking.FieldWorksAnalyses.Select(analysis => WindowWords.OpinionOf(analysis.Opinion))
            .Select(opinion => new CompareOpinionMarkViewModel(opinion, WindowWords.Of(opinion))).ToArray();
        if (OpinionMarks.Count == 0)
            OpinionMarks = [new(CompareViewModel.OpinionMarkFor(Row), CompareViewModel.OpinionLabelOf(Row))];
        OpinionMark = OpinionMarks[0].Kind;
        OpinionLabel = string.Join(", ", OpinionMarks.Select(mark => mark.Label));
        PanGlossLabel = CompareViewModel.PanGlossClassLabel(Marking.PanGlossClass);
        AccessibleName = $"{Word}: {CompareViewModel.HeldInFieldWorks(OpinionLabel)}, {CompareViewModel.ColumnSentenceOf(Column)}.";
        Occurrences = word.OccurrenceCount;
        ElapsedMs = word.ElapsedMs;
        (Meaning, Family) = CompareViewModel.MeaningOf(Row, Column);
        RowLabel = CompareViewModel.RowLabelOf(Row);
        RowMark = WordProjectStatuses.MarkOf(Row);
        ColumnLabel = CompareViewModel.ColumnLabelOf(Column);
        Outcome = WindowWords.OutcomeOf(Column);
        ColumnMark = Mark.Of(Outcome);
        Readings = word.Readings;
        FirstReading = word.Readings.FirstOrDefault()?.Text ?? string.Empty;
        MissedApproved = word.MissedApproved;
        FixFirst = word.FixFirst;
        NoReadingsText = word.NoReadingsText;
        ReadingCount = word.Morphology?.Analyses.Count ?? 0;
        ReadingChoices = word.Morphology?.Analyses.Select((reading, index) =>
            new CompareReadingChoice(index, reading,
                $"Reading {index + 1}: {(index < word.Readings.Count ? word.Readings[index].Text : "Unresolved")}"))
            .ToArray() ?? [];
    }

    public string Word { get; }

    /// <summary>The Assessment's result for the word, read by identity for what listed words share.</summary>
    public AssessmentWordResult Source { get; }

    /// <summary>The word as every page's word row shows it: the same row Lists and Timing reach.</summary>
    public WordRowViewModel WordRow { get; }
    public WordProjectStatus? Standing { get; }
    public WordProjectStatus Row { get; }
    public CompareColumnKind Column { get; }
    public AnalysisMarkingState Marking { get; }
    public IReadOnlyList<CompareOpinionMarkViewModel> OpinionMarks { get; }
    public OpinionMarkKind OpinionMark { get; }
    public string OpinionLabel { get; }
    public string PanGlossLabel { get; }
    public string AccessibleName { get; }
    public bool IsSame => Marking.PanGlossClass == AnalysisMarkingClass.Same;
    public bool IsConflict => Marking.PanGlossClass == AnalysisMarkingClass.Conflict;
    public bool IsDifferent => Marking.PanGlossClass == AnalysisMarkingClass.Different;
    public bool IsExtra => Marking.PanGlossClass == AnalysisMarkingClass.Extra;
    public bool IsNoParse => Marking.PanGlossClass == AnalysisMarkingClass.None;
    public bool IsCapped => Marking.PanGlossClass == AnalysisMarkingClass.Capped;
    public bool IsNotAssessed => Marking.PanGlossClass == AnalysisMarkingClass.NotAssessed;
    public int? Occurrences { get; }
    public string OccurrenceText => Occurrences is { } count ? $"×{count}" : "—";
    public IReadOnlyList<ParserReadingViewModel> MissedApproved { get; }
    public FixFirstPriority? FixFirst { get; }
    public int? ElapsedMs { get; }
    public string Meaning { get; }
    public CompareFamilyKind Family { get; }

    /// <summary>The tone the word's meaning takes.</summary>
    public MeaningTone Tone => WindowWords.ToneOf(Family);
    public bool IsViolation => Family == CompareFamilyKind.Violation;
    public string RowLabel { get; }

    /// <summary>What FieldWorks holds for the word, as the list's opinion mark; an incorrect spelling has none.</summary>
    public Mark? RowMark { get; }
    public string ColumnLabel { get; }

    /// <summary>What PanGloss built for the word.</summary>
    public ParserOutcome Outcome { get; }

    /// <summary>The outcome as the list's mark.</summary>
    public Mark ColumnMark { get; }

    /// <summary>Every parser reading of the word, morpheme by morpheme, for a list row opened to show them.</summary>
    public IReadOnlyList<ParserReadingViewModel> Readings { get; }

    /// <summary>The parser's first reading, so a listed word shows what was just calculated for it.</summary>
    public string FirstReading { get; }

    public IReadOnlyList<CompareReadingChoice> ReadingChoices { get; }

    /// <summary>Whether FieldWorks holds more than one analysis of the word, so the card names each opinion.</summary>
    public bool HasSeveralOpinions => OpinionMarks.Count > 1;

    /// <summary>Whether the row has a FieldWorks analysis to show, morpheme by morpheme.</summary>
    public bool HasFieldWorksAnalysis => WordRow.FieldWorksMorphemes.Count > 0;

    /// <summary>What PanGloss built, with how many analyses, for the card.</summary>
    public string ReadingsSummary => Readings.Count switch
    {
        0 => WordRow.OutcomeWord,
        1 => $"{WordRow.OutcomeWord} · 1 analysis",
        var count => $"{WordRow.OutcomeWord} · {count} analyses",
    };

    /// <summary>Why the word has no analyses from PanGloss, in the window's words.</summary>
    public string NoReadingsText { get; }

    // The row already shows one approved analysis; the card repeats a missed one only when there is more to see.
    public bool ShowsMissedApproved => MissedApproved.Count > (HasFieldWorksAnalysis ? 1 : 0);

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

/// <summary>One stored analysis opinion shown in a compact word cell.</summary>
public sealed record CompareOpinionMarkViewModel(OpinionMarkKind Kind, string Label)
{
    public Mark Mark => Mark.Of(Kind);
}

/// <summary>A parser reading chosen by its position in one recorded Assessment word.</summary>
public sealed record CompareReadingChoice(int Index, ParseAnalysis Reading, string Label);

/// <summary>One word in the ranked fix-first list, with the reason it needs attention in the window's words.</summary>
public sealed record CompareFixFirstViewModel(CompareWordViewModel Word, FixFirstPriority Priority)
{
    public string Category => Priority.Category switch
    {
        FixFirstCategory.ApprovedNoParse => "Approved, not parsed",
        FixFirstCategory.ApprovedNoMatch => "Approved, parsed differently",
        FixFirstCategory.RejectedRebuilt => "Disapproved but built",
        _ => "Unknown, grammar can't build it",
    };

    // A stored reason naming a missed approved analysis is kept; the rule's own reason is in the CLI's words.
    public string Explanation => Word.MissedApproved.Count > 0 ? Priority.Explanation : Priority.Category switch
    {
        FixFirstCategory.ApprovedNoParse or FixFirstCategory.ApprovedNoMatch => "An approved analysis was not built.",
        FixFirstCategory.RejectedRebuilt => "The grammar still builds an analysis you disapproved.",
        _ => "The grammar could not build this Unknown analysis.",
    };

    public string OccurrenceText => Word.Occurrences is { } count ? $"×{count}" : "—";

    public IReadOnlyList<ParserReadingViewModel> MissedApproved => Word.MissedApproved;

    public IReadOnlyList<CompareReadingChoice> ParserReadings => Word.ReadingChoices;
}
