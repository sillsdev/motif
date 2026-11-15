using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns the saved-project context for the word input and the displayed trace result.</summary>
public sealed partial class WordTraceSessionViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private bool _stopping;

    public WordTraceSessionViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Diagnostics = new DiagnosticToolsViewModel(Trace, context.Clipboard, context.DiagnosticFiles,
            context.DiagnosticDialogs);
        Trace.PropertyChanged += (_, e) =>
        {
            if (_stopping) return;
            if (e.PropertyName == nameof(TraceWordViewModel.WordToTry)) _ = RefreshExpectedAsync(Trace.WordToTry);
            else if (e.PropertyName == nameof(TraceWordViewModel.Result))
            {
                _ = RefreshResultAsync();
                NotifyResultPresentation();
            }
        };
        context.Assess.Words.PropertyChanged += (_, e) =>
        {
            if (_stopping) return;
            if (e.PropertyName is nameof(AssessWordsViewModel.TotalCount) or nameof(AssessWordsViewModel.SelectedRow))
                _ = RefreshExpectedAsync(Trace.WordToTry);
        };
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectEvidence.Baseline) ||
                e.PropertyName == nameof(ProjectEvidence.IsStale) && context.Evidence.IsStale) Clear();
            if (e.PropertyName is nameof(ProjectEvidence.Baseline) or nameof(ProjectEvidence.WordContextIsStale))
                OnPropertyChanged(nameof(IsTraceBaselineStale));
        };
        if (!string.IsNullOrWhiteSpace(Trace.WordToTry)) _ = RefreshExpectedAsync(Trace.WordToTry);
        if (Trace.Result is not null) _ = RefreshResultAsync();
    }

    private CancellationTokenSource? _expectedCancellation;
    private CancellationTokenSource? _resultCancellation;
    private readonly List<Task> _reads = [];
    private int _expectedGeneration;
    private int _resultGeneration;

    /// <summary>The shared trace whose input and result the stored context describes.</summary>
    public TraceWordViewModel Trace => _context.Assess.Trace;

    /// <summary>File and clipboard tools for the shared trace.</summary>
    public DiagnosticToolsViewModel Diagnostics { get; }

    /// <summary>Raised when a saved diagnostic or its refusal is ready for its own window.</summary>
    public event Action<OpenedDiagnostic>? SavedDiagnosticOpened;

    /// <summary>Reads a saved diagnostic without replacing the shared trace.</summary>
    public Task OpenSavedDiagnosticAsync() => SavedDiagnosticOpener.OpenFromPickerAsync(
        _context.DiagnosticFiles,
        trace => SavedDiagnosticOpened?.Invoke(new OpenedDiagnostic(trace, null)),
        refusal => SavedDiagnosticOpened?.Invoke(new OpenedDiagnostic(new TraceWordViewModel(), refusal)));

    /// <summary>The stored context for the editable word, or null when unavailable.</summary>
    [ObservableProperty]
    private WordContextResponse? _wordContext;

    /// <summary>The stored context for the displayed trace result, or null when unavailable.</summary>
    [ObservableProperty]
    private WordContextResponse? _resultWordContext;

    /// <summary>The returned membership or availability state, without treating a missing row as absence.</summary>
    public string FieldWorksContextStatus => ResultWordContext switch
    {
        null or { HasBaseline: false } => MotifAppStrings.TryWordContextUnavailable,
        { IsInFieldWorks: null } => MotifAppStrings.TryWordMembershipUnrecorded,
        { IsInFieldWorks: false } => MotifAppStrings.NotInFieldWorks,
        { Analyses.Count: 0 } => MotifAppStrings.TryWordNoStoredAnalyses,
        _ => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TryWordStoredAnalysesForFormat, ResultWordContext.Word),
    };

    /// <summary>Each stored analysis and its own recorded opinion, preserving the shared record order.</summary>
    public IReadOnlyList<TryWordStoredAnalysis> FieldWorksAnalyses { get; private set; } = [];
    /// <summary>Whether the comparison has stored FieldWorks analyses to display.</summary>
    public bool HasFieldWorksAnalyses => FieldWorksAnalyses.Count > 0;

    /// <summary>The distinct opinions actually stored for the returned word.</summary>
    public IReadOnlyList<TryWordOpinion> ResultOpinions => (ResultWordContext?.Analyses ?? []).Select(analysis => WindowWords.OpinionOf(analysis.StoredAnalysisOpinion))
        .Distinct().Select(opinion => new TryWordOpinion(opinion, string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.CompareHeldInFieldWorksFormat, WindowWords.Of(opinion)))).ToArray();

    /// <summary>A comparison meaning only when the trace establishes a completed search with no analysis.</summary>
    private (string Word, MeaningTone Tone) ResultComparison => Trace.Result is { Parsed: false, Complete: true, InvalidShape: false } &&
        ResultWordContext is { HasBaseline: true, IsInFieldWorks: not null } context
        ? WindowWords.MeaningOf(Standing(context), ParserOutcome.NoParse)
        : (string.Empty, MeaningTone.Neutral);

    public ParserRefusal? LastParseRefusal => Trace.IsStandalone || Trace.Result is not { } result ? null
        : _context.Evidence.Words.FirstOrDefault(word => string.Equals(
            word.Word.Normalize(System.Text.NormalizationForm.FormD),
            result.Word.Normalize(System.Text.NormalizationForm.FormD), StringComparison.Ordinal)) is { } word
            ? ParserRefusals.Of(word.Morphology, word.Outcome) : null;

    public string LastParseRefusalReason => LastParseRefusal?.Reason ?? string.Empty;
    public bool HasLastParseRefusal => LastParseRefusal is not null;

    public string ResultMeaning => HasLastParseRefusal ? string.Empty : ResultComparison.Word;
    public Mark ResultMeaningMark => Mark.Of(ResultComparison.Tone);

    private bool? SameAsFieldWorks
    {
        get
        {
            if (Trace.Result is not { Complete: true, Parsed: true } || Trace.Reading is not { } reading ||
                ResultWordContext is not { Analyses.Count: > 0 } context ||
                context.Analyses.Any(analysis => analysis.Identity is null || analysis.StoredAnalysisId is null) ||
                reading.Analyses.Count == 0 || reading.Analyses.Any(analysis => analysis.ProjectionStatus != "available" ||
                    analysis.Morphs.Any(morph => morph.IdentityQuality != "authored" ||
                        !Guid.TryParse(morph.FormId, out _) || !Guid.TryParse(morph.MsaId, out _)))) return null;
            var actual = reading.Analyses.Select(analysis => new ParseAnalysis(analysis.Morphs.Select(morph =>
                new ParseMorph(morph.FormId, morph.MsaId, morph.InflTypeId, morph.GuessedString)).ToArray())).ToArray();
            var comparison = CompareSemantics.Compare(new CompareWordFacts(Standing(context), "analysed", false,
                new ParseWordEvidence("trace", 0, reading.Word, 0, false, false, false, actual, []), null, 0)
            { StoredAnalyses = context.Analyses, StoredAnalysesAvailable = true });
            return comparison.Outcome == WordRowOutcome.Same;
        }
    }

    public string ResultOutcomeText => HasLastParseRefusal
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TryWordRefusalInLastParseFormat, ParserRefusals.Title) : SameAsFieldWorks is { } same ? same ? MotifAppStrings.OutcomeSame : MotifAppStrings.OutcomeDifferent
        : Trace.Result is { Complete: true, Parsed: true }
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TryWordParsedFormat, PluralText.Format(MotifAppStrings.TryWordAnalysesPattern, Trace.Analyses.Count)) : Trace.AnswerText;
    public Mark ResultOutcomeMark => HasLastParseRefusal ? Mark.ParserRefusal : SameAsFieldWorks is { } same ? same ? Mark.Same : Mark.Different : Trace.AnswerMark;

    private static string Standing(WordContextResponse context) => !context.IsInFieldWorks!.Value
        ? ProjectStanding.NotPresent
        : context.Analyses.Any(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved) ? ProjectStanding.Approved
        : context.Analyses.Any(analysis => WindowWords.OpinionOf(analysis.StoredAnalysisOpinion) == OpinionMarkKind.Unknown)
            ? ProjectStanding.Candidate
        : context.Analyses.Count > 0 ? ProjectStanding.Rejected : ProjectStanding.NotPresent;

    /// <summary>The returned trace's grammar capture, never borrowed from the current workspace.</summary>
    public string TracedGrammarText => Trace.Result?.HostCapture?.Baseline is { } baseline
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TryWordTracedWithBaselineFormat,
            WindowTimeText.Format(DateTimeOffset.Parse(baseline.Token.CapturedUtc, CultureInfo.InvariantCulture), _context.Clock))
        : Trace.Result?.GrammarSource is { Length: > 0 } ? MotifAppStrings.TryWordTracedSavedGrammar
        : MotifAppStrings.TryWordGrammarNotRecorded;

    /// <summary>Whether the live FieldWorks save is newer than the Baseline used by this trace.</summary>
    public bool IsTraceBaselineStale => Trace.Result?.HostCapture?.Baseline?.Token is { } traced &&
        _context.Evidence is { WordContextIsStale: true, Baseline.Token: { } baseline } &&
        traced.HasSameSemanticIdentity(baseline);

    partial void OnResultWordContextChanged(WordContextResponse? value)
    {
        FieldWorksAnalyses = value is { HasBaseline: true, IsInFieldWorks: true }
            ? value.Analyses.GroupBy(reading => System.Text.Json.JsonSerializer.Serialize(reading.Morphs.Select(morph =>
                new { morph.Form, morph.Gloss, morph.Category, morph.InflectionType })), StringComparer.Ordinal)
                .Select(group => new TryWordStoredAnalysis(group.ToArray(), Trace.Reading)).ToArray()
            : [];
        NotifyResultPresentation();
    }

    private void NotifyResultPresentation()
    {
        OnPropertyChanged(nameof(FieldWorksContextStatus));
        OnPropertyChanged(nameof(FieldWorksAnalyses));
        OnPropertyChanged(nameof(HasFieldWorksAnalyses));
        OnPropertyChanged(nameof(ResultOpinions));
        OnPropertyChanged(nameof(ResultMeaning));
        OnPropertyChanged(nameof(ResultMeaningMark));
        OnPropertyChanged(nameof(LastParseRefusal));
        OnPropertyChanged(nameof(LastParseRefusalReason));
        OnPropertyChanged(nameof(HasLastParseRefusal));
        OnPropertyChanged(nameof(ResultOutcomeText));
        OnPropertyChanged(nameof(ResultOutcomeMark));
        OnPropertyChanged(nameof(TracedGrammarText));
        OnPropertyChanged(nameof(IsTraceBaselineStale));
    }

    /// <summary>Refreshes the expected reading for the editable word.</summary>
    public Task RefreshExpectedAsync(string word, CancellationToken cancellationToken = default)
    {
        var generation = ++_expectedGeneration;
        _expectedCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _expectedCancellation = cancellation;
        word = word.Trim().Normalize(System.Text.NormalizationForm.FormD);
        WordContext = null;
        _context.Assess.Trace.SetExpected(word, null);
        return Track(ReadExpectedAsync(word, _context.ProjectPath, generation, cancellation));
    }

    /// <summary>Refreshes the stored context for the current trace result.</summary>
    public Task RefreshResultAsync()
    {
        var generation = ++_resultGeneration;
        _resultCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _resultCancellation = cancellation;
        ResultWordContext = null;
        return Track(ReadResultAsync(_context.ProjectPath, generation, cancellation));
    }

    /// <summary>Clears both contexts and invalidates any pending reads.</summary>
    public void Clear()
    {
        _expectedGeneration++;
        _resultGeneration++;
        _expectedCancellation?.Cancel();
        _resultCancellation?.Cancel();
        WordContext = null;
        ResultWordContext = null;
        Trace.SetExpected(Trace.WordToTry, null);
    }

    /// <summary>Cancels and joins context reads before the project is released.</summary>
    public async Task StopAsync()
    {
        _stopping = true;
        Clear();
        await Task.WhenAll(_reads.Append(Trace.StopAsync())).ConfigureAwait(true);
        _reads.Clear();
    }

    /// <summary>Drops the trace and its stored context when the project changes.</summary>
    public void ClearProject()
    {
        Clear();
        Trace.Reset();
        Trace.WordToTry = string.Empty;
        _stopping = false;
    }

    private SIL.Motif.Contract.Baselines.BaselineToken? CurrentBaseline() =>
        _context.Baseline?.Token ?? _context.Evidence.Stored?.Baseline?.Token ??
        _context.Evidence.Assessment?.Assessment.Baseline.Token;

    private Task Track(Task read)
    {
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        _reads.Add(read);
        return read;
    }

    private async Task ReadExpectedAsync(string word, string? projectPath, int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (projectPath is null || word.Length == 0) return;
            var baseline = CurrentBaseline();
            var result = await _context.Commands.ReadWordContextAsync(new WordContextRequest(projectPath, word, baseline),
                cancellation.Token).ConfigureAwait(true);
            if (generation != _expectedGeneration || cancellation.IsCancellationRequested || projectPath != _context.ProjectPath ||
                baseline != CurrentBaseline()) return;
            WordContext = result.Succeeded && (baseline is null || result.Value?.Baseline == baseline)
                ? result.Value : null;
            if (WordContext is not null) _context.Evidence.ObserveWordContext(WordContext);
            _context.Assess.Trace.SetExpected(word, WordContext?.ExpectedAnalysis?.Morphs.Select(morph =>
                new ParserReadingMorphViewModel(morph)).ToArray());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_expectedCancellation, cancellation)) _expectedCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ReadResultAsync(string? projectPath, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var trace = _context.Assess.Trace.Result;
            if (trace is null || projectPath is null) return;
            var current = CurrentBaseline();
            var baseline = trace.HostCapture?.Baseline?.Token ?? current;
            if (current is not null && baseline != current) return;
            var result = await _context.Commands.ReadWordContextAsync(new WordContextRequest(projectPath, trace.Word, baseline),
                cancellation.Token).ConfigureAwait(true);
            if (generation != _resultGeneration || cancellation.IsCancellationRequested ||
                !ReferenceEquals(trace, _context.Assess.Trace.Result) || projectPath != _context.ProjectPath ||
                current != CurrentBaseline()) return;
            ResultWordContext = result.Succeeded && (baseline is null || result.Value?.Baseline == baseline)
                ? result.Value : null;
            if (ResultWordContext is not null) _context.Evidence.ObserveWordContext(ResultWordContext);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_resultCancellation, cancellation)) _resultCancellation = null;
            cancellation.Dispose();
        }
    }
}
