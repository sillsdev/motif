using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

public enum TraceView
{
    Candidates,
    FullDerivation,
}

public enum TraceOutcomeFilter
{
    All,
    Succeeded,
    Failed,
}

/// <summary>
/// Traces one word against the current Baseline's grammar on demand, or presents a saved producer
/// diagnostic without opening a project or invoking a parser.
/// </summary>
public sealed partial class TraceWordViewModel : ObservableObject
{
    private readonly ICommandClient? _commandClient;
    private string? _projectPath;
    private int _generation;
    private CancellationTokenSource? _running;
    private IReadOnlyList<TraceCandidateViewModel> _candidates = [];
    private IReadOnlyList<TraceAnalysisViewModel> _analyses = [];
    private IReadOnlyList<TraceStepViewModel> _filteredRoots = [];
    private IReadOnlyList<TraceStopGroupViewModel> _stopGroups = [];
    private string? _diagnosticJson;

    public TraceWordViewModel(ICommandClient? commandClient = null)
    {
        _commandClient = commandClient;
        TryCommand = new AsyncRelayCommand(TryAsync, () => _projectPath is not null && WordToTry.Trim().Length > 0);
        SetViewCommand = new RelayCommand<TraceView>(view => View = view);
        CancelCommand = new RelayCommand(CancelRunning, () => IsLoading);
        // Choosing the group already in force clears the filter, so one control both narrows and widens.
        SelectStopGroupCommand = new RelayCommand<TraceStopGroupViewModel?>(group =>
            SelectedStopGroup = ReferenceEquals(group, SelectedStopGroup) ? null : group);
        ShowEveryAttemptCommand = new RelayCommand(() => ShowEveryAttempt = true);
    }

    [ObservableProperty]
    private TraceCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusToggleLabel))]
    private bool _isFocused;

    [ObservableProperty]
    private string _wordToTry = string.Empty;

    partial void OnWordToTryChanged(string value) => TryCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    private bool _isLoading;

    partial void OnIsLoadingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    private Refusal? _refusal;

    [ObservableProperty]
    private WordTraceResponse? _result;

    [ObservableProperty]
    private TraceView _view = TraceView.Candidates;

    [ObservableProperty]
    private TraceStepViewModel? _selectedStep;

    public IReadOnlyList<TraceCandidateViewModel> Candidates => _candidates;

    partial void OnResultChanged(WordTraceResponse? value)
    {
        var allowLiveLinks = _projectPath is not null && value?.Provenance?.CanNavigate == true;
        var directions = WritingSystemsById(value);
        _candidates = value?.Candidates.Select(candidate => new TraceCandidateViewModel(candidate, allowLiveLinks, directions)).ToArray() ?? [];
        _analyses = value?.Analyses.Select(analysis => new TraceAnalysisViewModel(analysis, allowLiveLinks, directions)).ToArray() ?? [];
        Effort = TraceEffortViewModel.Table(value?.Effort ?? []);
        OnPropertyChanged(nameof(Effort));
        OnPropertyChanged(nameof(HasEffort));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasDiagnosticJson));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StopReason));
        OnPropertyChanged(nameof(HasStopReason));
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(FailedAttemptCount));
        RebuildStopGroups();
        OnPropertyChanged(nameof(SearchStatusText));
        OnPropertyChanged(nameof(AnswerText));
        OnPropertyChanged(nameof(AnswerVerdict));
        OnPropertyChanged(nameof(ProvenanceWarning));
        OnPropertyChanged(nameof(HasProvenanceWarning));
        OnPropertyChanged(nameof(WritingSystemSummary));
        OnPropertyChanged(nameof(CaptureDetails));
        OnPropertyChanged(nameof(HasCaptureDetails));
        RebuildFilteredRoots();
    }

    public bool HasResult => Result is not null;

    public bool HasDiagnosticJson => !string.IsNullOrWhiteSpace(DiagnosticJson);

    public string SummaryText => Result is not { } result ? string.Empty : Summarize(result);

    public string? StopReason => Result is { Complete: false } result ? result.StopReason : null;

    public bool HasStopReason => StopReason is { Length: > 0 };

    public IReadOnlyList<TraceEffortViewModel> Effort { get; private set; } = [];

    public bool HasEffort => Effort.Count > 0;

    private static string Summarize(WordTraceResponse result)
    {
        var parts = new List<string> { result.Parsed ? "Parsed" : result.InvalidShape ? "Nothing to parse" : "No parse" };
        if (result.Guessed) parts.Add("guessed");
        if (result.ParserSteps is { } steps) parts.Add($"{steps:N0} parser steps");
        var overall = result.HostCapture?.WallElapsedMs is { } capturedElapsed
            ? $"{FormatMs(capturedElapsed)} overall"
            : result.ElapsedMs > 0 ? $"{FormatMs(result.ElapsedMs)} overall" : "overall time not recorded";
        parts.Add(result.ParserElapsedMs is { } parserMs
            ? $"{FormatMs(parserMs)} in the parser, {overall}"
            : overall);
        return string.Join("  ·  ", parts);
    }

    internal static string FormatMs(double ms) => ms switch
    {
        >= 1000 => $"{ms / 1000:0.0} s",
        >= 10 => $"{ms:0} ms",
        _ => $"{ms:0.0#} ms",
    };

    public TraceStepViewModel? Root =>
        Result is { } result ? new TraceStepViewModel(result.Root, result.DeepestRule, WritingSystemsById(result)) : null;

    public IReadOnlyList<TraceStepViewModel> Roots => Root is { } root ? [root] : [];

    /// <summary>Only analyses explicitly recorded by the diagnostic producer, in producer order.</summary>
    public IReadOnlyList<TraceAnalysisViewModel> Analyses => _analyses;

    public bool HasAnalyses => _analyses.Count > 0;

    public int FailedAttemptCount => _candidates.Count(candidate => candidate.IsFailure);

    /// <summary>Whether the word parsed, as the one word a reader wants before anything else.</summary>
    public string AnswerText => Result is not { } result ? string.Empty
        : result.Parsed ? "Parsed" : result.InvalidShape ? "Nothing to parse" : "No parse";

    /// <summary>The colour that answer wears: the same green and amber every other stage uses.</summary>
    public Verdict AnswerVerdict => Result is { Parsed: true } ? Verdict.Agrees
        : Result is { Complete: false } ? Verdict.Limit
        : Verdict.NoResult;

    /// <summary>The rules that stopped the failed attempts, the busiest first; empty when nothing failed.</summary>
    public IReadOnlyList<TraceStopGroupViewModel> StopGroups => _stopGroups;

    public bool HasStopGroups => _stopGroups.Count > 0;

    /// <summary>Over the groups: why the word failed, or, for a word that parsed, why its other attempts did.</summary>
    public string StopGroupsHeading => Result is { Parsed: true } ? "Why the other attempts stopped" : "Why it did not parse";

    /// <summary>One line over the groups: how many rules account for how many attempts.</summary>
    public string StopGroupsSummary
    {
        get
        {
            if (_stopGroups.Count == 0) return string.Empty;
            var attempts = _stopGroups.Sum(group => group.Count);
            var rules = _stopGroups.Count == 1 ? "1 rule" : $"{_stopGroups.Count} rules";
            var tries = attempts == 1 ? "1 attempt" : $"{attempts:N0} attempts";
            return $"{rules} stopped all {tries} · choose one to see only its attempts";
        }
    }

    /// <summary>The group whose attempts are shown, or <see langword="null"/> for every failed attempt.</summary>
    [ObservableProperty]
    private TraceStopGroupViewModel? _selectedStopGroup;

    partial void OnSelectedStopGroupChanged(TraceStopGroupViewModel? value)
    {
        foreach (var group in _stopGroups) group.IsSelected = ReferenceEquals(group, value);
        ShowEveryAttempt = false;
        RaiseAttempts();
    }

    /// <summary>Whether every attempt of the chosen group is listed, rather than the closest few.</summary>
    [ObservableProperty]
    private bool _showEveryAttempt;

    partial void OnShowEveryAttemptChanged(bool value) => RaiseAttempts();

    /// <summary>
    /// The failed attempts worth reading first: those of the chosen group, or all of them, ordered by how
    /// many morphemes they had assembled, since that is how close they came to building the word.
    /// </summary>
    public IReadOnlyList<TraceCandidateViewModel> ClosestAttempts =>
        [.. MatchingAttempts().Take(ShowEveryAttempt ? int.MaxValue : ClosestShown)];

    public bool HasClosestAttempts => ClosestAttempts.Count > 0;

    /// <summary>The analysis the project approves for the Results word being tried, which the parser missed.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> ExpectedMorphs { get; private set; } = [];

    private string? _expectedWord;

    /// <summary>
    /// Remembers what the project approves for <paramref name="word"/>, so a trace of that word can set it beside
    /// the attempt that got furthest; an empty analysis clears it.
    /// </summary>
    public void SetExpected(string word, IReadOnlyList<ParserReadingMorphViewModel>? morphs)
    {
        _expectedWord = word;
        ExpectedMorphs = morphs ?? [];
        RaiseComparison();
    }

    /// <summary>The failed attempt that built the most, to set beside the project's analysis.</summary>
    public TraceCandidateViewModel? FurthestAttempt => ClosestAttempts.FirstOrDefault(attempt => attempt.HasMorphs);

    /// <summary>Whether the trace is of the word whose approved analysis is known, and it failed with something built.</summary>
    public bool HasComparison => ExpectedMorphs.Count > 0 && Result is { Parsed: false } result &&
        string.Equals(result.Word, _expectedWord, StringComparison.Ordinal) && FurthestAttempt is not null;

    /// <summary>Whether this trace is of the word chosen in Results, whose header already names it.</summary>
    public bool ShowsChosenWord => Result is { } result && string.Equals(result.Word, _expectedWord, StringComparison.Ordinal);

    /// <summary>Whether the answer names its own word: a saved diagnostic, or a word typed in over the chosen one.</summary>
    public bool ShowsOtherWord => HasResult && !ShowsChosenWord;

    private void RaiseComparison()
    {
        OnPropertyChanged(nameof(ExpectedMorphs));
        OnPropertyChanged(nameof(FurthestAttempt));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(ShowsChosenWord));
        OnPropertyChanged(nameof(ShowsOtherWord));
    }

    /// <summary>What the button under the attempts offers, or empty when they are all on screen.</summary>
    public string MoreAttemptsText
    {
        get
        {
            var hidden = MatchingAttempts().Count() - ClosestAttempts.Count;
            return hidden <= 0 ? string.Empty
                : SelectedStopGroup is { } group ? $"Show the other {hidden:N0} stopped by {group.RuleText}"
                : $"Show the other {hidden:N0} attempts";
        }
    }

    public bool HasMoreAttempts => MoreAttemptsText.Length > 0;

    /// <summary>Shows every failed attempt of the chosen group instead of the closest few.</summary>
    public IRelayCommand ShowEveryAttemptCommand { get; }

    /// <summary>Chooses a stopping rule to filter the attempts by; the same one again clears the filter.</summary>
    public IRelayCommand<TraceStopGroupViewModel?> SelectStopGroupCommand { get; }

    private const int ClosestShown = 3;

    private IEnumerable<TraceCandidateViewModel> MatchingAttempts() => _candidates
        .Where(candidate => candidate.IsFailure)
        .Where(candidate => SelectedStopGroup is not { } group || group.Matches(candidate))
        .OrderByDescending(candidate => candidate.Morphs.Count)
        .ThenByDescending(candidate => candidate.Steps.Count);

    private void RebuildStopGroups()
    {
        _stopGroups = _candidates.Where(candidate => candidate.IsFailure)
            .GroupBy(candidate => (candidate.StoppedByRule, candidate.FailureReason))
            .Select(group => new TraceStopGroupViewModel(group.Key.StoppedByRule, group.Key.FailureReason,
                group.First().Explanation, group.Count()))
            .OrderByDescending(group => group.Count)
            .ToArray();
        var largest = _stopGroups.Count == 0 ? 0 : _stopGroups.Max(group => group.Count);
        foreach (var group in _stopGroups) group.SetShare(largest);
        SelectedStopGroup = null;
        OnPropertyChanged(nameof(StopGroups));
        OnPropertyChanged(nameof(HasStopGroups));
        OnPropertyChanged(nameof(StopGroupsHeading));
        OnPropertyChanged(nameof(StopGroupsSummary));
        RaiseAttempts();
    }

    private void RaiseAttempts()
    {
        OnPropertyChanged(nameof(ClosestAttempts));
        OnPropertyChanged(nameof(HasClosestAttempts));
        OnPropertyChanged(nameof(MoreAttemptsText));
        OnPropertyChanged(nameof(HasMoreAttempts));
        RaiseComparison();
    }

    public IReadOnlyList<TraceStepViewModel> FilteredRoots => _filteredRoots;

    public int HiddenStepCount { get; private set; }

    public string? ProjectPath => _projectPath;

    public bool IsStandalone => _projectPath is null;

    // A live trace of the project's own Baseline can navigate; only a trace that cannot link back needs a warning.
    public bool HasProvenanceWarning =>
        Result is { Provenance: null } or { Provenance.CanNavigate: false };

    public string ProvenanceWarning =>
        Result?.Provenance is { CanNavigate: false } comparison
            ? comparison.Warning
            : "Compatibility with the current FieldWorks project is not recorded; live links are unavailable for this saved diagnostic.";

    private static IReadOnlyDictionary<string, TraceWritingSystem> WritingSystemsById(WordTraceResponse? result) =>
        result?.HostCapture?.WritingSystems.GroupBy(system => system.Id).ToDictionary(group => group.Key, group => group.First())
        ?? new Dictionary<string, TraceWritingSystem>();

    public string WritingSystemSummary
    {
        get
        {
            var systems = Result?.HostCapture?.WritingSystems;
            if (systems is null || systems.Count == 0) return "Writing systems: not recorded; using the UI font fallback.";
            var summary = string.Join(", ", systems.Select(system =>
                $"{system.Id} ({system.Direction ?? "direction not recorded"}; font: {system.Font ?? "UI fallback"})"));
            return $"Writing systems: {summary}. Font fallback: Noto Sans, Segoe UI, Arial.";
        }
    }

    public bool HasCaptureDetails => Result is not null && (Result.HostCapture is not null ||
        Result.ParserName is not null || Result.ParserVersion is not null || Result.TraceProfile is not null ||
        Result.GrammarHash is not null || Result.DiagnosticFormat is not null);

    public string CaptureDetails
    {
        get
        {
            if (Result is not { } result) return string.Empty;
            var capture = result.HostCapture;
            var parts = new List<string>();
            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label}: {value}");
            }

            Add("Format", result.DiagnosticFormat);
            Add("Parser", result.ParserName);
            Add("Parser version", result.ParserVersion);
            Add("Trace profile", result.TraceProfile);
            Add("Grammar hash", result.GrammarHash ?? capture?.GrammarHash);
            Add("Grammar hash semantics", result.GrammarHashSemantics ?? capture?.GrammarHashSemantics);
            Add("Project identity", capture?.ProjectIdentity);
            Add("Bundle digest", capture?.BundleDigest);
            if (capture?.CapturedUtc is { } capturedUtc) parts.Add($"Captured: {capturedUtc:u}");
            if (capture?.WallElapsedMs is { } elapsed) parts.Add($"Host elapsed: {elapsed:N0} ms");
            return parts.Count == 0 ? "Capture details not recorded." : string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildFilteredRoots();

    [ObservableProperty]
    private string _ruleFilter = string.Empty;

    partial void OnRuleFilterChanged(string value) => RebuildFilteredRoots();

    [ObservableProperty]
    private string _morphFilter = string.Empty;

    partial void OnMorphFilterChanged(string value) => RebuildFilteredRoots();

    [ObservableProperty]
    private TraceOutcomeFilter _outcomeFilter;

    partial void OnOutcomeFilterChanged(TraceOutcomeFilter value) => RebuildFilteredRoots();

    public IReadOnlyList<TraceOutcomeFilter> OutcomeFilters { get; } = Enum.GetValues<TraceOutcomeFilter>();

    public string SelectedFilterDescription
    {
        get
        {
            var values = new List<string>();
            if (OutcomeFilter != TraceOutcomeFilter.All) values.Add(OutcomeFilter == TraceOutcomeFilter.Failed ? "failed" : "succeeded");
            if (!string.IsNullOrWhiteSpace(RuleFilter)) values.Add($"rule: {RuleFilter.Trim()}");
            if (!string.IsNullOrWhiteSpace(MorphFilter)) values.Add($"morph: {MorphFilter.Trim()}");
            return values.Count == 0
                ? (HiddenStepCount == 0 ? "all recorded steps" : $"{HiddenStepCount:N0} steps hidden by filters")
                : string.Join(", ", values) + (HiddenStepCount == 0 ? string.Empty : $" · {HiddenStepCount:N0} hidden");
        }
    }

    public string SearchStatusText
    {
        get
        {
            if (Result is not { } result) return string.Empty;
            var incomplete = !result.Complete || !string.Equals(result.SearchStatus, "complete", StringComparison.OrdinalIgnoreCase);
            var status = result.InvalidShape
                ? "Nothing to parse: the word has a character the grammar's character table does not define"
                : incomplete
                    ? $"Search incomplete: {result.StopReason ?? "the parser stopped before completion"}"
                    : "Search complete";
            return HiddenStepCount > 0 ? $"{status}  ·  {HiddenStepCount:N0} hidden by filters" : status;
        }
    }

    /// <summary>The raw producer document. Display filters never alter this string.</summary>
    public string DiagnosticJson => _diagnosticJson ?? Result?.DiagnosticJson ?? string.Empty;

    /// <summary>Loads a producer v1/v2 diagnostic through the Commands projection; no parser or project is opened.</summary>
    public static TraceWordViewModel FromDiagnosticJson(string json, TraceHostCapture? current = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var outcome = WordTraceQuery.LoadDiagnostic(json, current: current);
        if (!outcome.Succeeded)
            throw new JsonException(outcome.Refusal?.Message ?? "The diagnostic JSON was refused.");
        var viewModel = new TraceWordViewModel { Result = outcome.Value };
        viewModel._diagnosticJson = json;
        return viewModel;
    }

    public bool ShowCandidates => View == TraceView.Candidates;

    public bool ShowFullDerivation => View == TraceView.FullDerivation;

    public string FocusToggleLabel => IsFocused ? "Show Analyses" : "Focus this word";

    public IAsyncRelayCommand TryCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand<TraceView> SetViewCommand { get; }

    public void SetProjectPath(string? fwDataPath)
    {
        _projectPath = fwDataPath;
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(IsStandalone));
        TryCommand.NotifyCanExecuteChanged();
        if (Result is not null) OnResultChanged(Result);
    }

    public void SetWord(string word) => WordToTry = word;

    public void Reset()
    {
        CancelRunning();
        _generation++;
        IsLoading = false;
        Refusal = null;
        Result = null;
        _diagnosticJson = null;
        SelectedStep = null;
        SelectedCandidate = null;
        View = TraceView.Candidates;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));
    }

    private async Task TryAsync()
    {
        if (_projectPath is not { } path) return;
        var word = WordToTry.Trim();
        if (word.Length == 0) return;
        // Trying a word asks to read its story, which needs the full width, not the analyses beside it.
        IsFocused = true;

        CancelRunning();
        using var running = new CancellationTokenSource();
        _running = running;
        var generation = ++_generation;
        IsLoading = true;
        Refusal = null;
        Result = null;
        _diagnosticJson = null;
        SelectedStep = null;
        SelectedCandidate = null;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));

        if (_commandClient is null)
        {
            IsLoading = false;
            return;
        }

        var outcome = await _commandClient.TraceWordAsync(new WordTraceRequest(path, word), running.Token)
            .ConfigureAwait(true);
        if (ReferenceEquals(_running, running)) _running = null;
        if (generation != _generation) return;

        IsLoading = false;
        if (!outcome.Succeeded)
        {
            Refusal = outcome.Refusal;
            return;
        }

        Result = outcome.Value;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));
    }

    private void RebuildFilteredRoots()
    {
        var root = Root;
        if (root is null)
        {
            _filteredRoots = [];
            HiddenStepCount = 0;
            OnPropertyChanged(nameof(FilteredRoots));
            OnPropertyChanged(nameof(SearchStatusText));
            OnPropertyChanged(nameof(SelectedFilterDescription));
            return;
        }

        var filtered = FilterNode(root);
        _filteredRoots = filtered is null ? [] : [filtered];
        HiddenStepCount = CountNodes(root) - _filteredRoots.Sum(CountNodes);
        OnPropertyChanged(nameof(FilteredRoots));
        OnPropertyChanged(nameof(HiddenStepCount));
        OnPropertyChanged(nameof(SearchStatusText));
        OnPropertyChanged(nameof(SelectedFilterDescription));
    }

    private TraceStepViewModel? FilterNode(TraceStepViewModel node)
    {
        var children = node.Children.Select(FilterNode).Where(child => child is not null).Cast<TraceStepViewModel>().ToArray();
        var matches = Matches(node);
        return matches || children.Length > 0 ? node.WithChildren(children, HasActiveFilters && children.Length > 0) : null;
    }

    private bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) || !string.IsNullOrWhiteSpace(RuleFilter) ||
        !string.IsNullOrWhiteSpace(MorphFilter) || OutcomeFilter != TraceOutcomeFilter.All;

    private bool Matches(TraceStepViewModel node)
    {
        if (OutcomeFilter == TraceOutcomeFilter.Succeeded && !node.IsSuccessful) return false;
        if (OutcomeFilter == TraceOutcomeFilter.Failed && !node.IsFailure) return false;
        if (!string.IsNullOrWhiteSpace(RuleFilter) &&
            (node.Source?.Contains(RuleFilter.Trim(), StringComparison.OrdinalIgnoreCase) != true)) return false;
        var morphText = string.Join("\n", node.Type, node.Source, node.Input, node.Output, node.FailureReason,
            node.ContextualFailure, node.FailureRequired, node.FailureActual, node.FailureEnvironment,
            node.SourceIdentityId, node.MorphSearchText);
        if (!string.IsNullOrWhiteSpace(MorphFilter) &&
            !morphText.Contains(MorphFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return string.Join("\n", morphText, node.SubruleText, node.StatusText)
            .Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int CountNodes(TraceStepViewModel node) =>
        1 + node.Children.Sum(CountNodes);

    private void CancelRunning()
    {
        _running?.Cancel();
        _running = null;
        IsLoading = false;
    }
}

/// <summary>One recorded parser analysis, kept distinct from trace attempts.</summary>
public sealed class TraceAnalysisViewModel
{
    public TraceAnalysisViewModel(TraceAnalysis analysis, bool allowLiveLinks, IReadOnlyDictionary<string, TraceWritingSystem>? directions = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        AnalysisId = analysis.AnalysisId;
        Index = analysis.Index;
        Surface = analysis.Surface ?? "Surface not recorded";
        Availability = string.IsNullOrWhiteSpace(analysis.Availability) ? "availability not recorded" : analysis.Availability;
        ProjectionStatus = analysis.ProjectionStatus;
        ProjectionError = analysis.ProjectionError;
        LegacyMorphemes = analysis.LegacyMorphemes;
        Morphs = analysis.Morphs.Select(morph => new TraceMorphViewModel(morph, allowLiveLinks, directions)).ToArray();
    }

    public string? AnalysisId { get; }
    public int? Index { get; }
    public string Surface { get; }
    public string Availability { get; }
    public string? ProjectionStatus { get; }
    public string? ProjectionError { get; }
    public string? LegacyMorphemes { get; }
    public bool HasLegacyMorphemes => LegacyMorphemes is { Length: > 0 };
    public IReadOnlyList<TraceMorphViewModel> Morphs { get; }
    public string Label => Index is { } index ? $"Analysis {index + 1}" : AnalysisId is { Length: > 0 } id ? $"Analysis {id}" : "Recorded analysis";
    public bool HasProjectionError => ProjectionError is { Length: > 0 };
}

/// <summary>One rich morph record. Missing fields are named as unavailable rather than inferred from another field.</summary>
public sealed class TraceMorphViewModel
{
    public TraceMorphViewModel(TraceMorph morph, bool allowLiveLink, IReadOnlyDictionary<string, TraceWritingSystem>? directions = null)
    {
        ArgumentNullException.ThrowIfNull(morph);
        var wsDirections = directions ?? new Dictionary<string, TraceWritingSystem>();
        FormDirection = DirectionFor(morph.FormWritingSystem, wsDirections);
        HeadwordDirection = DirectionFor(morph.HeadwordWritingSystem, wsDirections);
        GlossDirection = DirectionFor(morph.GlossWritingSystem, wsDirections);
        FormFont = FontFor(morph.FormWritingSystem, wsDirections);
        HeadwordFont = FontFor(morph.HeadwordWritingSystem, wsDirections);
        GlossFont = FontFor(morph.GlossWritingSystem, wsDirections);
        RawDetails = morph.RawJson ?? "Full raw morph details not recorded";
        MsaDetails = FormatMsaDetails(morph.RawJson);
        Form = ValueOrUnavailable(morph.Form, "Form");
        Headword = ValueOrUnavailable(morph.Headword, "Headword");
        Gloss = ValueOrUnavailable(morph.Gloss, "Gloss");
        Category = ValueOrUnavailable(morph.CategoryName ?? morph.Category, "Category");
        Slot = ValueOrUnavailable(morph.Slot, "Slot");
        InflectionClass = ValueOrUnavailable(morph.InflectionClassName ?? morph.InflectionClass, "Inflection class");
        Features = ValueOrUnavailable(morph.Features, "Features");
        GuessedString = ValueOrUnavailable(morph.GuessedString, "Guessed string");
        Identity = ValueOrUnavailable(morph.Identity, "Identity");
        IdentityQuality = ValueOrUnavailable(morph.IdentityQuality, "Identity quality");
        FormId = ValueOrUnavailable(morph.FormId, "Form ID");
        EntryId = ValueOrUnavailable(morph.EntryId, "Entry ID");
        MsaId = ValueOrUnavailable(morph.MsaId, "MSA ID");
        InflTypeId = ValueOrUnavailable(morph.InflTypeId, "Inflection type ID");
        WritingSystems = string.Join("; ", new[] {
            FormatWs("form", morph.FormWritingSystem),
            FormatWs("headword", morph.HeadwordWritingSystem),
            FormatWs("gloss", morph.GlossWritingSystem),
        }.Where(value => value is not null)!);
        Details = string.Join(" · ", new[] {
            $"Slot: {Slot}", $"Inflection class: {InflectionClass}", $"Features: {Features}", $"Guessed: {GuessedString}",
            $"Form ID: {FormId}", $"Entry ID: {EntryId}", $"MSA ID: {MsaId}", $"Inflection type ID: {InflTypeId}",
            $"Writing systems: {(WritingSystems.Length == 0 ? "not recorded" : WritingSystems)}",
        });
        Link = allowLiveLink && Uri.TryCreate(morph.FieldWorksLink, UriKind.Absolute, out var link) &&
               string.Equals(link.Scheme, "silfw", StringComparison.OrdinalIgnoreCase) ? link : null;
    }

    private static string ValueOrUnavailable(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? $"{label} not recorded" : value;

    private static string FormatMsaDetails(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "Named MSA details not recorded.";
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "Named MSA details not recorded.";
            var fields = new (string Label, string Name)[]
            {
                ("MSA kind", "kind"),
                ("From category", "fromCategory"),
                ("To category", "toCategory"),
                ("From inflection class", "fromInflectionClass"),
                ("To inflection class", "toInflectionClass"),
                ("From features", "fromFeatures"),
                ("To features", "toFeatures"),
                ("Attaches to", "attachesTo"),
                ("Slots", "slots"),
                ("Inflection class", "inflectionClass"),
            };
            var values = new List<string>();
            foreach (var field in fields)
            {
                if (TryDisplay(root, field.Name, out var value) || (root.TryGetProperty("msa", out var msa) &&
                    TryDisplay(msa, field.Name, out value)))
                    values.Add($"{field.Label}: {value}");
            }

            return values.Count == 0 ? "Named MSA details not recorded." : string.Join(" · ", values);
        }
        catch (JsonException)
        {
            return "Named MSA details unavailable; raw morph details are retained below.";
        }
    }

    private static bool TryDisplay(JsonElement owner, string name, out string value)
    {
        value = string.Empty;
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return false;
        value = DisplayValue(property);

        return value.Length > 0;
    }
    private static string DisplayValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "not recorded",
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(DisplayValue)),
        JsonValueKind.Object when value.TryGetProperty("text", out var text) => DisplayValue(text),
        JsonValueKind.Object when value.TryGetProperty("form", out var form) => DisplayValue(form),
        JsonValueKind.Object when value.TryGetProperty("name", out var name) => DisplayValue(name) +
            (value.TryGetProperty("optional", out var optional) ? $" (optional: {DisplayValue(optional)})" : ""),
        JsonValueKind.Object => string.Join("; ", value.EnumerateObject().Select(field => $"{field.Name}: {DisplayValue(field.Value)}")),
        _ => value.GetRawText(),
    };
    private static Avalonia.Media.FlowDirection DirectionFor(string? writingSystem, IReadOnlyDictionary<string, TraceWritingSystem> directions) =>
        writingSystem is { Length: > 0 } id && directions.TryGetValue(id, out var direction) &&
        (string.Equals(direction.Direction, "rtl", StringComparison.OrdinalIgnoreCase) || string.Equals(direction.Direction, "right-to-left", StringComparison.OrdinalIgnoreCase))
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;

    private static Avalonia.Media.FontFamily FontFor(string? id, IReadOnlyDictionary<string, TraceWritingSystem> systems)
    {
        var fallback = "Noto Sans, Segoe UI, Arial";
        return new Avalonia.Media.FontFamily(id is not null && systems.TryGetValue(id, out var system) &&
            !string.IsNullOrWhiteSpace(system.Font) ? $"{system.Font}, {fallback}" : fallback);
    }

    public Avalonia.Media.FontFamily FormFont { get; }
    public Avalonia.Media.FontFamily HeadwordFont { get; }
    public Avalonia.Media.FontFamily GlossFont { get; }

    private static string? FormatWs(string role, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{role}={value}";

    public Avalonia.Media.FlowDirection FormDirection { get; }
    public Avalonia.Media.FlowDirection HeadwordDirection { get; }
    public Avalonia.Media.FlowDirection GlossDirection { get; }
    public string RawDetails { get; }
    public string MsaDetails { get; }
    public string Form { get; }
    public string Headword { get; }
    public string Gloss { get; }
    public string GlossOrPlaceholder => Gloss;
    public string Category { get; }
    public string Slot { get; }
    public string InflectionClass { get; }
    public string Features { get; }
    public string GuessedString { get; }
    public string Identity { get; }
    public string IdentityQuality { get; }
    public string FormId { get; }
    public string EntryId { get; }
    public string MsaId { get; }
    public string InflTypeId { get; }
    public string Details { get; }
    public string WritingSystems { get; }
    public Uri? Link { get; }
    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;
    public string LinkName => $"Open the compatible FieldWorks entry for {Form}";
}

/// <summary>
/// One rule that stopped attempts, with how many it stopped: the answer to "why did this word not parse",
/// before any single attempt is read. A group with no rule holds the attempts that simply ran out.
/// </summary>
public sealed partial class TraceStopGroupViewModel : ObservableObject
{
    public TraceStopGroupViewModel(string? rule, string? reasonCode, string? explanation, int count)
    {
        Rule = rule;
        ReasonCode = reasonCode;
        Explanation = explanation;
        Count = count;
        RuleText = rule is { Length: > 0 } named ? named : "no rule";
        ReasonText = explanation is { Length: > 0 } sentence ? sentence
            : reasonCode is { Length: > 0 } code ? code
            : "The attempt stopped without a recorded reason.";
        CountText = count.ToString("N0");
    }

    /// <summary>The rule as the project names it, or <see langword="null"/> when no rule was to blame.</summary>
    public string? Rule { get; }

    /// <summary>The parser's own reason code, shown after the sentence for anyone matching it to a trace.</summary>
    public string? ReasonCode { get; }

    public string? Explanation { get; }

    /// <summary>The rule's name for a heading, reading "no rule" when the attempts simply ran out.</summary>
    public string RuleText { get; }

    /// <summary>Why this rule stopped them, in the plain language FieldWorks uses where there is one.</summary>
    public string ReasonText { get; }

    public int Count { get; }

    public string CountText { get; }

    public bool HasRule => Rule is { Length: > 0 };

    /// <summary>How long this group's bar is: 1 for the rule that stopped the most attempts.</summary>
    public double Share { get; private set; }

    /// <summary>Whether the attempt list is filtered to this group.</summary>
    [ObservableProperty]
    private bool _isSelected;

    internal void SetShare(int largest)
    {
        Share = largest <= 0 ? 0 : (double)Count / largest;
        OnPropertyChanged(nameof(Share));
    }

    internal bool Matches(TraceCandidateViewModel candidate) =>
        string.Equals(candidate.StoppedByRule, Rule, StringComparison.Ordinal) &&
        string.Equals(candidate.FailureReason, ReasonCode, StringComparison.Ordinal);
}

/// <summary>One candidate attempt, kept separate from recorded analyses.</summary>
public sealed class TraceCandidateViewModel
{
    public TraceCandidateViewModel(TraceCandidate candidate, bool allowLiveLinks = false, IReadOnlyDictionary<string, TraceWritingSystem>? directions = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Morphs = candidate.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        RichMorphs = candidate.RichMorphs.Select(morph => new TraceMorphViewModel(morph, allowLiveLinks, directions)).ToArray();
        Succeeded = candidate.Succeeded;
        AttemptId = candidate.AttemptId;
        FailureReason = candidate.FailureReason;
        Explanation = candidate.Explanation;
        ContextualFailure = candidate.ContextualFailure;
        FailureRequired = candidate.FailureRequired;
        FailureActual = candidate.FailureActual;
        FailureEnvironment = candidate.FailureEnvironment;
        IsFailure = !candidate.Succeeded && (candidate.OutcomeStatus is "failed" or "failure" or "blocked" || candidate.FailureReason is { Length: > 0 } || candidate.ContextualFailure is { Length: > 0 } || candidate.Steps.Any(step => step.FailureReason is { Length: > 0 }));
        SourceIdentity = candidate.SourceIdentityId is { Length: > 0 }
            ? $"{candidate.SourceIdentityKind ?? "source"}: {candidate.SourceIdentityId} ({candidate.SourceIdentityQuality ?? "quality not recorded"})"
            : "Source identity not recorded";
        Steps = candidate.Steps.Select(step => new TraceStepViewModel(step, deepestRule: null, directions)).ToArray();
        Text = RichMorphs.Count > 0 ? string.Join(" + ", RichMorphs.Select(morph => morph.Form)) : Morphs.Count > 0 ? string.Join(" + ", Morphs.Select(morph => morph.Form)) : candidate.Steps.LastOrDefault()?.Source ?? "Recorded attempt";
        Gloss = string.Join(" + ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
        Surface = candidate.Surface;
        StoppedByRule = candidate.StoppedByRule;
        StopHeadline = Succeeded ? "Built the word"
            : StoppedByRule is { Length: > 0 } rule ? $"Stopped by {rule}"
            : "Stopped";
        StopReason = Explanation is { Length: > 0 } explanation
            ? FailureReason is { Length: > 0 } code ? $"{explanation} ({code})" : explanation
            : FailureReason ?? string.Empty;
    }

    /// <summary>The form the attempt had built when it ended.</summary>
    public string? Surface { get; }

    public bool HasSurface => Surface is { Length: > 0 };

    /// <summary>The rule whose step failed just before the attempt ended, by its FieldWorks name when known.</summary>
    public string? StoppedByRule { get; }

    /// <summary>What ended the attempt, in a few words: which rule, or that the attempt simply stopped.</summary>
    public string StopHeadline { get; }

    /// <summary>Why, in the plain language FieldWorks uses, with the parser's own reason code after it.</summary>
    public string StopReason { get; }

    public bool HasMorphs => Morphs.Count > 0;

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public IReadOnlyList<TraceMorphViewModel> RichMorphs { get; }
    public bool Succeeded { get; }
    public string? AttemptId { get; }
    public string? FailureReason { get; }
    public string? Explanation { get; }
    public string? ContextualFailure { get; }
    public string? FailureRequired { get; }
    public string? FailureActual { get; }
    public string? FailureEnvironment { get; }
    public bool IsFailure { get; }
    public string SourceIdentity { get; }
    public string FailureContext => string.Join(" · ", new[] { ContextualFailure, FailureReason, Explanation }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text
        ? text
        : "Failure context not recorded";
    public IReadOnlyList<TraceStepViewModel> Steps { get; }
    public string Text { get; }
    public string Gloss { get; }
    public string StatusText => Succeeded ? "succeeded" : IsFailure ? "failed" : "recorded attempt";

    /// <summary>The shared meaning behind this attempt: it built the word, or a rule stopped it.</summary>
    public Verdict Meaning => Succeeded ? Verdict.Agrees : IsFailure ? Verdict.Differs : Verdict.Limit;
}

/// <summary>One row of aggregate parser effort, explicitly separated from selected-step details.</summary>
public sealed class TraceEffortViewModel
{
    public TraceEffortViewModel(TraceEffort effort) : this(effort, effort) { }

    private TraceEffortViewModel(TraceEffort effort, TraceEffort largest)
    {
        ArgumentNullException.ThrowIfNull(effort);
        Kind = effort.Kind;
        Work = effort.Work.ToString("N0");
        Uses = effort.Uses.ToString("N0");
        Tried = effort.Attempts.ToString("N0");
        Produced = effort.Outputs.ToString("N0");
        var misses = new List<string>();
        if (effort.NotApplied > 0) misses.Add($"{effort.NotApplied:N0} did not apply");
        if (effort.NoRoot > 0) misses.Add($"{effort.NoRoot:N0} found no root");
        if (effort.SurfaceMismatch > 0) misses.Add($"{effort.SurfaceMismatch:N0} did not match the word");
        Missed = misses.Count == 0 ? "—" : string.Join(", ", misses);
        Time = effort.SelfMs is { } ms ? TraceWordViewModel.FormatMs(ms) : "not timed";

        WorkHeat = Shade(effort.Work, largest.Work);
        UsesHeat = Shade(effort.Uses, largest.Uses);
        TriedHeat = Shade(effort.Attempts, largest.Attempts);
        ProducedHeat = Shade(effort.Outputs, largest.Outputs);
        MissedHeat = Shade(effort.NotApplied + effort.NoRoot + effort.SurfaceMismatch,
            largest.NotApplied + largest.NoRoot + largest.SurfaceMismatch);
        TimeHeat = Shade(effort.SelfMs ?? 0, largest.SelfMs ?? 0);
    }

    /// <summary>The rows of the effort table, each cell shaded against the largest value in its own column.</summary>
    public static IReadOnlyList<TraceEffortViewModel> Table(IReadOnlyList<TraceEffort> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return [];
        // Misses are one column, so their largest total rides in NotApplied with the other two kinds left at zero.
        var largest = new TraceEffort("largest",
            Attempts: rows.Max(row => row.Attempts),
            Outputs: rows.Max(row => row.Outputs),
            NotApplied: rows.Max(row => row.NotApplied + row.NoRoot + row.SurfaceMismatch),
            NoRoot: 0,
            SurfaceMismatch: 0,
            Uses: rows.Max(row => row.Uses),
            SelfMs: rows.Max(row => row.SelfMs ?? 0))
        {
            Work = rows.Max(row => row.Work),
        };
        return rows.Select(row => new TraceEffortViewModel(row, largest)).ToArray();
    }

    /// <summary>How strongly a cell is shaded, from 0 for nothing to 1 for its column's largest value.</summary>
    public static double Intensity(double value, double largest) =>
        value <= 0 || largest <= 0 ? 0 : Math.Clamp(value / largest, 0, 1);

    // The opacity of a cell's heat layer; a small nonzero value still shows, so zero stays visibly different.
    private static double Shade(double value, double largest)
    {
        var intensity = Intensity(value, largest);
        return intensity == 0 ? 0 : 0.12 + 0.58 * intensity;
    }

    public string Kind { get; }
    public string Work { get; }
    public string Uses { get; }
    public string Tried { get; }
    public string Produced { get; }
    public string Missed { get; }
    public string Time { get; }

    /// <summary>The opacity of the Work cell's heat layer: 0 for nothing, rising to 0.7 for the column's largest.</summary>
    public double WorkHeat { get; }
    public double UsesHeat { get; }
    public double TriedHeat { get; }
    public double ProducedHeat { get; }
    public double MissedHeat { get; }
    public double TimeHeat { get; }
}

/// <summary>One node of the derivation tree, wrapped for a TreeView with its own children.</summary>
public sealed class TraceStepViewModel
{
    public TraceStepViewModel(TraceStep step, string? deepestRule,
        IReadOnlyDictionary<string, TraceWritingSystem>? directions = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        Type = step.Type;
        Source = step.Source;
        Input = step.Input;
        Output = step.Output;
        FailureReason = step.FailureReason;
        Subrule = step.Subrule;
        OutcomeStatus = step.OutcomeStatus;
        OutcomeEventType = step.OutcomeEventType;
        ContextualFailure = step.ContextualFailure;
        FailureRequired = step.FailureRequired;
        FailureActual = step.FailureActual;
        FailureEnvironment = step.FailureEnvironment;
        SourceIdentityKind = step.SourceIdentityKind;
        SourceIdentityId = step.SourceIdentityId;
        SourceIdentityQuality = step.SourceIdentityQuality;
        AttemptedMorphs = step.AttemptedMorphs.Select(morph => new TraceMorphViewModel(morph, false, directions)).ToArray();
        IsDeepest = deepestRule is not null && string.Equals(step.Source, deepestRule, StringComparison.Ordinal);
        Children = step.Children.Select(child => new TraceStepViewModel(child, deepestRule, directions)).ToArray();
        _directions = directions;
        Label = Source is { Length: > 0 } ? $"{Type}: {Source}" : Type;
    }

    private readonly IReadOnlyDictionary<string, TraceWritingSystem>? _directions;

    public string Type { get; }
    public string? Source { get; }
    public string? Input { get; }
    public string? Output { get; }
    public string? FailureReason { get; }
    public int? Subrule { get; }
    public string? OutcomeStatus { get; }
    public string? OutcomeEventType { get; }
    public string? ContextualFailure { get; }
    public string? FailureRequired { get; }
    public string? FailureActual { get; }
    public string? FailureEnvironment { get; }
    public string? SourceIdentityKind { get; }
    public string? SourceIdentityId { get; }
    public string? SourceIdentityQuality { get; }
    public string SourceIdentity => $"{SourceIdentityKind ?? "Source"}: {SourceIdentityId ?? "not recorded"} ({SourceIdentityQuality ?? "quality not recorded"})";
    public IReadOnlyList<TraceMorphViewModel> AttemptedMorphs { get; }
    public bool HasAttemptedMorphs => AttemptedMorphs.Count > 0;
    public bool HasFailureReason => FailureReason is { Length: > 0 };

    /// <summary>Legacy fact retained for existing candidate consumers; neutral nodes remain textual attempts.</summary>
    public bool Passed => !HasFailureReason;

    public bool IsFailure => HasFailureReason ||
        IsStatus(OutcomeStatus, "failure", "failed", "blocked", "error") ||
        Type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("blocked", StringComparison.OrdinalIgnoreCase);

    public bool IsSuccessful => IsStatus(OutcomeStatus, "success", "succeeded", "successful") ||
        Type.Contains("successful", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("success", StringComparison.OrdinalIgnoreCase);

    public string StatusText => IsFailure ? "failed" : IsSuccessful ? "succeeded" : "recorded attempt";

    public string SubruleText => Subrule is { } subrule ? $"Subrule: {subrule}" : "Subrule not recorded";

    public string ContextText => string.Join("\n", new[] {
        ContextualFailure,
        FailureRequired is { Length: > 0 } ? $"Required: {FailureRequired}" : null,
        FailureActual is { Length: > 0 } ? $"Actual: {FailureActual}" : null,
        FailureEnvironment is { Length: > 0 } ? $"Environment: {FailureEnvironment}" : null,
    }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } context ? context : "Contextual failure not recorded";

    public string MorphSearchText => string.Join("\n", AttemptedMorphs.SelectMany(morph =>
        new[] { morph.Form, morph.Headword, morph.Gloss, morph.Category, morph.Identity, morph.FormId, morph.EntryId }));

    private static bool IsStatus(string? status, params string[] values) =>
        status is not null && values.Any(value => string.Equals(status, value, StringComparison.OrdinalIgnoreCase));

    public TraceStepViewModel WithChildren(IReadOnlyList<TraceStepViewModel> children, bool expandForFilter = false) =>
        new(Type, Source, Input, Output, FailureReason, Subrule, OutcomeStatus, OutcomeEventType,
            ContextualFailure, FailureRequired, FailureActual, FailureEnvironment, SourceIdentityKind, SourceIdentityId, SourceIdentityQuality,
            AttemptedMorphs, _directions, children, IsDeepest, expandForFilter);

    private TraceStepViewModel(string type, string? source, string? input, string? output, string? failureReason,
        int? subrule, string? outcomeStatus, string? outcomeEventType, string? contextualFailure,
        string? failureRequired, string? failureActual, string? failureEnvironment, string? sourceIdentityKind, string? sourceIdentityId, string? sourceIdentityQuality,
        IReadOnlyList<TraceMorphViewModel> attemptedMorphs, IReadOnlyDictionary<string, TraceWritingSystem>? directions,
        IReadOnlyList<TraceStepViewModel> children, bool isDeepest, bool expandForFilter)
    {
        Type = type;
        Source = source;
        Input = input;
        Output = output;
        FailureReason = failureReason;
        Subrule = subrule;
        OutcomeStatus = outcomeStatus;
        OutcomeEventType = outcomeEventType;
        ContextualFailure = contextualFailure;
        FailureRequired = failureRequired;
        FailureActual = failureActual;
        FailureEnvironment = failureEnvironment;
        SourceIdentityKind = sourceIdentityKind;
        SourceIdentityId = sourceIdentityId;
        SourceIdentityQuality = sourceIdentityQuality;
        AttemptedMorphs = attemptedMorphs;
        _directions = directions;
        IsDeepest = isDeepest;
        ExpandForFilter = expandForFilter;
        Children = children;
        Label = Source is { Length: > 0 } ? $"{Type}: {Source}" : Type;
    }

    public bool IsDeepest { get; }
    public bool ExpandForFilter { get; }
    public IReadOnlyList<TraceStepViewModel> Children { get; }
    public string Label { get; }
}
