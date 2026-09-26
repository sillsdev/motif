using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private ParserReadingViewModel? _expectedAnalysis;

    public TryWordPageModel(WorkspaceContext context) : base(context)
    {
        Trace = context.Assess.Trace;
        Trace.PropertyChanged += OnTracePropertyChanged;
        OpenInTextsCommand = new RelayCommand(() => Context.OpenWord(Trace.WordToTry.Trim()), CanOpenCurrentWord);
        HandOffCommand = new RelayCommand(() => Context.HandOff([Trace.WordToTry.Trim()]), CanOpenCurrentWord);
        OpenTimingCommand = new RelayCommand(OpenTimingForCurrentWord, CanOpenCurrentWord);
        OpenRecentWordCommand = new RelayCommand<string?>(word =>
        {
            if (!string.IsNullOrWhiteSpace(word)) Context.TryWord(word);
        });
        AddExpectedAnalysisToReviewCommand = new AsyncRelayCommand(AddExpectedAnalysisToReviewAsync,
            CanAddExpectedAnalysisToReview);
        Context.Assess.Words.PropertyChanged += OnWordsPropertyChanged;
        Context.PropertyChanged += OnContextPropertyChanged;
        RefreshExpected(Trace.WordToTry);
    }

    /// <summary>The shared trace the Texts page also primes when a Results word is chosen.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>Recently traced words, newest first.</summary>
    public ObservableCollection<string> RecentWords { get; } = [];

    /// <summary>Unique named rules on the successful or furthest recorded attempt, in path order.</summary>
    public ObservableCollection<TryWordRuleRowViewModel> RulesOnBestPath { get; } = [];

    /// <summary>Whether the current trace has named rules on its successful or furthest attempt.</summary>
    public bool HasRulesOnBestPath => RulesOnBestPath.Count > 0;

    /// <summary>The sidebar link to the first named rule on the current trace, or its general Timing link.</summary>
    public string TimingLinkText => RulesOnBestPath.FirstOrDefault() is { } rule
        ? $"See {rule.Rule} in Timing" : "Timing for this word";

    /// <summary>Opens Texts on the current word, even when the word is absent from its list.</summary>
    public IRelayCommand OpenInTextsCommand { get; }

    /// <summary>Opens AI Handoff with only the current word selected.</summary>
    public IRelayCommand HandOffCommand { get; }

    /// <summary>Opens Timing for the one word being traced.</summary>
    public IRelayCommand OpenTimingCommand { get; }

    /// <summary>Traces a recent word again.</summary>
    public IRelayCommand<string?> OpenRecentWordCommand { get; }

    /// <summary>Adds this word's one stored expected analysis to Review changes for approval.</summary>
    public IAsyncRelayCommand AddExpectedAnalysisToReviewCommand { get; }

    /// <summary>Why the expected analysis cannot be collected, or <see langword="null"/> when it can.</summary>
    public string? ExpectedAnalysisReviewReason => _expectedAnalysis switch
    {
        null => "No expected analysis is available for this word.",
        { StoredAnalysisId: null } => "This expected analysis is not stored in the project.",
        { StoredAnalysisOpinion: "approved" } => "This analysis is already approved.",
        _ when Context.ProjectPath is null => "Open a project before adding this analysis.",
        { StoredAnalysisOpinion: "candidate" or "disapproved" } => null,
        _ => "The project's approval status is unavailable.",
    };

    /// <summary>Whether the expected-analysis explanation should be shown.</summary>
    public bool HasExpectedAnalysisReviewReason => ExpectedAnalysisReviewReason is not null;

    protected override void OnProjectCleared()
    {
        _assessmentId = null;
        _timingGeneration++;
        Trace.Reset();
        Trace.WordToTry = string.Empty;
        _expectedAnalysis = null;
        NotifyExpectedAnalysisChanged();
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
        AddExpectedAnalysisToReviewCommand.NotifyCanExecuteChanged();
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

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.ProjectPath)) NotifyExpectedAnalysisChanged();
    }

    private void RefreshExpected(string word)
    {
        _expectedAnalysis = Context.Assess.Words.Find(word)?.ExpectedAnalysis;
        Trace.SetExpected(word, _expectedAnalysis?.Morphs);
        NotifyExpectedAnalysisChanged();
    }

    private void NotifyExpectedAnalysisChanged()
    {
        OnPropertyChanged(nameof(ExpectedAnalysisReviewReason));
        OnPropertyChanged(nameof(HasExpectedAnalysisReviewReason));
        AddExpectedAnalysisToReviewCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddExpectedAnalysisToReview() =>
        Context.ProjectPath is not null && _expectedAnalysis is
        { StoredAnalysisId: not null, StoredAnalysisOpinion: "candidate" or "disapproved" };

    private async Task AddExpectedAnalysisToReviewAsync()
    {
        if (!CanAddExpectedAnalysisToReview() || _expectedAnalysis is not { StoredAnalysisId: { } storedId } expected)
            return;
        await Context.Changes.ApproveStoredAnalysisAsync(Trace.WordToTry.Trim(), storedId,
            expected.Text, WorkspacePage.TryAWord).ConfigureAwait(true);
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
                        string.Join(" · ", steps.Select(step => DescribeKind(step.Type)).Distinct(StringComparer.Ordinal)),
                        string.Join(" · ", steps.Select(step => step.StatusText).Distinct(StringComparer.Ordinal)),
                        string.Join(" · ", steps.Select(Explain).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal)),
                        () => Context.OpenTiming([result.Word], rule.Key)));
                }
            }
        }
        OnPropertyChanged(nameof(RulesOnBestPath));
        OnPropertyChanged(nameof(HasRulesOnBestPath));
        OnPropertyChanged(nameof(TimingLinkText));
    }

    private static string Explain(TraceStepViewModel step) =>
        step.ContextualFailure ?? step.FailureReason ?? step.Output ??
        (step.Input is { Length: > 0 } input ? $"Started with {input}" : "Outcome detail not recorded");

    private static string DescribeKind(string kind) => kind switch
    {
        "MorphologicalRule" or "MorphologicalRuleAnalysis" => "Morphological rule",
        "PhonologicalRule" or "PhonologicalRuleAnalysis" => "Phonological rule",
        "LexicalLookup" => "Lexical lookup",
        _ => kind,
    };

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

    /// <summary>The stored share of this word's measured time, or a statement that no measurement was recorded.</summary>
    public string Share => _timing is { } timing ? $"{timing.ShareOfTotal:P0}" : "Not recorded";

    /// <summary>Raised when one of the displayed timing values changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets the stored measurement returned for this rule, if one exists.</summary>
    internal void SetTiming(TimingAggregateRow? timing)
    {
        _timing = timing;
        OnPropertyChanged(nameof(StoredTime));
        OnPropertyChanged(nameof(Attempts));
        OnPropertyChanged(nameof(Share));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
