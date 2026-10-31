using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Controls;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Overview page's model: it displays the command's project summary and stored run numbers, routes each tile
/// to its detail page, and keeps the project's history available below the summary.
/// </summary>
public sealed partial class OverviewPageModel : PageModel
{
    private int _readGeneration;

    public OverviewPageModel(WorkspaceContext context) : base(context)
    {
        History = new ProjectHistoryViewModel(context.Commands);
        context.Assess.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AssessViewModel.ShownRefusal)) return;
            OnPropertyChanged(nameof(LastParseRefusal));
            OnPropertyChanged(nameof(HasLastParseRefusal));
        };
        OpenTextCoverageCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.Matrix, []));
        OpenAccuracyCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.Matrix,
            Enum.GetValues<CompareColumnKind>()
                .Select(column => new TextsListCell(WordProjectStatus.Approved, column)).ToArray()));
        OpenWordCommand = new RelayCommand<string?>(word =>
        {
            if (!string.IsNullOrWhiteSpace(word)) Context.OpenWord(word);
        });
        OpenLostWordsCommand = new RelayCommand(
            () => OpenCell(WordProjectStatus.Approved, CompareColumnKind.NoParse),
            () => Overview is { AssessmentId: not null, LookFirst.ApprovedLostWords.Count: > 0 });
        OpenTimingCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Timing));
        OpenWarningsCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Warnings));
        OpenAiHandoffCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.AiHandoff));
        RetryOverviewCommand = new AsyncRelayCommand(() =>
            Context.ProjectPath is { } path ? RefreshOverviewAsync(path, CancellationToken.None) : Task.CompletedTask);
        context.PropertyChanged += OnContextPropertyChanged;
        context.Evidence.PropertyChanged += OnEvidencePropertyChanged;
        context.Assess.Words.PropertyChanged += OnWordsChanged;
        context.KnownProjects.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ChooseProjectRows));
            OnPropertyChanged(nameof(HasKnownProjects));
            OnPropertyChanged(nameof(ShowNoKnownProjects));
            OnPropertyChanged(nameof(NoKnownProjectsText));
        };
    }

    /// <summary>The project's Baselines and Assessments, newest first, which the page loads for itself.</summary>
    public ProjectHistoryViewModel History { get; }

    public WindowRefusal? LastParseRefusal => Context.Assess.ShownRefusal ??
        (Overview?.LastParserRefusal is { } refusal ? WindowRefusal.From(refusal) : null);

    public bool HasLastParseRefusal => LastParseRefusal is not null;

    /// <summary>The stored Overview read for the open project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssessment))]
    [NotifyCanExecuteChangedFor(nameof(OpenLostWordsCommand))]
    [NotifyPropertyChangedFor(nameof(ShowNoAssessment))]
    [NotifyPropertyChangedFor(nameof(ShowNumbers))]
    [NotifyPropertyChangedFor(nameof(ShowTiles))]
    [NotifyPropertyChangedFor(nameof(ShowWarningsTile))]
    [NotifyPropertyChangedFor(nameof(SelectionIsUnresolved))]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    [NotifyPropertyChangedFor(nameof(ProjectFileName))]
    [NotifyPropertyChangedFor(nameof(SelectionWordCountText))]
    [NotifyPropertyChangedFor(nameof(TextOccurrenceCountText))]
    [NotifyPropertyChangedFor(nameof(WordformCountText))]
    [NotifyPropertyChangedFor(nameof(RuleCountText))]
    [NotifyPropertyChangedFor(nameof(LexemeCountText))]
    [NotifyPropertyChangedFor(nameof(SpeedMain))]
    [NotifyPropertyChangedFor(nameof(SpeedMedian))]
    [NotifyPropertyChangedFor(nameof(SpeedElapsed))]
    [NotifyPropertyChangedFor(nameof(SpeedDetails))]
    [NotifyPropertyChangedFor(nameof(TimingKindShares))]
    [NotifyPropertyChangedFor(nameof(HasTimingKindShares))]
    [NotifyPropertyChangedFor(nameof(TimingAttributionNote))]
    [NotifyPropertyChangedFor(nameof(SlowestWordRows))]
    [NotifyPropertyChangedFor(nameof(SlowestTableRows))]
    [NotifyPropertyChangedFor(nameof(HasSlowestWordRows))]
    [NotifyPropertyChangedFor(nameof(SlowestWordSummary))]
    [NotifyPropertyChangedFor(nameof(HasSlowestWordSummary))]
    [NotifyPropertyChangedFor(nameof(LookFirstRows))]
    [NotifyPropertyChangedFor(nameof(HasLookFirstRows))]
    [NotifyPropertyChangedFor(nameof(TextCoverageMain))]
    [NotifyPropertyChangedFor(nameof(TextCoverageWords))]
    [NotifyPropertyChangedFor(nameof(TextCoverageSegments))]
    [NotifyPropertyChangedFor(nameof(AccuracyMain))]
    [NotifyPropertyChangedFor(nameof(AccuracyCaption))]
    [NotifyPropertyChangedFor(nameof(AccuracyBreakdown))]
    [NotifyPropertyChangedFor(nameof(AccuracySegments))]
    [NotifyPropertyChangedFor(nameof(HasWarningSummary))]
    [NotifyPropertyChangedFor(nameof(WarningsCount))]
    [NotifyPropertyChangedFor(nameof(HasWarningsWordSummary))]
    [NotifyPropertyChangedFor(nameof(WarningsYourWordsText))]
    [NotifyPropertyChangedFor(nameof(WarningsSpellingCandidatesText))]
    [NotifyPropertyChangedFor(nameof(WarningKindRows))]
    [NotifyPropertyChangedFor(nameof(LostWordsContextText))]
    [NotifyPropertyChangedFor(nameof(SharedLostMorphemeContextText))]
    private OverviewResponse? _overview;

    partial void OnOverviewChanged(OverviewResponse? value)
    {
        var resolver = Context.TextStyles;
        void Refresh()
        {
            resolver.ReplaceContextIfChanged(value?.WritingSystems ?? []);
        }

        // Overview queries can complete on a worker, but resolver refresh touches Avalonia controls.
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>Why the stored Overview query was refused, in the window's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverviewRefusal))]
    [NotifyPropertyChangedFor(nameof(OverviewRefusalLine))]
    private WindowRefusal? _overviewRefusal;

    /// <summary>Opens a word named in recorded Overview evidence in the Texts page.</summary>
    public IRelayCommand<string?> OpenWordCommand { get; }

    /// <summary>Opens the Matrix cell containing Approved words the grammar did not build.</summary>
    public IRelayCommand OpenLostWordsCommand { get; }

    /// <summary>The stored Lost-word evidence shown beside the summary tables.</summary>
    public string LostWordsContextText => !HasAssessment ? MotifAppStrings.OverviewLostWordsParseFirst
        : Overview!.LookFirst.ApprovedLostWords.Count == 0 ? MotifAppStrings.OverviewLostWordsNone
        : MotifAppStrings.OverviewLostWordsSome;

    /// <summary>Whether shared morpheme evidence was recorded, without treating unavailable evidence as none.</summary>
    public string SharedLostMorphemeContextText => Overview?.LookFirst.SharedLostMorphemesAvailable == true
        ? Overview.LookFirst.SharedLostMorphemes.Count == 0 ? MotifAppStrings.OverviewSharedMorphemesNone : string.Empty
        : MotifAppStrings.OverviewSharedMorphemesUnavailable;

    /// <summary>Whether the response has an Assessment for its default Selection.</summary>
    public bool HasAssessment => Overview?.AssessmentId is not null;

    /// <summary>Whether the Overview should explain that this project has no Assessment.</summary>
    public bool ShowNoAssessment => Context.NeedsAssessment;

    /// <summary>Whether the project's counts were read, so they are real numbers rather than placeholders.</summary>
    public bool ShowNumbers => Overview is not null;

    /// <summary>Whether the parse tiles have a read Overview and a parse to describe.</summary>
    public bool ShowTiles => Overview is not null && !Context.NeedsAssessment;

    /// <summary>Whether the grammar warnings tile has a read Overview; it needs no parse.</summary>
    public bool ShowWarningsTile => Overview is not null;

    /// <summary>Whether the Overview is serving as the project chooser.</summary>
    public bool ShowChooseProject => !Context.HasProject;

    /// <summary>The Known projects offered on the initial Choose a project page.</summary>
    public IReadOnlyList<RecentProjectViewModel> ChooseProjectRows =>
        RecentProjectViewModel.MostRecentlySeenCopies(Context.KnownProjects);

    /// <summary>Whether there are Known projects to open from the initial page.</summary>
    public bool HasKnownProjects => Context.KnownProjects.Count > 0;

    /// <summary>Whether the initial page needs to explain the empty Known projects list.</summary>
    public bool ShowNoKnownProjects => !HasKnownProjects;

    /// <summary>What the chooser says when this computer has no Known projects.</summary>
    public string NoKnownProjectsText => HasKnownProjects ? string.Empty : MotifAppStrings.OverviewNoKnownProjects;

    /// <summary>Whether the Overview read was refused.</summary>
    public bool HasOverviewRefusal => OverviewRefusal is not null;

    /// <summary>
    /// What the page says when the Overview read is refused: the refusal's own sentence when the window has one
    /// for its code, otherwise that the numbers could not be read.
    /// </summary>
    public string OverviewRefusalLine => OverviewRefusal?.Sentence ?? string.Empty;

    /// <summary>Reads the stored Overview again after a refusal.</summary>
    public IAsyncRelayCommand RetryOverviewCommand { get; }

    /// <summary>Whether the numbers behind this Overview describe an older state of the project.</summary>
    public bool OverviewIsStale => Context.Evidence.IsStale;

    /// <summary>Whether this response could not resolve its default Selection against the current Baseline.</summary>
    public bool SelectionIsUnresolved => Overview is { SelectionResolved: false } &&
        Context.Baseline?.HasBaseline == true && Context.Setup?.CanRunDefaultSelection == true;

    /// <summary>The project name from the Overview response, else the project file's name without its extension.</summary>
    public string ProjectTitle => Overview?.ProjectName ??
        (Context.ProjectPath is { } path ? Path.GetFileNameWithoutExtension(path) : Context.ProjectName);

    /// <summary>The FieldWorks project file the page describes.</summary>
    public string ProjectFileName => Overview?.ProjectFileName is { Length: > 0 } name ? name
        : Path.GetFileName(Context.ProjectPath ?? string.Empty);

    /// <summary>The number of words in the response's default Selection.</summary>
    public string SelectionWordCountText => Overview is { SelectionResolved: false }
        ? Context.Baseline?.HasBaseline == true && Context.Setup?.CanRunDefaultSelection == true
            ? MotifAppStrings.OverviewSelectionNotResolved : MotifAppStrings.OverviewSelectionNotSetUp :
        Overview?.SelectionWordCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The number of places in the response's selected Texts.</summary>
    public string TextOccurrenceCountText => Overview is { SelectionResolved: false }
        ? Context.Baseline?.HasBaseline == true && Context.Setup?.CanRunDefaultSelection == true
            ? MotifAppStrings.OverviewSelectionNotResolved : MotifAppStrings.OverviewSelectionNotSetUp :
        Overview?.TextOccurrenceCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's wordform count from the response.</summary>
    public string WordformCountText =>
        Overview is { } response ? InventoryCountText(response.WordformCount) : string.Empty;

    /// <summary>The project's rule count from the response.</summary>
    public string RuleCountText =>
        Overview is { } response ? InventoryCountText(response.RuleCount) : string.Empty;

    /// <summary>The project's lexeme count from the response.</summary>
    public string LexemeCountText =>
        Overview is { } response ? InventoryCountText(response.LexemeCount) : string.Empty;

    private static string InventoryCountText(int? count) =>
        count?.ToString("N0", CultureInfo.CurrentCulture) ?? MotifAppStrings.OverviewNotMeasured;

    /// <summary>
    /// The speed headline: how many words PanGloss counted work for and their total work steps, each word's count
    /// added up. There is no comparison with another parser, because Motif does not measure one.
    /// </summary>
    public string SpeedMain => Overview is { Timing.Work: { MeasuredWordCount: > 0 } work }
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSpeedMainFormat,
            PluralText.Format(MotifAppStrings.OverviewSpeedWordsPattern, work.MeasuredWordCount),
            TimingShare.FormatWorkSteps(work.TotalWorkSteps))
        : MotifAppStrings.OverviewSpeedNoTimes;

    /// <summary>How long the whole run took on this computer, which is a time and not a measure of work.</summary>
    public string SpeedElapsed => Overview is { Timing: { MeasuredWordCount: > 0 } or { Work.MeasuredWordCount: > 0 },
            AssessmentElapsedSeconds: { } seconds }
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSpeedElapsedFormat,
            TimingShare.FormatDuration(seconds * 1000))
        : string.Empty;

    /// <summary>The stored median and 95th percentile work steps per word.</summary>
    public string SpeedMedian => Overview?.Timing.Work is { MedianWorkSteps: { } median } work
        ? work.Percentile95WorkSteps is { } p95
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSpeedMedianWithPercentileFormat,
                TimingShare.FormatWorkSteps(median), TimingShare.FormatWorkSteps(p95))
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSpeedMedianFormat, TimingShare.FormatWorkSteps(median))
        : string.Empty;

    /// <summary>How many words stopped at a parser search limit, or empty when none did.</summary>
    public string SpeedDetails => Overview?.Timing is { MeasuredWordCount: > 0, StepLimitedWordCount: > 0 and var stopped }
        ? PluralText.Format(MotifAppStrings.OverviewSpeedStoppedPattern, stopped)
        : string.Empty;

    /// <summary>The slowest words as word rows, each with the work steps the Overview read for it.</summary>
    public IReadOnlyList<ListedWordViewModel> SlowestWordRows =>
        Overview?.Timing is { MeasuredWordCount: > 0 } timing
            ? [.. timing.SlowestWords.Select(slow =>
            {
                var listed = Context.Assess.Words.Listed(slow.Word);
                listed.TimeText = WorkTextOf(slow);
                return listed;
            })]
            : [];

    /// <summary>The measured slow words, with outcomes from the summary or an already loaded parse.</summary>
    public IReadOnlyList<OverviewSlowWord> SlowestTableRows => Overview is { Timing.MeasuredWordCount: > 0 } overview
        ? overview.Timing.SlowestWords.Select(word => new OverviewSlowWord(word.Word,
            WorkTextOf(word),
            overview.LookFirst.StepLimitedWords.Contains(word.Word, StringComparer.Ordinal)
                ? Mark.Stopped : Context.Assess.Words.Find(word.Word)?.WordRow.OutcomeMark)).ToArray()
        : [];

    public bool HasSlowestWordRows => SlowestTableRows.Count > 0;

    public string SlowestWordSummary => Overview is { Timing.MeasuredWordCount: > 0 } overview &&
        overview.Timing.SlowestWords.Count > 0
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSlowestSummaryFormat,
            string.Join(" · ", overview.Timing.SlowestWords.Select(word => string.Format(CultureInfo.CurrentCulture,
                overview.LookFirst.StepLimitedWords.Contains(word.Word, StringComparer.Ordinal)
                    ? MotifAppStrings.OverviewSlowestStoppedWordFormat : MotifAppStrings.OverviewSlowestWordFormat,
                word.Word, WorkTextOf(word)))))
        : string.Empty;

    public bool HasSlowestWordSummary => SlowestWordSummary.Length > 0;

    private static string WorkTextOf(SlowWordTiming word) => word.WorkSteps is { } work
        ? TimingShare.FormatWorkSteps(work) : MotifAppStrings.WorkStepsNotRecorded;

    /// <summary>The work shares by kind, including any part no kind accounts for; empty when work by kind was not kept.</summary>
    public IReadOnlyList<TimingShare> TimingKindShares => Overview?.Timing is
        { WorkStepAggregates.Count: > 0, Work.MeasuredWordCount: > 0 } timing
        ? TimingShare.ByKind(timing.WorkStepAggregates, timing.Work.TotalWorkSteps, timing.Kinds,
            MotifAppStrings.OverviewNotAttributed, timing.Work.MeasuredWordCount, alwaysShowUnattributed: false)
        : [];

    public bool HasTimingKindShares => TimingKindShares.Count > 0;

    /// <summary>Says what the work shares leave out: words whose detail was not kept, or work not kept by kind.</summary>
    public string TimingAttributionNote
    {
        get
        {
            if (Overview?.Timing is not { } timing) return string.Empty;
            var notes = new List<string>();
            if (timing.ObjectTimingDetail is { IsComplete: false } detail)
            {
                notes.AddRange(detail.DetailWordCounts.Select(count => string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.TimingDetailLimitedFormat, count.ToString("N0", CultureInfo.CurrentCulture))));
                if (detail.HasRetriedWords) notes.Add(MotifAppStrings.TimingDetailRetried);
                if (detail.StatisticsUnavailable) notes.Add(MotifAppStrings.TimingNoStoredTimingForWords);
            }
            if (timing.Work.MeasuredWordCount > 0 && timing.WorkStepAggregates.Count == 0)
                notes.Add(MotifAppStrings.TimingWorkByObjectUnavailable);
            if (!timing.ObjectTimingAggregatesAvailable) notes.Add(MotifAppStrings.TimingObjectTotalsUnavailable);
            return string.Join(" ", notes);
        }
    }

    /// <summary>The stored-result groups ranked for a useful next step.</summary>
    public IReadOnlyList<OverviewLookFirstRow> LookFirstRows
    {
        get
        {
            if (Overview is not { AssessmentId: not null } overview) return [];
            var data = overview.LookFirst;
            var rows = new List<OverviewLookFirstRow>();
            if (data.ApprovedSameTextDifferentEntryWords.Count > 0)
            {
                var count = data.ApprovedSameTextDifferentEntryWords.Count;
                rows.Add(new OverviewLookFirstRow("1",
                    PluralText.Format(MotifAppStrings.OverviewApprovedDifferentEntryPattern, count),
                    string.Empty, PluralText.Format(MotifAppStrings.OverviewSeeWordsPattern, count),
                    new RelayCommand(() => OpenCell(WordProjectStatus.Approved, CompareColumnKind.NoMatch))));
            }
            if (data.ApprovedLostWords.Count > 0)
            {
                var detail = !data.SharedLostMorphemesAvailable ? MotifAppStrings.OverviewSharedMorphemesUnavailable :
                    string.Join(" · ", data.SharedLostMorphemes.Select(morpheme =>
                    string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewSharedMorphemeUseFormat,
                        PluralText.Format(MotifAppStrings.OverviewSpeedWordsPattern, morpheme.WordCount), morpheme.Form) +
                    (morpheme.NamedByWarning ? MotifAppStrings.OverviewSharedMorphemeNamedByWarning : string.Empty)));
                var approvedLostWordCount = data.ApprovedLostWords.Count;
                var approvedLostSummary = PluralText.Format(MotifAppStrings.OverviewApprovedLostPattern, approvedLostWordCount);
                var approvedLostLink = PluralText.Format(MotifAppStrings.OverviewSeeLostWordsPattern, approvedLostWordCount);
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    approvedLostSummary, detail, approvedLostLink,
                    new RelayCommand(() => OpenCell(WordProjectStatus.Approved, CompareColumnKind.NoParse))));
            }
            if (data.StepLimitedWords.Count > 0)
            {
                var detail = data.StepLimitedWordWorkSteps is { } stopped && overview.Timing.Work.TotalWorkSteps is > 0 and var total
                    ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewStoppedWordsDetailFormat,
                        stopped.ToString("N0", CultureInfo.CurrentCulture), TimingShare.FormatWorkSteps(total))
                    : string.Empty;
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    PluralText.Format(MotifAppStrings.OverviewStoppedWordsRowPattern, data.StepLimitedWords.Count),
                    detail, PluralText.Format(MotifAppStrings.OverviewSeeWordsPattern, data.StepLimitedWords.Count),
                    new RelayCommand(() => Context.OpenTiming(data.StepLimitedWords, null))));
            }
            if (data.UnknownDifferentWordCount > 0)
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    PluralText.Format(MotifAppStrings.OverviewUnknownDifferentPattern, data.UnknownDifferentWordCount),
                    string.Empty,
                    PluralText.Format(MotifAppStrings.OverviewSeeWordsPattern, data.UnknownDifferentWordCount),
                    new RelayCommand(() => OpenCell(WordProjectStatus.Candidate, CompareColumnKind.NoMatch))));
            return rows;
        }
    }

    public bool HasLookFirstRows => LookFirstRows.Count > 0;

    /// <summary>How many Selection words produced a completed parse.</summary>
    public string TextCoverageMain => !HasAssessment || Overview is not { } overview ? MotifAppStrings.OverviewNotParsedYet :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCoverageMainFormat,
            Number(overview.TextCoverage.ParsedWords), Number(overview.SelectionWordCount));

    /// <summary>The parsed share of the Selection's words and of their places in the Texts.</summary>
    public string TextCoverageWords => !HasAssessment || Overview is not { } overview ? string.Empty :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCoverageWordsFormat,
            FormatPercent(overview.WordCoveragePercent),
            overview.TextCoverage.TotalOccurrences == 0
                ? MotifAppStrings.OverviewCoverageNoPlaces
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCoveragePlacesFormat,
                    FormatPercent(overview.TextCoverage.OccurrenceCoveragePercent),
                    Number(overview.TextCoverage.TotalOccurrences)));

    /// <summary>The word outcomes that make up the Selection coverage bar.</summary>
    public IReadOnlyList<OutcomeSegment> TextCoverageSegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            Segment(Mark.Same, overview.TextCoverage.SameWords, MotifAppStrings.OverviewLabelSame, CompareColumnKind.Match,
                MotifAppStrings.OverviewOutcomeSame),
            Segment(Mark.Different, overview.TextCoverage.DifferentWords, MotifAppStrings.OverviewLabelDifferent, CompareColumnKind.NoMatch,
                MotifAppStrings.OverviewOutcomeDifferent),
            Segment(Mark.NoParse, overview.TextCoverage.NoParseWords, MotifAppStrings.OverviewLabelNoParse, CompareColumnKind.NoParse,
                MotifAppStrings.OverviewOutcomeNoParse),
            Segment(Mark.Stopped, overview.TextCoverage.UnknownWords, MotifAppStrings.OverviewLabelStopped, CompareColumnKind.Timeout,
                MotifAppStrings.OverviewOutcomeStopped),
            Segment(Mark.ParserRefusal, overview.TextCoverage.SkippedWords, MotifAppStrings.OverviewLabelCantRead, CompareColumnKind.NoParse,
                ParserRefusals.Title));

    /// <summary>How many approved words the grammar still builds.</summary>
    public string AccuracyMain => !HasAssessment || Overview is not { } overview ? MotifAppStrings.OverviewNotParsedYet :
        overview.Accuracy.ApprovedWordCount == 0 ? MotifAppStrings.OverviewAccuracyNothingApproved :
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewAccuracyMainFormat,
            Number(overview.Accuracy.ApprovedWordsKept), Number(overview.Accuracy.ApprovedWordCount));

    /// <summary>The recorded outcomes among Approved words that were not rebuilt exactly.</summary>
    public string AccuracyCaption
    {
        get
        {
            if (!HasAssessment || Overview is not { } overview) return string.Empty;
            var accuracy = overview.Accuracy;
            var identityOnly = overview.LookFirst.ApprovedSameTextDifferentEntryWords.Count;
            var otherDifferent = Math.Max(0, accuracy.ApprovedWordsNoMatch - identityOnly);
            var parts = new List<string>();
            if (!overview.LookFirst.ApprovedSameTextDifferentEntryAvailable && accuracy.ApprovedWordsNoMatch > 0)
                parts.Add(string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.OverviewAccuracyBuiltElseUnavailableFormat, Number(accuracy.ApprovedWordsNoMatch)));
            else if (identityOnly > 0)
                parts.Add(string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.OverviewAccuracySameFormsFormat, Number(identityOnly)));
            if (overview.LookFirst.ApprovedSameTextDifferentEntryAvailable && otherDifferent > 0)
                parts.Add(string.Format(CultureInfo.CurrentCulture,
                    MotifAppStrings.OverviewAccuracyBuiltDifferentlyFormat, Number(otherDifferent)));
            if (accuracy.ApprovedWordsNoParse > 0) parts.Add(string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.OverviewAccuracyLostFormat, Number(accuracy.ApprovedWordsNoParse)));
            if (accuracy.ApprovedWordsUnknown > 0) parts.Add(string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.OverviewAccuracyStoppedFormat, Number(accuracy.ApprovedWordsUnknown)));
            if (accuracy.ApprovedWordsSkipped > 0) parts.Add(string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.OverviewAccuracyNotParsedFormat, Number(accuracy.ApprovedWordsSkipped)));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// What the bar above cannot show: the disapproved analyses still built, and the Unknown words PanGloss
    /// confirms, each placed by the Compare matrix's rules. The bar's key gives the approved words' own outcomes.
    /// </summary>
    public string AccuracyBreakdown
    {
        get
        {
            if (!HasAssessment || Overview is not { } overview) return string.Empty;
            var accuracy = overview.Accuracy;
            var parts = new List<string>
            {
                PluralText.Format(MotifAppStrings.OverviewDisapprovedStillBuiltPattern, accuracy.RejectedAnalysesRebuilt),
            };
            if (accuracy.CandidateWordCount > 0)
                parts.Add(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCandidatesConfirmedFormat,
                    Number(accuracy.CandidatesConfirmed),
                    PluralText.Format(MotifAppStrings.OverviewSpeedWordsPattern, accuracy.CandidateWordCount)));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The approved-word outcomes that make up the Accuracy bar.</summary>
    public IReadOnlyList<OutcomeSegment> AccuracySegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            ApprovedSegment(Mark.Of(MeaningTone.Fine), overview.Accuracy.ApprovedWordsKept,
                MotifAppStrings.OverviewLabelKept, CompareColumnKind.Match, MotifAppStrings.OverviewOutcomeKept),
            ApprovedSegment(Mark.Of(MeaningTone.Problem), overview.Accuracy.ApprovedWordsNoMatch,
                MotifAppStrings.OverviewLabelBuiltSomethingElse, CompareColumnKind.NoMatch,
                MotifAppStrings.OverviewOutcomeBuiltSomethingElse),
            ApprovedSegment(Mark.Of(MeaningTone.Problem), overview.Accuracy.ApprovedWordsNoParse,
                MotifAppStrings.OverviewLabelLost, CompareColumnKind.NoParse, MotifAppStrings.OverviewOutcomeLost),
            ApprovedSegment(Mark.Of(MeaningTone.Neutral), overview.Accuracy.ApprovedWordsUnknown,
                MotifAppStrings.OverviewLabelStopped, CompareColumnKind.Timeout, MotifAppStrings.OverviewOutcomeNoResult),
            ApprovedSegment(Mark.Of(MeaningTone.Neutral), overview.Accuracy.ApprovedWordsSkipped,
                MotifAppStrings.OverviewLabelCantRead, CompareColumnKind.NoParse, ParserRefusals.Title));

    /// <summary>Whether the Overview response contains its stored grammar warning summary.</summary>
    public bool HasWarningSummary => Overview?.Warnings is not null;

    public bool HasWarningsWordSummary => Overview?.Warnings is { } warnings &&
        (!HasAssessment || warnings.YourWords is { IsComplete: true });

    /// <summary>The Selection words reached by exact identity from a stored grammar warning.</summary>
    public string WarningsYourWordsText => Overview?.Warnings is not { } warnings ? string.Empty
        : !HasAssessment ? MotifAppStrings.OverviewWarningsParseToSee
        : warnings.YourWords is null ? string.Empty
        : !warnings.YourWords.IsComplete ? string.Empty
        : warnings.YourWords.Words == 0 &&
          (warnings.YourWords.BySpellingOnly > 0 || warnings.YourWords.ByMembershipOnly > 0)
            ? MotifAppStrings.OverviewWarningsNoConfirmedUse
        : warnings.YourWords.Words == 0 ? MotifAppStrings.OverviewWarningsNoWordUse
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewWarningsWordsUseFormat,
            Number(warnings.YourWords.Words));

    /// <summary>Spelling matches stay visible as candidates, separate from confirmed identity matches.</summary>
    public string WarningsSpellingCandidatesText => Overview?.Warnings?.YourWords is { BySpellingOnly: > 0 } touched
        ? PluralText.Format(MotifAppStrings.OverviewSpellingOnlyMatchesPattern, touched.BySpellingOnly)
        : string.Empty;

    /// <summary>The largest warning kinds, with exact identity matches and spelling candidates shown apart.</summary>
    public IReadOnlyList<OverviewWarningKindRow> WarningKindRows => Overview?.Warnings?.ByKind.Take(3)
        .Select(row => new OverviewWarningKindRow(row.GroupName ?? row.Code,
            row.Level switch
            {
                GrammarDiagnosticLevel.Error => PluralText.Format(MotifAppStrings.OverviewWarningErrorsPattern, row.Count),
                GrammarDiagnosticLevel.Warning => PluralText.Format(MotifAppStrings.OverviewWarningWarningsPattern, row.Count),
                _ => PluralText.Format(MotifAppStrings.OverviewWarningInformationPattern, row.Count),
            },
            WarningKindWordText(row),
            row.BySpellingOnly is > 0 and var candidates
                ? PluralText.Format(MotifAppStrings.OverviewSpellingOnlyMatchesPattern, candidates)
                : string.Empty))
        .ToArray() ?? [];

    private static string WarningKindWordText(GrammarWarningSummary row) =>
        row.YourWords is { } known && row.WordAttributionComplete == true
            ? PluralText.Format(MotifAppStrings.OverviewSpeedWordsPattern, known) : string.Empty;

    /// <summary>The reported error, warning, and information totals for the grammar check.</summary>
    public string WarningsCount
    {
        get
        {
            if (Overview?.Warnings is not { } warnings) return MotifAppStrings.OverviewWarningsNotCheckedYet;
            var warningCount = warnings.WarningCount;
            if (warningCount is null && warnings.Count == 0) warningCount = 0;
            if (warningCount is null && warnings.Count is { } count && warnings.ErrorCount is { } errors &&
                warnings.InformationCount is { } information)
                warningCount = Math.Max(0, count - errors - information);
            if (warningCount is null) return warnings.Count is not null
                ? MotifAppStrings.OverviewWarningsHeadlineOnly : MotifAppStrings.OverviewWarningsNotCheckedYet;
            var headline = PluralText.Format(MotifAppStrings.OverviewWarningWarningsPattern, warningCount.Value);
            return warnings.ErrorCount is > 0 and var errorCount
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewWarningsWithErrorsFormat, headline,
                    PluralText.Format(MotifAppStrings.OverviewWarningErrorsPattern, errorCount))
                : headline;
        }
    }

    /// <summary>Opens the Texts matrix that shows the words behind Text Coverage.</summary>
    public IRelayCommand OpenTextCoverageCommand { get; }

    /// <summary>Opens the Texts matrix that shows the word standings behind Accuracy.</summary>
    public IRelayCommand OpenAccuracyCommand { get; }

    /// <summary>Opens the page with the stored parse timing.</summary>
    public IRelayCommand OpenTimingCommand { get; }

    /// <summary>Opens the page with the grammar warning findings.</summary>
    public IRelayCommand OpenWarningsCommand { get; }

    /// <summary>Opens AI Handoff.</summary>
    public IRelayCommand OpenAiHandoffCommand { get; }

    private static IReadOnlyList<OutcomeSegment> NonZeroSegments(params OutcomeSegment[] segments) =>
        segments.Where(segment => segment.Count > 0).ToArray();

    private OutcomeSegment Segment(Mark mark, int count, string label, CompareColumnKind column, string outcome) =>
        new(mark, count, label)
        {
            Command = new RelayCommand(() => OpenOutcomeColumn(column)),
            ActionName = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewOpenOutcomeWordsFormat,
                Number(count), outcome),
        };

    private OutcomeSegment ApprovedSegment(Mark mark, int count, string label, CompareColumnKind column,
        string meaning) => new(mark, count, label)
    {
        Command = new RelayCommand(() => OpenCell(WordProjectStatus.Approved, column)),
        ActionName = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewOpenApprovedWordsFormat,
            Number(count), meaning),
    };

    private void OpenOutcomeColumn(CompareColumnKind column) => Context.OpenTexts(TextsTab.Matrix,
        Enum.GetValues<WordProjectStatus>().Select(status => new TextsListCell(status, column)).ToArray());

    private void OpenCell(WordProjectStatus status, CompareColumnKind column) =>
        Context.OpenTexts(TextsTab.Matrix, [new TextsListCell(status, column)]);

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("N0", CultureInfo.CurrentCulture) + "%" : MotifAppStrings.OverviewNotRecorded;

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        Overview = null;
        OverviewRefusal = null;
    }

    protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        await History.SetProjectAsync(projectPath, cancellationToken).ConfigureAwait(true);
        await RefreshOverviewAsync(projectPath, cancellationToken).ConfigureAwait(true);
    }

    protected override async Task OnBaselineCapturedAsync(CancellationToken cancellationToken)
    {
        await History.LoadAsync(cancellationToken).ConfigureAwait(true);
        if (Context.ProjectPath is { } path)
            await RefreshOverviewAsync(path, cancellationToken).ConfigureAwait(true);
    }

    protected override async Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        OnPropertyChanged(nameof(LastParseRefusal));
        OnPropertyChanged(nameof(HasLastParseRefusal));
        if (lens.Today.Assessment is null) return;
        await History.LoadAsync(cancellationToken).ConfigureAwait(true);
        if (Context.ProjectPath is { } path) await RefreshOverviewAsync(path, cancellationToken).ConfigureAwait(true);
    }

    protected override Task OnGrammarCheckedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } path ? RefreshOverviewAsync(path, cancellationToken) : Task.CompletedTask;

    private async Task RefreshOverviewAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_readGeneration;
        var overview = await Context.Commands.OverviewAsync(new OverviewRequest(projectPath), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _readGeneration || !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal))
            return;
        Overview = overview.Succeeded ? overview.Value : null;
        OnPropertyChanged(nameof(LastParseRefusal));
        OnPropertyChanged(nameof(HasLastParseRefusal));
        OverviewRefusal = overview.Succeeded || overview.Refusal is null ? null :
            WindowRefusal.From(overview.Refusal, MotifAppStrings.OverviewReadRefusalNumbers);
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.NeedsAssessment))
        {
            OnPropertyChanged(nameof(ShowNoAssessment));
            OnPropertyChanged(nameof(ShowTiles));
        }
        if (e.PropertyName is nameof(WorkspaceContext.ProjectPath))
        {
            OnPropertyChanged(nameof(ShowChooseProject));
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectFileName));
        }
        if (e.PropertyName == nameof(WorkspaceContext.Baseline))
        {
            OnPropertyChanged(nameof(SelectionIsUnresolved));
            OnPropertyChanged(nameof(SelectionWordCountText));
            OnPropertyChanged(nameof(TextOccurrenceCountText));
        }
    }

    // The slowest words' rows come from the parse on screen, which can arrive after the Overview was read.
    private void OnWordsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssessWordsViewModel.AllRows)) return;
        OnPropertyChanged(nameof(SlowestWordRows));
        OnPropertyChanged(nameof(SlowestTableRows));
        OnPropertyChanged(nameof(HasSlowestWordRows));
    }

    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.IsStale)) OnPropertyChanged(nameof(OverviewIsStale));
    }
}

/// <summary>One stored-result group on the Overview's ranked next steps.</summary>
/// <param name="Number">Its place in the displayed order.</param>
/// <param name="Summary">The measured condition in the group's words.</param>
/// <param name="Detail">Additional stored evidence for the group.</param>
/// <param name="LinkText">The destination described by its link.</param>
/// <param name="OpenCommand">Opens the exact Matrix cell or Timing word set.</param>
public sealed record OverviewLookFirstRow(
    string Number, string Summary, string Detail, string LinkText, IRelayCommand OpenCommand);

/// <summary>One warning kind in the Overview, keeping exact matches apart from spelling candidates.</summary>
/// <param name="Name">The kind's window name.</param>
/// <param name="WarningCount">How many findings of this kind were recorded.</param>
/// <param name="IdentityMatchedWords">How many words use a named object by exact identity.</param>
/// <param name="SpellingCandidates">The spelling-only candidates, not confirmed uses.</param>
public sealed record OverviewWarningKindRow(
    string Name, string WarningCount, string IdentityMatchedWords, string SpellingCandidates);
