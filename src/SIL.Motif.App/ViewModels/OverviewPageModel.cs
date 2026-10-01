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
    [NotifyPropertyChangedFor(nameof(TextCoverageMain))]
    [NotifyPropertyChangedFor(nameof(TextCoverageWords))]
    [NotifyPropertyChangedFor(nameof(TextCoverageSegments))]
    [NotifyPropertyChangedFor(nameof(AccuracyMain))]
    [NotifyPropertyChangedFor(nameof(AccuracyCaption))]
    [NotifyPropertyChangedFor(nameof(AccuracyBreakdown))]
    [NotifyPropertyChangedFor(nameof(AccuracySegments))]
    [NotifyPropertyChangedFor(nameof(HasWarningSummary))]
    [NotifyPropertyChangedFor(nameof(WarningsCount))]
    [NotifyPropertyChangedFor(nameof(WarningsDetails))]
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

    /// <summary>Whether the Overview read was refused.</summary>
    public bool HasOverviewRefusal => OverviewRefusal is not null;

    /// <summary>
    /// What the page says when the Overview read is refused: the refusal's own sentence when the window has one
    /// for its code, otherwise that the numbers could not be read.
    /// </summary>
    public string OverviewRefusalLine => OverviewRefusal is not { } refusal ? string.Empty
        : refusal.Sentence == WindowRefusal.GenericSentence ? "Motif could not read this project's numbers."
        : refusal.Sentence;

    /// <summary>Where a person reports a refused read.</summary>
    public Uri ReportProblemUri { get; } = new(AppLinks.Issues);

    /// <summary>Reads the stored Overview again after a refusal.</summary>
    public IAsyncRelayCommand RetryOverviewCommand { get; }

    /// <summary>Whether the numbers behind this Overview describe an older state of the project.</summary>
    public bool OverviewIsStale => Context.Evidence.IsStale;

    /// <summary>Whether this response could not resolve its default Selection against the current Baseline.</summary>
    public bool SelectionIsUnresolved => Overview is { SelectionResolved: false };

    /// <summary>The project name from the Overview response, else the project file's name without its extension.</summary>
    public string ProjectTitle => Overview?.ProjectName ??
        (Context.ProjectPath is { } path ? Path.GetFileNameWithoutExtension(path) : Context.ProjectName);

    /// <summary>The FieldWorks project file the page describes.</summary>
    public string ProjectFileName => Overview?.ProjectFileName is { Length: > 0 } name ? name
        : Path.GetFileName(Context.ProjectPath ?? string.Empty);

    /// <summary>The number of words in the response's default Selection.</summary>
    public string SelectionWordCountText => Overview is { SelectionResolved: false } ? "not resolved" :
        Overview?.SelectionWordCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The number of occurrences in the response's selected Texts.</summary>
    public string TextOccurrenceCountText => Overview is { SelectionResolved: false } ? "not resolved" :
        Overview?.TextOccurrenceCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's wordform count from the response.</summary>
    public string WordformCountText =>
        Overview?.WordformCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's rule count from the response.</summary>
    public string RuleCountText => Overview?.RuleCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The project's lexeme count from the response.</summary>
    public string LexemeCountText => Overview?.LexemeCount.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>
    /// The speed headline: how many words PanGloss timed and their summed parse time, both as stored. There is
    /// no comparison with another parser, because Motif does not measure one.
    /// </summary>
    public string SpeedMain => Overview is
        { Timing.MeasuredWordCount: > 0 and var measured, AssessmentElapsedSeconds: { } seconds }
        ? $"{SpeedText.Count(measured, "word", "words")} in {SpeedText.Duration(seconds * 1000)}"
        : "No parse times recorded";

    /// <summary>The stored median and 95th percentile per-word parse time.</summary>
    public string SpeedMedian => Overview?.Timing is { MedianMs: { } median } timing
        ? $"median {SpeedText.PerWord(median)} a word" +
          (timing.Percentile95Ms is { } p95 ? $" · 95th percentile {SpeedText.PerWord(p95)}" : string.Empty)
        : "Parse all words to measure how fast PanGloss is.";

    /// <summary>How many words stopped at the step limit, and the slowest words with their times.</summary>
    public string SpeedDetails
    {
        get
        {
            if (Overview?.Timing is not { MeasuredWordCount: > 0 } timing) return string.Empty;
            var parts = new List<string>();
            if (timing.StepLimitedWordCount > 0)
                parts.Add($"{timing.StepLimitedWordCount:N0} stopped at the step limit");
            if (timing.SlowestWords.Count > 0)
                parts.Add("slowest: " + string.Join(", ",
                    timing.SlowestWords.Select(word => $"{word.Word} {SpeedText.PerWord(word.ElapsedMs)}")));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>How many Selection words produced a completed parse.</summary>
    public string TextCoverageMain => !HasAssessment || Overview is not { } overview ? "Not parsed yet" :
        $"{overview.TextCoverage.ParsedWords:N0} of {overview.SelectionWordCount:N0} words parse";

    /// <summary>The parsed share of the Selection's words and of their occurrences in the Texts.</summary>
    public string TextCoverageWords => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{FormatPercent(overview.WordCoveragePercent)} of the words in your Selection · " +
        $"{FormatPercent(overview.TextCoverage.OccurrenceCoveragePercent)} of their " +
        $"{overview.TextCoverage.TotalOccurrences:N0} occurrences";

    /// <summary>The word outcomes that make up the Selection coverage bar.</summary>
    public IReadOnlyList<OutcomeSegment> TextCoverageSegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            new(Mark.Same, overview.TextCoverage.ParsedWords, "parsed"),
            new(Mark.NoParse, overview.TextCoverage.NoParseWords, "no parse"),
            new(Mark.Stopped, overview.TextCoverage.UnknownWords, "stopped (step or time limit)"),
            new(Mark.NotParsed, overview.TextCoverage.SkippedWords, "skipped"));

    /// <summary>How many approved words the grammar still builds.</summary>
    public string AccuracyMain => !HasAssessment || Overview is not { } overview ? "Not parsed yet" :
        $"{overview.Accuracy.ApprovedWordsKept:N0} of {overview.Accuracy.ApprovedWordCount:N0} rebuilt";

    /// <summary>The Approved analyses tile's headline, said as a sentence.</summary>
    public string AccuracyCaption => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"The grammar still builds {overview.Accuracy.ApprovedWordsKept:N0} of the " +
        $"{overview.Accuracy.ApprovedWordCount:N0} words you approved in FieldWorks.";

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
            new(Mark.Of(MeaningTone.Fine), overview.Accuracy.ApprovedWordsKept, "rebuilt"),
            new(Mark.Of(MeaningTone.Problem), overview.Accuracy.ApprovedWordsNoMatch, "built another reading"),
            new(Mark.NoParse, overview.Accuracy.ApprovedWordsNoParse, "no parse"),
            new(Mark.Stopped, overview.Accuracy.ApprovedWordsUnknown, "stopped"),
            new(Mark.NotParsed, overview.Accuracy.ApprovedWordsSkipped, "skipped"));

    /// <summary>Whether the Overview response contains its stored grammar warning summary.</summary>
    public bool HasWarningSummary => Overview?.Warnings is not null;

    /// <summary>
    /// Every grammar finding, the number the Warnings page's sidebar badge also shows, then the errors among them.
    /// </summary>
    public string WarningsCount => Overview?.Warnings switch
    {
        { Count: { } count, ErrorCount: { } errors } =>
            $"{SpeedText.Count(count, "warning", "warnings")} · {SpeedText.Count(errors, "error", "errors")}",
        { Count: { } count } => SpeedText.Count(count, "warning", "warnings"),
        _ => "Not checked yet",
    };

    /// <summary>The informational findings, or why there is nothing more to say.</summary>
    public string WarningsDetails => Overview?.Warnings is not { } warnings ? "No warning summary is available."
        : warnings.InformationCount is { } information ? $"{information:N0} worth a look"
        : warnings.Count is null ? "No findings count was recorded." : string.Empty;

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

    // Opening and Refresh already read the stored Overview, so only a run from this window makes it older.
    protected override async Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.Assessment is not { IsStored: false }) return;
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
        OverviewRefusal = overview.Succeeded || overview.Refusal is null ? null : WindowRefusal.From(overview.Refusal);
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
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectFileName));
        }
    }

    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.IsStale)) OnPropertyChanged(nameof(OverviewIsStale));
    }
}
