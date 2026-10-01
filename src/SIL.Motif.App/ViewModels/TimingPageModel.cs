using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Timing page on some words, filtered to one rule when one is named.</summary>
/// <param name="Words">The words whose time is in question.</param>
/// <param name="Rule">The grammar object to filter to, or <see langword="null"/> for every one.</param>
public sealed record OpenTimingRequest(IReadOnlyList<string> Words, string? Rule) : PageRequest(WorkspacePage.Timing);

/// <summary>The Timing page's model: where the latest Assessment's parse time went.</summary>
public sealed partial class TimingPageModel : PageModel
{
    /// <summary>What the page says, where the numbers would be, when no parse time was measured.</summary>
    public const string NoTimingRecordedText = "No parse times were recorded for these words.";

    private int _loadGeneration;
    private bool _isLoadingTiming;
    private IReadOnlyList<string>? _explicitWords;
    private TimingWordSet _wordSet = new TimingWordSet.All();
    private CancellationTokenSource? _rerunCancellation;

    public TimingPageModel(WorkspaceContext context) : base(context)
    {
        Statistics = new StatisticsViewModel(context.Commands);
        Statistics.AssessedWord = context.Assess.Words.Find;
        Statistics.TryWord = context.TryWord;
        Statistics.OpenTimeLimit = () => context.OpenTexts(TextsTab.AnalyzeTexts);
        LoadFocusedTimingCommand = new AsyncRelayCommand(LoadFocusedTimingAsync,
            () => Focus is not null && Context.ProjectPath is not null);
        SelectWordSetCommand = new AsyncRelayCommand<string>(SelectWordSetAsync);
        UsePickedWordsCommand = new AsyncRelayCommand(UsePickedWordsAsync, CanUsePickedWords);
        UseTextsListCommand = new AsyncRelayCommand(UseTextsListAsync, CanUseTextsList);
        UseCheckedWordsCommand = new AsyncRelayCommand(UseCheckedWordsAsync, CanUseCheckedWords);
        UseMatrixCellCommand = new AsyncRelayCommand(UseMatrixCellAsync, CanUseMatrixCell);
        ChooseRuleCommand = new AsyncRelayCommand<TimingAggregateRow>(ChooseRuleAsync);
        HandOffWordsCommand = new RelayCommand(() => context.HandOff(SelectedWords),
            () => SelectedWords.Count > 0);
        HandOffRuleCommand = new RelayCommand(() => context.HandOff(
            RuleDetail?.CostliestWords.Select(word => word.Word).ToArray() ?? []),
            () => RuleDetail?.CostliestWords.Count > 0);
        RerunWordsCommand = new AsyncRelayCommand(RerunWordsAsync,
            () => SelectedWords.Count > 0 && !IsRerunning && !context.Assess.IsActive);
        CancelRerunCommand = new RelayCommand(CancelRerun, () => IsRerunning);
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectEvidence.IsStale)) OnPropertyChanged(nameof(ShowStaleTiming));
        };
        context.PropertyChanged += OnContextPropertyChanged;
        context.Assess.Compare.CheckedWordsChanged += OnCheckedWordsChanged;
        // The word rows come from the parse on screen, which can arrive after the times were read.
        context.Assess.Words.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AssessWordsViewModel.AllRows)) return;
            OnPropertyChanged(nameof(SlowestWordRows));
            OnPropertyChanged(nameof(CostliestRuleWordRows));
        };
    }

    /// <summary>The page's own statistics, read through the context's commands.</summary>
    public StatisticsViewModel Statistics { get; }

    /// <summary>What another page last opened Timing on, or <see langword="null"/> when none has.</summary>
    public OpenTimingRequest? Focus { get; private set; }

    /// <summary>Stored timing for the words and rule another page opened.</summary>
    public TimingResponse? FocusedTiming { get; private set; }

    /// <summary>Timing for the current stored Assessment across all its words.</summary>
    public TimingResponse? StoredTiming { get; private set; }

    /// <summary>Why the requested stored timing could not be read, in the window's words.</summary>
    public WindowRefusal? FocusedTimingRefusal { get; private set; }

    /// <summary>Why the current stored timing could not be read, in the window's words.</summary>
    public WindowRefusal? StoredTimingRefusal { get; private set; }

    public bool HasFocus => Focus is not null;

    public bool ShowNoEvidence => Context.NeedsAssessment && Focus is null;

    public bool ShowStatistics => !Context.NeedsAssessment && Context.HasEvidence && Focus is null;

    /// <summary>Whether to show the stored timing while no in-memory Assessment or focused request is active.</summary>
    public bool ShowStoredTiming => !Context.NeedsAssessment && Context.Evidence.StoredAssessmentId is not null &&
        !Context.HasEvidence && Focus is null;

    public string FocusSummary => Focus is not { } focus ? string.Empty :
        focus.Rule is null
            ? $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")}"
            : $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")} under {focus.Rule}";

    public bool HasFocusedTiming => FocusedTiming is not null;

    public bool HasFocusedTimingRefusal => FocusedTimingRefusal is not null;

    /// <summary>Whether stored timing was read for the current Assessment.</summary>
    public bool HasStoredTiming => StoredTiming is not null;

    /// <summary>Whether the stored timing read returned a refusal.</summary>
    public bool HasStoredTimingRefusal => StoredTimingRefusal is not null;

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

    /// <summary>The slowest words as word rows, each with the parse time Timing measured for it.</summary>
    public IReadOnlyList<ListedWordViewModel> SlowestWordRows => KindTiming?.SlowestWords
        .Select(slow => Listed(slow.Word, SpeedText.PerWord(slow.ElapsedMs))).ToArray() ?? [];

    public bool HasTiming => KindTiming is not null;

    /// <summary>Whether the stored parse times for the chosen words are being read.</summary>
    public bool IsLoadingTiming => _isLoadingTiming;

    /// <summary>Whether an open project has no measured parse time to show, and no read is pending or refused.</summary>
    public bool ShowNoTimingRecorded => Context.ProjectPath is not null && !Context.NeedsAssessment &&
        KindTiming is null && !_isLoadingTiming && TimingRefusal is null;

    /// <summary>Whether the chosen words have measured parse times to lead the page with.</summary>
    public bool HasHeadline => MeasuredWords.Count > 0;

    /// <summary>The chosen words' summed parse time, as PanGloss measured it.</summary>
    public string HeadlineTotal => HasHeadline ? SpeedText.Duration(WordTimeMs)
        : string.Empty;

    /// <summary>Which words the total covers: all of them, or the group chosen above, and how many were timed.</summary>
    public string HeadlineTotalCaption => !HasHeadline ? string.Empty
        : MeasuredWords.Count < ChosenWordCount
            ? $"for {MeasuredWords.Count:N0} of {(IsAllSelected ? "the" : "these")} {ChosenWordCount:N0} words"
        : MeasuredWords.Count == 1 ? "for 1 word"
        : $"for {(IsAllSelected ? "all" : "these")} {MeasuredWords.Count:N0} words";

    /// <summary>The chosen words' median parse time.</summary>
    public string HeadlineMedian => HasHeadline && KindTiming?.MedianMs is { } median
        ? SpeedText.PerWord(median) : string.Empty;

    /// <summary>How many of the chosen words stopped at the step limit.</summary>
    public string HeadlineStopped => KindTiming?.Words
        .Count(word => word.Completion == TimingCompletion.StepLimit).ToString("N0", System.Globalization.CultureInfo.CurrentCulture)
        ?? string.Empty;

    /// <summary>The caption under the stopped-word count.</summary>
    public string HeadlineStoppedCaption => "stopped at the step limit";

    private IReadOnlyList<TimingWordRow> MeasuredWords =>
        KindTiming?.Words.Where(word => word.ElapsedMs is not null).ToArray() ?? [];

    private int ChosenWordCount => KindTiming?.Words.Count ?? 0;

    private double WordTimeMs => KindTiming?.Attribution.WordTimeMs ?? 0;

    private string MeasuredWordsPhrase => MeasuredWords.Count < ChosenWordCount
        ? $"{MeasuredWords.Count:N0} of {(IsAllSelected ? "the" : "these")} {ChosenWordCount:N0} words"
        : MeasuredWords.Count == 1 ? "this word"
        : $"{(IsAllSelected ? "all" : "these")} {MeasuredWords.Count:N0} words";

    /// <summary>The chosen words' time by kind, with the recorded unattributed time.</summary>
    public IReadOnlyList<TimingShare> KindShares
    {
        get
        {
            var shares = SharesOf(KindTiming?.Aggregates, byKind: true);
            if (!HasHeadline || KindTiming is not { } timing) return shares;
            var attribution = timing.Attribution;
            return [.. shares, new TimingShare("Not attributed", string.Empty, attribution.NotAttributedMs,
                attribution.NotAttributedShare, 0, null)];
        }
    }

    /// <summary>The chosen words' time by rule, each a share of the same whole parse time as the kinds.</summary>
    public IReadOnlyList<TimingShare> RuleShares => SharesOf(RuleTiming?.Aggregates, byKind: false);

    /// <summary>The rule table's share column, named by what it divides by.</summary>
    public string RuleShareHeader => HasHeadline ? $"SHARE OF {HeadlineTotal}" : "SHARE";

    /// <summary>What every share on the page is a share of.</summary>
    public string ShareDenominatorText
    {
        get
        {
            if (KindTiming is null) return string.Empty;
            if (!HasHeadline) return "No parse time was kept for these words, so no shares are shown.";
            var missing = ChosenWordCount - MeasuredWords.Count;
            return $"Every share is of the {HeadlineTotal} {MeasuredWordsPhrase} took to parse" + (missing > 0
                ? $"; {missing:N0} {(missing == 1 ? "has" : "have")} no parse time." : ".");
        }
    }

    /// <summary>What the response recorded as unattributed time and overrun.</summary>
    public string OtherTimeText
    {
        get
        {
            if (!HasHeadline || KindTiming is not { } timing) return string.Empty;
            var attribution = timing.Attribution;
            var notAttributed = attribution.NotAttributedMs is { } time
                ? $"{SpeedText.PerWord(time)} was not attributed to rules or lookups."
                : "Not attributed time was not recorded for these words.";
            if (!attribution.Overrun) return notAttributed;
            var overrun = attribution.OverrunMs > 0
                ? $"Object time exceeded word time by {SpeedText.PerWord(attribution.OverrunMs)} across the words."
                : "Object time exceeded word time for one or more words.";
            return $"{notAttributed} {overrun}";
        }
    }

    private IReadOnlyList<TimingShare> SharesOf(IReadOnlyList<TimingAggregateRow>? rows, bool byKind) => rows is null ? [] :
        [.. rows.Select(row => new TimingShare(byKind ? TimingShare.KindName(row.Name) : row.Name, row.Kind, row.SelfMs,
            row.ShareOfWordTime, row.WordsTouched, row))];
    public bool HasSelectedWords => KindTiming?.WordCount > 0;
    public bool ShowEmptySelection => KindTiming is { WordCount: 0 };
    public bool ShowStaleTiming => HasTiming && Context.Evidence.IsStale;
    public bool HasRule => SelectedRule is not null;
    public bool HasRuleDetail => RuleDetail is not null;
    public bool HasTimingRefusal => TimingRefusal is not null;
    public WindowRefusal? TimingRefusal { get; private set; }
    public string WordSet => _wordSet.ToWireValue();
    public bool IsStepLimitSelected => _wordSet is TimingWordSet.StepLimited && _explicitWords is null;
    public bool IsSlowestSelected => _wordSet is TimingWordSet.Slowest && _explicitWords is null;
    public bool IsAllSelected => _wordSet is TimingWordSet.All && _explicitWords is null;
    public string ScopeLabel => KindTiming is null ? "No stored timing for these words" :
        $"Where the time went, for these {KindTiming.WordCount:N0} words: by kind of rule";
    public string PercentileSummary => KindTiming is null ? string.Empty :
        KindTiming.WordCount == 0 ? "No words in this selection have recorded parse time." :
        $"Median {KindTiming.MedianMs:N1} ms · 95th percentile {KindTiming.Percentile95Ms:N1} ms";
    /// <summary>The chosen parser object's key, or the name another page asked for until the rules are read.</summary>
    public string? SelectedRule { get; private set; }
    public TimingAggregateRow? SelectedRuleRow => RuleTiming?.Aggregates.FirstOrDefault(row => row.Key == SelectedRule);

    /// <summary>The chosen rule's label, for the side card's title.</summary>
    public string? SelectedRuleName => SelectedRuleRow?.Name ??
        (Focus?.Rule is not null ? "Requested rule" : SelectedRule);

    /// <summary>The by-rule table's rows, each saying whether it is the rule the side card describes.</summary>
    public IReadOnlyList<TimingRuleRow> RuleRows =>
        [.. RuleShares.Select(share => new TimingRuleRow(share, share.Source?.Key == SelectedRule))];
    public IReadOnlyList<WordRuleTiming> CostliestRuleWords => RuleDetail?.CostliestWords.Take(5).ToArray() ?? [];

    /// <summary>
    /// The selected rule's costliest words as word rows, each with the rule's time in it beside that word's whole
    /// parse time.
    /// </summary>
    public IReadOnlyList<ListedWordViewModel> CostliestRuleWordRows => [.. CostliestRuleWords.Select(word =>
        Listed(word.Word, KindTiming?.Words.FirstOrDefault(row => row.Word == word.Word)?.ElapsedMs is { } whole
            ? $"{SpeedText.PerWord(word.SelfMs)} of its {SpeedText.PerWord(whole)}"
            : SpeedText.PerWord(word.SelfMs)))];

    /// <summary>The heading over the chosen rule's costliest words, naming the rule.</summary>
    public string RuleWordsTitle => $"Words where {SelectedRuleName} took longest";

    private ListedWordViewModel Listed(string word, string timeText)
    {
        var listed = Context.Assess.Words.Listed(word);
        listed.TimeText = timeText;
        return listed;
    }

    /// <summary>The selected rule's time, its share of the chosen words' whole parse time, and its words.</summary>
    public string RuleSummary
    {
        get
        {
            if (RuleShares.FirstOrDefault(share => share.Source?.Key == SelectedRule) is not { } rule)
                return Focus?.Rule is null ? string.Empty : "No stored timing for this rule.";
            return (rule.Share is null ? rule.TimeText :
                $"{rule.TimeText} · {rule.ShareText} of {MeasuredWordsPhrase}' {HeadlineTotal}") +
                $" · recorded in {SpeedText.Count(rule.Words, "word", "words")}";
        }
    }
    public IReadOnlyList<CompareCellViewModel> MatrixCells =>
        Context.Assess.Compare.Cells.Where(cell => cell.Count > 0).ToArray();
    public IReadOnlyList<ComparePresetViewModel> TextsLists =>
        Context.Assess.Compare.Presets.Where(preset => preset.Count > 0).ToArray();

    public string UseMatrixCellDisabledReason => Context.ProjectPath is null
        ? "Open a project first."
        : SelectedMatrixCell is null ? "Choose a matrix cell above first." : string.Empty;

    public bool UseMatrixCellUnavailable => UseMatrixCellDisabledReason.Length > 0;

    public string UseTextsListDisabledReason => Context.ProjectPath is null
        ? "Open a project first."
        : SelectedTextsList is null ? "Choose a word list above first."
        : Context.Assess.Compare.WordsInPreset(SelectedTextsList).Count == 0
            ? "No words in this list to use for Timing." : string.Empty;

    public bool UseTextsListUnavailable => UseTextsListDisabledReason.Length > 0;

    public string UsePickedWordsDisabledReason => Context.ProjectPath is null
        ? "Open a project first."
        : PickedWordValues.Count == 0 ? "Enter one or more words, one per line." : string.Empty;

    public bool UsePickedWordsUnavailable => UsePickedWordsDisabledReason.Length > 0;

    public string UseCheckedWordsDisabledReason => Context.ProjectPath is null
        ? "Open a project first."
        : Context.Assess.Compare.CheckedWords.Count == 0 ? "Tick words in Texts first." : string.Empty;

    public bool UseCheckedWordsUnavailable => UseCheckedWordsDisabledReason.Length > 0;

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
        _isLoadingTiming = false;
        Focus = null;
        FocusedTiming = null;
        FocusedTimingRefusal = null;
        StoredTiming = null;
        StoredTimingRefusal = null;
        KindTiming = null;
        RuleTiming = null;
        RuleDetail = null;
        TimingRefusal = null;
        SelectedRule = null;
        SelectedMatrixCell = null;
        SelectedTextsList = null;
        _wordSet = new TimingWordSet.All();
        _explicitWords = null;
        RaiseTimingState();
        Statistics.ProjectPath = null;
        Statistics.Reset();
        NotifySourceAvailability();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Statistics.ProjectPath = projectPath;
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
        NotifySourceAvailability();
        return Task.CompletedTask;
    }

    protected override async Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.Assessment is { } shown)
        {
            Statistics.Reset();
            Statistics.SummaryMarkdown = shown.Assessment.SummaryMarkdown;
            Statistics.AssessmentId = evidence.ObjectTimingAssessmentId;
        }
        OnPropertyChanged(nameof(MatrixCells));
        OnPropertyChanged(nameof(TextsLists));
        NotifySourceAvailability();
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
        if (evidence.ParseTimeAssessmentId is not { } assessmentId || Context.ProjectPath is not { } projectPath)
        {
            StoredTiming = null;
            StoredTimingRefusal = null;
            RaiseStoredTimingState();
            return;
        }

        RaiseStoredTimingState();
        // A run answers whatever the page is focused on; a stored read never replaces a focused view.
        if (evidence.Assessment is { IsStored: false } || Focus is null)
            await LoadScopeAsync(projectPath, assessmentId, cancellationToken).ConfigureAwait(true);
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is not OpenTimingRequest timing) return;
        Focus = timing with { Words = timing.Words.ToArray() };
        _explicitWords = Focus.Words.Count > 0 ? Focus.Words : null;
        _wordSet = new TimingWordSet.All();
        SelectedRule = timing.Rule;
        FocusedTiming = null;
        FocusedTimingRefusal = null;
        RaiseFocusState();
        if (LoadFocusedTimingCommand.CanExecute(null)) _ = LoadFocusedTimingCommand.ExecuteAsync(null);
    }

    private async Task LoadFocusedTimingAsync()
    {
        if (Focus is null || Context.ProjectPath is not { } projectPath) return;
        await LoadScopeAsync(projectPath, CurrentAssessmentId, CancellationToken.None);
    }

    private string? CurrentAssessmentId => Context.Evidence.ParseTimeAssessmentId;

    private IReadOnlyList<string>? CurrentTimingOverrides =>
        Context.HasEvidence ? Context.Evidence.TimingOverrideAssessmentIds : null;

    private async Task SelectWordSetAsync(string? wordSet)
    {
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.Text("wordSet"));
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

    private async Task UsePickedWordsAsync()
    {
        var words = PickedWordValues;
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.List("words", words.Count));
        await SelectExplicitWordsAsync(words);
    }

    private async Task UseTextsListAsync()
    {
        var words = SelectedTextsList is { } list
            ? Context.Assess.Compare.WordsInPreset(list) : [];
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.List("words", words.Count));
        await SelectExplicitWordsAsync(words);
    }

    private async Task UseCheckedWordsAsync()
    {
        var words = Context.Assess.Compare.CheckedWords;
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.List("words", words.Count));
        await SelectExplicitWordsAsync(words);
    }

    private IReadOnlyList<string> PickedWordValues => PickedWords.Replace("\r\n", "\n").Split('\n')
        .Select(word => word.Trim()).Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private bool CanUsePickedWords() => Context.ProjectPath is not null && PickedWordValues.Count > 0;

    private bool CanUseTextsList() => Context.ProjectPath is not null && SelectedTextsList is { } list &&
        Context.Assess.Compare.WordsInPreset(list).Count > 0;

    private bool CanUseCheckedWords() => Context.ProjectPath is not null &&
        Context.Assess.Compare.CheckedWords.Count > 0;

    private bool CanUseMatrixCell() => Context.ProjectPath is not null && SelectedMatrixCell is not null;

    private void OnCheckedWordsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(MatrixCells));
        OnPropertyChanged(nameof(TextsLists));
        NotifySourceAvailability();
    }

    private void NotifySourceAvailability()
    {
        UseMatrixCellCommand.NotifyCanExecuteChanged();
        UseTextsListCommand.NotifyCanExecuteChanged();
        UsePickedWordsCommand.NotifyCanExecuteChanged();
        UseCheckedWordsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(UseMatrixCellDisabledReason));
        OnPropertyChanged(nameof(UseMatrixCellUnavailable));
        OnPropertyChanged(nameof(UseTextsListDisabledReason));
        OnPropertyChanged(nameof(UseTextsListUnavailable));
        OnPropertyChanged(nameof(UsePickedWordsDisabledReason));
        OnPropertyChanged(nameof(UsePickedWordsUnavailable));
        OnPropertyChanged(nameof(UseCheckedWordsDisabledReason));
        OnPropertyChanged(nameof(UseCheckedWordsUnavailable));
    }

    partial void OnPickedWordsChanged(string value) => NotifySourceAvailability();

    partial void OnSelectedMatrixCellChanged(CompareCellViewModel? value) => NotifySourceAvailability();

    partial void OnSelectedTextsListChanged(ComparePresetViewModel? value) => NotifySourceAvailability();

    private async Task UseMatrixCellAsync()
    {
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.Text("wordSet"));
        if (SelectedMatrixCell is not { } cell) return;
        var standing = cell.Row switch
        {
            WordProjectStatus.NotPresent => TimingStanding.NotPresent,
            WordProjectStatus.Approved => TimingStanding.Approved,
            WordProjectStatus.Candidate => TimingStanding.Candidate,
            WordProjectStatus.Rejected => TimingStanding.Rejected,
            WordProjectStatus.IncorrectSpelling => TimingStanding.IncorrectSpelling,
            _ => throw new ArgumentOutOfRangeException(nameof(cell)),
        };
        await SelectTimingWordSetAsync(new TimingWordSet.MatrixCell(standing, cell.Column));
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
        TimingRefusal = null;
        _isLoadingTiming = true;
        RaiseTimingState();
        var top = _wordSet is TimingWordSet.Slowest ? Math.Max(1, SlowestCount) : 10;
        CommandOutcome<TimingResponse> kind;
        try
        {
            kind = await Context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId,
                WordSet, "kind", Top: top, ExplicitWords: _explicitWords,
                OverrideAssessmentIds: CurrentTimingOverrides), cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            if (generation == _loadGeneration) _isLoadingTiming = false;
        }
        if (generation != _loadGeneration || Context.ProjectPath != projectPath) return;
        if (!kind.Succeeded)
        {
            TimingRefusal = kind.Refusal is null ? null : WindowRefusal.From(kind.Refusal);
            FocusedTimingRefusal = Focus is null ? null : TimingRefusal;
            StoredTimingRefusal = Focus is null ? TimingRefusal : null;
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
        TimingRefusal = rule.Succeeded || rule.Refusal is null ? null : WindowRefusal.From(rule.Refusal);
        var requestedRule = Focus?.Rule;
        SelectedRule = requestedRule is null
            ? RuleTiming?.Aggregates.FirstOrDefault()?.Key
            : RuleTiming?.Aggregates.FirstOrDefault(row => StringComparer.Ordinal.Equals(row.Key, requestedRule))?.Key
                ?? requestedRule;
        RaiseTimingState();
        if (SelectedRuleRow is not null) await LoadRuleDetailAsync(projectPath, assessmentId, generation);
    }

    private async Task ChooseRuleAsync(TimingAggregateRow? row)
    {
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.Text("rule"));
        if (row is null || Context.ProjectPath is not { } projectPath) return;
        SelectedRule = row.Key;
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
        TimingRefusal = detail.Succeeded || detail.Refusal is null ? null : WindowRefusal.From(detail.Refusal);
        if (Focus?.Rule is not null) FocusedTiming = RuleDetail;
        RaiseTimingState();
    }

    private async Task RerunWordsAsync()
    {
        var words = SelectedWords;
        var shapes = new List<string>
        {
            UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.List("words", words.Count),
            UsageArgumentShape.Number("perWordLimitMs"),
        };
        if (RerunSteps is not null)
            shapes.Add(UsageArgumentShape.Number("perWordStepLimit"));
        using var usageAction = Context.Commands.BeginUsageAction("assess", [.. shapes]);
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
                    RerunMessage = Context.Assess.ShownRefusal?.Sentence;
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
            nameof(KindTiming), nameof(RuleTiming), nameof(RuleDetail), nameof(TimingRefusal),
            nameof(WordSet), nameof(IsStepLimitSelected), nameof(IsSlowestSelected),
            nameof(IsAllSelected), nameof(SelectedRule), nameof(SelectedRuleName), nameof(SelectedRuleRow), nameof(RuleRows), nameof(CostliestRuleWords),
            nameof(SelectedWords),
            nameof(SlowestWordRows), nameof(HasTiming), nameof(HasSelectedWords),
            nameof(ShowEmptySelection), nameof(HasRule), nameof(HasRuleDetail),
            nameof(HasTimingRefusal), nameof(ShowStaleTiming), nameof(ScopeLabel),
            nameof(PercentileSummary), nameof(RuleSummary), nameof(IsLoadingTiming),
            nameof(ShowNoTimingRecorded), nameof(HasHeadline), nameof(HeadlineTotal),
            nameof(HeadlineTotalCaption), nameof(HeadlineMedian), nameof(HeadlineStopped),
            nameof(KindShares), nameof(RuleShares), nameof(RuleShareHeader), nameof(ShareDenominatorText),
            nameof(OtherTimeText), nameof(CostliestRuleWordRows), nameof(RuleWordsTitle),
        }) OnPropertyChanged(property);
        RaiseFocusState();
        RaiseStoredTimingState();
        RerunWordsCommand.NotifyCanExecuteChanged();
        HandOffWordsCommand.NotifyCanExecuteChanged();
        HandOffRuleCommand.NotifyCanExecuteChanged();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.ProjectPath)) OnPropertyChanged(nameof(ShowNoTimingRecorded));
        if (e.PropertyName != nameof(WorkspaceContext.NeedsAssessment)) return;
        OnPropertyChanged(nameof(ShowNoTimingRecorded));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
        OnPropertyChanged(nameof(ShowStoredTiming));
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
        OnPropertyChanged(nameof(FocusedTimingRefusal));
        OnPropertyChanged(nameof(HasFocusedTimingRefusal));
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
    }

    private void RaiseStoredTimingState()
    {
        OnPropertyChanged(nameof(StoredTiming));
        OnPropertyChanged(nameof(StoredTimingRefusal));
        OnPropertyChanged(nameof(HasStoredTiming));
        OnPropertyChanged(nameof(HasStoredTimingRefusal));
        OnPropertyChanged(nameof(ShowStoredTiming));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(StoredTimingSummary));
    }
}

/// <summary>
/// One part of some words' measured parse time: a kind of rule, one rule, or the other time the parser recorded
/// against no rule. Its share is always of the words' whole parse time, never of the time recorded against rules.
/// </summary>
/// <param name="Label">The kind's or rule's name as the window shows it.</param>
/// <param name="Kind">The stored kind, for a rule; empty for unattributed time.</param>
/// <param name="ElapsedMs">The time the parser recorded, in milliseconds, or null when not recorded.</param>
/// <param name="Share">The share of the words' whole parse time, or <see langword="null"/> when none was kept.</param>
/// <param name="Words">How many words the parser recorded this part's time in.</param>
/// <param name="Source">The command's row behind it, or <see langword="null"/> for the other time.</param>
public sealed record TimingShare(string Label, string Kind, double? ElapsedMs, double? Share, int Words,
    TimingAggregateRow? Source)
{
    /// <summary>Minimum time shown as a separate entry in the compact timing views.</summary>
    public const double SmallestShownMs = 0.05;

    /// <summary>Whether this row carries the response's unattributed time.</summary>
    public bool IsNotAttributed => Source is null;

    /// <summary>The window's name for this part's stored kind.</summary>
    public string KindLabel => Kind.Length == 0 ? string.Empty : KindName(Kind);

    public string TimeText => ElapsedMs is { } time ? SpeedText.PerWord(time) : "Not recorded";

    public string ShareText => Share is { } share ? share.ToString("P0", CultureInfo.CurrentCulture) :
        IsNotAttributed ? "Not recorded" : "—";

    public string WordsText => Words.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The window's name for a stored parser kind.</summary>
    public static string KindName(string kind) => kind switch
    {
        "morph_rule" => "Morphological rules",
        "phon_rule" => "Phonological rules",
        "root_index" => "Root lookup",
        "lex_entry" => "Lexical entries",
        _ => kind.Replace('_', ' '),
    };
}

/// <summary>One row of the Timing page's by-rule table, and whether it is the chosen rule.</summary>
/// <param name="Share">The rule's time and its share of the words' whole parse time.</param>
/// <param name="IsChosen">Whether the side card describes this rule.</param>
public sealed record TimingRuleRow(TimingShare Share, bool IsChosen)
{
    /// <summary>The rule's timing, as the command reported it.</summary>
    public TimingAggregateRow Row => Share.Source!;
}
