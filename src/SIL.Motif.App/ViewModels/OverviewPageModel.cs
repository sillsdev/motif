using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        var openMatrix = new RelayCommand(() => Context.OpenTexts(TextsTab.Matrix));
        OpenTextCoverageCommand = openMatrix;
        OpenAccuracyCommand = openMatrix;
        OpenTimingCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Timing));
        OpenWarningsCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Warnings));
        OpenAiHandoffCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.AiHandoff));
        context.PropertyChanged += OnContextPropertyChanged;
        context.Evidence.PropertyChanged += OnEvidencePropertyChanged;
    }

    /// <summary>The project's Baselines and Assessments, newest first, which the page loads for itself.</summary>
    public ProjectHistoryViewModel History { get; }

    /// <summary>The stored Overview read for the open project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssessment))]
    [NotifyPropertyChangedFor(nameof(ShowNoAssessment))]
    [NotifyPropertyChangedFor(nameof(SelectionIsUnresolved))]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    [NotifyPropertyChangedFor(nameof(ProjectDetails))]
    [NotifyPropertyChangedFor(nameof(AssessmentDetails))]
    [NotifyPropertyChangedFor(nameof(BaselineDetails))]
    [NotifyPropertyChangedFor(nameof(FingerprintSummary))]
    [NotifyPropertyChangedFor(nameof(SelectionWordCountText))]
    [NotifyPropertyChangedFor(nameof(TextOccurrenceCountText))]
    [NotifyPropertyChangedFor(nameof(WordformCountText))]
    [NotifyPropertyChangedFor(nameof(RuleCountText))]
    [NotifyPropertyChangedFor(nameof(LexemeCountText))]
    [NotifyPropertyChangedFor(nameof(TextCoverageMain))]
    [NotifyPropertyChangedFor(nameof(TextCoverageWords))]
    [NotifyPropertyChangedFor(nameof(TextCoverageBreakdown))]
    [NotifyPropertyChangedFor(nameof(TextCoverageOccurrences))]
    [NotifyPropertyChangedFor(nameof(TextCoverageSegments))]
    [NotifyPropertyChangedFor(nameof(AccuracyMain))]
    [NotifyPropertyChangedFor(nameof(AccuracyBreakdown))]
    [NotifyPropertyChangedFor(nameof(AccuracySegments))]
    [NotifyPropertyChangedFor(nameof(TimingMedian))]
    [NotifyPropertyChangedFor(nameof(TimingDetails))]
    [NotifyPropertyChangedFor(nameof(SlowestWords))]
    [NotifyPropertyChangedFor(nameof(HasWarningSummary))]
    [NotifyPropertyChangedFor(nameof(WarningsCount))]
    [NotifyPropertyChangedFor(nameof(WarningsLeftOut))]
    [NotifyPropertyChangedFor(nameof(WarningsDetails))]
    private OverviewResponse? _overview;

    /// <summary>Why the stored Overview query was refused, in the window's words.</summary>
    [ObservableProperty]
    private WindowRefusal? _overviewRefusal;

    /// <summary>Whether the response has an Assessment for its default Selection.</summary>
    public bool HasAssessment => Overview?.AssessmentId is not null;

    /// <summary>Whether the Overview should explain that this project has no Assessment.</summary>
    public bool ShowNoAssessment => !HasAssessment;

    /// <summary>Whether the numbers behind this Overview describe an older state of the project.</summary>
    public bool OverviewIsStale => Context.Evidence.IsStale;

    /// <summary>Whether this response could not resolve its default Selection against the current Baseline.</summary>
    public bool SelectionIsUnresolved => Overview is { SelectionResolved: false };

    /// <summary>The project name from the Overview response.</summary>
    public string ProjectTitle => Overview?.ProjectName ?? Context.ProjectName;

    /// <summary>The project file, when it was opened, and the last FieldWorks save.</summary>
    public string ProjectDetails =>
        $"{Overview?.ProjectFileName ?? Path.GetFileName(Context.ProjectPath ?? string.Empty)} · " +
        $"opened {FormatTime(Overview?.MotifStoreCreatedUtc)} · last FieldWorks save {FormatTime(Overview?.LastFieldWorksSaveUtc)}";

    /// <summary>When the current Assessment completed and how long its parsing took.</summary>
    public string AssessmentDetails => Overview?.AssessedUtc is { } at
        ? $"Assessed {FormatTime(at)}" +
          (Overview.AssessmentElapsedSeconds is { } elapsed ? $" in {elapsed:N0} s" : string.Empty)
        : string.Empty;

    /// <summary>When the current Baseline was captured and whether the FieldWorks file has changed since.</summary>
    public string BaselineDetails => Overview?.BaselineCapturedUtc is { } captured
        ? $"Baseline {FormatTime(captured)}" +
          (Overview.BaselineSourceLastWriteUtc is { } saved ? $" · saved {FormatTime(saved)}" : string.Empty)
        : "No Baseline captured";

    /// <summary>The Assessment's grammar and Selection fingerprints, shortened for the page header.</summary>
    public string FingerprintSummary => Overview is not { } overview ? string.Empty :
        $"grammar {ShortFingerprint(overview.GrammarFingerprint)} · Selection {ShortFingerprint(overview.SelectionFingerprint)}";

    /// <summary>The number of words in the response's default Selection.</summary>
    public string SelectionWordCountText => Overview is { SelectionResolved: false } ? "not resolved" :
        Overview?.SelectionWordCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    /// <summary>The number of occurrences in the response's selected Texts.</summary>
    public string TextOccurrenceCountText => Overview is { SelectionResolved: false } ? "not resolved" :
        Overview?.TextOccurrenceCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    /// <summary>The project's wordform count from the response.</summary>
    public string WordformCountText => Overview?.WordformCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    /// <summary>The project's rule count from the response.</summary>
    public string RuleCountText => Overview?.RuleCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    /// <summary>The project's lexeme count from the response.</summary>
    public string LexemeCountText => Overview?.LexemeCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";

    /// <summary>The share of Selection words with a completed parse, as returned by the Overview command.</summary>
    public string TextCoverageMain => !HasAssessment ? "No Assessment" : FormatPercent(Overview?.WordCoveragePercent);

    /// <summary>The parsed words and Selection total returned by the Overview command.</summary>
    public string TextCoverageWords => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{overview.TextCoverage.ParsedWords:N0} of {overview.SelectionWordCount:N0} words in the default Selection";

    /// <summary>The no-parse, timed-out and skipped word counts returned by the Overview command.</summary>
    public string TextCoverageBreakdown => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{overview.TextCoverage.NoParseWords:N0} no parse · {overview.TextCoverage.UnknownWords:N0} timed out · " +
        $"{overview.TextCoverage.SkippedWords:N0} skipped";

    /// <summary>The occurrence total and covered share returned by the Overview command.</summary>
    public string TextCoverageOccurrences => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{overview.TextCoverage.ParsedOccurrences:N0} of {overview.TextCoverage.TotalOccurrences:N0} occurrences · " +
        $"{FormatPercent(overview.TextCoverage.OccurrenceCoveragePercent)} covered";

    /// <summary>The word outcomes that make up the Selection coverage bar.</summary>
    public IReadOnlyList<OutcomeSegment> TextCoverageSegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            new(Verdict.Agrees, overview.TextCoverage.ParsedWords, "parsed"),
            new(Verdict.NoResult, overview.TextCoverage.NoParseWords, "no parse"),
            new(Verdict.Limit, overview.TextCoverage.UnknownWords, "timed out"),
            new(Verdict.Limit, overview.TextCoverage.SkippedWords, "skipped"));

    /// <summary>The approved words kept and total returned by the Overview command.</summary>
    public string AccuracyMain => !HasAssessment || Overview is not { } overview ? "No Assessment" :
        $"{overview.Accuracy.ApprovedWordsKept:N0} of {overview.Accuracy.ApprovedWordCount:N0}";

    /// <summary>The accuracy counts returned by the Overview command, using the Compare matrix's definitions.</summary>
    public string AccuracyBreakdown => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"{overview.Accuracy.Violations:N0} violations · {overview.Accuracy.UnknownWords:N0} Unknown (timed out) · " +
        $"{overview.Accuracy.RejectedAnalysesRebuilt:N0} rejected analyses rebuilt · " +
        $"candidates confirmed: {overview.Accuracy.CandidatesConfirmed:N0} of {overview.Accuracy.CandidateWordCount:N0}";

    /// <summary>The approved-word outcomes that make up the Accuracy bar.</summary>
    public IReadOnlyList<OutcomeSegment> AccuracySegments => !HasAssessment || Overview is not { } overview ? [] :
        NonZeroSegments(
            new(Verdict.Agrees, overview.Accuracy.ApprovedWordsKept, "kept"),
            new(Verdict.Differs, overview.Accuracy.ApprovedWordsNoMatch, "built another reading"),
            new(Verdict.NoResult, overview.Accuracy.ApprovedWordsNoParse, "no parse"),
            new(Verdict.Limit, overview.Accuracy.ApprovedWordsUnknown, "timed out"),
            new(Verdict.Several, overview.Accuracy.ApprovedWordsSkipped, "skipped"));

    /// <summary>The median per-word time returned by the Overview command.</summary>
    public string TimingMedian => Overview?.Timing.MedianMs is { } median ? $"{median:N1} ms" : "No timing recorded";

    /// <summary>The 95th percentile and step-limit count returned by the Overview command.</summary>
    public string TimingDetails => !HasAssessment || Overview is not { } overview ? string.Empty :
        $"95th percentile {FormatMilliseconds(overview.Timing.Percentile95Ms)} · " +
        $"{overview.Timing.StepLimitedWordCount:N0} words hit the step limit";

    /// <summary>The slowest words and times returned by the Overview command.</summary>
    public string SlowestWords => Overview?.Timing.SlowestWords is { Count: > 0 } slowest
        ? $"Slowest: {string.Join(" · ", slowest.Select(word => $"{word.Word} {word.ElapsedMs:N0} ms"))}"
        : string.Empty;

    /// <summary>Whether the Overview response contains its stored grammar warning summary.</summary>
    public bool HasWarningSummary => Overview?.Warnings is not null;

    /// <summary>The warning count returned by the Overview command or its empty state.</summary>
    public string WarningsCount => Overview?.Warnings?.Count is { } count ? $"{count:N0} findings" : "Not recorded";

    /// <summary>The warning findings that were left out of the grammar.</summary>
    public string WarningsLeftOut => Overview?.Warnings?.WarningCount is { } count
        ? $"{count:N0} left out of the grammar" : string.Empty;

    /// <summary>The error and informational findings and largest kind returned by the Overview command.</summary>
    public string WarningsDetails => Overview?.Warnings is not { } warnings ? "No warning summary is available."
        : warnings.Count is null ? "No findings count was recorded."
        : FormatWarningDetails(warnings);

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

    private static string FormatWarningDetails(OverviewWarningsSummary warnings)
    {
        var parts = new List<string>();
        if (warnings.ErrorCount is { } errorCount)
            parts.Add($"{errorCount:N0} {(errorCount == 1 ? "error" : "errors")}");
        if (warnings.InformationCount is { } informationCount)
            parts.Add($"{informationCount:N0} worth a look");
        if (warnings.LargestKind is { } kind)
            parts.Add($"Largest kind: {kind}" +
                (warnings.LargestKindCount is { } largestCount ? $" ({largestCount:N0})" : string.Empty));
        return parts.Count == 0 ? "No additional warning details are available." : string.Join(" · ", parts);
    }

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
        if (e.PropertyName is nameof(WorkspaceContext.ProjectPath))
        {
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectDetails));
        }
    }

    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.IsStale)) OnPropertyChanged(nameof(OverviewIsStale));
    }

    private static string FormatTime(DateTimeOffset? value) => value is { } at
        ? at.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture) : "not recorded";

    private static string FormatMilliseconds(double? value) => value is { } milliseconds
        ? $"{milliseconds:N1} ms" : "not recorded";

    private static string ShortFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "not recorded";
        if (value.StartsWith("sha256:", StringComparison.Ordinal)) value = value[7..];
        return value.Length <= 8 ? value : $"{value[..8]}…";
    }
}
