using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Warnings page on one reported diagnostic kind.</summary>
/// <param name="Code">The stable kind code supplied by the stored report.</param>
public sealed record OpenGrammarWarningRequest(string Code) : PageRequest(WorkspacePage.Warnings);

/// <summary>
/// The Warnings page's model: the grammar's findings, counted beside the page's sidebar label, and the check a
/// person starts when nothing has been checked for this Baseline yet. It owns the grammar check and publishes a
/// <see cref="GrammarSummary"/> of it to the context for the pages that summarise it.
/// </summary>
public sealed class WarningsPageModel : PageModel
{
    private readonly Dictionary<GrammarWarningRowViewModel, string> _rowAreas =
        new(ReferenceEqualityComparer.Instance);
    private int _readGeneration;
    private string? _requestedWarningCode;
    private string? _currentArea;
    private string? _selectedArea;

    public WarningsPageModel(WorkspaceContext context) : base(context)
    {
        Grammar = new GrammarViewModel(context.Commands, context.Clock);
        Grammar.Warnings.TableEntries.CollectionChanged += OnWarningTableEntriesChanged;
        FilteredTableEntries = new DataGridCollectionView(Grammar.Warnings.TableEntries)
        {
            Filter = MatchesSelectedArea,
        };
        AreaFilters.Add(new WarningsAreaFilterOption(null, "All areas", 0, true));
        SetAreaCommand = new RelayCommand<WarningsAreaFilterOption>(SelectArea);
        Grammar.Warnings.ConfigureActions(
            row => Context.HandOffWarning(WordsFor(row), new WarningHandoffScope(
                row.Message, row.GroupCode, row.AttributionState, row.IsPartialReach)),
            row => Context.Assess.RerunAsync(WordsFor(row), 30_000),
            new WordRowRoutes
            {
                OpenInText = Context.OpenWord,
                TryWord = Context.TryWord,
            });
        CheckGrammarCommand = new AsyncRelayCommand(CheckGrammarAsync, () => Context.HasProject && !Grammar.IsLoading);
        Grammar.PropertyChanged += OnGrammarChanged;
        Grammar.Warnings.PropertyChanged += OnGrammarChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        PublishSummary();
    }

    /// <summary>The page's own grammar check.</summary>
    public GrammarViewModel Grammar { get; }

    /// <summary>The table entries filtered by the selected Warnings area.</summary>
    public DataGridCollectionView FilteredTableEntries { get; }

    /// <summary>The Warnings area choices and their current finding counts.</summary>
    public ObservableCollection<WarningsAreaFilterOption> AreaFilters { get; } = [];

    /// <summary>Selects the Warnings area shown in the page's table.</summary>
    public IRelayCommand<WarningsAreaFilterOption> SetAreaCommand { get; }

    /// <summary>The selected finding's area for its Inspector summary, or empty when no row is selected.</summary>
    public string SelectedWarningArea => Grammar.Warnings.SelectedTableEntry is GrammarWarningRowViewModel row &&
        _rowAreas.TryGetValue(row, out var area) ? area : string.Empty;

    /// <summary>Whether a selected finding area is available for the Inspector summary.</summary>
    public bool HasSelectedWarningArea => SelectedWarningArea.Length > 0;

    /// <summary>The selected finding's severity for its Inspector summary.</summary>
    public string SelectedWarningSeverity => (Grammar.Warnings.SelectedTableEntry as GrammarWarningRowViewModel)?.SeverityLabel
        ?? string.Empty;

    /// <summary>The selected finding's exact word count, or its evidence label when no count is available.</summary>
    public string SelectedWarningWordCount => (Grammar.Warnings.SelectedTableEntry as GrammarWarningRowViewModel)?.TableNamedWordsText
        ?? string.Empty;

    /// <summary>Whether the open project's grammar has no check to show, so the page offers one.</summary>
    public bool IsGrammarNotChecked => Context.HasProject && !Grammar.HasChecked && !Grammar.IsLoading;

    /// <summary>Checks the open project's grammar: started only by a person, since it can take a minute.</summary>
    public IAsyncRelayCommand CheckGrammarCommand { get; }

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        _requestedWarningCode = null;
        Grammar.Clear();
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is not OpenGrammarWarningRequest warning) return;
        _requestedWarningCode = warning.Code;
        TryOpenRequestedWarning();
    }

    // Opening shows the check stored for this Baseline, and never starts one of its own.
    protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Grammar.Clear();
        var generation = ++_readGeneration;
        var stored = await Context.Commands
            .ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken).ConfigureAwait(true);
        if (generation != _readGeneration || !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;
        if (stored.Succeeded) Grammar.LoadStored(projectPath, stored.Value?.Check, stored.Value?.LastParserRefusal);
    }

    protected override async Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        if (Grammar.IsLoading || Context.ProjectPath is not { } path) return;
        var generation = ++_readGeneration;
        var stored = await Context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (generation == _readGeneration && path == Context.ProjectPath && stored.Succeeded)
            Grammar.LoadStored(path, stored.Value?.Check, stored.Value?.LastParserRefusal);
    }

    // A person asked for the new Baseline, so its grammar is checked rather than only read.
    protected override async Task OnBaselineCapturedAsync(CancellationToken cancellationToken)
    {
        if (Context.ProjectPath is not { } path) return;
        _readGeneration++;
        await Grammar.SetProjectAsync(path, cancellationToken).ConfigureAwait(true);
        await Context.PublishGrammarCheckedAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task CheckGrammarAsync()
    {
        if (Context.ProjectPath is not { } path) return;
        // A stored read still in flight would otherwise replace this check with the older stored one.
        _readGeneration++;
        await Grammar.SetProjectAsync(path).ConfigureAwait(true);
        await Context.PublishGrammarCheckedAsync().ConfigureAwait(true);
    }

    private static IReadOnlyList<string> WordsFor(GrammarWarningRowViewModel row)
    {
        var rows = row.AttributionState switch
        {
            WarningDisplayState.MembershipCandidates => row.MembershipCandidateRows,
            WarningDisplayState.SpellingCandidates => row.SpellingCandidateRows,
            _ => row.WordRows,
        };
        return rows.Select(word => word.Word).Distinct(StringComparer.Ordinal).ToArray();
    }

    private void OnGrammarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Grammar.Warnings)
        {
            if (e.PropertyName == nameof(GrammarWarningsViewModel.Findings)) ResetAreaFilters();
            else if (e.PropertyName == nameof(GrammarWarningsViewModel.ShownCount)) UpdateAllAreasCount();
            else if (e.PropertyName == nameof(GrammarWarningsViewModel.SelectedTableEntry))
            {
                OnPropertyChanged(nameof(SelectedWarningArea));
                OnPropertyChanged(nameof(SelectedWarningSeverity));
                OnPropertyChanged(nameof(SelectedWarningWordCount));
            }
            OnPropertyChanged(nameof(HasSelectedWarningArea));
        }
        TryOpenRequestedWarning();
        BadgeCount = Grammar.ShowFindings ? Grammar.Warnings.TotalCount : null;
        PublishSummary();
        RaiseCheckState();
    }

    private void TryOpenRequestedWarning()
    {
        if (_requestedWarningCode is not { } code) return;
        Grammar.Warnings.Bucket = GrammarFindingBucket.All;
        Grammar.Warnings.TouchYourWords = false;
        var row = Grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>()
            .FirstOrDefault(item => string.Equals(item.GroupCode, code, StringComparison.Ordinal));
        if (row is null) return;
        Grammar.Warnings.OpenDetailsCommand.Execute(row);
        _requestedWarningCode = null;
    }

    private void PublishSummary() => Context.GrammarSummary =
        new GrammarSummary(Grammar.SummaryText, Grammar.ShowFindings, Grammar.Warnings.BreakdownText)
        {
            Findings = Grammar.Warnings.Findings,
        };

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.HasProject)) RaiseCheckState();
    }

    private void RaiseCheckState()
    {
        OnPropertyChanged(nameof(IsGrammarNotChecked));
        CheckGrammarCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesSelectedArea(object item)
    {
        if (_selectedArea is null) return true;
        return item switch
        {
            DenseTableGroup group => string.Equals(group.Label, _selectedArea, StringComparison.Ordinal),
            GrammarWarningRowViewModel row => _rowAreas.TryGetValue(row, out var area) &&
                string.Equals(area, _selectedArea, StringComparison.Ordinal),
            _ => false,
        };
    }

    private void SelectArea(WarningsAreaFilterOption? option)
    {
        if (option is null || !AreaFilters.Contains(option)) return;
        _selectedArea = option.Area;
        foreach (var item in AreaFilters) item.Update(item.Count,
            string.Equals(item.Area, _selectedArea, StringComparison.Ordinal));
        FilteredTableEntries.Refresh();
        if (Grammar.Warnings.SelectedTableEntry is { } selected &&
            !FilteredTableEntries.Cast<object>().Contains(selected))
            Grammar.Warnings.SelectedTableEntry = null;
        OnPropertyChanged(nameof(SelectedWarningArea));
        OnPropertyChanged(nameof(SelectedWarningSeverity));
        OnPropertyChanged(nameof(SelectedWarningWordCount));
        OnPropertyChanged(nameof(HasSelectedWarningArea));
    }

    private void OnWarningTableEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _rowAreas.Clear();
            _currentArea = null;
            foreach (var option in AreaFilters.Where(item => item.Area is not null))
                option.Update(0, option.Area == _selectedArea);
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is DenseTableGroup group)
                {
                    _currentArea = group.Label;
                    var option = AreaFilters.FirstOrDefault(candidate => candidate.Area == group.Label);
                    if (option is null)
                    {
                        option = new WarningsAreaFilterOption(group.Label, group.Label, 0,
                            group.Label == _selectedArea);
                        AreaFilters.Add(option);
                    }
                    else
                    {
                        option.Update(0, group.Label == _selectedArea);
                    }
                }
                else if (item is GrammarWarningRowViewModel row && _currentArea is { } area)
                {
                    _rowAreas[row] = area;
                    var option = AreaFilters.FirstOrDefault(candidate => candidate.Area == area);
                    if (option is not null) option.Update(option.Count + row.RepeatCount, area == _selectedArea);
                }
            }
        }

        UpdateAllAreasCount();
        OnPropertyChanged(nameof(SelectedWarningArea));
        OnPropertyChanged(nameof(SelectedWarningSeverity));
        OnPropertyChanged(nameof(SelectedWarningWordCount));
        OnPropertyChanged(nameof(HasSelectedWarningArea));
    }

    private void ResetAreaFilters()
    {
        _selectedArea = null;
        _currentArea = null;
        _rowAreas.Clear();
        AreaFilters.Clear();
        AreaFilters.Add(new WarningsAreaFilterOption(null, "All areas", Grammar.Warnings.ShownCount, true));
        FilteredTableEntries.Refresh();
        OnPropertyChanged(nameof(SelectedWarningArea));
        OnPropertyChanged(nameof(HasSelectedWarningArea));
    }

    private void UpdateAllAreasCount()
    {
        var allAreas = AreaFilters.FirstOrDefault(item => item.Area is null);
        allAreas?.Update(Grammar.Warnings.ShownCount, _selectedArea is null);
    }
}
