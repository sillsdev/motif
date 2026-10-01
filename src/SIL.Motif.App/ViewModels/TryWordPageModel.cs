using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
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
    private int _timingGeneration;
    private string? _assessmentId;

    public TryWordPageModel(WorkspaceContext context) : base(context)
    {
        Trace = context.Assess.Trace;
        Diagnostics = new DiagnosticToolsViewModel(Trace, context.Clipboard, context.DiagnosticFiles,
            context.DiagnosticDialogs);
        Trace.PropertyChanged += OnTracePropertyChanged;
        OpenInTextsCommand = new RelayCommand(() => Context.OpenWord(Trace.WordToTry.Trim()), CanOpenCurrentWord);
        HandOffCommand = new RelayCommand(() => Context.HandOff([Trace.WordToTry.Trim()]), CanOpenCurrentWord);
        OpenTimingCommand = new RelayCommand(OpenTimingForCurrentWord, CanOpenCurrentWord);
        OpenRecentWordCommand = new RelayCommand<string?>(word =>
        {
            if (!string.IsNullOrWhiteSpace(word)) Context.TryWord(word);
        });
        Context.Assess.Words.PropertyChanged += OnWordsPropertyChanged;
        RefreshExpected(Trace.WordToTry);
    }

    /// <summary>The shared trace the Texts page also primes when a Results word is chosen.</summary>
    public TraceWordViewModel Trace { get; }

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
    /// Unique named rules on the successful or furthest recorded attempt, in building order: outward from the stem,
    /// as a linguist builds the word, not in the order the parser took it apart.
    /// </summary>
    public ObservableCollection<TryWordRuleRowViewModel> RulesOnBestPath { get; } = [];

    /// <summary>
    /// The parser's taking-apart pass on that attempt in one line, its pieces in the word's own order, such as
    /// "Taking the word apart found ma- · tin · -lu"; empty when a step's forms do not split into affix and rest.
    /// </summary>
    public string TakingApartText { get; private set; } = string.Empty;

    public bool HasTakingApart => TakingApartText.Length > 0;

    /// <summary>Whether the current trace has named rules on its successful or furthest attempt.</summary>
    public bool HasRulesOnBestPath => RulesOnBestPath.Count > 0;

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

    /// <summary>The sidebar link to the first named rule on the current trace, or its general Timing link.</summary>
    public string TimingLinkText => RulesOnBestPath.FirstOrDefault() is { } rule
        ? $"See {rule.Rule} in Timing" : "Timing for this word";

    /// <summary>Opens Texts on the current word, even when the word is absent from its list.</summary>
    public IRelayCommand OpenInTextsCommand { get; }

    /// <summary>The sidebar's offer to read the current word in its texts, where its analyses are approved.</summary>
    public string OpenInTextsText => Trace.WordToTry.Trim() is { Length: > 0 } word
        ? $"Open {word} in Analyze texts" : "Open in Analyze texts";

    /// <summary>Opens AI Handoff with only the current word selected.</summary>
    public IRelayCommand HandOffCommand { get; }

    /// <summary>Opens Timing for the one word being traced.</summary>
    public IRelayCommand OpenTimingCommand { get; }

    /// <summary>Traces a recent word again.</summary>
    public IRelayCommand<string?> OpenRecentWordCommand { get; }

    protected override void OnProjectCleared()
    {
        _assessmentId = null;
        _timingGeneration++;
        ShowEarlierTiming(null, null);
        Trace.Reset();
        Trace.WordToTry = string.Empty;
        RecentWords.Clear();
        RebuildRules(null);
    }

    protected override Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        SetStoredAssessmentId(evidence.ParseTimeAssessmentId);
        return Task.CompletedTask;
    }

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

    private bool CanOpenCurrentWord() => !string.IsNullOrWhiteSpace(Trace.WordToTry);

    private void OpenTimingForCurrentWord() => Context.OpenTiming(
        [Trace.WordToTry.Trim()], RulesOnBestPath.FirstOrDefault()?.Rule);

    private void OnTracePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TraceWordViewModel.WordToTry))
        {
            RefreshExpected(Trace.WordToTry);
            OnPropertyChanged(nameof(OpenInTextsText));
            OpenInTextsCommand.NotifyCanExecuteChanged();
            HandOffCommand.NotifyCanExecuteChanged();
            OpenTimingCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(TraceWordViewModel.IsLoading) && Trace.IsLoading)
            TrackRecent(Trace.WordToTry.Trim());
        else if (e.PropertyName == nameof(TraceWordViewModel.Candidates))
        {
            RebuildRules(Trace.Result);
            if (Trace.Result is not null) _ = LoadStoredRuleTimingsAsync();
        }
    }

    private void OnWordsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssessWordsViewModel.TotalCount) or nameof(AssessWordsViewModel.SelectedRow))
            RefreshExpected(Trace.WordToTry);
    }

    private void RefreshExpected(string word)
    {
        Trace.SetExpected(word, Context.Assess.Words.Find(word)?.ExpectedAnalysis?.Morphs);
    }

    private void TrackRecent(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        var prior = RecentWords.IndexOf(word);
        if (prior >= 0) RecentWords.RemoveAt(prior);
        RecentWords.Insert(0, word);
        while (RecentWords.Count > RecentWordLimit) RecentWords.RemoveAt(RecentWords.Count - 1);
    }

    private void SetStoredAssessmentId(string? assessmentId)
    {
        _assessmentId = assessmentId;
        _timingGeneration++;
        ShowEarlierTiming(null, null);
        if (assessmentId is not null && Trace.Result is not null) _ = LoadStoredRuleTimingsAsync();
    }

    private void RebuildRules(WordTraceResponse? result)
    {
        _timingGeneration++;
        ShowEarlierTiming(null, null);
        RulesOnBestPath.Clear();
        TakingApartText = string.Empty;
        if (result is not null)
        {
            var attempt = result.Parsed
                ? Trace.Candidates.FirstOrDefault(candidate => candidate.Succeeded)
                : Trace.ClosestAttempts.FirstOrDefault();
            if (attempt is not null)
            {
                TakingApartText = TakingApart(attempt.Steps);
                foreach (var rule in attempt.Steps
                             .Where(step => !string.IsNullOrWhiteSpace(step.Source))
                             .GroupBy(step => step.Source!, StringComparer.Ordinal)
                             .OrderBy(rule => BuildingPlace(rule, attempt.Steps)))
                {
                    var steps = rule.ToArray();
                    RulesOnBestPath.Add(new TryWordRuleRowViewModel(rule.Key,
                        string.Join(" · ", steps.Select(step => step.KindText).Distinct(StringComparer.Ordinal)),
                        steps.Any(step => step.IsFailure) ? "stopped"
                            : attempt.Succeeded || steps.Any(step => step.IsSuccessful) ? "applied" : "tried",
                        Explain(rule.Key, steps, attempt.Steps),
                        () => Context.OpenTiming([result.Word], rule.Key)));
                }
            }
        }
        OnPropertyChanged(nameof(RulesOnBestPath));
        OnPropertyChanged(nameof(HasRulesOnBestPath));
        OnPropertyChanged(nameof(TakingApartText));
        OnPropertyChanged(nameof(HasTakingApart));
        OnPropertyChanged(nameof(TimingLinkText));
    }

    // Rules the parser rebuilt come in the order it rebuilt them; those only taken off follow, innermost first.
    private static (int Pass, int Place) BuildingPlace(IEnumerable<TraceStepViewModel> rule, IReadOnlyList<TraceStepViewModel> path)
    {
        var places = rule.Select(step => (Step: step, Place: IndexOf(path, step))).ToArray();
        return places.FirstOrDefault(place => !TakesApart(place.Step)) is { Step: not null } built
            ? (0, built.Place)
            : (1, -places.Max(place => place.Place));
    }

    // One plain line per rule: why it stopped the word, else the affix and the form it built, in building order.
    private static string Explain(string rule, IReadOnlyList<TraceStepViewModel> steps, IReadOnlyList<TraceStepViewModel> path)
    {
        if (steps.FirstOrDefault(step => step.IsFailure) is { } failed)
            return failed.ContextualFailure ??
                (failed.FailureReason is { Length: > 0 } code ? TraceStepKinds.ExplainReason(code) : "stopped here");
        // Building runs from the shorter form to the longer, whichever pass recorded the step.
        foreach (var step in steps.OrderBy(TakesApart))
        {
            if (step.Output is not { Length: > 0 } output) continue;
            var input = step.Input is { Length: > 0 } own ? own : FormBefore(step, path);
            if (input is null || string.Equals(input, output, StringComparison.Ordinal)) continue;
            var reversed = input.Length != output.Length ? input.Length > output.Length : TakesApart(step);
            var (before, after) = reversed ? (output, input) : (input, output);
            return $"{AffixForm(before, after) ?? rule} · {before} → {after}";
        }
        return steps.Select(step => step.Output ?? step.Input).FirstOrDefault(text => text is { Length: > 0 }) ?? "—";
    }

    private static string TakingApart(IReadOnlyList<TraceStepViewModel> path)
    {
        var prefixes = new List<string>();
        var suffixes = new List<string>();
        string? stem = null;
        var affixSteps = path.Where(step =>
            TakesApart(step) && step.Type.StartsWith("MorphologicalRule", StringComparison.Ordinal));
        foreach (var step in affixSteps)
        {
            var whole = step.Input is { Length: > 0 } own ? own : FormBefore(step, path);
            if (whole is null || step.Output is not { Length: > 0 } rest || AffixForm(rest, whole) is not { } affix)
                return string.Empty;
            // Each affix comes off the outside, so prefixes arrive left to right and suffixes right to left.
            if (affix.EndsWith('-')) prefixes.Add(affix);
            else suffixes.Insert(0, affix);
            stem = rest;
        }
        return stem is null ? string.Empty : $"Taking the word apart found {string.Join(" · ", [.. prefixes, stem, .. suffixes])}";
    }

    // The affix a step added, written as FieldWorks writes it: "ma-" before the form, "-lu" after it.
    private static string? AffixForm(string before, string after) =>
        after.Length <= before.Length ? null
        : after.EndsWith(before, StringComparison.Ordinal) ? $"{after[..^before.Length]}-"
        : after.StartsWith(before, StringComparison.Ordinal) ? $"-{after[before.Length..]}"
        : null;

    private static bool TakesApart(TraceStepViewModel step) => step.Type.Contains("Analysis", StringComparison.Ordinal);

    // A rule step records only the form it left; the form it met is the one the step before it on the path holds.
    private static string? FormBefore(TraceStepViewModel step, IReadOnlyList<TraceStepViewModel> path)
    {
        for (var index = IndexOf(path, step) - 1; index >= 0; index--)
            if ((path[index].Output ?? path[index].Input) is { Length: > 0 } form) return form;
        return null;
    }

    private static int IndexOf(IReadOnlyList<TraceStepViewModel> path, TraceStepViewModel step)
    {
        for (var index = 0; index < path.Count; index++)
            if (ReferenceEquals(path[index], step)) return index;
        return -1;
    }

    private async Task LoadStoredRuleTimingsAsync()
    {
        var result = Trace.Result;
        var projectPath = Context.ProjectPath;
        var assessmentId = _assessmentId;
        var parsedAt = EarlierParseTime;
        if (result is null || projectPath is null || assessmentId is null || parsedAt is null) return;

        var generation = ++_timingGeneration;
        var outcome = await Context.Commands.TimingAsync(
            new TimingRequest(projectPath, assessmentId, By: "rule", ExplicitWords: [result.Word]),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _timingGeneration || !ReferenceEquals(result, Trace.Result) ||
            !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;

        ShowEarlierTiming(outcome.Succeeded ? outcome.Value : null, parsedAt);
    }

    // When the stored parse behind the word's timing ran; unknown once a re-run has replaced some words' times.
    private DateTimeOffset? EarlierParseTime => Context.Evidence.TimingOverrideAssessmentIds.Count > 0 ? null
        : Context.Evidence.Assessment is { } shown &&
            shown.Assessment.Measurements.Any(measurement => measurement.Kind == AssessmentKinds.ParseTime)
            ? shown.WasRerun ? null : shown.CompletedAt
            : Context.Evidence.Stored?.AssessedUtc;

    private void ShowEarlierTiming(TimingResponse? timing, DateTimeOffset? parsedAt)
    {
        EarlierRuleTimes.Clear();
        EarlierTimingTitle = EarlierTimingSource = EarlierShareHeader = string.Empty;
        if (timing is { Aggregates.Count: > 0 } && parsedAt is { } ranAt && Trace.Result is { } result)
        {
            var wordMs = timing.Words.FirstOrDefault(word => word.Word == result.Word)?.ElapsedMs is > 0 and var ms
                ? (double?)ms : null;
            string ShareOf(double elapsed) => wordMs is { } whole
                ? (elapsed / whole).ToString("P0", CultureInfo.CurrentCulture) : string.Empty;
            foreach (var rule in timing.Aggregates.Take(EarlierRuleLimit))
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime(rule.Name, TimingShare.KindName(rule.Kind),
                    SpeedText.PerWord(rule.ElapsedMs), ShareOf(rule.ElapsedMs), IsOtherTime: false));
            if (timing.Aggregates.Skip(EarlierRuleLimit).ToArray() is { Length: > 0 } rest)
            {
                var restMs = rest.Sum(rule => rule.ElapsedMs);
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime(SpeedText.Count(rest.Length, "other rule", "other rules"),
                    string.Empty, SpeedText.PerWord(restMs), ShareOf(restMs), IsOtherTime: false));
            }
            if (wordMs - timing.Aggregates.Sum(rule => rule.ElapsedMs) is double other && other >= TimingShare.SmallestShownMs)
                EarlierRuleTimes.Add(new TryWordEarlierRuleTime("Other time", string.Empty,
                    SpeedText.PerWord(other), ShareOf(other), IsOtherTime: true));
            EarlierTimingTitle = $"Time from the parse of {ranAt.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)}";
            EarlierTimingSource = "Not from this try. " + (wordMs is { } took
                ? $"In that parse {result.Word} took {SpeedText.PerWord(took)}; each rule's time covers that parse's " +
                    "whole search for the word"
                : $"Each rule's time covers that parse's whole search for {result.Word}") +
                ", including attempts that stopped, not only the path above." +
                (Context.Evidence.IsStale ? " FieldWorks has changed since that parse." : string.Empty);
            EarlierShareHeader = wordMs is { } whole ? $"SHARE OF {SpeedText.PerWord(whole)}" : string.Empty;
        }
        OnPropertyChanged(nameof(HasEarlierTiming));
        OnPropertyChanged(nameof(EarlierTimingTitle));
        OnPropertyChanged(nameof(EarlierTimingSource));
        OnPropertyChanged(nameof(EarlierShareHeader));
        OnPropertyChanged(nameof(HasEarlierShares));
    }
}

/// <summary>One rule's time for the traced word in an earlier parse, or that parse's time against no rule.</summary>
/// <param name="Rule">The rule's name, or what the row gathers.</param>
/// <param name="KindLabel">The rule's kind as the window names it; empty for a gathered row.</param>
/// <param name="TimeText">The time the earlier parse recorded.</param>
/// <param name="ShareText">Its share of the word's whole time in that parse; empty when that time was not kept.</param>
/// <param name="IsOtherTime">Whether this is the time the parser recorded against no rule.</param>
public sealed record TryWordEarlierRuleTime(string Rule, string KindLabel, string TimeText, string ShareText,
    bool IsOtherTime);

/// <summary>One named rule on the best path, as this try recorded it.</summary>
/// <param name="rule">The rule's name as recorded by the trace.</param>
/// <param name="kind">The kinds of steps attributed to the rule.</param>
/// <param name="outcome">The outcomes recorded for the rule's steps.</param>
/// <param name="explanation">The recorded detail for the rule's steps.</param>
/// <param name="openTiming">Opens Timing filtered to the rule and current word.</param>
public sealed class TryWordRuleRowViewModel(
    string rule, string kind, string outcome, string explanation, Action openTiming)
{
    /// <summary>The rule's name as recorded by the trace.</summary>
    public string Rule { get; } = rule;

    /// <summary>The kinds of steps attributed to the rule.</summary>
    public string Kind { get; } = kind;

    /// <summary>The outcomes recorded for the rule's steps.</summary>
    public string Outcome { get; } = outcome;

    /// <summary>The recorded detail for the rule's steps.</summary>
    public string Explanation { get; } = explanation;

    /// <summary>Opens Timing filtered to the rule and current word.</summary>
    public IRelayCommand OpenTimingCommand { get; } = new RelayCommand(openTiming);
}
