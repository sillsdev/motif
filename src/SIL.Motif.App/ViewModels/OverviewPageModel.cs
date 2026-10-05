using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
        OpenTextCoverageCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.Matrix, []));
        OpenAccuracyCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.Matrix,
            Enum.GetValues<CompareColumnKind>()
                .Select(column => new TextsListCell(WordProjectStatus.Approved, column)).ToArray()));
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

    /// <summary>The stored Overview read for the open project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssessment))]
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
    [NotifyPropertyChangedFor(nameof(SpeedDetails))]
    [NotifyPropertyChangedFor(nameof(TimingKindShares))]
    [NotifyPropertyChangedFor(nameof(HasTimingKindShares))]
    [NotifyPropertyChangedFor(nameof(TimingAttributionNote))]
    [NotifyPropertyChangedFor(nameof(SlowestWordRows))]
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
    private OverviewResponse? _overview;

    /// <summary>Why the stored Overview query was refused, in the window's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverviewRefusal))]
    [NotifyPropertyChangedFor(nameof(OverviewRefusalLine))]
    private WindowRefusal? _overviewRefusal;

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
    public IReadOnlyList<RecentProjectViewModel> ChooseProjectRows => Context.KnownProjects
        .Select(project => new RecentProjectViewModel(project.FullFwDataPath)).ToArray();

    /// <summary>Whether there are Known projects to open from the initial page.</summary>
    public bool HasKnownProjects => Context.KnownProjects.Count > 0;

    /// <summary>Whether the initial page needs to explain the empty Known projects list.</summary>
    public bool ShowNoKnownProjects => !HasKnownProjects;

    /// <summary>What the chooser says when this computer has no Known projects.</summary>
    public string NoKnownProjectsText => HasKnownProjects ? string.Empty : "No recent projects on this computer.";

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
            ? "not resolved" : "not set up" :
        Overview?.SelectionWordCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The number of places in the response's selected Texts.</summary>
    public string TextOccurrenceCountText => Overview is { SelectionResolved: false }
        ? Context.Baseline?.HasBaseline == true && Context.Setup?.CanRunDefaultSelection == true
            ? "not resolved" : "not set up" :
        Overview?.TextOccurrenceCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's wordform count from the response.</summary>
    public string WordformCountText =>
        Overview?.WordformCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's rule count from the response.</summary>
    public string RuleCountText => Overview?.RuleCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's lexeme count from the response.</summary>
    public string LexemeCountText => Overview?.LexemeCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>
    /// The speed headline: how many words PanGloss timed and their total word time, each word's parse time added
    /// up rather than how long the run took. There is no comparison with another parser, because Motif does not
    /// measure one.
    /// </summary>
    public string SpeedMain => Overview is
        { Timing.MeasuredWordCount: > 0 and var measured, AssessmentElapsedSeconds: { } seconds }
        ? $"{SpeedText.Count(measured, "word", "words")} · {TimingShare.FormatDuration(seconds * 1000)} total word time"
        : "No parse times recorded";

    /// <summary>The stored median and 95th percentile per-word parse time.</summary>
    public string SpeedMedian => Overview?.Timing is { MedianMs: { } median } timing
        ? $"median {TimingShare.FormatDuration(median)} a word" +
          (timing.Percentile95Ms is { } p95 ? $" · 95th percentile {TimingShare.FormatDuration(p95)}" : string.Empty)
        : string.Empty;

    /// <summary>How many words stopped at a parser search limit, or empty when none did.</summary>
    public string SpeedDetails => Overview?.Timing is { MeasuredWordCount: > 0, StepLimitedWordCount: > 0 and var stopped }
        ? $"{stopped:N0} stopped at a search limit"
        : string.Empty;

    /// <summary>The slowest words as word rows, each with the parse time the Overview read for it.</summary>
    public IReadOnlyList<ListedWordViewModel> SlowestWordRows =>
        Overview?.Timing is { MeasuredWordCount: > 0 } timing
            ? [.. timing.SlowestWords.Select(slow =>
            {
                var listed = Context.Assess.Words.Listed(slow.Word);
                listed.TimeText = TimingShare.FormatDuration(slow.ElapsedMs);
                return listed;
            })]
            : [];

    public bool HasSlowestWordRows => SlowestWordRows.Count > 0;

    public string SlowestWordSummary => Overview is { Timing.MeasuredWordCount: > 0 } overview &&
        overview.Timing.SlowestWords.Count > 0
        ? "Slowest: " + string.Join(" · ", overview.Timing.SlowestWords.Select(word =>
            $"{word.Word}{(overview.LookFirst.StepLimitedWords.Contains(word.Word, StringComparer.Ordinal) ? " Stopped" : string.Empty)} · " +
            TimingShare.FormatDuration(word.ElapsedMs)))
        : string.Empty;

    public bool HasSlowestWordSummary => SlowestWordSummary.Length > 0;

    /// <summary>The measured time shares by kind, including any part no kind's timer recorded.</summary>
    public IReadOnlyList<TimingShare> TimingKindShares
    {
        get
        {
            if (Overview?.Timing is not { } timing) return [];
            var shares = timing.Kinds.Select(row => new TimingShare(TimingShare.KindName(row.Kind), row.Kind,
                row.SelfMs, row.ShareOfWordTime, row.WordsTouched, row)).ToList();
            if (timing.Attribution.NotAttributedMs is > 0 and var other &&
                timing.Attribution.NotAttributedShare is { } share)
                shares.Add(new TimingShare("Not attributed", string.Empty, other, share,
                    timing.Attribution.MeasuredWordCount, null));
            return shares;
        }
    }

    public bool HasTimingKindShares => TimingKindShares.Count > 0;

    /// <summary>Names the measured overrun if parser-object timers exceed total word time.</summary>
    public string TimingAttributionNote => Overview?.Timing.Attribution is { Overrun: true } attribution
        ? $"Object timers exceed total word time by {TimingShare.FormatDuration(attribution.OverrunMs)}."
        : string.Empty;

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
                    $"{SpeedText.Count(count, "approved word", "approved words")} " +
                    (count == 1 ? "is built from a different entry with the same form and gloss." :
                        "are built from a different entry with the same form and gloss."),
                    string.Empty, $"See the {SpeedText.Count(count, "word", "words")}",
                    new RelayCommand(() => OpenCell(WordProjectStatus.Approved, CompareColumnKind.NoMatch))));
            }
            if (data.ApprovedLostWords.Count > 0)
            {
                var detail = !data.SharedLostMorphemesAvailable ? "Shared morpheme information is unavailable." :
                    string.Join(" · ", data.SharedLostMorphemes.Select(morpheme =>
                    $"{SpeedText.Count(morpheme.WordCount, "word", "words")} use {morpheme.Form}" +
                    (morpheme.NamedByWarning ? " (named by a grammar warning)" : string.Empty)));
                var approvedLostWordCount = data.ApprovedLostWords.Count;
                var approvedLostSummary = $"{SpeedText.Count(approvedLostWordCount, "approved word", "approved words")} " +
                    (approvedLostWordCount == 1 ? "is Lost; the grammar builds nothing for it." :
                        "are Lost; the grammar builds nothing for them.");
                var approvedLostLink = approvedLostWordCount == 1 ? "See the word" :
                    $"See the {SpeedText.Count(approvedLostWordCount, "word", "words")}";
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    approvedLostSummary, detail, approvedLostLink,
                    new RelayCommand(() => OpenCell(WordProjectStatus.Approved, CompareColumnKind.NoParse))));
            }
            if (data.StepLimitedWords.Count > 0)
            {
                var detail = data.StepLimitedWordTimeMs is { } stopped && overview.AssessmentElapsedSeconds is { } total
                    ? $"{TimingShare.FormatDuration(stopped)} of {TimingShare.FormatDuration(total * 1000)} total word time"
                    : string.Empty;
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    $"{SpeedText.Count(data.StepLimitedWords.Count, "word", "words")} stopped at a search limit.",
                    detail, $"See the {SpeedText.Count(data.StepLimitedWords.Count, "word", "words")}",
                    new RelayCommand(() => Context.OpenTiming(data.StepLimitedWords, null))));
            }
            if (data.UnknownDifferentWordCount > 0)
                rows.Add(new OverviewLookFirstRow((rows.Count + 1).ToString(CultureInfo.CurrentCulture),
                    $"{SpeedText.Count(data.UnknownDifferentWordCount, "Unknown word", "Unknown words")} differ " +
                    "from what PanGloss builds.", string.Empty,
                    $"See the {SpeedText.Count(data.UnknownDifferentWordCount, "word", "words")}",
                    new RelayCommand(() => OpenCell(WordProjectStatus.Candidate, CompareColumnKind.NoMatch))));
            return rows;
        }
    }

    public bool HasLookFirstRows => LookFirstRows.Count > 0;

    /// <summary>How many Selection words produced a completed parse.</summary>
    public string TextCoverageMain => !HasAssessment || Overview is not { } overview ? "Not parsed yet" :
        $"{overview.TextCoverage.ParsedWords:N0} of {overview.SelectionWordCount:N0} words parse";

    /// <summary>The parsed share of the Selection's words and of their places in the Texts.</summary>
    public string TextCoverageWords => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{FormatPercent(overview.WordCoveragePercent)} of the words in your Selection · " +
        (overview.TextCoverage.TotalOccurrences == 0
            ? "No places in the chosen Texts"
            : $"{FormatPercent(overview.TextCoverage.OccurrenceCoveragePercent)} of their " +
              $"{overview.TextCoverage.TotalOccurrences:N0} places");

    /// <summary>The word outcomes that make up the Selection coverage bar.</summary>
    public IReadOnlyList<OutcomeSegment> TextCoverageSegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            Segment(Mark.Same, overview.TextCoverage.SameWords, "same", CompareColumnKind.Match,
                "Same outcome"),
            Segment(Mark.Different, overview.TextCoverage.DifferentWords, "different", CompareColumnKind.NoMatch,
                "Different outcome"),
            Segment(Mark.NoParse, overview.TextCoverage.NoParseWords, "no parse", CompareColumnKind.NoParse,
                "No parse"),
            Segment(Mark.Stopped, overview.TextCoverage.UnknownWords, "stopped", CompareColumnKind.Timeout,
                "Stopped"),
            Segment(Mark.ParserRefusal, overview.TextCoverage.SkippedWords, "can't read", CompareColumnKind.NoParse,
                ParserRefusals.Title));

    /// <summary>How many approved words the grammar still builds.</summary>
    public string AccuracyMain => !HasAssessment || Overview is not { } overview ? "Not parsed yet" :
        $"{overview.Accuracy.ApprovedWordsKept:N0} of {overview.Accuracy.ApprovedWordCount:N0} rebuilt exactly";

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
                parts.Add($"{accuracy.ApprovedWordsNoMatch:N0} built something else; entry comparison unavailable");
            else if (identityOnly > 0)
                parts.Add($"{identityOnly:N0} with the same forms and glosses but a different entry");
            if (overview.LookFirst.ApprovedSameTextDifferentEntryAvailable && otherDifferent > 0)
                parts.Add($"{otherDifferent:N0} built differently");
            if (accuracy.ApprovedWordsNoParse > 0) parts.Add($"{accuracy.ApprovedWordsNoParse:N0} Lost");
            if (accuracy.ApprovedWordsUnknown > 0) parts.Add($"{accuracy.ApprovedWordsUnknown:N0} Stopped");
            if (accuracy.ApprovedWordsSkipped > 0) parts.Add($"{accuracy.ApprovedWordsSkipped:N0} Not parsed");
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
                SpeedText.Count(accuracy.RejectedAnalysesRebuilt, "disapproved analysis", "disapproved analyses") +
                " still built",
            };
            if (accuracy.CandidateWordCount > 0)
                parts.Add($"PanGloss confirms {accuracy.CandidatesConfirmed:N0} of " +
                    SpeedText.Count(accuracy.CandidateWordCount, "word", "words") + " marked Unknown");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The approved-word outcomes that make up the Accuracy bar.</summary>
    public IReadOnlyList<OutcomeSegment> AccuracySegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            ApprovedSegment(Mark.Of(MeaningTone.Fine), overview.Accuracy.ApprovedWordsKept, "kept",
                CompareColumnKind.Match, "Kept"),
            ApprovedSegment(Mark.Of(MeaningTone.Problem), overview.Accuracy.ApprovedWordsNoMatch,
                "built something else", CompareColumnKind.NoMatch, "Built something else"),
            ApprovedSegment(Mark.Of(MeaningTone.Problem), overview.Accuracy.ApprovedWordsNoParse,
                "lost", CompareColumnKind.NoParse, "Lost"),
            ApprovedSegment(Mark.Of(MeaningTone.Neutral), overview.Accuracy.ApprovedWordsUnknown,
                "stopped", CompareColumnKind.Timeout, "No result"),
            ApprovedSegment(Mark.Of(MeaningTone.Neutral), overview.Accuracy.ApprovedWordsSkipped,
                "can't read", CompareColumnKind.NoParse, ParserRefusals.Title));

    /// <summary>Whether the Overview response contains its stored grammar warning summary.</summary>
    public bool HasWarningSummary => Overview?.Warnings is not null;

    public bool HasWarningsWordSummary => Overview?.Warnings is { } warnings &&
        (!HasAssessment || warnings.YourWords is { IsComplete: true });

    /// <summary>The Selection words reached by exact identity from a stored grammar warning.</summary>
    public string WarningsYourWordsText => Overview?.Warnings is not { } warnings ? string.Empty
        : !HasAssessment ? "Parse to see which of your words they touch"
        : warnings.YourWords is null ? string.Empty
        : !warnings.YourWords.IsComplete ? string.Empty
        : warnings.YourWords.Words == 0 &&
          (warnings.YourWords.BySpellingOnly > 0 || warnings.YourWords.ByMembershipOnly > 0)
            ? "No confirmed word uses; possible matches were found"
        : warnings.YourWords.Words == 0 ? "None of your words use what a warning names"
        : $"{warnings.YourWords.Words:N0} of your words use what a warning names";

    /// <summary>Spelling matches stay visible as candidates, separate from confirmed identity matches.</summary>
    public string WarningsSpellingCandidatesText => Overview?.Warnings?.YourWords is { BySpellingOnly: > 0 } touched
        ? $"{SpeedText.Count(touched.BySpellingOnly, "spelling-only match", "spelling-only matches")}; not confirmed uses"
        : string.Empty;

    /// <summary>The largest warning kinds, with exact identity matches and spelling candidates shown apart.</summary>
    public IReadOnlyList<OverviewWarningKindRow> WarningKindRows => Overview?.Warnings?.ByKind.Take(3)
        .Select(row => new OverviewWarningKindRow(row.GroupName ?? row.Code,
            row.Level switch
            {
                GrammarDiagnosticLevel.Error => SpeedText.Count(row.Count, "error", "errors"),
                GrammarDiagnosticLevel.Warning => SpeedText.Count(row.Count, "warning", "warnings"),
                _ => SpeedText.Count(row.Count, "information finding", "information findings"),
            },
            WarningKindWordText(row),
            row.BySpellingOnly is > 0 and var candidates
                ? $"{SpeedText.Count(candidates, "spelling-only match", "spelling-only matches")}; not confirmed uses"
                : string.Empty))
        .ToArray() ?? [];

    private static string WarningKindWordText(GrammarWarningSummary row) =>
        row.YourWords is { } known && row.WordAttributionComplete == true
            ? SpeedText.Count(known, "word", "words") : string.Empty;

    /// <summary>The reported error, warning, and information totals for the grammar check.</summary>
    public string WarningsCount
    {
        get
        {
            if (Overview?.Warnings is not { } warnings) return "Not checked yet";
            var warningCount = warnings.WarningCount;
            if (warningCount is null && warnings.Count == 0) warningCount = 0;
            if (warningCount is null && warnings.Count is { } count && warnings.ErrorCount is { } errors &&
                warnings.InformationCount is { } information)
                warningCount = Math.Max(0, count - errors - information);
            if (warningCount is null) return warnings.Count is not null ? "Warnings" : "Not checked yet";
            var headline = SpeedText.Count(warningCount.Value, "warning", "warnings");
            return warnings.ErrorCount is > 0 and var errorCount
                ? $"{headline} · {SpeedText.Count(errorCount, "error", "errors")}" : headline;
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
            ActionName = $"Open {count:N0} {outcome} words in the Matrix",
        };

    private OutcomeSegment ApprovedSegment(Mark mark, int count, string label, CompareColumnKind column,
        string meaning) => new(mark, count, label)
    {
        Command = new RelayCommand(() => OpenCell(WordProjectStatus.Approved, column)),
        ActionName = $"Open {count:N0} Approved {meaning} words in the Matrix",
    };

    private void OpenOutcomeColumn(CompareColumnKind column) => Context.OpenTexts(TextsTab.Matrix,
        Enum.GetValues<WordProjectStatus>().Select(status => new TextsListCell(status, column)).ToArray());

    private void OpenCell(WordProjectStatus status, CompareColumnKind column) =>
        Context.OpenTexts(TextsTab.Matrix, [new TextsListCell(status, column)]);

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("N0", CultureInfo.CurrentCulture) + "%" : "not recorded";

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

    protected override async Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.Assessment is null) return;
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
        OverviewRefusal = overview.Succeeded || overview.Refusal is null ? null :
            WindowRefusal.From(overview.Refusal, "Motif could not read this project's numbers.");
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
