using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>One exact cell of the Compare matrix.</summary>
public sealed record TextsListCell(WordProjectStatus Row, CompareColumnKind Column);

/// <summary>A named question whose words are the union of its explicit Compare cells.</summary>
public sealed partial class TextsListDefinitionViewModel : ObservableObject
{
    internal TextsListDefinitionViewModel(
        string name, string question, IReadOnlyList<TextsListCell> cells, CompareViewModel compare)
    {
        Name = name;
        Question = question;
        Cells = cells;
        Compare = compare;
        foreach (var cell in Compare.Cells) cell.PropertyChanged += OnCellPropertyChanged;
    }

    private CompareViewModel Compare { get; }

    public string Name { get; }

    public string Question { get; }

    public IReadOnlyList<TextsListCell> Cells { get; }

    public int WordCount => Compare.Cells.Where(IsCell).Sum(cell => cell.WordCount);

    public int OccurrenceCount => Compare.Cells.Where(IsCell).Sum(cell => cell.OccurrenceCount);

    public bool HasWords => WordCount > 0;

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

    public string CountText => WordCount == 1 ? "1 word" : $"{WordCount:N0} words";

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
            OnPropertyChanged(nameof(CountText));
        }
        if (e.PropertyName is nameof(CompareCellViewModel.OccurrenceCount) or nameof(CompareCellViewModel.Count))
            OnPropertyChanged(nameof(OccurrenceCount));
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

/// <summary>The fixed question lists on the Texts page; each one selects cells from the page's shared Compare model.</summary>
public sealed partial class TextsListsViewModel : ObservableObject
{
    public TextsListsViewModel(CompareViewModel compare)
    {
        ArgumentNullException.ThrowIfNull(compare);
        Compare = compare;
        Lists =
        [
            Definition("Approved, not parsed",
                "Words you approved in FieldWorks that the grammar can no longer build. " +
                "Same as the Matrix cell Approved × No parse.",
                Cell(WordProjectStatus.Approved, CompareColumnKind.NoParse)),
            Definition("Approved, parsed differently",
                "Words you approved where the grammar builds something else. " +
                "Same as the Matrix cell Approved × Different.",
                Cell(WordProjectStatus.Approved, CompareColumnKind.NoMatch)),
            Definition("Unknown the parser confirms",
                "Words with an Unknown analysis that the grammar builds too. Same as the Matrix cell Unknown × Same.",
                Cell(WordProjectStatus.Candidate, CompareColumnKind.Match)),
            Definition("Parsed, not in FieldWorks",
                "Words the grammar parses that FieldWorks has no analysis for. " +
                "Same as the Matrix cell Not in FieldWorks × Different.",
                Cell(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch)),
            Definition("Nobody can analyze",
                "Words neither FieldWorks nor the grammar can analyze. " +
                "Same as the Matrix cell Not in FieldWorks × No parse.",
                Cell(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)),
            Definition("Disapproved but built",
                "Words whose disapproved analysis the grammar still builds. Same as the Matrix cell Disapproved × Same.",
                Cell(WordProjectStatus.Rejected, CompareColumnKind.Match)),
            Definition("Stopped at a limit",
                "Words PanGloss stopped on at a time or step limit. Same as the Matrix column Stopped.",
                Enum.GetValues<WordProjectStatus>().Select(row => new TextsListCell(row, CompareColumnKind.Timeout)).ToArray()),
        ];
        SelectListCommand = new RelayCommand<TextsListDefinitionViewModel>(SelectList);
        HandOffListCommand = new RelayCommand(HandOffList, CanHandOffList);
        HandOffCheckedWordsCommand = new RelayCommand(HandOffCheckedWords, CanHandOffCheckedWords);
        foreach (var list in Lists) list.PropertyChanged += OnListPropertyChanged;
        compare.ChosenCellsChanged += OnChosenCellsChanged;
        compare.CheckedWordsChanged += OnCheckedWordsChanged;
        RefreshSelection();
        SelectList(FirstWithWords());
    }

    public CompareViewModel Compare { get; }

    public IReadOnlyList<TextsListDefinitionViewModel> Lists { get; }

    public bool HasSelectedList => SelectedList is not null;

    public string HandOffListDisabledReason => SelectedList is null
        ? "Choose a word list first."
        : SelectedList.HasWords ? string.Empty : "No words in this list to send to AI Handoff.";

    public string HandOffListLabel => "Hand off the whole list";

    /// <summary>Names the ticked words the second AI Handoff button sends, so it never reads like the first.</summary>
    public string HandOffCheckedWordsLabel => SelectedList is { } list
        ? Compare.CheckedWordsInCells(list.Cells).Count switch
        {
            0 => "Hand off selected words",
            1 => "Hand off the 1 selected word",
            var count => $"Hand off the {count:N0} selected words",
        }
        : "Hand off selected words";

    public bool HandOffListUnavailable => HandOffListDisabledReason.Length > 0;

    public string HandOffCheckedWordsDisabledReason => SelectedList is { HasWords: true }
        ? HandOffCheckedWordsHelpText : string.Empty;

    public bool HandOffCheckedWordsUnavailable => HandOffCheckedWordsDisabledReason.Length > 0;

    public string HandOffCheckedWordsHelpText => SelectedList is not { } list
        ? "Choose a word list first."
        : !list.HasWords ? "This word list has no words to tick."
        : Compare.CheckedWordsInCells(list.Cells).Count == 0
            ? "Tick words in this list before starting an AI Handoff."
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

    /// <summary>Selects the first list with words when the chosen matrix cells do not match a named list.</summary>
    public void SelectFirstIfNeeded()
    {
        if (SelectedList is null)
            SelectList(FirstWithWords());
        else if (!string.IsNullOrEmpty(Compare.SearchText))
            SelectList(SelectedList);
    }

    private TextsListDefinitionViewModel Definition(string name, string question, params TextsListCell[] cells) =>
        new(name, question, cells, Compare);

    private TextsListDefinitionViewModel? FirstWithWords() =>
        Lists.FirstOrDefault(list => list.HasWords) ?? Lists.FirstOrDefault();

    private static TextsListCell[] Cell(WordProjectStatus row, CompareColumnKind column) => [new(row, column)];

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

    private void OnChosenCellsChanged(object? sender, EventArgs e)
    {
        RefreshSelection();
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
