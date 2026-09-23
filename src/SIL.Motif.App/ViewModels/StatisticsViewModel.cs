using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Queries PanGloss's per-object statistics for one of the six groups <c>pangloss stats --group</c>
/// accepts and holds them as a client-side, filterable, sortable grid (design decision 5). Motif does
/// not own PanGloss's statistics vocabulary: <see cref="Groups"/> is exactly
/// <see cref="StatsCommand.StatisticsGroups"/>, and each <see cref="StatsRowViewModel"/> keeps every
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
    public IReadOnlyList<string> Groups => StatsCommand.StatisticsGroups;

    /// <summary>The same six groups, each named the way a person would ask for it.</summary>
    public IReadOnlyList<StatsGroupChoice> GroupChoices { get; } =
    [
        .. StatsCommand.StatisticsGroups.Select(group => new StatsGroupChoice(group, group switch
        {
            "word" => "By word",
            "object" => "By grammar object",
            "allomorph" => "By allomorph",
            "morpheme" => "By morpheme",
            "group" => "By object group",
            "never-fires" => "Objects that never fired",
            _ => group,
        })),
    ];

    /// <summary>The chosen group as a <see cref="StatsGroupChoice"/>, for the picker.</summary>
    public StatsGroupChoice SelectedGroupChoice
    {
        get => GroupChoices.First(choice => choice.Value == SelectedGroup);
        set
        {
            if (value is not null) SelectedGroup = value.Value;
        }
    }

    partial void OnSelectedGroupChanged(string value) => OnPropertyChanged(nameof(SelectedGroupChoice));

    /// <summary>
    /// Looks up a word in the Assessment these statistics came from, so a word's completion here is the same
    /// answer Results gives. The statistics pass times its own parse, which can stop at a limit differently.
    /// </summary>
    public Func<string, AssessWordRowViewModel?>? AssessedWord { get; set; }

    /// <summary>Opens wherever the per-word time limit is set, for the card that suggests raising it.</summary>
    public Action? OpenTimeLimit { get; set; }

    /// <summary>Tries a word in Try a Word, for the card that names the slowest word.</summary>
    public Action<string>? TryWord { get; set; }

    /// <summary>The word that took longest, or <see langword="null"/> when no word rows are loaded.</summary>
    public StatsRowViewModel? SlowestWord => _allRows.Where(row => row.Word is not null && row.ElapsedMs is not null)
        .MaxBy(row => row.ElapsedMs);

    public bool HasSlowestWord => SlowestWord is not null;

    /// <summary>Whether any fetched row needed more than one pass; when none did, the column says nothing.</summary>
    public bool AnyPasses => _allRows.Any(row => row.Passes is > 0);

    /// <summary>Whether the rows are words, which is when the summary cards have something to say.</summary>
    public bool HasWordRows => _allRows.Any(row => row.Word is not null);

    public string IncompleteHeadline => IncompleteCount == 1 ? "1 word stopped at a limit" : $"{IncompleteCount:N0} words stopped at a limit";

    public string SlowestHeadline => SlowestWord is { } row ? $"Slowest word: {row.Word}" : string.Empty;

    public string SlowestDetail => SlowestWord is { } row
        ? $"{row.ElapsedText} ms and {row.AttemptsText} attempts{(row.IsIncomplete ? " before a limit stopped it" : string.Empty)}."
        : string.Empty;

    public string PassesHeadline => AnyPasses ? "Some words needed a second pass" : "No word needed a second pass";

    public string PassesDetail => AnyPasses
        ? "The Passes column shows how many each word needed."
        : $"Passes are 0 for all {RowCount:N0} rows, so the column is hidden.";

    /// <summary>The project containing the retained Assessment, or <c>null</c> before one is chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    [ObservableProperty]
    private string? _assessmentId;

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

    public ObservableCollection<JsonElement> Metadata { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>How many rows were fetched, before any filter.</summary>
    public int RowCount => _allRows.Count;

    /// <summary>How many fetched words stopped at a time or step limit.</summary>
    public int IncompleteCount => _allRows.Count(row => row.IsIncomplete);

    /// <summary>Whether the grid shows only the words that did not finish.</summary>
    [ObservableProperty]
    private bool _onlyIncomplete;

    partial void OnOnlyIncompleteChanged(bool value) => ApplyView();

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

    /// <summary>
    /// Forgets every fetched row, so nothing of one project's statistics can reappear under another's.
    /// </summary>
    /// <remarks>
    /// Clearing <see cref="Rows"/> alone is not enough: it is only the view, re-derived from the fetched
    /// set whenever the filter or sort changes. Leaving that set behind means a sort after switching
    /// projects repopulates the grid with the previous project's words.
    /// </remarks>
    public void Reset()
    {
        _allRows.Clear();
        Rows.Clear();
        SortColumn = null;
        FilterText = string.Empty;
        OnlyIncomplete = false;
        RaiseSummary();
        IsStale = false;
        Refusal = null;
        SummaryMarkdown = null;
        AssessmentId = null;
        Metadata.Clear();
    }

    private async Task LoadAsync()
    {
        if (ProjectPath is null) return;

        var request = new StatsRequest(ProjectPath, AssessmentId, StatsOutputKind.JsonRows,
            ["--group", SelectedGroup]);
        var outcome = await _commandClient.StatsAsync(request, CancellationToken.None);

        if (outcome.Succeeded)
        {
            _allRows.Clear();
            Metadata.Clear();
            foreach (var row in outcome.Value!.Rows!)
            {
                if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("meta", out var meta) &&
                    meta.ValueKind == JsonValueKind.True)
                    Metadata.Add(row.Clone());
                else
                    _allRows.Add(new StatsRowViewModel(row));
            }
            // Shaded against every fetched row, so filtering never changes what a shade means.
            var largestAttempts = _allRows.Max(row => row.Attempts) ?? 0;
            var largestPasses = _allRows.Max(row => row.Passes) ?? 0;
            var largestElapsed = _allRows.Max(row => row.ElapsedMs) ?? 0;
            foreach (var row in _allRows) row.ShadeAgainst(largestAttempts, largestPasses, largestElapsed);
            if (AssessedWord is { } assessed)
                foreach (var row in _allRows)
                    if (row.Word is { } word && assessed(word) is { } result)
                        row.UseAssessment(result.StoppedAtALimit, result.CompletionStatus);
            RaiseSummary();
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

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(IncompleteCount));
        OnPropertyChanged(nameof(IncompleteHeadline));
        OnPropertyChanged(nameof(SlowestWord));
        OnPropertyChanged(nameof(HasSlowestWord));
        OnPropertyChanged(nameof(SlowestHeadline));
        OnPropertyChanged(nameof(SlowestDetail));
        OnPropertyChanged(nameof(AnyPasses));
        OnPropertyChanged(nameof(HasWordRows));
        OnPropertyChanged(nameof(PassesHeadline));
        OnPropertyChanged(nameof(PassesDetail));
    }

    // Reapplies the filter and sort from the fetched rows in memory; never calls the command client.
    private void ApplyView()
    {
        IEnumerable<StatsRowViewModel> view = _allRows;

        if (!string.IsNullOrEmpty(FilterText))
            view = view.Where(row => row.MatchesFilter(FilterText));
        if (OnlyIncomplete)
            view = view.Where(row => row.IsIncomplete);

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

    private static bool IsNumericColumn(string column) => column is "attempts" or "passes" or "elapsedMs";
}

/// <summary>One statistics grouping: PanGloss's own name for it, and how the picker shows it.</summary>
public sealed record StatsGroupChoice(string Value, string Label)
{
    public override string ToString() => Label;
}
