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
public sealed record OpenTimingRequest(IReadOnlyList<string> Words, TraceTimingKey? Rule, string? Label = null) : PageRequest(WorkspacePage.Timing);

/// <summary>The Timing page's model: where the latest Assessment's parse time went.</summary>
public sealed partial class TimingPageModel : PageModel
{
    /// <summary>What the page says, where the numbers would be, when no parse time was measured.</summary>
    public static string NoTimingRecordedText => MotifAppStrings.TimingNoTimesRecorded;

    private int _loadGeneration;
    private bool _isLoadingTiming;
    private IReadOnlyList<string>? _explicitWords;
    private IReadOnlyList<GrammarWarning> _grammarWarnings = [];
    private TimingWordSet _wordSet = new TimingWordSet.All();
    private CancellationTokenSource? _rerunCancellation;

    public TimingPageModel(WorkspaceContext context) : base(context)
    {
        Statistics = new StatisticsViewModel(context.Commands) { Memory = context.ViewMemory };
        LoadFocusedTimingCommand = new AsyncRelayCommand(LoadFocusedTimingAsync,
            () => Focus is not null && Context.ProjectPath is not null);
        SelectWordSetCommand = new AsyncRelayCommand<string>(SelectWordSetAsync);
        UsePickedWordsCommand = new AsyncRelayCommand(UsePickedWordsAsync, CanUsePickedWords);
        UseTextsListCommand = new AsyncRelayCommand(UseTextsListAsync, CanUseTextsList);
        UseCheckedWordsCommand = new AsyncRelayCommand(UseCheckedWordsAsync, CanUseCheckedWords);
        UseMatrixCellCommand = new AsyncRelayCommand(UseMatrixCellAsync, CanUseMatrixCell);
        ChooseRuleCommand = new AsyncRelayCommand<TimingAggregateRow>(ChooseRuleAsync);
        OpenGrammarWarningCommand = new RelayCommand<TimingRuleRow>(row =>
        {
            if (row?.WarningCode is { } code) context.Open(new OpenGrammarWarningRequest(code));
        });
        ToggleKindCallsCommand = new RelayCommand(() => ShowKindCalls = !ShowKindCalls);
        HandOffWordsCommand = new RelayCommand(() => context.HandOff(SelectedWords),
            () => SelectedWords.Count > 0);
        HandOffRuleCommand = new RelayCommand(() => context.HandOff(
            RuleDetail?.CostliestWords.Select(word => word.Word).ToArray() ?? []),
            () => RuleDetail?.CostliestWords.Count > 0);
        RerunWordsCommand = new AsyncRelayCommand(RerunWordsAsync,
            () => RerunnableWords.Count > 0 && !IsRerunning && !context.Assess.IsActive);
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
            ? focus.Words.Count == 0
                ? MotifAppStrings.TimingFocusAllWords
                : PluralText.Format(MotifAppStrings.TimingFocusSelectedWordsPattern, focus.Words.Count)
            : focus.Words.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingFocusAllWordsUnderRuleFormat,
                    focus.Label ?? SelectedRuleName ?? MotifAppStrings.TimingRequestedRuleLower)
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingFocusSelectedWordsUnderRuleFormat,
                    focus.Words.Count, focus.Label ?? SelectedRuleName ?? MotifAppStrings.TimingRequestedRuleLower);

    public bool HasFocusedTiming => FocusedTiming is not null;

    public bool HasFocusedTimingRefusal => FocusedTimingRefusal is not null;

    /// <summary>Whether stored timing was read for the current Assessment.</summary>
    public bool HasStoredTiming => StoredTiming is not null;

    /// <summary>Whether the stored timing read returned a refusal.</summary>
    public bool HasStoredTimingRefusal => StoredTimingRefusal is not null;

    /// <summary>The stored timing response's word count and percentiles in one line.</summary>
    public string StoredTimingSummary => StoredTiming is not { } timing ? string.Empty :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingStoredSummaryFormat,
            timing.WordCount.ToString("N0", CultureInfo.CurrentCulture), FormatWork(timing.Work.MedianWorkSteps),
            FormatWork(timing.Work.Percentile95WorkSteps));

    /// <summary>The selected words' stored totals grouped by kind.</summary>
    public TimingResponse? KindTiming { get; private set; }

    /// <summary>The same selected words' totals grouped by rule.</summary>
    public TimingResponse? RuleTiming { get; private set; }

    /// <summary>The selected rule's costliest words.</summary>
    public TimingResponse? RuleDetail { get; private set; }

    /// <summary>The exact words returned by the timing command.</summary>
    public IReadOnlyList<string> SelectedWords => KindTiming?.Words.Select(word => word.Word).ToArray() ?? [];

    private IReadOnlyList<string> RerunnableWords => KindTiming?.Words
        .Where(word => word.Completion != TimingCompletion.Refused).Select(word => word.Word).ToArray() ?? [];

    public bool HasTiming => KindTiming is not null;

    /// <summary>Whether the stored parse times for the chosen words are being read.</summary>
    public bool IsLoadingTiming => _isLoadingTiming;

    /// <summary>Whether an open project has no measured parse time to show, and no read is pending or refused.</summary>
    public bool ShowNoTimingRecorded => Context.ProjectPath is not null && !Context.NeedsAssessment &&
        KindTiming is null && !_isLoadingTiming && TimingRefusal is null;

    /// <summary>What an empty Timing result says about the words the current choice included.</summary>
    public string EmptySelectionText => Focus is not null || _explicitWords is not null
        ? MotifAppStrings.TimingNoTimesRecordedSelected
        : _wordSet switch
        {
            TimingWordSet.StepLimited => MotifAppStrings.TimingNoWordsStopped,
            TimingWordSet.Slowest => MotifAppStrings.TimingNoWordsInGroup,
            _ => MotifAppStrings.TimingNoWordsInSelection,
        };

    /// <summary>Whether the empty result came from a choice other than All.</summary>
    public bool CanShowAllWords => !IsAllSelected;

    /// <summary>Whether the chosen words have measured parse times to lead the page with.</summary>
    public bool HasHeadline => MeasuredWords.Count > 0;

    /// <summary>The chosen words' summed parser work, as PanGloss counted it.</summary>
    public string HeadlineTotal => HasHeadline && KindTiming is { } timing
        ? TimingShare.FormatWorkSteps(timing.Work.TotalWorkSteps) : string.Empty;

    /// <summary>Which words the total covers: all of them, or the group chosen above, and how many were timed.</summary>
    public string HeadlineTotalCaption => !HasHeadline ? string.Empty
        : MeasuredWords.Count < ChosenWordCount
            ? string.Format(CultureInfo.CurrentCulture, IsAllSelected
                ? MotifAppStrings.TimingCaptionSomeOfAllFormat : MotifAppStrings.TimingCaptionSomeOfTheseFormat,
                MeasuredWords.Count.ToString("N0", CultureInfo.CurrentCulture),
                ChosenWordCount.ToString("N0", CultureInfo.CurrentCulture))
        : MeasuredWords.Count == 1 ? MotifAppStrings.TimingCaptionOneWord
        : string.Format(CultureInfo.CurrentCulture, IsAllSelected
            ? MotifAppStrings.TimingCaptionAllWordsFormat : MotifAppStrings.TimingCaptionTheseWordsFormat,
            MeasuredWords.Count.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>The chosen words' median parser work.</summary>
    public string HeadlineMedian => HasHeadline && KindTiming?.Work.MedianWorkSteps is { } median
        ? TimingShare.FormatWorkSteps(median) : string.Empty;

    /// <summary>How many of the chosen words stopped at a parser search limit.</summary>
    public string HeadlineStopped => KindTiming?.Words
        .Count(word => word.Completion == TimingCompletion.StepLimit).ToString("N0", System.Globalization.CultureInfo.CurrentCulture)
        ?? string.Empty;

    /// <summary>The caption under the stopped-word count.</summary>
    public string HeadlineStoppedCaption => MotifAppStrings.TimingStoppedCaption;

    /// <summary>The All filter's label, including the timing response's word count.</summary>
    public string AllWordsButtonText => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.TimingAllWordsButtonFormat, (KindTiming?.WordCount ?? 0).ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>Whether the selected group has a word stopped at either recorded limit.</summary>
    public bool HasStoppedWords => HasStepLimitedWords || HasTimeLimitedWords;

    /// <summary>What the selected words' recorded stop reason means for the controls on this page.</summary>
    public string StoppedWordsAdviceText => (HasStepLimitedWords, HasTimeLimitedWords) switch
    {
        (true, true) => MotifAppStrings.TimingAdviceBothLimits,
        (true, false) => MotifAppStrings.TimingAdviceSearchLimit,
        (false, true) => MotifAppStrings.TimingAdviceTimeLimit,
        _ => string.Empty,
    };

    private bool HasStepLimitedWords => KindTiming?.Words.Any(word =>
        word.Completion == TimingCompletion.StepLimit) == true;

    private bool HasTimeLimitedWords => KindTiming?.Words.Any(word =>
        word.Completion == "Time limit") == true;

    /// <summary>The stopped-words filter's label, including words stopped at a parser search limit.</summary>
    public string StoppedWordsButtonText => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.TimingStoppedButtonFormat, KindTiming?.Words.Count(word =>
        word.Completion == TimingCompletion.StepLimit).ToString("N0", CultureInfo.CurrentCulture) ?? "0");

    /// <summary>Whether these totals include separately recorded re-run measurements.</summary>
    public string RerunMeasurementsText => CurrentTimingOverrides is { Count: > 0 } overrides
        ? PluralText.Format(MotifAppStrings.TimingRerunMeasurementsPattern, overrides.Count)
        : string.Empty;

    public bool HasRerunMeasurements => RerunMeasurementsText.Length > 0;

    private IReadOnlyList<TimingWordRow> MeasuredWords =>
        KindTiming?.Words.Where(word => word.WorkSteps is not null).ToArray() ?? [];

    private int ChosenWordCount => KindTiming?.Words.Count ?? 0;

    private string MeasuredWordsPhrase => MeasuredWords.Count < ChosenWordCount
        ? string.Format(CultureInfo.CurrentCulture, IsAllSelected
            ? MotifAppStrings.TimingPhraseSomeOfAllFormat : MotifAppStrings.TimingPhraseSomeOfTheseFormat,
            MeasuredWords.Count.ToString("N0", CultureInfo.CurrentCulture),
            ChosenWordCount.ToString("N0", CultureInfo.CurrentCulture))
        : MeasuredWords.Count == 1 ? MotifAppStrings.TimingPhraseThisWord
        : string.Format(CultureInfo.CurrentCulture, IsAllSelected
            ? MotifAppStrings.TimingPhraseAllWordsFormat : MotifAppStrings.TimingPhraseTheseWordsFormat,
            MeasuredWords.Count.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>The chosen words' work by kind, with the recorded unattributed work; empty when work by object was not kept.</summary>
    public IReadOnlyList<TimingShare> KindShares => !HasHeadline || WorkByObject(KindTiming) is not { } work ? []
        : TimingShare.ByKind(work, KindTiming!.Work.TotalWorkSteps, KindTiming.Aggregates,
            MotifAppStrings.TimingNotAttributedLabel, 0, alwaysShowUnattributed: true);

    /// <summary>The per-kind call counts, kept behind the page's call-count disclosure.</summary>
    public IReadOnlyList<TimingShare> KindCallShares => CallSharesOf(KindTiming?.Aggregates);

    /// <summary>The chosen words' work by rule, each a share of the same whole work as the kinds.</summary>
    public IReadOnlyList<TimingShare> RuleShares
    {
        get
        {
            if (RuleTiming is not { } timing) return [];
            if (WorkByObject(timing) is not { } work) return CallSharesOf(timing.Aggregates);
            var total = timing.Work.TotalWorkSteps;
            return [.. work.Where(row => !row.IsUnattributed).Select(row =>
            {
                var source = timing.Aggregates.FirstOrDefault(time =>
                    ObjectIdentity.Same(time.TimingKey.Identity, row.TimingKey.Identity)) ??
                    new TimingAggregateRow(row.Key, row.Name, 0, null, 0)
                        { Kind = row.Kind, IdentityQuality = row.IdentityQuality, Scope = row.Scope };
                return new TimingShare(row.Name, row.Kind, row.WorkSteps, TimingShare.ShareOf(row.WorkSteps, total),
                    source.WordsTouched, source);
            })];
        }
    }

    /// <summary>The rule table's share column, named by what it divides by.</summary>
    public string RuleShareHeader => HasHeadline
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingShareHeaderFormat, HeadlineTotal)
        : MotifAppStrings.TimingShareHeader;

    /// <summary>What every share on the page is a share of.</summary>
    public string ShareDenominatorText
    {
        get
        {
            if (KindTiming is null) return string.Empty;
            if (!HasHeadline) return MotifAppStrings.TimingNoSharesShown;
            if (WorkByObject(KindTiming) is null) return MotifAppStrings.TimingWorkByObjectUnavailable;
            var missing = ChosenWordCount - MeasuredWords.Count;
            return missing switch
            {
                <= 0 => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingShareOfFormat,
                    HeadlineTotal, MeasuredWordsPhrase),
                1 => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingShareOfOneMissingFormat,
                    HeadlineTotal, MeasuredWordsPhrase),
                _ => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingShareOfManyMissingFormat,
                    HeadlineTotal, MeasuredWordsPhrase, missing.ToString("N0", CultureInfo.CurrentCulture)),
            };
        }
    }

    /// <summary>What the response recorded as work the parser did not attribute to an object.</summary>
    public string NotAttributedText
    {
        get
        {
            if (!HasHeadline || KindTiming is not { } timing) return string.Empty;
            if (WorkByObject(timing)?.FirstOrDefault(row => row.IsUnattributed) is not { } other)
                return MotifAppStrings.TimingNotAttributedNotRecorded;
            return TimingShare.ShareOf(other.WorkSteps, timing.Work.TotalWorkSteps) is { } share
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingNotAttributedWithShareFormat,
                    TimingShare.FormatWorkSteps(other.WorkSteps), TimingShare.FormatPercent(share))
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingNotAttributedFormat,
                    TimingShare.FormatWorkSteps(other.WorkSteps));
        }
    }

    // The per-object work, only when the command could make it add up to the words' totals.
    private static IReadOnlyList<TimingWorkAggregate>? WorkByObject(TimingResponse? timing) =>
        timing is { WorkStepAggregates.Count: > 0 } ? timing.WorkStepAggregates : null;

    private static IReadOnlyList<TimingShare> CallSharesOf(IReadOnlyList<TimingAggregateRow>? rows) => rows is null ? [] :
        [.. rows.Select(row => new TimingShare(TimingShare.KindName(row.Name), row.Kind, null, null, row.WordsTouched, row))];

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
    public string ScopeLabel
    {
        get
        {
            if (KindTiming is null) return MotifAppStrings.TimingNoStoredTimingForWords;
            var label = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingScopeFormat,
                KindTiming.WordCount.ToString("N0", CultureInfo.CurrentCulture));
            if (KindTiming.ObjectTimingDetail is not { IsComplete: false } detail) return label;
            var notes = detail.DetailWordCounts.Select(count => string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.TimingDetailLimitedFormat, count.ToString("N0", CultureInfo.CurrentCulture)));
            return label + Environment.NewLine + string.Join(" ", notes) +
                (detail.HasRetriedWords ? " " + MotifAppStrings.TimingDetailRetried : string.Empty) +
                (detail.StatisticsUnavailable ? " " + MotifAppStrings.TimingNoStoredTimingForWords : string.Empty) +
                (!KindTiming.ObjectTimingAggregatesAvailable ? " " + MotifAppStrings.TimingObjectTotalsUnavailable : string.Empty);
        }
    }
    public string PercentileSummary => KindTiming is null ? string.Empty :
        KindTiming.WordCount == 0 ? MotifAppStrings.TimingNoWordsInSelection :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingPercentileSummaryFormat,
            FormatWork(KindTiming.Work.MedianWorkSteps), FormatWork(KindTiming.Work.Percentile95WorkSteps));

    private static string FormatWork(double? workSteps) => workSteps is { } value
        ? TimingShare.FormatWorkSteps(value) : MotifAppStrings.TimingNotRecordedLower;
    /// <summary>The complete address selected here or requested by another page.</summary>
    public TraceTimingKey? SelectedRule { get; private set; }
    public TimingAggregateRow? SelectedRuleRow => RuleTiming?.Aggregates.FirstOrDefault(row => ObjectIdentity.Same(row.TimingKey.Identity, SelectedRule?.Identity));

    /// <summary>The chosen rule's label, for the side card's title.</summary>
    public string? SelectedRuleName => SelectedRuleRow?.Name ??
        (Focus?.Rule is not null ? Focus.Label ?? MotifAppStrings.TimingRequestedRule : null);

    /// <summary>The by-rule table's rows, each saying whether it is the rule the side card describes.</summary>
    public IReadOnlyList<TimingRuleRow> RuleRows =>
        [.. RuleShares.OrderByDescending(share => share.Share ?? -1).Select(share =>
        {
            var warningCode = MatchingWarningCode(share.Source, _grammarWarnings);
            return new TimingRuleRow(share,
                ObjectIdentity.Same(share.Source?.TimingKey.Identity, SelectedRule?.Identity), warningCode);
        })];
    public IReadOnlyList<WordRuleTiming> CostliestRuleWords => RuleDetail?.CostliestWords.Take(5).ToArray() ?? [];

    /// <summary>
    /// The selected rule's costliest words as word rows, each with the rule's work in it beside that word's whole
    /// work.
    /// </summary>
    public IReadOnlyList<ListedWordViewModel> CostliestRuleWordRows => [.. CostliestRuleWords.Select(word =>
        Listed(word.Word, word.WorkSteps is not { } part ? MotifAppStrings.WorkStepsNotRecorded
            : word.WordWorkSteps is { } whole
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingRuleTimeOfFormat,
                    TimingShare.FormatWorkSteps(part), TimingShare.FormatWorkSteps(whole))
                : TimingShare.FormatWorkSteps(part)))];

    /// <summary>The heading over the chosen rule's costliest words, naming the rule.</summary>
    public string RuleWordsTitle => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.TimingRuleWordsTitleFormat, SelectedRuleName);

    private ListedWordViewModel Listed(string word, string timeText)
    {
        var listed = Context.Assess.Words.Listed(word);
        listed.TimeText = timeText;
        listed.Note = KindTiming?.Words.FirstOrDefault(row => row.Word == word)?.Completion switch
        {
            TimingCompletion.StepLimit => MotifAppStrings.TimingStoppedAtSearchLimit,
            "Time limit" => MotifAppStrings.TimingStoppedAtTimeLimit,
            _ => null,
        };
        return listed;
    }

    /// <summary>The selected rule's work, its share of the chosen words' whole work, and its words.</summary>
    public string RuleSummary
    {
        get
        {
            if (RuleShares.FirstOrDefault(share => ObjectIdentity.Same(share.Source?.TimingKey.Identity, SelectedRule?.Identity)) is not { } rule)
                return Focus?.Rule is null ? string.Empty : MotifAppStrings.TimingNoStoredTimingForRule;
            var recorded = PluralText.Format(MotifAppStrings.TimingRecordedInWordsPattern, rule.Words);
            return rule.Share is null
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingRuleSummaryFormat, rule.WorkText, recorded)
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingRuleSummaryShareFormat,
                    rule.WorkText, rule.ShareText, MeasuredWordsPhrase, HeadlineTotal, recorded);
        }
    }
    public IReadOnlyList<CompareCellViewModel> MatrixCells =>
        Context.Assess.Compare.Cells.Where(cell => cell.Count > 0).ToArray();
    public IReadOnlyList<ComparePresetViewModel> TextsLists =>
        Context.Assess.Compare.Presets.Where(preset => preset.Count > 0).ToArray();

    public string UseMatrixCellDisabledReason => Context.ProjectPath is null
        ? MotifAppStrings.TimingOpenProjectFirst
        : SelectedMatrixCell is null ? MotifAppStrings.TimingChooseMatrixCellFirst : string.Empty;

    public bool UseMatrixCellUnavailable => UseMatrixCellDisabledReason.Length > 0;

    public string UseTextsListDisabledReason => Context.ProjectPath is null
        ? MotifAppStrings.TimingOpenProjectFirst
        : SelectedTextsList is null ? MotifAppStrings.TimingChooseWordListFirst
        : Context.Assess.Compare.WordsInPreset(SelectedTextsList).Count == 0
            ? MotifAppStrings.TimingNoWordsInList : string.Empty;

    public bool UseTextsListUnavailable => UseTextsListDisabledReason.Length > 0;

    public string UsePickedWordsDisabledReason => Context.ProjectPath is null
        ? MotifAppStrings.TimingOpenProjectFirst
        : PickedWordValues.Count == 0 ? MotifAppStrings.TimingEnterWords : string.Empty;

    public bool UsePickedWordsUnavailable => UsePickedWordsDisabledReason.Length > 0;

    public string UseCheckedWordsDisabledReason => Context.ProjectPath is null
        ? MotifAppStrings.TimingOpenProjectFirst
        : Context.Assess.Compare.CheckedWords.Count == 0 ? MotifAppStrings.TimingTickWordsInTexts : string.Empty;

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
    private decimal? _rerunSteps = StepCap.DefaultSteps;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindCallsButtonText))]
    [NotifyPropertyChangedFor(nameof(KindCallsButtonName))]
    private bool _showKindCalls;

    public string KindCallsButtonText => ShowKindCalls ? MotifAppStrings.TimingHideCalls : MotifAppStrings.TimingShowCalls;

    public string KindCallsButtonName => ShowKindCalls
        ? MotifAppStrings.TimingHideCallsAccessibleName : MotifAppStrings.TimingShowCallsAccessibleName;

    public bool HasKindCalls => KindCallShares.Count > 0;

    public string RerunProgressText => RerunWord is null
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingRerunProgressFormat, RerunCompleted, RerunTotal)
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingRerunProgressParsingFormat,
            RerunCompleted, RerunTotal, RerunWord);

    /// <summary>Refreshes the stored timing for the words and rule last opened.</summary>
    public IAsyncRelayCommand LoadFocusedTimingCommand { get; }
    public IAsyncRelayCommand<string> SelectWordSetCommand { get; }
    public IAsyncRelayCommand UsePickedWordsCommand { get; }
    public IAsyncRelayCommand UseTextsListCommand { get; }
    public IAsyncRelayCommand UseCheckedWordsCommand { get; }
    public IAsyncRelayCommand UseMatrixCellCommand { get; }
    public IAsyncRelayCommand<TimingAggregateRow> ChooseRuleCommand { get; }
    public IRelayCommand<TimingRuleRow> OpenGrammarWarningCommand { get; }
    public IRelayCommand ToggleKindCallsCommand { get; }
    public IRelayCommand HandOffWordsCommand { get; }
    public IRelayCommand HandOffRuleCommand { get; }
    public IAsyncRelayCommand RerunWordsCommand { get; }
    public IRelayCommand CancelRerunCommand { get; }

    protected override void OnProjectCleared()
    {
        CancelRerun();
        _loadGeneration++;
        _grammarWarnings = [];
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
        Statistics.RestoreView();
        _grammarWarnings = Context.GrammarSummary?.Findings ?? [];
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
        NotifySourceAvailability();
        return Task.CompletedTask;
    }

    protected override async Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        var evidence = lens.Today;
        if (evidence.Assessment is { } shown)
        {
            Statistics.Reset();
            Statistics.SummaryMarkdown = shown.Assessment.SummaryMarkdown;
            Statistics.AssessmentId = evidence.ObjectTimingAssessmentId;
            Statistics.HasTimingOverrides = evidence.TimingOverrideAssessmentIds.Count > 0;
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
            OnPropertyChanged(nameof(RerunMeasurementsText));
            OnPropertyChanged(nameof(HasRerunMeasurements));
            return;
        }

        RaiseStoredTimingState();
        // A run answers whatever the page is focused on; a stored read never replaces a focused view.
        if (evidence.Assessment is { IsStored: false } || Focus is null)
            await LoadScopeAsync(projectPath, assessmentId, cancellationToken).ConfigureAwait(true);
    }

    internal static string? MatchingWarningCode(TimingAggregateRow? row, IReadOnlyList<GrammarWarning> findings)
    {
        var identity = row?.TimingKey.Identity;
        if (identity is null) return null;
        return findings.FirstOrDefault(finding => WarningIdentities(finding)
            .Any(candidate => ObjectIdentity.Same(identity, candidate)))?.Code;
    }

    private static IEnumerable<ObjectIdentity> WarningIdentities(GrammarWarning finding)
    {
        foreach (var key in finding.Subject.SelectMany(part => part.Reach?.TimingKeys ?? []))
            if (key.Identity is { } identity) yield return identity;

        foreach (var subject in finding.Subject.Where(part => part.Role == GrammarWarningPartRole.Object))
        {
            var kind = subject.FieldWorksKind switch
            {
                "LexEntry" => "lex_entry",
                "PhRegularRule" or "PhMetathesisRule" => "phon_rule",
                "MoCompoundRule" or "MoEndoCompound" or "MoExoCompound" => "morph_rule",
                _ => null,
            };
            var key = subject.FieldWorksGuid ?? subject.SubjectGuid ?? subject.ObjectId;
            if (kind is not null && Guid.TryParse(key, out var guid) &&
                ObjectIdentity.Create(kind, guid.ToString("D")) is { } identity)
                yield return identity;
        }
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
        Statistics.WordScope = null;
        TimingRefusal = null;
        ShowKindCalls = false;
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
        Statistics.WordScope = SelectedWords;
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
            ? RuleTiming?.Aggregates.FirstOrDefault()?.TimingKey
            : RuleTiming?.Aggregates.FirstOrDefault(row => ObjectIdentity.Same(row.TimingKey.Identity, requestedRule.Identity))?.TimingKey
                ?? requestedRule;
        RaiseTimingState();
        if (SelectedRuleRow is not null) await LoadRuleDetailAsync(projectPath, assessmentId, generation);
    }

    private async Task ChooseRuleAsync(TimingAggregateRow? row)
    {
        using var usageAction = Context.Commands.BeginUsageAction("timing",
            UsageArgumentShape.Text("rule"));
        if (row is null || Context.ProjectPath is not { } projectPath) return;
        SelectedRule = row.TimingKey;
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
        var words = RerunnableWords;
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
            RerunMessage = MotifAppStrings.TimingRerunInvalidLimits;
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
                RerunMessage = RerunCompleted == RerunTotal
                    ? MotifAppStrings.TimingRerunComplete : MotifAppStrings.TimingRerunCancelled;
        }
        catch (OperationCanceledException)
        {
            RerunMessage = MotifAppStrings.TimingRerunCancelled;
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
            nameof(AllWordsButtonText), nameof(StoppedWordsButtonText),
            nameof(SelectedWords),
            nameof(HasTiming), nameof(HasSelectedWords),
            nameof(ShowEmptySelection), nameof(HasRule), nameof(HasRuleDetail),
            nameof(HasTimingRefusal), nameof(ShowStaleTiming), nameof(HasStoppedWords), nameof(StoppedWordsAdviceText), nameof(ScopeLabel),
            nameof(EmptySelectionText), nameof(CanShowAllWords),
            nameof(PercentileSummary), nameof(RuleSummary), nameof(IsLoadingTiming),
            nameof(ShowNoTimingRecorded), nameof(HasHeadline), nameof(HeadlineTotal),
            nameof(HeadlineTotalCaption), nameof(HeadlineMedian), nameof(HeadlineStopped),
            nameof(KindShares), nameof(RuleShares), nameof(RuleShareHeader), nameof(ShareDenominatorText),
            nameof(NotAttributedText), nameof(KindCallShares), nameof(HasKindCalls),
            nameof(RerunMeasurementsText), nameof(HasRerunMeasurements),
            nameof(CostliestRuleWordRows), nameof(RuleWordsTitle),
        }) OnPropertyChanged(property);
        RaiseFocusState();
        RaiseStoredTimingState();
        RerunWordsCommand.NotifyCanExecuteChanged();
        HandOffWordsCommand.NotifyCanExecuteChanged();
        HandOffRuleCommand.NotifyCanExecuteChanged();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.GrammarSummary))
        {
            _grammarWarnings = Context.GrammarSummary?.Findings ?? [];
            OnPropertyChanged(nameof(RuleRows));
        }
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
