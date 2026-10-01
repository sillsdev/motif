using System.Collections.ObjectModel;
using System.ComponentModel;
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

    /// <summary>Unique named rules on the successful or furthest recorded attempt, in path order.</summary>
    public ObservableCollection<TryWordRuleRowViewModel> RulesOnBestPath { get; } = [];

    /// <summary>Whether the current trace has named rules on its successful or furthest attempt.</summary>
    public bool HasRulesOnBestPath => RulesOnBestPath.Count > 0;

    /// <summary>Whether any best-path rule has a stored share of this word's measured time.</summary>
    public bool HasWordShare => RulesOnBestPath.Any(row => row.HasShare);

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
        foreach (var row in RulesOnBestPath) row.SetTiming(null);
        if (assessmentId is not null && Trace.Result is not null) _ = LoadStoredRuleTimingsAsync();
    }

    private void RebuildRules(WordTraceResponse? result)
    {
        _timingGeneration++;
        foreach (var row in RulesOnBestPath) row.SetTiming(null);
        RulesOnBestPath.Clear();
        if (result is not null)
        {
            var attempt = result.Parsed
                ? Trace.Candidates.FirstOrDefault(candidate => candidate.Succeeded)
                : Trace.ClosestAttempts.FirstOrDefault();
            if (attempt is not null)
            {
                foreach (var rule in attempt.Steps
                             .Where(step => !string.IsNullOrWhiteSpace(step.Source))
                             .GroupBy(step => step.Source!, StringComparer.Ordinal))
                {
                    var steps = rule.ToArray();
                    RulesOnBestPath.Add(new TryWordRuleRowViewModel(rule.Key,
                        string.Join(" · ", steps.Select(step => step.KindText).Distinct(StringComparer.Ordinal)),
                        steps.Any(step => step.IsFailure) ? "stopped"
                            : attempt.Succeeded || steps.Any(step => step.IsSuccessful) ? "applied" : "tried",
                        Explain(steps, attempt.Steps),
                        () => Context.OpenTiming([result.Word], rule.Key)));
                }
            }
        }
        OnPropertyChanged(nameof(RulesOnBestPath));
        OnPropertyChanged(nameof(HasRulesOnBestPath));
        OnPropertyChanged(nameof(HasWordShare));
        OnPropertyChanged(nameof(TimingLinkText));
    }

    // One plain line per rule: why it stopped the word, else the form it met and the form it left.
    private static string Explain(IReadOnlyList<TraceStepViewModel> steps, IReadOnlyList<TraceStepViewModel> path)
    {
        if (steps.FirstOrDefault(step => step.IsFailure) is { } failed)
            return failed.ContextualFailure ??
                (failed.FailureReason is { Length: > 0 } code ? TraceStepKinds.ExplainReason(code) : "stopped here");
        foreach (var step in steps)
        {
            if (step.Output is not { Length: > 0 } output) continue;
            var input = step.Input is { Length: > 0 } own ? own : FormBefore(step, path);
            if (input is not null && !string.Equals(input, output, StringComparison.Ordinal)) return $"{input} → {output}";
        }
        return steps.Select(step => step.Output ?? step.Input).FirstOrDefault(text => text is { Length: > 0 }) ?? "—";
    }

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
        if (result is null || projectPath is null || assessmentId is null || RulesOnBestPath.Count == 0) return;

        var generation = ++_timingGeneration;
        var outcome = await Context.Commands.TimingAsync(
            new TimingRequest(projectPath, assessmentId, By: "rule", ExplicitWords: [result.Word]),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _timingGeneration || !ReferenceEquals(result, Trace.Result) ||
            !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;

        foreach (var row in RulesOnBestPath)
            row.SetTiming(outcome.Succeeded
                ? outcome.Value!.Aggregates.SingleOrDefault(aggregate =>
                    string.Equals(aggregate.Name, row.Rule, StringComparison.Ordinal))
                : null);
        OnPropertyChanged(nameof(HasWordShare));
    }
}

/// <summary>One named rule on the path and only the timing stored for that rule.</summary>
/// <param name="rule">The rule's name as recorded by the trace.</param>
/// <param name="kind">The kinds of steps attributed to the rule.</param>
/// <param name="outcome">The outcomes recorded for the rule's steps.</param>
/// <param name="explanation">The recorded detail for the rule's steps.</param>
/// <param name="openTiming">Opens Timing filtered to the rule and current word.</param>
public sealed class TryWordRuleRowViewModel(
    string rule, string kind, string outcome, string explanation, Action openTiming) : INotifyPropertyChanged
{
    private TimingAggregateRow? _timing;

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

    /// <summary>The stored elapsed time, or a statement that no measurement was recorded.</summary>
    public string StoredTime => _timing is { } timing ? TraceWordViewModel.FormatMs(timing.ElapsedMs) : "Not recorded";

    /// <summary>The number of stored attempts for this rule, or an em dash when no measurement was recorded.</summary>
    public string Attempts => _timing?.Attempts.ToString("N0") ?? "—";

    /// <summary>The stored share of this word's measured time, or a dash when no measurement was recorded.</summary>
    public string Share => _timing is { } timing ? $"{timing.ShareOfTotal:P0}" : "—";

    /// <summary>Whether a measured share was stored, so the cell wears the share colour rather than a plain dash.</summary>
    public bool HasShare => _timing is not null;

    /// <summary>Raised when one of the displayed timing values changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets the stored measurement returned for this rule, if one exists.</summary>
    internal void SetTiming(TimingAggregateRow? timing)
    {
        _timing = timing;
        OnPropertyChanged(nameof(StoredTime));
        OnPropertyChanged(nameof(Attempts));
        OnPropertyChanged(nameof(Share));
        OnPropertyChanged(nameof(HasShare));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
