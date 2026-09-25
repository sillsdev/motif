using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Timing page on some words, filtered to one rule when one is named.</summary>
/// <param name="Words">The words whose time is in question.</param>
/// <param name="Rule">The grammar object to filter to, or <see langword="null"/> for every one.</param>
public sealed record OpenTimingRequest(IReadOnlyList<string> Words, string? Rule) : PageRequest(WorkspacePage.Timing);

/// <summary>The Timing page's model: where the latest Assessment's parse time went.</summary>
public sealed partial class TimingPageModel : PageModel
{
    private int _loadGeneration;
    private IReadOnlyList<string>? _explicitWords;
    private TimingWordSet _wordSet = new TimingWordSet.All();
    private CancellationTokenSource? _rerunCancellation;

    public TimingPageModel(WorkspaceContext context) : base(context)
    {
        Statistics = new StatisticsViewModel(context.Commands);
        Statistics.AssessedWord = context.Assess.Words.Find;
        Statistics.TryWord = context.TryWord;
        Statistics.OpenTimeLimit = () => context.OpenTexts(TextsTab.Texts);
        LoadFocusedTimingCommand = new AsyncRelayCommand(LoadFocusedTimingAsync,
            () => Focus is not null && Context.ProjectPath is not null);
        SelectWordSetCommand = new AsyncRelayCommand<string>(SelectWordSetAsync);
        UsePickedWordsCommand = new AsyncRelayCommand(UsePickedWordsAsync);
        UseTextsListCommand = new AsyncRelayCommand(UseTextsListAsync);
        UseCheckedWordsCommand = new AsyncRelayCommand(UseCheckedWordsAsync);
        UseMatrixCellCommand = new AsyncRelayCommand(UseMatrixCellAsync);
        ChooseRuleCommand = new AsyncRelayCommand<TimingAggregateRow>(ChooseRuleAsync);
        HandOffWordsCommand = new RelayCommand(() => context.HandOff(SelectedWords),
            () => SelectedWords.Count > 0);
        HandOffRuleCommand = new RelayCommand(() => context.HandOff(
            RuleDetail?.CostliestWords.Select(word => word.Word).ToArray() ?? []),
            () => RuleDetail?.CostliestWords.Count > 0);
        RerunWordsCommand = new AsyncRelayCommand(RerunWordsAsync,
            () => SelectedWords.Count > 0 && !IsRerunning && !context.Assess.IsActive);
        CancelRerunCommand = new RelayCommand(CancelRerun, () => IsRerunning);
    }

    /// <summary>The page's own statistics, read through the context's commands.</summary>
    public StatisticsViewModel Statistics { get; }

    /// <summary>What another page last opened Timing on, or <see langword="null"/> when none has.</summary>
    public OpenTimingRequest? Focus { get; private set; }

    /// <summary>Stored timing for the words and rule another page opened.</summary>
    public TimingResponse? FocusedTiming { get; private set; }

    /// <summary>Timing for the current stored Assessment across all its words.</summary>
    public TimingResponse? StoredTiming { get; private set; }

    /// <summary>Why the requested stored timing could not be read.</summary>
    public string? FocusedTimingError { get; private set; }

    /// <summary>Why the current stored timing could not be read.</summary>
    public string? StoredTimingError { get; private set; }

    public bool HasFocus => Focus is not null;

    public bool ShowNoEvidence => Context.HasNoEvidence && Context.CurrentEvidence?.MatchingAssessment is null &&
        Focus is null;

    public bool ShowStatistics => Context.HasEvidence && Focus is null;

    /// <summary>Whether to show the stored timing while no in-memory Assessment or focused request is active.</summary>
    public bool ShowStoredTiming => Context.CurrentEvidence?.MatchingAssessment is not null &&
        !Context.HasEvidence && Focus is null;

    public string FocusSummary => Focus is not { } focus ? string.Empty :
        focus.Rule is null
            ? $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")}"
            : $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")} under {focus.Rule}";

    public bool HasFocusedTiming => FocusedTiming is not null;

    public bool HasFocusedTimingError => FocusedTimingError is not null;

    /// <summary>Whether stored timing was read for the current Assessment.</summary>
    public bool HasStoredTiming => StoredTiming is not null;

    /// <summary>Whether the stored timing read returned a refusal.</summary>
    public bool HasStoredTimingError => StoredTimingError is not null;

    /// <summary>The stored timing response's word count and percentiles in one line.</summary>
    public string StoredTimingSummary => StoredTiming is not { } timing ? string.Empty :
        $"{timing.WordCount:N0} words · median {timing.MedianMs:N1} ms · 95th percentile {timing.Percentile95Ms:N1} ms";

    /// <summary>The selected words' stored totals grouped by kind.</summary>
    public TimingResponse? KindTiming { get; private set; }

    /// <summary>The same selected words' totals grouped by rule.</summary>
    public TimingResponse? RuleTiming { get; private set; }

    /// <summary>The selected rule's costliest words.</summary>
    public TimingResponse? RuleDetail { get; private set; }

    /// <summary>The exact words returned by the timing command.</summary>
    public IReadOnlyList<string> SelectedWords => KindTiming?.Words.Select(word => word.Word).ToArray() ?? [];

    /// <summary>The slowest words with the reason each stopped.</summary>
    public IReadOnlyList<TimingSlowWord> SlowestWords => KindTiming?.SlowestWords.Select(slow =>
        new TimingSlowWord(slow.Word, slow.ElapsedMs,
            KindTiming.Words.FirstOrDefault(word => word.Word == slow.Word)?.Completion ?? "Finished")).ToArray() ?? [];

    public bool HasTiming => KindTiming is not null;
    public bool HasSelectedWords => KindTiming?.WordCount > 0;
    public bool ShowEmptySelection => KindTiming is { WordCount: 0 };
    public bool ShowStaleTiming => KindTiming?.IsStale == true;
    public bool HasRule => SelectedRule is not null;
    public bool HasRuleDetail => RuleDetail is not null;
    public bool HasTimingError => TimingError is not null;
    public string? TimingError { get; private set; }
    public string WordSet => _wordSet.ToWireValue();
    public bool IsStepLimitSelected => _wordSet is TimingWordSet.StepLimited && _explicitWords is null;
    public bool IsSlowestSelected => _wordSet is TimingWordSet.Slowest && _explicitWords is null;
    public bool IsAllSelected => _wordSet is TimingWordSet.All && _explicitWords is null;
    public string ScopeLabel => KindTiming is null ? "No stored timing for these words" :
        $"Where the time went, for these {KindTiming.WordCount:N0} words: by kind of rule";
    public string PercentileSummary => KindTiming is null ? string.Empty :
        KindTiming.WordCount == 0 ? "No words in this selection have recorded parse time." :
        $"Median {KindTiming.MedianMs:N1} ms · 95th percentile {KindTiming.Percentile95Ms:N1} ms";
    public string? SelectedRule { get; private set; }
    public TimingAggregateRow? SelectedRuleRow => RuleTiming?.Aggregates.FirstOrDefault(row => row.Name == SelectedRule);
    public IReadOnlyList<WordRuleTiming> CostliestRuleWords => RuleDetail?.CostliestWords.Take(5).ToArray() ?? [];
    public string RuleSummary => SelectedRuleRow is not { } row ? string.Empty :
        $"{row.ShareOfTotal:P0} of these words' time · {row.Attempts:N0} attempts · {row.WordsTouched:N0} words touched";
    public IReadOnlyList<CompareCellViewModel> MatrixCells =>
        Context.Assess.Compare.Cells.Where(cell => cell.Count > 0).ToArray();
    public IReadOnlyList<ComparePresetViewModel> TextsLists =>
        Context.Assess.Compare.Presets.Where(preset => preset.Count > 0).ToArray();

    [ObservableProperty]
    private int _slowestCount = 20;

    [ObservableProperty]
    private string _pickedWords = string.Empty;

    [ObservableProperty]
    private CompareCellViewModel? _selectedMatrixCell;

    [ObservableProperty]
    private ComparePresetViewModel? _selectedTextsList;

    [ObservableProperty]
    private decimal _rerunSeconds = 30;

    [ObservableProperty]
    private decimal? _rerunSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RerunProgressText))]
    private int _rerunCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RerunProgressText))]
    private int _rerunTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RerunProgressText))]
    private string? _rerunWord;

    [ObservableProperty]
    private bool _isRerunning;

    [ObservableProperty]
    private string? _rerunMessage;

    public string RerunProgressText => RerunWord is null ? $"{RerunCompleted} of {RerunTotal} words done" :
        $"{RerunCompleted} of {RerunTotal} words done · parsing {RerunWord}";

    /// <summary>Refreshes the stored timing for the words and rule last opened.</summary>
    public IAsyncRelayCommand LoadFocusedTimingCommand { get; }
    public IAsyncRelayCommand<string> SelectWordSetCommand { get; }
    public IAsyncRelayCommand UsePickedWordsCommand { get; }
    public IAsyncRelayCommand UseTextsListCommand { get; }
    public IAsyncRelayCommand UseCheckedWordsCommand { get; }
    public IAsyncRelayCommand UseMatrixCellCommand { get; }
    public IAsyncRelayCommand<TimingAggregateRow> ChooseRuleCommand { get; }
    public IRelayCommand HandOffWordsCommand { get; }
    public IRelayCommand HandOffRuleCommand { get; }
    public IAsyncRelayCommand RerunWordsCommand { get; }
    public IRelayCommand CancelRerunCommand { get; }

    protected override void OnProjectCleared()
    {
        CancelRerun();
        _loadGeneration++;
        Focus = null;
        FocusedTiming = null;
        FocusedTimingError = null;
        StoredTiming = null;
        StoredTimingError = null;
        KindTiming = null;
        RuleTiming = null;
        RuleDetail = null;
        TimingError = null;
        SelectedRule = null;
        _wordSet = new TimingWordSet.All();
        _explicitWords = null;
        RaiseTimingState();
        Statistics.Reset();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Statistics.ProjectPath = projectPath;
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Statistics.Reset();
        Statistics.SummaryMarkdown = evidence.Assessment.SummaryMarkdown;
        Statistics.AssessmentId = evidence.Assessment.Measurements
            .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
        OnPropertyChanged(nameof(MatrixCells));
        OnPropertyChanged(nameof(TextsLists));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
        RaiseStoredTimingState();
        if (Context.ProjectPath is { } projectPath)
            _ = LoadScopeAsync(projectPath, CurrentAssessmentId, CancellationToken.None);
    }

    protected override async Task OnCurrentEvidencePublishedAsync(
        CurrentEvidenceSnapshot evidence, CancellationToken cancellationToken)
    {
        if (evidence.MatchingAssessment is not { } assessment || Context.ProjectPath is not { } projectPath)
        {
            StoredTiming = null;
            StoredTimingError = null;
            RaiseStoredTimingState();
            return;
        }

        if (Focus is null) await LoadScopeAsync(projectPath, assessment.AssessmentId, cancellationToken);
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is not OpenTimingRequest timing) return;
        Focus = timing with { Words = timing.Words.ToArray() };
        _explicitWords = Focus.Words.Count > 0 ? Focus.Words : null;
        _wordSet = new TimingWordSet.All();
        SelectedRule = timing.Rule;
        FocusedTiming = null;
        FocusedTimingError = null;
        RaiseFocusState();
        if (LoadFocusedTimingCommand.CanExecute(null)) _ = LoadFocusedTimingCommand.ExecuteAsync(null);
    }

    private async Task LoadFocusedTimingAsync()
    {
        if (Focus is null || Context.ProjectPath is not { } projectPath) return;
        await LoadScopeAsync(projectPath, CurrentAssessmentId, CancellationToken.None);
    }

    private string? CurrentAssessmentId => Context.Evidence?.Assessment.Measurements
        .SingleOrDefault(measurement => measurement.Kind == "ParseTime")?.AssessmentId ??
        Context.CurrentEvidence?.MatchingAssessment?.AssessmentId;

    private IReadOnlyList<string>? CurrentTimingOverrides => Context.Evidence?.Assessment.TimingOverrideAssessmentIds;

    private async Task SelectWordSetAsync(string? wordSet)
    {
        var selection = TimingWordSet.Parse(wordSet);
        if (selection is null) return;
        await SelectTimingWordSetAsync(selection);
    }

    private async Task SelectTimingWordSetAsync(TimingWordSet selection)
    {
        if (Context.ProjectPath is not { } projectPath) return;
        Focus = null;
        _explicitWords = null;
        _wordSet = selection;
        SelectedRule = null;
        RaiseFocusState();
        await LoadScopeAsync(projectPath, CurrentAssessmentId, CancellationToken.None);
    }

    private Task UsePickedWordsAsync() => SelectExplicitWordsAsync(
        PickedWords.Replace("\r\n", "\n").Split('\n').Select(word => word.Trim()).Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray());

    private Task UseTextsListAsync() => SelectExplicitWordsAsync(SelectedTextsList is { } list
        ? Context.Assess.Compare.WordsInFamily(list.Family) : []);

    private Task UseCheckedWordsAsync() => SelectExplicitWordsAsync(Context.Assess.Compare.CheckedWords);

    private Task UseMatrixCellAsync()
    {
        if (SelectedMatrixCell is not { } cell) return Task.CompletedTask;
        var standing = cell.Row switch
        {
            WordProjectStatus.NotPresent => TimingStanding.NotPresent,
            WordProjectStatus.Approved => TimingStanding.Approved,
            WordProjectStatus.Candidate => TimingStanding.Candidate,
            WordProjectStatus.Rejected => TimingStanding.Rejected,
            WordProjectStatus.IncorrectSpelling => TimingStanding.IncorrectSpelling,
            _ => throw new ArgumentOutOfRangeException(nameof(cell)),
        };
        return SelectTimingWordSetAsync(new TimingWordSet.MatrixCell(standing, cell.Column));
    }

    private async Task SelectExplicitWordsAsync(IReadOnlyList<string> words)
    {
        if (words.Count == 0 || Context.ProjectPath is not { } projectPath) return;
        Focus = null;
        _explicitWords = words;
        _wordSet = new TimingWordSet.All();
        SelectedRule = null;
        RaiseFocusState();
        await LoadScopeAsync(projectPath, CurrentAssessmentId, CancellationToken.None);
    }

    private async Task LoadScopeAsync(string projectPath, string? assessmentId, CancellationToken cancellationToken)
    {
        var generation = ++_loadGeneration;
        KindTiming = null;
        RuleTiming = null;
        RuleDetail = null;
        TimingError = null;
        RaiseTimingState();
        var top = _wordSet is TimingWordSet.Slowest ? Math.Max(1, SlowestCount) : 10;
        var kind = await Context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId,
            WordSet, "kind", Top: top, ExplicitWords: _explicitWords,
            OverrideAssessmentIds: CurrentTimingOverrides), cancellationToken).ConfigureAwait(true);
        if (generation != _loadGeneration || Context.ProjectPath != projectPath) return;
        if (!kind.Succeeded)
        {
            TimingError = kind.Refusal?.Message;
            FocusedTimingError = Focus is null ? null : TimingError;
            StoredTimingError = Focus is null ? TimingError : null;
            RaiseTimingState();
            return;
        }

        KindTiming = kind.Value;
        FocusedTiming = Focus is null ? null : kind.Value;
        StoredTiming = Focus is null && _wordSet is TimingWordSet.All && _explicitWords is null ? kind.Value : StoredTiming;
        var rule = await Context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId,
            WordSet, "rule", Top: top, ExplicitWords: _explicitWords,
            OverrideAssessmentIds: CurrentTimingOverrides), cancellationToken).ConfigureAwait(true);
        if (generation != _loadGeneration || Context.ProjectPath != projectPath) return;
        RuleTiming = rule.Succeeded ? rule.Value : null;
        TimingError = rule.Succeeded ? null : rule.Refusal?.Message;
        if (RuleTiming is null || RuleTiming.Aggregates.All(row => row.Name != SelectedRule))
            SelectedRule = RuleTiming?.Aggregates.FirstOrDefault()?.Name;
        RaiseTimingState();
        if (SelectedRule is not null) await LoadRuleDetailAsync(projectPath, assessmentId, generation);
    }

    private async Task ChooseRuleAsync(TimingAggregateRow? row)
    {
        if (row is null || Context.ProjectPath is not { } projectPath) return;
        SelectedRule = row.Name;
        RuleDetail = null;
        RaiseTimingState();
        await LoadRuleDetailAsync(projectPath, CurrentAssessmentId, _loadGeneration);
    }

    private async Task LoadRuleDetailAsync(string projectPath, string? assessmentId, int generation)
    {
        var rule = SelectedRule;
        var detail = await Context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId,
            WordSet, "rule", rule, Top: Math.Max(10, KindTiming?.WordCount ?? 10), ExplicitWords: _explicitWords,
            OverrideAssessmentIds: CurrentTimingOverrides),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _loadGeneration || Context.ProjectPath != projectPath || SelectedRule != rule) return;
        RuleDetail = detail.Succeeded ? detail.Value : null;
        TimingError = detail.Succeeded ? null : detail.Refusal?.Message;
        if (Focus?.Rule is not null) FocusedTiming = RuleDetail;
        RaiseTimingState();
    }

    private async Task RerunWordsAsync()
    {
        var words = SelectedWords;
        if (words.Count == 0) return;
        if (RerunSeconds <= 0 || RerunSeconds > int.MaxValue / 1000m ||
            RerunSteps is <= 0 || RerunSteps is > long.MaxValue ||
            RerunSteps is { } steps && decimal.Truncate(steps) != steps)
        {
            RerunMessage = "Enter a positive per-word time and a positive whole-number step limit.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _rerunCancellation = cancellation;
        IsRerunning = true;
        RerunWordsCommand.NotifyCanExecuteChanged();
        CancelRerunCommand.NotifyCanExecuteChanged();
        RerunCompleted = 0;
        RerunTotal = words.Count;
        RerunMessage = null;
        try
        {
            foreach (var word in words)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                RerunWord = word;
                await Context.Assess.RerunAsync([word], (int)(RerunSeconds * 1000),
                    RerunSteps is { } cap ? new StepCap((long)cap) : null);
                if (Context.Assess.State == RunState.Cancelled || cancellation.IsCancellationRequested) break;
                if (Context.Assess.State == RunState.Refused)
                {
                    RerunMessage = Context.Assess.Refusal?.Message;
                    break;
                }
                RerunCompleted++;
            }
            if (RerunMessage is null)
                RerunMessage = RerunCompleted == RerunTotal ? "Re-run complete." : "Re-run cancelled.";
        }
        catch (OperationCanceledException)
        {
            RerunMessage = "Re-run cancelled.";
        }
        finally
        {
            RerunWord = null;
            _rerunCancellation = null;
            IsRerunning = false;
            RerunWordsCommand.NotifyCanExecuteChanged();
            CancelRerunCommand.NotifyCanExecuteChanged();
        }
    }

    private void CancelRerun()
    {
        _rerunCancellation?.Cancel();
        if (Context.Assess.CancelCommand.CanExecute(null)) Context.Assess.CancelCommand.Execute(null);
    }

    protected override Task OnStopWorkAsync()
    {
        CancelRerun();
        return RerunWordsCommand.ExecutionTask ?? Task.CompletedTask;
    }

    private void RaiseTimingState()
    {
        foreach (var property in new[]
        {
            nameof(KindTiming), nameof(RuleTiming), nameof(RuleDetail), nameof(TimingError),
            nameof(WordSet), nameof(IsStepLimitSelected), nameof(IsSlowestSelected),
            nameof(IsAllSelected), nameof(SelectedRule), nameof(SelectedRuleRow), nameof(CostliestRuleWords),
            nameof(SelectedWords),
            nameof(SlowestWords), nameof(HasTiming), nameof(HasSelectedWords),
            nameof(ShowEmptySelection), nameof(HasRule), nameof(HasRuleDetail),
            nameof(HasTimingError), nameof(ShowStaleTiming), nameof(ScopeLabel),
            nameof(PercentileSummary), nameof(RuleSummary),
        }) OnPropertyChanged(property);
        RaiseFocusState();
        RaiseStoredTimingState();
        RerunWordsCommand.NotifyCanExecuteChanged();
        HandOffWordsCommand.NotifyCanExecuteChanged();
        HandOffRuleCommand.NotifyCanExecuteChanged();
    }

    private void RaiseFocusState()
    {
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(HasFocus));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
        OnPropertyChanged(nameof(FocusSummary));
        OnPropertyChanged(nameof(FocusedTiming));
        OnPropertyChanged(nameof(HasFocusedTiming));
        OnPropertyChanged(nameof(FocusedTimingError));
        OnPropertyChanged(nameof(HasFocusedTimingError));
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
    }

    private void RaiseStoredTimingState()
    {
        OnPropertyChanged(nameof(StoredTiming));
        OnPropertyChanged(nameof(StoredTimingError));
        OnPropertyChanged(nameof(HasStoredTiming));
        OnPropertyChanged(nameof(HasStoredTimingError));
        OnPropertyChanged(nameof(ShowStoredTiming));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(StoredTimingSummary));
    }
}

/// <summary>A slow word's recorded time and completion shown together.</summary>
public sealed record TimingSlowWord(string Word, int ElapsedMs, string Completion);
