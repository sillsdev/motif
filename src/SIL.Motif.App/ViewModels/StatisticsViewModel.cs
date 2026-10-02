using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Projection.Usage;

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

    partial void OnSelectedGroupChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedGroupChoice));
        OnPropertyChanged(nameof(CountHeader));
        OnPropertyChanged(nameof(TimeHeader));
        OnPropertyChanged(nameof(ShadingNote));
        OnPropertyChanged(nameof(ScopeNote));
        RefreshShading();
        ApplyView();
        RaiseSummary();
    }

    private bool IsWordGroup => SelectedGroup == "word";

    /// <summary>What the count column counts: a word's search steps, or each grammar object's own attempts.</summary>
    public string CountHeader => IsWordGroup ? "Search steps" : "Attempts";

    /// <summary>What the time column times: a word's whole parse, or the time spent in the object itself.</summary>
    public string TimeHeader => IsWordGroup ? "Word time (ms)" : "Own time (ms)";

    /// <summary>What the shading compares, and where each number comes from.</summary>
    public string ShadingNote => IsWordGroup
        ? "Shading: darker is larger, against the largest in its column. Search steps and word time are the " +
            "parser's own record of the parse; whether a word finished comes from the last parse."
        : "Shading: darker is larger. An attempt is a different event for each kind (a rule tried, an entry " +
            "matched, a root looked up), so attempts are shaded only against rows of their own kind. Own time " +
            "leaves out time spent in the objects this one called.";

    /// <summary>Whether these rows use Timing's selected words or cover the whole Assessment.</summary>
    public string ScopeNote => !IsWordGroup
        ? $"{SelectedGroupChoice.Label} · all rows from the complete parse; selected words do not filter them."
        : WordScope is { Count: 0 }
            ? "By word · no words selected in Timing."
        : WordScope is { } scope
            ? $"By word · the same {SpeedText.Count(scope.Count, "word", "words")} selected in Timing."
            : "By word · every word in the parse.";

    /// <summary>
    /// Looks up a word in the Assessment these statistics came from, so a word's completion here is the same
    /// answer Results gives.
    /// </summary>
    public Func<string, AssessWordRowViewModel?>? AssessedWord { get; set; }

    /// <summary>Opens wherever the per-word time limit is set, for the card that suggests raising it.</summary>
    public Action? OpenTimeLimit { get; set; }

    /// <summary>Tries a word in Try a Word, for the card that names the slowest word.</summary>
    public Action<string>? TryWord { get; set; }

    /// <summary>The word that took longest, or <see langword="null"/> when no word rows are loaded.</summary>
    public StatsRowViewModel? SlowestWord => ScopeRows.Where(row => row.Word is not null && row.ElapsedMs is not null)
        .MaxBy(row => row.ElapsedMs);

    public bool HasSlowestWord => SlowestWord is not null;

    /// <summary>Whether any fetched row needed more than one pass; when none did, the column says nothing.</summary>
    public bool AnyPasses => ScopeRows.Any(row => row.Passes is > 0);

    /// <summary>Whether the rows are words, which is when the summary cards have something to say.</summary>
    public bool HasWordRows => ScopeRows.Any(row => row.Word is not null);

    public bool AnyIncomplete => IncompleteCount > 0;

    public string IncompleteHeadline => IncompleteCount switch
    {
        0 => "Every word finished its search",
        1 => "1 word stopped at a limit",
        _ => $"{IncompleteCount:N0} words stopped at a limit",
    };

    public string IncompleteDetail => IncompleteCount == 0
        ? "No word reached the time or step limit, so every No parse is the grammar's answer."
        : "Each was still searching when its time or step limit ran out, so it may parse with longer.";

    public string SlowestHeadline => SlowestWord is { } row ? $"Slowest word: {row.Word}" : string.Empty;

    public string SlowestDetail => SlowestWord is { } row
        ? $"{row.ElapsedText} ms{(row.Attempts is null ? string.Empty : $" and {row.AttemptsText} search steps")}" +
            $"{(row.IsIncomplete ? " before a limit stopped it" : string.Empty)}."
        : string.Empty;

    // PanGloss's "passes" column counts the analyses a word produced, so it is shown as readings.
    public int SeveralReadingsCount => ScopeRows.Count(row => row.Word is not null && row.Passes is > 1);

    public string PassesHeadline => SeveralReadingsCount switch
    {
        0 => "No word has more than one reading",
        1 => "1 word has more than one reading",
        _ => $"{SeveralReadingsCount:N0} words have more than one reading",
    };

    public string PassesDetail => SeveralReadingsCount == 0
        ? "Each word the parser built, it built one way."
        : "Homographs, or a grammar that allows more than it should. Sort by Readings to see them.";

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
    [NotifyPropertyChangedFor(nameof(ShownRefusal))]
    private Refusal? _refusal;

    /// <summary>The current refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => Refusal is null ? null : WindowRefusal.From(Refusal);

    /// <summary>PanGloss's rendered statistics summary, set by whoever ran the producing Assessment.</summary>
    [ObservableProperty]
    private string? _summaryMarkdown;

    /// <summary>The rows currently on display: <see cref="_allRows"/> filtered by <see cref="FilterText"/> and sorted.</summary>
    public ObservableCollection<StatsRowViewModel> Rows { get; } = [];

    public ObservableCollection<JsonElement> Metadata { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>How many rows are in the applicable scope, before the row filter.</summary>
    public int RowCount => ScopeRows.Count();

    /// <summary>How many fetched words stopped at a time or step limit.</summary>
    public int IncompleteCount => ScopeRows.Count(row => row.IsIncomplete);

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
        _wordScope = null;
        OnPropertyChanged(nameof(ScopeNote));
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

        var shapes = new List<string>
        {
            UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.Text("group"),
        };
        if (AssessmentId is not null) shapes.Add(UsageArgumentShape.Text("assessmentId"));
        using var usageAction = _commandClient.BeginUsageAction("stats", [.. shapes]);
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
            RefreshShading();
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
        OnPropertyChanged(nameof(IncompleteDetail));
        OnPropertyChanged(nameof(AnyIncomplete));
        OnPropertyChanged(nameof(SeveralReadingsCount));
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
    /// <summary>
    /// The words the rows are restricted to, or <see langword="null"/> for every row fetched. PanGloss's
    /// <c>stats</c> cannot select words itself, so the restriction is applied here to the rows already fetched.
    /// </summary>
    public IReadOnlyList<string>? WordScope
    {
        get => _wordScope;
        set
        {
            _wordScope = value;
            OnPropertyChanged(nameof(ScopeNote));
            RefreshShading();
            ApplyView();
            RaiseSummary();
        }
    }

    private IReadOnlyList<string>? _wordScope;

    private IEnumerable<StatsRowViewModel> ScopeRows => IsWordGroup && WordScope is { } scope
        ? _allRows.Where(row => row.Word is { } word && scope.Contains(word, StringComparer.Ordinal))
        : _allRows;

    private void RefreshShading()
    {
        IEnumerable<StatsRowViewModel> scopeRows = _allRows;
        if (IsWordGroup && WordScope is { } scope)
            scopeRows = scopeRows.Where(row => row.Word is { } word && scope.Contains(word, StringComparer.Ordinal));
        var rows = scopeRows.ToArray();
        var largestAttempts = rows.GroupBy(row => row.Kind ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(kind => kind.Key, kind => kind.Max(row => row.Attempts) ?? 0, StringComparer.Ordinal);
        var largestPasses = rows.Select(row => row.Passes).DefaultIfEmpty().Max() ?? 0;
        var largestElapsed = rows.Select(row => row.ElapsedMs).DefaultIfEmpty().Max() ?? 0;
        foreach (var row in _allRows)
            row.ShadeAgainst(largestAttempts.GetValueOrDefault(row.Kind ?? string.Empty), largestPasses, largestElapsed);
    }

    private void ApplyView()
    {
        IEnumerable<StatsRowViewModel> view = ScopeRows;

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
