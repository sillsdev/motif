using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>One exact cell of the Compare matrix.</summary>
public sealed record TextsListCell(WordProjectStatus Row, CompareColumnKind Column);

/// <summary>One of the Matrix's named shortcuts as a word list: its words are the union of the shortcut's cells.</summary>
public sealed partial class TextsListDefinitionViewModel : ObservableObject
{
    internal TextsListDefinitionViewModel(
        string name, string sentence, IReadOnlyList<TextsListCell> cells, CompareViewModel compare)
    {
        Name = name;
        _sentence = sentence;
        Cells = cells;
        Compare = compare;
        foreach (var cell in Compare.Cells) cell.PropertyChanged += OnCellPropertyChanged;
    }

    private CompareViewModel Compare { get; }

    public string Name { get; }

    /// <summary>What the list holds, in one sentence; the rest of the explanation is in Help.</summary>
    private readonly string _sentence;
    public string Sentence => Compare.RefusalCountInCells(Cells) is var refused && refused > 0
        ? refused == WordCount ? ParserRefusals.ListExplanation
            : _sentence.TrimEnd('.') + "; refused words carry their recorded reason."
        : _sentence;

    public IReadOnlyList<TextsListCell> Cells { get; }

    public int WordCount => Compare.Cells.Where(IsCell).Sum(cell => cell.WordCount);

    public int OccurrenceCount => Compare.Cells.Where(IsCell).Sum(cell => cell.OccurrenceCount);

    public bool HasWords => WordCount > 0;

    /// <summary>Whether the list's words carry more than one meaning, so its rows need their meaning column.</summary>
    public bool HasSeveralMeanings => Compare.ShowsMeaningsInCells(Cells);

    public bool HasPendingChanges => PendingState != PendingChangeState.None;

    public PendingChangeState PendingState
    {
        get
        {
            var states = Compare.Cells.Where(IsCell).Select(cell => cell.PendingState).ToArray();
            if (states.Contains(PendingChangeState.NoLongerFits)) return PendingChangeState.NoLongerFits;
            if (states.Contains(PendingChangeState.Uncertain)) return PendingChangeState.Uncertain;
            return states.Any(state => state != PendingChangeState.None)
                ? PendingChangeState.NotAppliedYet : PendingChangeState.None;
        }
    }

    public string? PendingChangeStatus => PendingChangeStates.Label(PendingState);

    /// <summary>The list's words and places together, such as <c>5 words · 8 places</c>.</summary>
    public string CountText => $"{Counted(WordCount, "word")} · {Counted(OccurrenceCount, "place")}";

    private static string Counted(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";

    [ObservableProperty]
    private bool _isSelected;

    private bool IsCell(CompareCellViewModel cell) => Cells.Contains(new TextsListCell(cell.Row, cell.Column));

    private void OnCellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not CompareCellViewModel cell || !IsCell(cell)) return;
        if (e.PropertyName is nameof(CompareCellViewModel.WordCount) or nameof(CompareCellViewModel.Count))
        {
            OnPropertyChanged(nameof(WordCount));
            OnPropertyChanged(nameof(HasWords));
            OnPropertyChanged(nameof(HasSeveralMeanings));
            OnPropertyChanged(nameof(Sentence));
            OnPropertyChanged(nameof(CountText));
        }
        if (e.PropertyName is nameof(CompareCellViewModel.OccurrenceCount) or nameof(CompareCellViewModel.Count))
        {
            OnPropertyChanged(nameof(OccurrenceCount));
            OnPropertyChanged(nameof(CountText));
        }
        if (e.PropertyName == nameof(CompareCellViewModel.HasPendingChanges))
        {
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(PendingChangeStatus));
        }
        if (e.PropertyName == nameof(CompareCellViewModel.PendingState))
        {
            OnPropertyChanged(nameof(PendingState));
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(PendingChangeStatus));
        }
    }
}

/// <summary>
/// The word lists on the Texts page: the Matrix's named shortcuts, by the same names and cells, each selecting its
/// cells in the page's shared Compare model.
/// </summary>
public sealed partial class TextsListsViewModel : ObservableObject
{
    public TextsListsViewModel(CompareViewModel compare)
    {
        ArgumentNullException.ThrowIfNull(compare);
        Compare = compare;
        Lists = compare.Presets.Select(preset => new TextsListDefinitionViewModel(preset.Label,
                SentenceFor(preset),
                preset.Cells.Select(cell => new TextsListCell(cell.Row, cell.Column)).ToArray(), compare))
            .ToArray();
        SelectListCommand = new RelayCommand<TextsListDefinitionViewModel>(SelectList);
        HandOffListCommand = new RelayCommand(HandOffList, CanHandOffList);
        HandOffCheckedWordsCommand = new RelayCommand(HandOffCheckedWords, CanHandOffCheckedWords);
        ParseAgainCommand = new AsyncRelayCommand(() => Compare.RerunCommand.ExecuteAsync(null), () => CanParseAgain);
        compare.PropertyChanged += OnComparePropertyChanged;
        foreach (var list in Lists) list.PropertyChanged += OnListPropertyChanged;
        compare.ChosenCellsChanged += OnChosenCellsChanged;
        compare.CheckedWordsChanged += OnCheckedWordsChanged;
        RefreshSelection();
        SelectList(FirstWithWords());
    }

    private static string SentenceFor(ComparePresetViewModel preset)
    {
        if (preset.Label == "Have a look")
            return "Unknown analyses PanGloss builds differently or cannot build, and misspellings it builds.";

        var sentences = preset.Cells.Select(cell => CompareViewModel.ExplanationOf(cell.Row, cell.Column))
            .Where(sentence => sentence is not null).Distinct(StringComparer.Ordinal).ToArray();
        return sentences.Length == 1 ? sentences[0]! :
            throw new InvalidOperationException($"The Matrix shortcut {preset.Label} has no single list sentence.");
    }

    public CompareViewModel Compare { get; }

    public IReadOnlyList<TextsListDefinitionViewModel> Lists { get; }

    public bool HasSelectedList => SelectedList is not null;

    /// <summary>
    /// Whether the rows show their meaning column: only when the chosen list mixes meanings, since the header line
    /// already names a list's one meaning.
    /// </summary>
    public bool ShowsMeaning => SelectedList?.HasSeveralMeanings ?? true;

    public string HandOffListDisabledReason => SelectedList is null
        ? "Choose a word list first."
        : SelectedList.HasWords ? string.Empty : "No words in this list to send to AI Handoff.";

    public string HandOffListLabel => "AI Handoff for this list";

    /// <summary>Names the ticked words the second AI Handoff button sends, so it never reads like the first.</summary>
    public string HandOffCheckedWordsLabel => SelectedList is { } list
        ? Compare.CheckedWordsInCells(list.Cells).Count switch
        {
            0 => "AI Handoff",
            1 => "AI Handoff for this word",
            var count => $"AI Handoff for {count:N0} words",
        }
        : "AI Handoff";

    public bool HandOffListUnavailable => HandOffListDisabledReason.Length > 0;

    public string HandOffCheckedWordsDisabledReason => SelectedList is { HasWords: true }
        ? HandOffCheckedWordsHelpText : string.Empty;

    public bool HandOffCheckedWordsUnavailable => HandOffCheckedWordsDisabledReason.Length > 0;

    public string HandOffCheckedWordsHelpText => SelectedList is not { } list
        ? "Choose a word list first."
        : !list.HasWords ? "This word list has no words to tick."
        : Compare.CheckedWordsInCells(list.Cells).Count == 0
            ? "Tick words first."
            : string.Empty;

    private Action<IReadOnlyList<string>>? _handOff;

    /// <summary>The workspace action that opens AI Handoff on these words.</summary>
    public Action<IReadOnlyList<string>>? HandOff
    {
        get => _handOff;
        set
        {
            if (_handOff == value) return;
            _handOff = value;
            OnPropertyChanged();
            HandOffListCommand.NotifyCanExecuteChanged();
            HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedList))]
    [NotifyPropertyChangedFor(nameof(ShowsMeaning))]
    [NotifyPropertyChangedFor(nameof(HandOffListDisabledReason))]
    [NotifyPropertyChangedFor(nameof(HandOffListUnavailable))]
    [NotifyPropertyChangedFor(nameof(HandOffCheckedWordsDisabledReason))]
    [NotifyPropertyChangedFor(nameof(HandOffCheckedWordsUnavailable))]
    [NotifyPropertyChangedFor(nameof(HandOffCheckedWordsHelpText))]
    [NotifyPropertyChangedFor(nameof(HandOffCheckedWordsLabel))]
    private TextsListDefinitionViewModel? _selectedList;

    public IRelayCommand<TextsListDefinitionViewModel> SelectListCommand { get; }

    public IRelayCommand HandOffListCommand { get; }

    public IRelayCommand HandOffCheckedWordsCommand { get; }

    /// <summary>Whether the chosen list holds stopped or unparsed words, the only ones worth parsing again.</summary>
    public bool CanParseAgain => SelectedList is { } list && Compare.Rerun is not null &&
        list.Cells.Any(cell => cell.Column is CompareColumnKind.Timeout or CompareColumnKind.Skipped) &&
        Compare.RerunWords.Count > 0;

    public string ParseAgainLabel => "Parse again";

    /// <summary>Which words Parse again sends, and how long each one gets.</summary>
    public string ParseAgainHelpText
    {
        get
        {
            var seconds = Compare.RerunSeconds == 1 ? "1 second" : $"{Compare.RerunSeconds:0} seconds";
            return Compare.RerunWords.Count == 1
                ? $"Parse this 1 word again, with {seconds}."
                : $"Parse these {Compare.RerunWords.Count:N0} words again, with {seconds} for each.";
        }
    }

    /// <summary>Parses the chosen list's stopped and unparsed words again, with the Matrix's time for each.</summary>
    public IAsyncRelayCommand ParseAgainCommand { get; }

    /// <summary>Selects the first list with words when the chosen matrix cells do not match a named list.</summary>
    public void SelectFirstIfNeeded()
    {
        if (SelectedList is null)
            SelectList(FirstWithWords());
        else if (!string.IsNullOrEmpty(Compare.SearchText))
            SelectList(SelectedList);
    }

    private TextsListDefinitionViewModel? FirstWithWords() =>
        Lists.FirstOrDefault(list => list.HasWords) ?? Lists.FirstOrDefault();

    private void SelectList(TextsListDefinitionViewModel? list)
    {
        if (list is null) return;
        SelectedList = list;
        Compare.SelectCells(list.Cells);
        RefreshSelection();
    }

    private bool CanHandOffList() => HandOff is not null && SelectedList?.HasWords == true;

    private void HandOffList()
    {
        if (SelectedList is { } list) HandOff?.Invoke(Compare.WordsInCells(list.Cells));
    }

    private bool CanHandOffCheckedWords() => HandOff is not null && SelectedList is { } list &&
        Compare.CheckedWordsInCells(list.Cells).Count > 0;

    private void HandOffCheckedWords()
    {
        if (SelectedList is { } list) HandOff?.Invoke(Compare.CheckedWordsInCells(list.Cells));
    }

    private void OnComparePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(CompareViewModel.RerunWords) or nameof(CompareViewModel.RerunSeconds)
            or nameof(CompareViewModel.RerunText))) return;
        NotifyParseAgain();
    }

    private void NotifyParseAgain()
    {
        OnPropertyChanged(nameof(CanParseAgain));
        OnPropertyChanged(nameof(ParseAgainHelpText));
        ParseAgainCommand.NotifyCanExecuteChanged();
    }

    private void OnChosenCellsChanged(object? sender, EventArgs e)
    {
        RefreshSelection();
        NotifyParseAgain();
        HandOffListCommand.NotifyCanExecuteChanged();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
    }

    private void OnCheckedWordsChanged(object? sender, EventArgs e)
    {
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HandOffCheckedWordsDisabledReason));
        OnPropertyChanged(nameof(HandOffCheckedWordsUnavailable));
        OnPropertyChanged(nameof(HandOffCheckedWordsHelpText));
        OnPropertyChanged(nameof(HandOffCheckedWordsLabel));
    }

    private void OnListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextsListDefinitionViewModel.HasSeveralMeanings) && ReferenceEquals(sender, SelectedList))
            OnPropertyChanged(nameof(ShowsMeaning));
        if (e.PropertyName == nameof(TextsListDefinitionViewModel.HasWords))
        {
            HandOffListCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HandOffListDisabledReason));
            OnPropertyChanged(nameof(HandOffListUnavailable));
            OnPropertyChanged(nameof(HandOffCheckedWordsDisabledReason));
            OnPropertyChanged(nameof(HandOffCheckedWordsUnavailable));
            OnPropertyChanged(nameof(HandOffCheckedWordsHelpText));
        }
    }

    partial void OnSelectedListChanged(TextsListDefinitionViewModel? value)
    {
        NotifyParseAgain();
        HandOffListCommand.NotifyCanExecuteChanged();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSelection()
    {
        var selected = Compare.Cells.Where(cell => cell.IsSelected)
            .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet();
        SelectedList = Lists.FirstOrDefault(list => selected.SetEquals(list.Cells));
        foreach (var list in Lists) list.IsSelected = ReferenceEquals(list, SelectedList);
    }
}
