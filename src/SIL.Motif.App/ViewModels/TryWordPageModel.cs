using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens Try a Word on one word and traces it straight away.</summary>
/// <param name="Word">The word to trace.</param>
public sealed record TryWordRequest(string Word) : PageRequest(WorkspacePage.TryAWord);

/// <summary>The Try a Word page and the links that keep its evidence in the shared workspace.</summary>
public sealed class TryWordPageModel : PageModel
{
    private const int RecentWordLimit = 5;
    private const int EarlierRuleLimit = 5;
    private int _resultContextGeneration;
    private CancellationTokenSource? _resultContextCancellation;
    private int _timingGeneration;
    private string? _assessmentId;
    private TimingResponse? _earlierTiming;
    private IReadOnlyList<string> _timingOverrideAssessmentIds = [];
    private int _wordContextGeneration;
    private CancellationTokenSource? _wordContextCancellation;

    public TryWordPageModel(WorkspaceContext context) : base(context)
    {
        Trace = context.Assess.Trace;
        Diagnostics = new DiagnosticToolsViewModel(Trace, context.Clipboard, context.DiagnosticFiles,
            context.DiagnosticDialogs);
        Trace.PropertyChanged += OnTracePropertyChanged;
        OpenInTextsCommand = new RelayCommand(() =>
        {
            if (Trace.Result is { } result) Context.OpenWord(result.Word);
        }, CanOpenResultWord);
        HandOffCommand = new RelayCommand(() =>
        {
            if (Trace.Result is { } result) Context.HandOff([result.Word], result);
        }, CanOpenResultWord);
        OpenTimingCommand = new RelayCommand(OpenTimingForResultWord, CanOpenResultWord);
        OpenRecentWordCommand = new RelayCommand<string?>(word =>
        {
            if (!string.IsNullOrWhiteSpace(word)) Context.TryWord(word);
        });
        Context.Assess.Words.PropertyChanged += OnWordsPropertyChanged;
        RefreshExpected(Trace.WordToTry);
    }

    /// <summary>The shared trace the Texts page also primes when a Results word is chosen.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>All stored opinions for the typed word; null while the Baseline read is unavailable or pending.</summary>
    public WordContextResponse? WordContext { get; private set; }

    /// <summary>The shared FieldWorks record for the displayed result, independent of the editable input.</summary>
    public WordContextResponse? ResultWordContext { get; private set; }

    /// <summary>The returned membership or availability state, without treating a missing row as absence.</summary>
    public string FieldWorksContextStatus => ResultWordContext switch
    {
        null or { HasBaseline: false } => "FieldWorks word context unavailable",
        { IsInFieldWorks: null } => "FieldWorks membership not recorded",
        { IsInFieldWorks: false } => "Not in FieldWorks",
        { Analyses.Count: 0 } => "No stored analyses in the captured FieldWorks Baseline",
        _ => "Stored analyses for " + ResultWordContext.Word,
    };

    /// <summary>Each stored analysis and its own recorded opinion, preserving the shared record order.</summary>
    public IReadOnlyList<TryWordStoredAnalysis> FieldWorksAnalyses { get; private set; } = [];

    /// <summary>The distinct opinions actually stored for the returned word.</summary>
    public IReadOnlyList<TryWordOpinion> ResultOpinions => (ResultWordContext?.Analyses ?? []).Select(analysis => WindowWords.OpinionOf(analysis.StoredAnalysisOpinion))
        .Distinct().Select(opinion => new TryWordOpinion(opinion, WindowWords.Of(opinion) + " in FieldWorks")).ToArray();

    /// <summary>A comparison meaning only when the trace establishes a completed search with no analysis.</summary>
    private (string Word, MeaningTone Tone) ResultComparison => Trace.Result is { Parsed: false, Complete: true, InvalidShape: false } &&
        ResultWordContext is { HasBaseline: true, IsInFieldWorks: not null } context
        ? WindowWords.MeaningOf(Standing(context), ParserOutcome.NoParse)
        : (string.Empty, MeaningTone.Neutral);

    public ParserRefusal? LastParseRefusal => Trace.IsStandalone || Trace.Result is not { } result ? null
        : Context.Evidence.Words.FirstOrDefault(word => string.Equals(
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
        ? ParserRefusals.Title + " in last parse" : SameAsFieldWorks is { } same ? same ? "Same" : "Different"
        : Trace.Result is { Complete: true, Parsed: true }
            ? $"Parsed · {Trace.Analyses.Count} {(Trace.Analyses.Count == 1 ? "analysis" : "analyses")}" : Trace.AnswerText;
    public Mark ResultOutcomeMark => HasLastParseRefusal ? Mark.ParserRefusal : SameAsFieldWorks is { } same ? same ? Mark.Same : Mark.Different : Trace.AnswerMark;

    private static string Standing(WordContextResponse context) => !context.IsInFieldWorks!.Value
        ? ProjectStanding.NotPresent
        : context.Analyses.Any(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved) ? ProjectStanding.Approved
        : context.Analyses.Any(analysis => WindowWords.OpinionOf(analysis.StoredAnalysisOpinion) == OpinionMarkKind.Unknown)
            ? ProjectStanding.Candidate
        : context.Analyses.Count > 0 ? ProjectStanding.Rejected : ProjectStanding.NotPresent;

    /// <summary>The returned trace's grammar capture, never borrowed from the current workspace.</summary>
    public string TracedGrammarText => Trace.Result?.HostCapture?.Baseline is { } baseline
        ? "Traced with the grammar of the Baseline from " + WindowTimeText.Format(DateTimeOffset.Parse(
            baseline.Token.CapturedUtc, CultureInfo.InvariantCulture), Context.Clock)
        : Trace.Result?.GrammarSource is { Length: > 0 } ? "Traced with a saved grammar; its Baseline was not recorded."
        : "The grammar of this trace was not recorded.";

    /// <summary>Different recorded project states, without inferring that their grammars or outcomes differ.</summary>
    public string TraceBaselineWarning => Trace.Result?.HostCapture?.Baseline?.Token is { } traced &&
        _earlierTiming?.Baseline is { } measured && !traced.HasSameSemanticIdentity(measured) &&
        _earlierTiming.Words.Any(word => word.Word == Trace.Result.Word && word.Origin is not null)
        ? "This trace and the earlier parse used different Baselines. Refresh and try again." : string.Empty;

    /// <summary>The diagnostic tools beside the page's trace.</summary>
    public DiagnosticToolsViewModel Diagnostics { get; }

    /// <summary>
    /// Raised with a diagnostic <see cref="OpenSavedDiagnosticAsync"/> read, or with an empty trace and why the
    /// chosen file could not be shown; either way it belongs in a window of its own.
    /// </summary>
    public event Action<OpenedDiagnostic>? SavedDiagnosticOpened;

    /// <summary>
    /// Asks for a saved diagnostic and raises <see cref="SavedDiagnosticOpened"/> for it, leaving the page's own
    /// trace as it was, whether the file was shown or refused.
    /// </summary>
    public Task OpenSavedDiagnosticAsync() => SavedDiagnosticOpener.OpenFromPickerAsync(
        Context.DiagnosticFiles,
        trace => SavedDiagnosticOpened?.Invoke(new OpenedDiagnostic(trace, null)),
        refusal => SavedDiagnosticOpened?.Invoke(new OpenedDiagnostic(new TraceWordViewModel(), refusal)));

    /// <summary>Recently traced words, newest first.</summary>
    public ObservableCollection<string> RecentWords { get; } = [];

    /// <summary>
    /// The traced word's rule times from the earlier parse named in <see cref="EarlierTimingTitle"/>, then the time
    /// that parse recorded against no rule; empty when no parse can be named as their source.
    /// </summary>
    public ObservableCollection<TryWordEarlierRuleTime> EarlierRuleTimes { get; } = [];

    /// <summary>Whether timing from a named earlier parse is shown beside this try.</summary>
    public bool HasEarlierTiming => EarlierRuleTimes.Count > 0;

    /// <summary>Which parse the earlier timing comes from, by when it ran.</summary>
    public string EarlierTimingTitle { get; private set; } = string.Empty;

    /// <summary>That the earlier timing is not this try's, and what its rule times cover.</summary>
    public string EarlierTimingSource { get; private set; } = string.Empty;

    /// <summary>The share column's heading, naming the word's whole time it divides by; empty without one.</summary>
    public string EarlierShareHeader { get; private set; } = string.Empty;

    /// <summary>Whether the earlier parse kept the word's whole time, so its rule times have shares.</summary>
    public bool HasEarlierShares => EarlierShareHeader.Length > 0;

    /// <summary>The overrun the earlier parse recorded when its object timers exceeded the word's time; else empty.</summary>
    public string EarlierOverrunText { get; private set; } = string.Empty;

    /// <summary>Opens Texts on the current word, even when the word is absent from its list.</summary>
    public IRelayCommand OpenInTextsCommand { get; }

    /// <summary>Opens AI Handoff with only the current word selected.</summary>
    public IRelayCommand HandOffCommand { get; }

    /// <summary>Opens Timing for the one word being traced.</summary>
    public IRelayCommand OpenTimingCommand { get; }

    /// <summary>Traces a recent word again.</summary>
    public IRelayCommand<string?> OpenRecentWordCommand { get; }

    protected override async Task OnStopWorkAsync()
    {
        await Trace.StopAsync().ConfigureAwait(true);
        _wordContextGeneration++;
        _wordContextCancellation?.Cancel();
        _wordContextCancellation?.Dispose();
        _wordContextCancellation = null;
        _resultContextGeneration++;
        _resultContextCancellation?.Cancel();
        _resultContextCancellation?.Dispose();
        _resultContextCancellation = null;
        _timingGeneration++;
    }

    protected override void OnProjectCleared()
    {
        _wordContextGeneration++;
        _wordContextCancellation?.Cancel();
        WordContext = null;
        OnPropertyChanged(nameof(WordContext));
        _resultContextGeneration++;
        _resultContextCancellation?.Cancel();
        ShowResultContext(null);
        _assessmentId = null;
        _timingOverrideAssessmentIds = [];
        _timingGeneration++;
        ShowEarlierTiming(null);
        Trace.Reset();
        Trace.WordToTry = string.Empty;
        RecentWords.Clear();
    }

    protected override Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        SetStoredAssessmentId(evidence.ParseTimeAssessmentId, evidence.TimingOverrideAssessmentIds);
        return Task.WhenAll(RefreshExpectedAsync(Trace.WordToTry, cancellationToken), RefreshResultContextAsync());
    }

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(RefreshExpectedAsync(Trace.WordToTry, cancellationToken), RefreshResultContextAsync());

    protected override void OnRequested(PageRequest request)
    {
        if (request is not TryWordRequest tried) return;
        Context.Assess.SelectWord(tried.Word);
        Trace.SetWord(tried.Word);
        RefreshExpected(tried.Word);
        OpenInTextsCommand.NotifyCanExecuteChanged();
        HandOffCommand.NotifyCanExecuteChanged();
        OpenTimingCommand.NotifyCanExecuteChanged();
        if (!Trace.TryCommand.CanExecute(null)) return;
        TrackRecent(tried.Word);
        _ = Trace.TryCommand.ExecuteAsync(null);
    }

    private bool CanOpenResultWord() => !string.IsNullOrWhiteSpace(Trace.Result?.Word);

    private void OpenTimingForResultWord()
    {
        if (Trace.Result is { } result) Context.OpenTiming([result.Word], null);
    }

    private void OnTracePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TraceWordViewModel.WordToTry))
            RefreshExpected(Trace.WordToTry);
        else if (e.PropertyName == nameof(TraceWordViewModel.Result))
        {
            OnPropertyChanged(nameof(TracedGrammarText));
            _timingGeneration++;
            ShowEarlierTiming(null);
            if (Trace.Result is not null) _ = LoadStoredRuleTimingsAsync();
            _ = RefreshResultContextAsync();
            OpenInTextsCommand.NotifyCanExecuteChanged();
            HandOffCommand.NotifyCanExecuteChanged();
            OpenTimingCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(TraceWordViewModel.IsLoading) && Trace.IsLoading)
            TrackRecent(Trace.WordToTry.Trim());
    }

    private void OnWordsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssessWordsViewModel.TotalCount) or nameof(AssessWordsViewModel.SelectedRow))
            RefreshExpected(Trace.WordToTry);
    }

    private void RefreshExpected(string word) => _ = RefreshExpectedAsync(word, CancellationToken.None);

    private async Task RefreshExpectedAsync(string word, CancellationToken cancellationToken)
    {
        var generation = ++_wordContextGeneration;
        _wordContextCancellation?.Cancel();
        _wordContextCancellation?.Dispose();
        _wordContextCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _wordContextCancellation.Token;
        var projectPath = Context.ProjectPath;
        word = word.Trim().Normalize(System.Text.NormalizationForm.FormD);
        WordContext = null;
        Trace.SetExpected(word, null);
        OnPropertyChanged(nameof(WordContext));
        if (projectPath is null || word.Length == 0) return;
        var baseline = Context.Evidence.Stored?.Baseline?.Token ?? Context.Evidence.Assessment?.Assessment.Baseline.Token;
        try
        {
            var result = await Context.Commands.ReadWordContextAsync(new WordContextRequest(projectPath, word, baseline),
                token).ConfigureAwait(true);
            if (generation != _wordContextGeneration || token.IsCancellationRequested || projectPath != Context.ProjectPath)
                return;
            WordContext = result.Succeeded ? result.Value : null;
            if (WordContext is not null) Context.Evidence.ObserveWordContext(WordContext);
            Trace.SetExpected(word, WordContext?.ExpectedAnalysis?.Morphs.Select(morph =>
                new ParserReadingMorphViewModel(morph)).ToArray());
            OnPropertyChanged(nameof(WordContext));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshResultContextAsync()
    {
        var generation = ++_resultContextGeneration;
        _resultContextCancellation?.Cancel();
        _resultContextCancellation?.Dispose();
        _resultContextCancellation = new CancellationTokenSource();
        var token = _resultContextCancellation.Token;
        var result = Trace.Result;
        var projectPath = Context.ProjectPath;
        ShowResultContext(null);
        if (result is null || projectPath is null) return;
        var baseline = Context.Evidence.Stored?.Baseline?.Token ?? Context.Evidence.Assessment?.Assessment.Baseline.Token;
        try
        {
            var outcome = await Context.Commands.ReadWordContextAsync(new WordContextRequest(projectPath, result.Word,
                baseline), token).ConfigureAwait(true);
            if (generation != _resultContextGeneration || token.IsCancellationRequested ||
                !ReferenceEquals(result, Trace.Result) || projectPath != Context.ProjectPath) return;
            ShowResultContext(outcome.Succeeded ? outcome.Value : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ShowResultContext(WordContextResponse? context)
    {
        ResultWordContext = context;
        if (context is not null) Context.Evidence.ObserveWordContext(context);
        FieldWorksAnalyses = context is { HasBaseline: true, IsInFieldWorks: true }
            ? context.Analyses.GroupBy(reading => System.Text.Json.JsonSerializer.Serialize(reading.Morphs.Select(morph =>
                new { morph.Form, morph.Gloss, morph.Category, morph.InflectionType })), StringComparer.Ordinal)
                .Select(group => new TryWordStoredAnalysis(group.ToArray(), Trace.Reading)).ToArray()
            : [];
        OnPropertyChanged(nameof(ResultWordContext));
        OnPropertyChanged(nameof(FieldWorksContextStatus));
        OnPropertyChanged(nameof(FieldWorksAnalyses));
        OnPropertyChanged(nameof(ResultOpinions));
        OnPropertyChanged(nameof(ResultMeaning));
        OnPropertyChanged(nameof(ResultMeaningMark));
        OnPropertyChanged(nameof(LastParseRefusal));
        OnPropertyChanged(nameof(LastParseRefusalReason));
        OnPropertyChanged(nameof(HasLastParseRefusal));
        OnPropertyChanged(nameof(ResultOutcomeText));
        OnPropertyChanged(nameof(ResultOutcomeMark));
    }

    private void TrackRecent(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        var prior = RecentWords.IndexOf(word);
        if (prior >= 0) RecentWords.RemoveAt(prior);
        RecentWords.Insert(0, word);
        while (RecentWords.Count > RecentWordLimit) RecentWords.RemoveAt(RecentWords.Count - 1);
    }

    private void SetStoredAssessmentId(string? assessmentId, IReadOnlyList<string> timingOverrideAssessmentIds)
    {
        _assessmentId = assessmentId;
        _timingOverrideAssessmentIds = timingOverrideAssessmentIds.ToArray();
        _timingGeneration++;
        ShowEarlierTiming(null);
        if (assessmentId is not null && Trace.Result is not null) _ = LoadStoredRuleTimingsAsync();
    }

    private async Task LoadStoredRuleTimingsAsync()
    {
        var result = Trace.Result;
        var projectPath = Context.ProjectPath;
        var assessmentId = _assessmentId;
        if (result is null || projectPath is null || assessmentId is null) return;

        var generation = ++_timingGeneration;
        var outcome = await Context.Commands.TimingAsync(
            new TimingRequest(projectPath, assessmentId, By: "rule", ExplicitWords: [result.Word],
                OverrideAssessmentIds: _timingOverrideAssessmentIds),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _timingGeneration || !ReferenceEquals(result, Trace.Result) ||
            !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;

        ShowEarlierTiming(outcome.Succeeded ? outcome.Value : null);
    }

    private void ShowEarlierTiming(TimingResponse? timing)
    {
        _earlierTiming = timing;
        OnPropertyChanged(nameof(TraceBaselineWarning));
        EarlierRuleTimes.Clear();
        EarlierTimingTitle = EarlierTimingSource = EarlierShareHeader = EarlierOverrunText = string.Empty;
        if (timing is { Aggregates.Count: > 0 } && Trace.Result is { } result &&
            timing.Words.FirstOrDefault(word => word.Word == result.Word)?.Origin is { } origin)
        {
            var wordMs = timing.Words.FirstOrDefault(word => word.Word == result.Word)?.ElapsedNs is >= 0 and var ns
                ? ns / 1_000_000d : timing.Attribution.MeasuredWordCount > 0 ? (double?)timing.Attribution.WordTimeMs : null;
            static string ShareOf(double? share) => share?.ToString("P0", CultureInfo.CurrentCulture) ?? string.Empty;
            foreach (var rule in timing.Aggregates.Take(EarlierRuleLimit))
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime(rule.Name, TimingShare.KindName(rule.Kind),
                    SpeedText.PerWord(rule.SelfMs), ShareOf(rule.ShareOfWordTime), IsOtherTime: false));
            if (timing.Aggregates.Skip(EarlierRuleLimit).ToArray() is { Length: > 0 } rest)
            {
                var restMs = rest.Sum(rule => rule.SelfMs);
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime(SpeedText.Count(rest.Length, "other rule", "other rules"),
                    string.Empty, SpeedText.PerWord(restMs),
                    ShareOf(rest.All(rule => rule.ShareOfWordTime is not null) ? rest.Sum(rule => rule.ShareOfWordTime) : null),
                    IsOtherTime: false));
            }
            if (timing.Attribution.NotAttributedMs is { } other && other >= TimingShare.SmallestShownMs)
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime("Not attributed", string.Empty,
                    SpeedText.PerWord(other), ShareOf(timing.Attribution.NotAttributedShare), IsOtherTime: true));
            EarlierTimingTitle = $"Time from the parse of {origin.MeasuredUtc.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)}";
            EarlierTimingSource = "Not from this traced search. " + (wordMs is { } took
                ? $"In that parse {result.Word} took {SpeedText.PerWord(took)}; each rule's time covers that parse's " +
                    "whole search for the word"
                : $"Each rule's time covers that parse's whole search for {result.Word}") +
                ", including attempts that stopped.";
            EarlierShareHeader = timing.Aggregates.Any(rule => rule.ShareOfWordTime is not null)
                ? wordMs is { } whole ? $"Share of {SpeedText.PerWord(whole)}" : "Word share" : string.Empty;
            if (timing.Attribution.Overrun)
                EarlierOverrunText = $"Recorded object timers exceeded word time by {SpeedText.PerWord(timing.Attribution.OverrunMs)}.";
        }
        OnPropertyChanged(nameof(HasEarlierTiming));
        OnPropertyChanged(nameof(EarlierTimingTitle));
        OnPropertyChanged(nameof(EarlierTimingSource));
        OnPropertyChanged(nameof(EarlierShareHeader));
        OnPropertyChanged(nameof(HasEarlierShares));
        OnPropertyChanged(nameof(EarlierOverrunText));
    }
}

/// <summary>One rule's time for the traced word in an earlier parse, or that parse's time against no rule.</summary>
/// <param name="Rule">The rule's name, or what the row gathers.</param>
/// <param name="KindLabel">The rule's kind as the window names it; empty for a gathered row.</param>
/// <param name="TimeText">The time the earlier parse recorded.</param>
/// <param name="ShareText">Its share of the word's whole time in that parse; empty when that time was not kept.</param>
/// <param name="IsOtherTime">Whether this is the time the parser recorded against no rule.</param>
public sealed record TryWordEarlierRuleTime(string Rule, string KindLabel, string TimeText, string ShareText,
    bool IsOtherTime)
{
    /// <summary>The recorded timing kind, absent for rows that gather several kinds or record no object time.</summary>
    public string? KindTip => KindLabel.Length > 0 ? KindLabel : null;
}

/// <summary>One stored analysis, with its FieldWorks opinion rendered in the window's words.</summary>
public sealed class TryWordStoredAnalysis
{
    public TryWordStoredAnalysis(IReadOnlyList<ParserReading> readings, WordTraceReading? trace)
    {
        var reading = readings[0];
        Opinion = WindowWords.OpinionOf(reading.StoredAnalysisOpinion);
        HasMultipleOpinions = readings.Select(item => WindowWords.OpinionOf(item.StoredAnalysisOpinion)).Distinct().Count() > 1;
        OpinionLabel = readings.Count == 1 ? WindowWords.Of(Opinion) : string.Join(" · ", readings
            .GroupBy(item => WindowWords.OpinionOf(item.StoredAnalysisOpinion))
            .OrderBy(group => group.Key).Select(group => $"{WindowWords.Of(group.Key)} {group.Count()}"));
        Text = string.Join(" + ", reading.Morphs.Select(morph => morph.Form));
        Morphs = reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Pieces = reading.Morphs.Select((morph, index) => new TryWordTracePiece(
            readings.Select(item => item.Morphs[index]).ToArray(), trace)).ToArray();
    }

    public OpinionMarkKind Opinion { get; }
    public Mark OpinionMark => HasMultipleOpinions ? Mark.Of(MeaningTone.Neutral) : Mark.Of(Opinion);
    public bool HasMultipleOpinions { get; private set; }
    public string OpinionLabel { get; }
    public string Text { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public IReadOnlyList<TryWordTracePiece> Pieces { get; }
}

/// <summary>A stored piece and only the trace events that name its exact authored identity.</summary>
public sealed class TryWordTracePiece
{
    public TryWordTracePiece(ParserReadingMorph morph, WordTraceReading? trace) : this([morph], trace) { }

    public TryWordTracePiece(IReadOnlyList<ParserReadingMorph> morphs, WordTraceReading? trace)
    {
        Morphs = [new ParserReadingMorphViewModel(morphs[0])];
        var statuses = morphs.Select(morph => StatusFor(morph, trace)).Distinct(StringComparer.Ordinal).ToArray();
        Status = statuses.Length == 1 ? statuses[0] : "Trace marks differ";
        StatusParts = Status switch
        {
            "built · refused" =>
            [
                new(Mark.Of(TraceStepMark.Built), "built", string.Empty),
                new(Mark.Of(TraceStepMark.Refused), "refused", "· "),
            ],
            "refused" => [new(Mark.Of(TraceStepMark.Refused), Status, string.Empty)],
            "built" => [new(Mark.Of(TraceStepMark.Built), Status, string.Empty)],
            "No step recorded" => [new(Mark.Of(TraceStepMark.Tried), Status, string.Empty)],
            _ => [new(null, Status, string.Empty)],
        };
    }

    private static string StatusFor(ParserReadingMorph morph, WordTraceReading? trace)
    {
        var steps = trace is null ? [] : Flatten(trace.Root).Where(step =>
            step.SourceIdentityQuality == "authored" && step.SourceIdentityKind == "morphRule" &&
            Guid.TryParse(step.SourceIdentityId, out var source) &&
            Guid.TryParse(morph.GrammaticalInfoId, out var msa) && source == msa).ToArray();
        var refusal = steps.FirstOrDefault(step => step.FailureReason is { Length: > 0 });
        var built = steps.Any(step => step.OutcomeStatus is "success" or "successful" or "succeeded");
        return built && refusal is not null ? "built · refused" : refusal is not null ? "refused"
            : built ? "built" : steps.Length > 0 ? "Tried" : "No step recorded";
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public string Status { get; }
    /// <summary>The status words and signs that form the visible line.</summary>
    public IReadOnlyList<TryWordTraceStatus> StatusParts { get; }

    private static IEnumerable<TraceStep> Flatten(TraceStep step)
    {
        yield return step;
        foreach (var child in step.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }
}

/// <summary>One trace status word, optionally preceded by its mark and a separator.</summary>
/// <param name="Mark">The sign for the status word, or <see langword="null"/> for plain text.</param>
/// <param name="Text">The status word.</param>
/// <param name="Separator">The visible separator before this status word.</param>
public sealed record TryWordTraceStatus(Mark? Mark, string Text, string Separator);

/// <summary>An opinion with its explicit FieldWorks source for the result line.</summary>
public sealed record TryWordOpinion(OpinionMarkKind Opinion, string Label)
{
    public Mark Mark => Mark.Of(Opinion);
}
