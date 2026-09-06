using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Queries PanGloss's per-object statistics for one of the six groups <c>pangloss stats --group</c>
/// accepts and holds them as a client-side, filterable, sortable grid (design decision 5). Motif does
/// not own PanGloss's statistics vocabulary: <see cref="Groups"/> is exactly
/// <see cref="HandoffWriter.StatisticsGroups"/>, and each <see cref="StatsRowViewModel"/> keeps every
/// column PanGloss's row carried, known or not.
/// </summary>
/// <remarks>
/// Sorting and filtering re-order and re-select <see cref="Rows"/> from the fetched set already held in
/// memory; neither ever calls <see cref="ICommandClient.StatsAsync"/>. Only <see cref="LoadCommand"/>
/// does, and it is the sole point where a new set of rows replaces the old one. A refusal from it never
/// clears an already-displayed successful result — it only marks that result <see cref="IsStale"/>, so a
/// person keeps seeing the last statistics PanGloss actually produced rather than a blank grid.
/// </remarks>
public sealed partial class StatisticsViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private readonly List<StatsRowViewModel> _allRows = [];

    public StatisticsViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        _selectedGroup = Groups[0];
        LoadCommand = new AsyncRelayCommand(LoadAsync, CanLoad);
    }

    /// <summary>The six groups PanGloss's own <c>stats</c> vocabulary accepts.</summary>
    public IReadOnlyList<string> Groups => HandoffWriter.StatisticsGroups;

    /// <summary>The project whose Baseline supplies statistics, or <c>null</c> before one is chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    /// <summary><c>null</c> queries the current Baseline; a Proposal id queries its Trial instead.</summary>
    [ObservableProperty]
    private string? _proposalId;

    [ObservableProperty]
    private string _selectedGroup;

    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>The known column a click on its header last sorted by, or <c>null</c> for fetched order.</summary>
    [ObservableProperty]
    private string? _sortColumn;

    [ObservableProperty]
    private bool _sortDescending;

    /// <summary><c>true</c> once a refusal left an earlier successful load's rows displayed as stale.</summary>
    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private Refusal? _refusal;

    /// <summary>PanGloss's rendered statistics summary, set by whoever ran the producing Assessment.</summary>
    [ObservableProperty]
    private string? _summaryMarkdown;

    /// <summary>The rows currently on display: <see cref="_allRows"/> filtered by <see cref="FilterText"/> and sorted.</summary>
    public ObservableCollection<StatsRowViewModel> Rows { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    partial void OnProjectPathChanged(string? value) => LoadCommand.NotifyCanExecuteChanged();

    partial void OnFilterTextChanged(string value) => ApplyView();

    /// <summary>Sorts by <paramref name="column"/>, toggling direction on a repeated click of the same column.</summary>
    public void SortBy(string column)
    {
        if (SortColumn == column) SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        ApplyView();
    }

    private bool CanLoad() => ProjectPath is not null;

    private async Task LoadAsync()
    {
        if (ProjectPath is null) return;

        var request = new StatsRequest(ProjectPath, ProposalId, StatsOutputKind.JsonRows, ["--group", SelectedGroup]);
        var outcome = await _commandClient.StatsAsync(request, CancellationToken.None);

        if (outcome.Succeeded)
        {
            _allRows.Clear();
            _allRows.AddRange(outcome.Value!.Rows!.Select(row => new StatsRowViewModel(row)));
            IsStale = false;
            Refusal = null;
            ApplyView();
        }
        else
        {
            IsStale = true;
            Refusal = outcome.Refusal;
        }
    }

    // Reapplies the filter and sort from the fetched rows in memory; never calls the command client.
    private void ApplyView()
    {
        IEnumerable<StatsRowViewModel> view = _allRows;

        if (!string.IsNullOrEmpty(FilterText))
            view = view.Where(row => row.MatchesFilter(FilterText));

        if (SortColumn is { } column)
        {
            view = IsNumericColumn(column)
                ? OrderBy(view, row => row.NumericValue(column), Comparer<double?>.Default)
                : OrderBy(view, row => row.TextValue(column), StringComparer.OrdinalIgnoreCase);
        }

        Rows.Clear();
        foreach (var row in view) Rows.Add(row);
    }

    private IOrderedEnumerable<StatsRowViewModel> OrderBy<TKey>(
        IEnumerable<StatsRowViewModel> view, Func<StatsRowViewModel, TKey> selector, IComparer<TKey> comparer) =>
        SortDescending ? view.OrderByDescending(selector, comparer) : view.OrderBy(selector, comparer);

    private static bool IsNumericColumn(string column) => column is "attempts" or "failures" or "elapsed";
}
