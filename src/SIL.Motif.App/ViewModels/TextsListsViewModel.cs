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

    public bool HasPendingChanges => Compare.Cells.Where(IsCell).Any(cell => cell.HasPendingChanges);

    public string? PendingChangeStatus => Compare.Cells.Where(IsCell)
        .Select(cell => cell.PendingChangeStatus).FirstOrDefault(status => status == "No longer fits")
        ?? (HasPendingChanges ? "Not applied yet" : null);

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
        if (e.PropertyName == nameof(CompareCellViewModel.PendingChangeStatus))
            OnPropertyChanged(nameof(PendingChangeStatus));
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
            Definition("Approved, not parsed", "What approved words could the grammar not rebuild?",
                Cell(WordProjectStatus.Approved, CompareColumnKind.NoParse)),
            Definition("Approved, parsed differently", "Where did the grammar build something other than an approved analysis?",
                Cell(WordProjectStatus.Approved, CompareColumnKind.NoMatch)),
            Definition("Candidate the parser confirms", "Which candidate words did the grammar reproduce?",
                Cell(WordProjectStatus.Candidate, CompareColumnKind.Match)),
            Definition("Parsed, not in the project", "Which new words did the grammar parse that the project does not store?",
                Cell(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch)),
            Definition("Nobody can analyse", "Which unstored words had no parser reading?",
                Cell(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)),
            Definition("Rejected but rebuilt", "Which words did the grammar rebuild after the project rejected them?",
                Cell(WordProjectStatus.Rejected, CompareColumnKind.Match)),
            Definition("Timed out", "Which words stopped at a time or step limit?",
                Enum.GetValues<WordProjectStatus>().Select(row => new TextsListCell(row, CompareColumnKind.Timeout)).ToArray()),
        ];
        SelectListCommand = new RelayCommand<TextsListDefinitionViewModel>(SelectList);
        compare.ChosenCellsChanged += OnChosenCellsChanged;
        RefreshSelection();
        SelectList(Lists.FirstOrDefault());
    }

    public CompareViewModel Compare { get; }

    public IReadOnlyList<TextsListDefinitionViewModel> Lists { get; }

    public bool HasSelectedList => SelectedList is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedList))]
    private TextsListDefinitionViewModel? _selectedList;

    public IRelayCommand<TextsListDefinitionViewModel> SelectListCommand { get; }

    /// <summary>Selects the first question when the chosen matrix cells do not match a named list.</summary>
    public void SelectFirstIfNeeded()
    {
        if (SelectedList is null) SelectList(Lists.FirstOrDefault());
    }

    private TextsListDefinitionViewModel Definition(string name, string question, params TextsListCell[] cells) =>
        new(name, question, cells, Compare);

    private static TextsListCell[] Cell(WordProjectStatus row, CompareColumnKind column) => [new(row, column)];

    private void SelectList(TextsListDefinitionViewModel? list)
    {
        if (list is null) return;
        SelectedList = list;
        Compare.SelectCells(list.Cells);
        RefreshSelection();
    }

    private void OnChosenCellsChanged(object? sender, EventArgs e) => RefreshSelection();

    private void RefreshSelection()
    {
        var selected = Compare.Cells.Where(cell => cell.IsSelected)
            .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet();
        SelectedList = Lists.FirstOrDefault(list => selected.SetEquals(list.Cells));
        foreach (var list in Lists) list.IsSelected = ReferenceEquals(list, SelectedList);
    }
}
