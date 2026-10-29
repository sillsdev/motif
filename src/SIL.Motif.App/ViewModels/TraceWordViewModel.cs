using SIL.Motif.Contract.Responses;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Projection.Usage;

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
    private TextStyles? _workspaceTextStyles;

    private readonly ICommandClient? _commandClient;
    private string? _projectPath;
    private int _generation;
    private CancellationTokenSource? _running;
    private IReadOnlyList<TraceCandidateViewModel> _candidates = [];
    private IReadOnlyList<TraceCandidateViewModel> _closestAttempts = [];
    private WordTraceReading? _reading;
    private TraceDisplayLabels _labels = new([]);
    private IReadOnlyDictionary<string, TraceRef> _refs = new Dictionary<string, TraceRef>();
    private IReadOnlyList<TraceAnalysisViewModel> _analyses = [];
    private IReadOnlyList<TraceStepViewModel> _filteredRoots = [];
    private IReadOnlyList<TraceStopGroupViewModel> _stopGroups = [];
    private string? _diagnosticJson;

    public TraceWordViewModel(ICommandClient? commandClient = null, ITraceViewPreferences? preferences = null, TimeProvider? clock = null)
    {
        _commandClient = commandClient;
        ParseProgress = new ParseProgressViewModel(clock ?? TimeProvider.System);
        InitializeExpert(preferences);
        TryCommand = new AsyncRelayCommand(TryAsync, () => _projectPath is not null && !IsLoading &&
            !ParseProgress.IsActive && WordToTry.Trim().Length > 0);
        ParseProgress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParseProgressViewModel.IsActive)) TryCommand.NotifyCanExecuteChanged();
        };
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

    /// <summary>The resident parser sessions the pending word may be waiting on; the workspace supplies them.</summary>
    public ITryWordSessions TryWordSessions { get; set; } = NoTryWordSessions.Instance;

    /// <summary>Whether the word being traced is waiting on the parser to finish loading the grammar.</summary>
    [ObservableProperty]
    private bool _isLoadingGrammar;

    partial void OnIsLoadingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        TryCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownRefusal))]
    [NotifyPropertyChangedFor(nameof(OffersTryAgain))]
    private Refusal? _refusal;

    /// <summary>The current refusal in the window's words, with the command's own account under Details.</summary>
    /// <summary>The Proposal a trace reads the copy of, or null to trace against the project.</summary>
    public Func<string?>? ProposalIdSource { get; set; }

    /// <summary>Whether the trace was refused because the Proposal's copy was cleaned up, so Try again helps.</summary>
    public bool OffersTryAgain => Refusal?.Code == "trial.scratch-gone";

    public WindowRefusal? ShownRefusal => Refusal is null ? null : WindowRefusal.From(Refusal);

    [ObservableProperty]
    private WordTraceResponse? _result;

    /// <summary>The captured display settings for this trace, or the current project settings before it runs.</summary>
    public TextStyles? TextStyles { get; private set; }

    /// <summary>The default vernacular used for a word typed into Try a Word.</summary>
    public string? WordWritingSystem => TextStyles?.DefaultVernacularId;

    /// <summary>Uses the open project's display settings for typed words and traces without captured settings.</summary>
    public void SetTextStyles(TextStyles styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        _workspaceTextStyles = styles;
        UpdateTextStyles(Result);
    }

    [ObservableProperty]
    private TraceView _view = TraceView.Candidates;

    [ObservableProperty]
    private TraceStepViewModel? _selectedStep;

    [ObservableProperty]
    private bool _showParserRecord;

    public IReadOnlyList<TraceCandidateViewModel> Candidates => _candidates;
    public WordTraceReading? Reading => _reading;

    partial void OnResultChanging(WordTraceResponse? value)
    {
        SelectedStep = null;
        SelectedCandidate = null;
    }

    partial void OnResultChanged(WordTraceResponse? value)
    {
        var allowLiveLinks = _projectPath is not null && value?.Provenance?.CanNavigate == true;
        var directions = WritingSystemsById(value);
        UpdateTextStyles(value);
        _reading = value?.Reading;
        _labels = new TraceDisplayLabels(_reading?.Refs ?? []);
        _refs = (_reading?.Refs ?? []).ToDictionary(reference => reference.Id, StringComparer.Ordinal);
        _candidates = _reading?.Attempts.Select(candidate => new TraceCandidateViewModel(candidate, allowLiveLinks, directions, _labels, _reading.Root, _refs)).ToArray() ?? [];
        _analyses = _reading is null ? [] : _reading.LogicalAnalyses.Count > 0
            ? _reading.LogicalAnalyses.Select((summary, index) => new TraceAnalysisViewModel(
                _reading.Analyses[summary.SourcePositions[0]], allowLiveLinks, directions, index + 1, summary.RecordCount)).ToArray()
            : _reading.Analyses.Select((analysis, index) => new TraceAnalysisViewModel(analysis, allowLiveLinks, directions, index + 1)).ToArray();
        var candidateViews = _candidates.ToDictionary(candidate => candidate.AttemptId!, StringComparer.Ordinal);
        _closestAttempts = _reading?.ClosestAttempts.Select(candidate => candidateViews[candidate.AttemptId!]).ToArray() ?? [];
        ShowDroppedPaths = false;
        ShowParserRecord = false;
        Effort = TraceEffortViewModel.Table(value?.Effort ?? []);
        OnPropertyChanged(nameof(Effort));
        OnPropertyChanged(nameof(InspectorTrace));
        OnPropertyChanged(nameof(HasEffort));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(RecordedRoots));
        OnPropertyChanged(nameof(GrammarSourceText));
        OnPropertyChanged(nameof(HasDiagnosticJson));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ParserWorkText));
        OnPropertyChanged(nameof(StopReason));
        OnPropertyChanged(nameof(HasStopReason));
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(SourceAnalyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(HasNoAnalyses));
        OnPropertyChanged(nameof(AnalysesHeading));
        OnPropertyChanged(nameof(FailedAttemptCount));
        OnPropertyChanged(nameof(HasDroppedPaths));
        OnPropertyChanged(nameof(DroppedPathsText));
        RebuildStopGroups();
        OnPropertyChanged(nameof(SearchStatusText));
        OnPropertyChanged(nameof(AnswerText));
        OnPropertyChanged(nameof(AnswerMark));
        OnPropertyChanged(nameof(ProvenanceWarning));
        OnPropertyChanged(nameof(HasProvenanceWarning));
        OnPropertyChanged(nameof(WritingSystemSummary));
        OnPropertyChanged(nameof(WordWritingSystem));
        OnPropertyChanged(nameof(CaptureDetails));
        OnPropertyChanged(nameof(HasCaptureDetails));
        RebuildFilteredRoots();
        RebuildExpert();
    }

    private void UpdateTextStyles(WordTraceResponse? value)
    {
        var captured = value?.HostCapture?.WritingSystems;
        var styles = captured is { Count: > 0 }
            ? SIL.Motif.App.Services.TextStyles.FromTrace(captured)
            : _workspaceTextStyles;
        if (ReferenceEquals(TextStyles, styles)) return;
        TextStyles = styles;
        OnPropertyChanged(nameof(TextStyles));
        OnPropertyChanged(nameof(WordWritingSystem));
    }

    public ParseProgressViewModel ParseProgress { get; }

    public bool HasResult => Result is not null;

    public bool HasDiagnosticJson => !string.IsNullOrWhiteSpace(DiagnosticJson);

    public string SummaryText => Result is not { } result ? string.Empty : Summarize(result);

    /// <summary>The parser work for this trace as a count of work steps, or a note that none was recorded; blank for a refused trace, which did no parser work.</summary>
    public string ParserWorkText => Result is not { InvalidShape: false } result ? string.Empty
        : result.ParserWorkSteps is { } steps
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.ParserWorkFormat,
                PluralText.Format(MotifAppStrings.WorkStepsPattern, steps))
            : MotifAppStrings.ParserWorkNotRecorded;

    public string? StopReason => Result is { Complete: false } result ? result.StopReason : null;

    public bool HasStopReason => StopReason is { Length: > 0 };

    public IReadOnlyList<TraceEffortViewModel> Effort { get; private set; } = [];

    public bool HasEffort => Effort.Count > 0;

    private static string Summarize(WordTraceResponse result)
    {
        var parts = new List<string> { result.Parsed ? MotifAppStrings.TraceParsed : result.InvalidShape ? ParserRefusals.Title : MotifAppStrings.OutcomeNoParse };
        if (result.Guessed) parts.Add(MotifAppStrings.TraceGuessed);
        if (result.ParserSteps is { } steps)
            parts.Add(PluralText.Format(MotifAppStrings.TraceAnalysisAttemptsPattern, steps));
        var overall = result.HostCapture?.WallElapsedMs is { } capturedElapsed
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceOverallFormat, FormatMs(capturedElapsed))
            : result.ElapsedMs > 0 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceOverallFormat, FormatMs(result.ElapsedMs))
            : MotifAppStrings.TraceOverallNotRecorded;
        if (result.InvalidShape)
            parts.Add(overall);
        else if (result.ParserWorkSteps is { } work)
        {
            parts.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceInParserFormat,
                PluralText.Format(MotifAppStrings.WorkStepsPattern, work), overall));
        }
        else
        {
            parts.Add(MotifAppStrings.WorkStepsNotRecorded);
            parts.Add(overall);
        }
        return string.Join("  ·  ", parts);
    }

    internal static string FormatMs(double ms) => ms switch
    {
        >= 1000 => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.DurationSecondsFormat, ms / 1000),
        >= 10 => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.DurationMillisecondsFormat, ms),
        _ => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.DurationSubMillisecondsFormat, ms),
    };

    private static string? DeepestRuleStepId(TraceStep root)
    {
        string? result = null;
        var deepest = -1;
        Walk(root, 0);
        return result;

        void Walk(TraceStep step, int depth)
        {
            if (depth > deepest && step.Source is not null && step.Type.Contains("Rule", StringComparison.Ordinal))
            {
                deepest = depth;
                result = step.StepId;
            }
            foreach (var child in step.Children) Walk(child, depth + 1);
        }
    }

    public TraceStepViewModel? Root =>
        Result is { } result ? new TraceStepViewModel(result.Reading.Root, DeepestRuleStepId(result.Reading.Root), WritingSystemsById(result), _labels, _refs) : null;

    public IReadOnlyList<TraceStepViewModel> Roots => Root is { } root ? [root] : [];

    /// <summary>The original tree context, with its root expanded without implying attempt membership.</summary>
    public IReadOnlyList<TraceStepViewModel> RecordedRoots => Root is { } root ? [root.WithChildren(root.Children, true)] : [];

    /// <summary>The grammar source returned with this trace, independent of the current workspace.</summary>
    public string GrammarSourceText => Result is not { } result ? string.Empty
        : result.GrammarSourceAvailability == TraceEvidenceAvailability.NotRecorded ? MotifAppStrings.TraceGrammarSourceNotRecorded
        : result.HostCapture?.Baseline is { } baseline
            ? TraceText.Format(MotifAppStrings.TraceGrammarSourceBaselineFormat, baseline.Token.CapturedUtc, baseline.SourceLastWriteUtc, baseline.CaptureDescription)
            : TraceText.Format(MotifAppStrings.TraceGrammarSourceFormat, result.GrammarSource);

    /// <summary>
    /// Logical summaries in first-recorded order, each with the number of source records it represents.
    /// Individual source records remain available through <see cref="SourceAnalyses"/>.
    /// </summary>
    public IReadOnlyList<TraceAnalysisViewModel> Analyses => _analyses;
    public IReadOnlyList<TraceAnalysis> SourceAnalyses => _reading?.Analyses ?? [];

    /// <summary>The trace as the inspector names it, with the Baseline it read; <see langword="null"/> before a trace.</summary>
    public InspectorTrace? InspectorTrace => Result is { } result
        ? new InspectorTrace(result.Word, result.HostCapture?.BundleDigest) : null;

    public bool HasAnalyses => _analyses.Count > 0;

    /// <summary>Whether a result is shown that recorded no analysis at all.</summary>
    public bool HasNoAnalyses => HasResult && !HasAnalyses;

    /// <summary>Over the analyses: the answer and how many there are, such as "Parsed: 1 analysis, 2 source records".</summary>
    public string AnalysesHeading
    {
        get
        {
            var records = _analyses.Sum(analysis => analysis.RecordCount);
            var count = PluralText.Format(MotifAppStrings.TraceAnalysesPattern, _analyses.Count);
            if (records > _analyses.Count)
                count = string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceAnalysesWithRecordsFormat, count,
                    PluralText.Format(MotifAppStrings.TraceSourceRecordsPattern, records));
            return Result is { Parsed: true } ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceParsedAnalysesFormat, count) : count;
        }
    }

    /// <summary>
    /// Whether the word parsed and other attempts stopped. Those are the search's normal tidying up, not a fault, so
    /// they wait folded under <see cref="DroppedPathsText"/>.
    /// </summary>
    public bool HasDroppedPaths => Result is { Parsed: true } && FailedAttemptCount > 0;

    /// <summary>The fold over a parsed word's stopped attempts, saying how many there are and that they are normal.</summary>
    public string DroppedPathsText => PluralText.Format(MotifAppStrings.TraceDroppedPathsPattern, FailedAttemptCount);

    /// <summary>Whether a parsed word's dropped paths are unfolded; a new answer folds them again.</summary>
    [ObservableProperty]
    private bool _showDroppedPaths;

    partial void OnShowDroppedPathsChanged(bool value) => RaiseAttempts();

    /// <summary>Whether the stop groups are on screen: always for a word that failed, unfolded for one that parsed.</summary>
    public bool ShowsStopGroups => HasStopGroups && (!HasDroppedPaths || ShowDroppedPaths);

    /// <summary>Whether the closest attempts are on screen, folded with the stop groups for a word that parsed.</summary>
    public bool ShowsClosestAttempts => HasClosestAttempts && (!HasDroppedPaths || ShowDroppedPaths);

    public int FailedAttemptCount => _candidates.Count(candidate => candidate.IsFailure);

    /// <summary>Whether the word parsed, as the one word a reader wants before anything else.</summary>
    public string AnswerText => Result is not { } result ? string.Empty
        : result.InvalidShape ? ParserRefusals.Title : !result.Complete ? MotifAppStrings.TraceSearchIncomplete : result.Parsed ? MotifAppStrings.TraceParsed : MotifAppStrings.OutcomeNoParse;

    /// <summary>
    /// The mark that answer wears: a trace that built the word has no action glyph, a limit is Stopped,
    /// and one that finished without building it is No parse.
    /// </summary>
    public Mark AnswerMark => Result is { InvalidShape: true } ? Mark.ParserRefusal : Result is { Complete: false } ? Mark.Stopped
        : Result is { Parsed: true } ? new Mark(MarkKind.Outcome, "parsed", string.Empty, MotifAppStrings.TraceParsed)
        : Mark.NoParse;

    /// <summary>Recorded stopped attempts grouped by explicit attribution and reason; empty when nothing failed.</summary>
    public IReadOnlyList<TraceStopGroupViewModel> StopGroups => _stopGroups;

    public bool HasStopGroups => _stopGroups.Count > 0;

    /// <summary>Whether the diagnostic recorded no terminal event.</summary>
    public bool NoAttemptRecorded => Result is { InvalidShape: false } && _reading?.Attempts.Count == 0 && !HasNoParseReasons;
    public IReadOnlyList<string> NoParseReasons => _reading?.NoParseReasons ?? [];
    public bool HasNoParseReasons => Result is { Parsed: false } && NoParseReasons.Count > 0;
    public string NoAttemptNotice => Result is { Complete: false }
        ? MotifAppStrings.TraceNoTerminalAttemptStopped
        : MotifAppStrings.TraceNoTerminalAttempt;

    /// <summary>Over the groups: why the word failed, or, for a word that parsed, why its other attempts did.</summary>
    public string StopGroupsHeading => Result is { Parsed: true } ? MotifAppStrings.TraceWhyOtherAttemptsFailed : MotifAppStrings.TraceWhyNotParsed;

    /// <summary>One line over the groups counting recorded stopped attempts.</summary>
    public string StopGroupsSummary
    {
        get
        {
            if (_stopGroups.Count == 0) return string.Empty;
            var attempts = _stopGroups.Sum(group => group.Count);
            return PluralText.Format(MotifAppStrings.TraceAttemptsFailedPattern, attempts);
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
    public string ClosestAttemptsHeading => ClosestAttempts.Count == 1
        ? MotifAppStrings.TraceClosestAttemptsHeadingOne : MotifAppStrings.TraceClosestAttemptsHeadingMany;

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
        OnPropertyChanged(nameof(ParserWorkText));
        OnPropertyChanged(nameof(ShowsChosenWord));
        OnPropertyChanged(nameof(ShowsOtherWord));
    }

    /// <summary>What the button under the attempts offers, or empty when they are all on screen.</summary>
    public string MoreAttemptsText
    {
        get
        {
            var hidden = MatchingAttempts().Count() - ClosestAttempts.Count;
            var others = PluralText.Format(MotifAppStrings.TraceMoreAttemptsCountPattern, hidden);
            return hidden <= 0 ? string.Empty
                : SelectedStopGroup is { } group
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceShowOtherRefusedByFormat, others, group.RuleText)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceShowOtherFormat, others);
        }
    }

    public bool HasMoreAttempts => MoreAttemptsText.Length > 0;

    /// <summary>Shows every failed attempt of the chosen group instead of the closest few.</summary>
    public IRelayCommand ShowEveryAttemptCommand { get; }

    /// <summary>Chooses a stopping rule to filter the attempts by; the same one again clears the filter.</summary>
    public IRelayCommand<TraceStopGroupViewModel?> SelectStopGroupCommand { get; }

    private const int ClosestShown = 3;

    private IEnumerable<TraceCandidateViewModel> MatchingAttempts() => _closestAttempts
        .Where(candidate => SelectedStopGroup is not { } group || group.Matches(candidate));

    private void RebuildStopGroups()
    {
        _stopGroups = _reading?.StopGroups.Select(group => new TraceStopGroupViewModel(
            _labels.Resolve(group.RuleRefId, group.Rule), group.ReasonCode,
            group.Explanation ?? TraceFailureSentences.Explain(group.ReasonCode,
                _labels.Resolve(group.RuleRefId, group.Rule), group.Attempts.FirstOrDefault()?.FailureRequired), group.Count, group.RuleId,
            group.Attempts.Select(attempt => attempt.AttemptId!).ToHashSet(StringComparer.Ordinal))).ToArray() ?? [];
        var largest = _stopGroups.Count <= 1 ? 0 : _stopGroups.Max(group => group.Count);
        foreach (var group in _stopGroups) group.SetShare(largest);
        SelectedStopGroup = null;
        OnPropertyChanged(nameof(StopGroups));
        OnPropertyChanged(nameof(HasStopGroups));
        OnPropertyChanged(nameof(NoAttemptRecorded));
        OnPropertyChanged(nameof(NoParseReasons));
        OnPropertyChanged(nameof(HasNoParseReasons));
        OnPropertyChanged(nameof(NoAttemptNotice));
        OnPropertyChanged(nameof(StopGroupsHeading));
        OnPropertyChanged(nameof(StopGroupsSummary));
        RaiseAttempts();
    }

    private void RaiseAttempts()
    {
        OnPropertyChanged(nameof(ClosestAttempts));
        OnPropertyChanged(nameof(HasClosestAttempts));
        OnPropertyChanged(nameof(ClosestAttemptsHeading));
        OnPropertyChanged(nameof(ShowsStopGroups));
        OnPropertyChanged(nameof(ShowsClosestAttempts));
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
            : MotifAppStrings.TraceProvenanceNotRecorded;

    private static IReadOnlyDictionary<string, TraceWritingSystem> WritingSystemsById(WordTraceResponse? result) =>
        result?.HostCapture?.WritingSystems.GroupBy(system => system.Id).ToDictionary(group => group.Key, group => group.First())
        ?? new Dictionary<string, TraceWritingSystem>();

    public string WritingSystemSummary
    {
        get
        {
            var systems = Result?.HostCapture?.WritingSystems;
            if (systems is null || systems.Count == 0) return MotifAppStrings.TraceWritingSystemsNotRecorded;
            var summary = string.Join(", ", systems.Select(system =>
                TraceText.Format(MotifAppStrings.TraceWritingSystemFormat, system.Id,
                    system.Direction ?? MotifAppStrings.TraceDirectionNotRecorded, system.Font ?? MotifAppStrings.TraceFontUiFallback)));
            return TraceText.Format(MotifAppStrings.TraceWritingSystemsSummaryFormat, summary);
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
                if (!string.IsNullOrWhiteSpace(value)) parts.Add(TraceText.Format(MotifAppStrings.TraceLabelValueFormat, label, value));
            }

            Add(MotifAppStrings.TraceCaptureFormatLabel, result.DiagnosticFormat);
            Add(MotifAppStrings.TraceCaptureParserLabel, result.ParserName);
            Add(MotifAppStrings.TraceCaptureParserVersionLabel, result.ParserVersion);
            Add(MotifAppStrings.TraceCaptureProfileLabel, result.TraceProfile);
            Add(MotifAppStrings.TraceCaptureGrammarHashLabel, result.GrammarHash ?? capture?.GrammarHash);
            Add(MotifAppStrings.TraceCaptureGrammarHashSemanticsLabel, result.GrammarHashSemantics ?? capture?.GrammarHashSemantics);
            Add(MotifAppStrings.TraceCaptureProjectIdentityLabel, capture?.ProjectIdentity);
            Add(MotifAppStrings.TraceCaptureBundleDigestLabel, capture?.BundleDigest);
            if (capture?.Baseline is { } baseline)
            {
                parts.Add(TraceText.Format(MotifAppStrings.TraceBaselineCapturedFormat, baseline.Token.CapturedUtc));
                parts.Add(TraceText.Format(MotifAppStrings.TraceSourceSavedFormat, baseline.SourceLastWriteUtc));
                parts.Add(baseline.CaptureDescription);
            }
            else parts.Add(MotifAppStrings.TraceBaselineSourceNotRecorded);
            if (result.GrammarSourceAvailability == TraceEvidenceAvailability.NotRecorded)
                parts.Add(MotifAppStrings.TraceGrammarSourceNotRecorded);
            if (capture?.CapturedUtc is { } capturedUtc) parts.Add(TraceText.Format(MotifAppStrings.TraceCapturedAtFormat, capturedUtc));
            if (capture?.WallElapsedMs is { } elapsed) parts.Add(TraceText.Format(MotifAppStrings.TraceHostElapsedFormat, elapsed.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
            return parts.Count == 0 ? MotifAppStrings.TraceCaptureDetailsNotRecorded : string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildFilteredRoots();

    [ObservableProperty]
    private string _ruleFilter = string.Empty;

    private ProjectFilterMemory? _filterMemory;

    internal void UseViewMemory(ProjectViewMemory memory) => _filterMemory = new(memory, "Trace",
        () => RuleFilter, saved => RuleFilter = saved ?? string.Empty);

    partial void OnRuleFilterChanged(string value)
    {
        RebuildFilteredRoots();
        _filterMemory?.Changed();
    }

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
            if (OutcomeFilter != TraceOutcomeFilter.All) values.Add(OutcomeFilter == TraceOutcomeFilter.Failed ? MotifAppStrings.TraceFilterFailed : MotifAppStrings.TraceFilterSucceeded);
            if (!string.IsNullOrWhiteSpace(RuleFilter)) values.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceFilterRuleFormat, RuleFilter.Trim()));
            if (!string.IsNullOrWhiteSpace(MorphFilter)) values.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceFilterMorphFormat, MorphFilter.Trim()));
            return values.Count == 0
                ? (HiddenStepCount == 0 ? MotifAppStrings.TraceFilterAllSteps
                    : PluralText.Format(MotifAppStrings.TraceStepsHiddenPattern, HiddenStepCount))
                : string.Join(", ", values) + (HiddenStepCount == 0 ? string.Empty
                    : " · " + PluralText.Format(MotifAppStrings.TraceFilterHiddenSuffixPattern, HiddenStepCount));
        }
    }

    public string SearchStatusText
    {
        get
        {
            if (Result is not { } result) return string.Empty;
            var status = result.SearchCompletion switch
            {
                TraceSearchCompletion.InvalidShape =>
                    ParserRefusals.Title + ": " + ParserRefusals.InvalidShape.Reason,
                TraceSearchCompletion.Incomplete =>
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceSearchIncompleteFormat,
                        string.IsNullOrWhiteSpace(result.StopReason) ? MotifAppStrings.TraceReasonNotRecorded : result.StopReason),
                TraceSearchCompletion.Complete => MotifAppStrings.TraceSearchComplete,
                _ => MotifAppStrings.TraceSearchCompletionNotRecorded,
            };
            return HiddenStepCount > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TraceStatusHiddenFormat, status,
                    PluralText.Format(MotifAppStrings.TraceHiddenByFiltersPattern, HiddenStepCount))
                : status;
        }
    }

    /// <summary>The raw producer document. Display filters never alter this string.</summary>
    public string DiagnosticJson => _diagnosticJson ?? Result?.DiagnosticJson ?? string.Empty;

    /// <summary>Loads a producer v1/v2 diagnostic through the Commands projection; no parser or project is opened.</summary>
    /// <returns>The loaded diagnostic, or the typed refusal that says why it could not be read.</returns>
    public static CommandOutcome<TraceWordViewModel> LoadDiagnostic(string json, TraceHostCapture? current = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var outcome = WordTraceQuery.LoadDiagnostic(json, current: current);
        if (!outcome.Succeeded) return CommandOutcome<TraceWordViewModel>.Refused(outcome.Refusal!);
        var viewModel = new TraceWordViewModel { Result = outcome.Value };
        viewModel._diagnosticJson = json;
        return CommandOutcome<TraceWordViewModel>.Success(viewModel);
    }

    /// <summary>As <see cref="LoadDiagnostic"/>, but a refused document throws <see cref="JsonException"/>.</summary>
    public static TraceWordViewModel FromDiagnosticJson(string json, TraceHostCapture? current = null)
    {
        var outcome = LoadDiagnostic(json, current);
        return outcome.Succeeded ? outcome.Value! : throw new JsonException(outcome.Refusal!.Message);
    }

    public bool ShowCandidates => View == TraceView.Candidates;

    public bool ShowFullDerivation => View == TraceView.FullDerivation;

    public string FocusToggleLabel => IsFocused ? MotifAppStrings.TraceShowAnalyses : MotifAppStrings.TraceFocusThisWord;

    public IAsyncRelayCommand TryCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand<TraceView> SetViewCommand { get; }

    public void SetProjectPath(string? fwDataPath)
    {
        _projectPath = fwDataPath;
        _filterMemory?.Open(fwDataPath);
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(IsStandalone));
        TryCommand.NotifyCanExecuteChanged();
        if (Result is not null) OnResultChanged(Result);
    }

    public void SetWord(string word) => WordToTry = word;

    /// <summary>Cancels the word trace and returns once its invocation and progress tracking have ended.</summary>
    public async Task StopAsync()
    {
        CancelRunning();
        if (TryCommand.IsRunning && TryCommand.ExecutionTask is { } running)
            await running.ConfigureAwait(true);
    }

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

    // Listens only while a word is pending, so the static session event never outlives the word it serves.
    private IDisposable WatchGrammarLoad(ITryWordSessions sessions, string path)
    {
        void Refresh(object? sender, EventArgs e) =>
            IsLoadingGrammar = IsLoading && sessions.GetPhase(path) == WarmSessionPhase.Loading;
        sessions.PhaseChanged += Refresh;
        Refresh(null, EventArgs.Empty);
        return new GrammarWatch(() =>
        {
            sessions.PhaseChanged -= Refresh;
            IsLoadingGrammar = false;
        });
    }

    private sealed class GrammarWatch(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

    private async Task TryAsync()
    {
        if (IsLoading || ParseProgress.IsActive || TryCommand.IsRunning || _projectPath is not { } path) return;
        var word = WordToTry.Trim();
        if (word.Length == 0) return;
        // Trying a word asks to read its story, which needs the full width, not the analyses beside it.
        IsFocused = true;

        CancelRunning();
        using var running = new CancellationTokenSource();
        _running = running;
        var generation = ++_generation;
        var previousResult = Result;
        var previousDiagnostic = _diagnosticJson;
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

        using var usageAction = _commandClient.BeginUsageAction("word trace",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("word"));
        var progress = new Progress<AssessmentProgress>(value =>
        {
            if (generation == _generation && IsLoading) ParseProgress.Report(value);
        });
        using var grammarWatch = WatchGrammarLoad(TryWordSessions, path);
        var outcome = await ParseProgress.TrackAsync(() =>
            _commandClient.TraceWordAsync(new WordTraceRequest(path, word, ProposalIdSource?.Invoke()), running.Token, progress))
            .ConfigureAwait(true);
        if (ReferenceEquals(_running, running)) _running = null;
        if (generation != _generation) return;

        IsLoading = false;
        if (!outcome.Succeeded)
        {
            Refusal = outcome.Refusal;
            if (outcome.Refusal?.Reason == FailureReason.Cancelled)
            {
                Result = previousResult;
                _diagnosticJson = previousDiagnostic;
            }
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
            node.Source?.Contains(RuleFilter.Trim(), StringComparison.OrdinalIgnoreCase) != true &&
            node.RecordedStep.Source?.Contains(RuleFilter.Trim(), StringComparison.OrdinalIgnoreCase) != true) return false;
        var morphText = string.Join("\n", node.Type, node.Source, node.RecordedStep.Source, node.Input, node.Output, node.FailureReason,
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
    public TraceAnalysisViewModel(TraceAnalysis analysis, bool allowLiveLinks, IReadOnlyDictionary<string, TraceWritingSystem>? directions = null, int? position = null, int recordCount = 1)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        AnalysisId = analysis.AnalysisId;
        Index = analysis.Index;
        Surface = analysis.Surface ?? MotifAppStrings.TraceSurfaceNotRecorded;
        Availability = string.IsNullOrWhiteSpace(analysis.Availability) ? MotifAppStrings.TraceAvailabilityNotRecorded : analysis.Availability;
        ProjectionStatus = analysis.ProjectionStatus;
        ProjectionError = analysis.ProjectionError;
        LegacyMorphemes = analysis.LegacyMorphemes;
        RecordCount = recordCount;
        Position = position;
        Morphs = analysis.Morphs.Select(morph => new TraceMorphViewModel(morph, allowLiveLinks, directions)).ToArray();
    }

    public string? AnalysisId { get; }
    public int? Index { get; }
    public string Surface { get; }
    public string Availability { get; }
    public string? ProjectionStatus { get; }
    public string? ProjectionError { get; }
    public string? LegacyMorphemes { get; }
    /// <summary>Whether producer morpheme labels lack a projected morph list for the compact comparison.</summary>
    public bool HasUnprojectedMorphemes => !HasProjectedMorphemes && HasLegacyMorphemes;

    /// <summary>Whether the recorded analysis includes projected morphs.</summary>
    public bool HasProjectedMorphemes => Morphs.Count > 0;

    public bool HasLegacyMorphemes => LegacyMorphemes is { Length: > 0 };
    /// <summary>The parser's recorded morphemes, shown without assigning FieldWorks identities or roles.</summary>
    public string LegacyMorphemesText => HasLegacyMorphemes
        ? TraceText.Format(MotifAppStrings.TraceMorphemesFormat, string.Join(" + ", LegacyMorphemes!.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        : string.Empty;

    /// <summary>The parser's own names for the analysis's morphemes, kept for the tooltip on its surface form.</summary>
    public string? LegacyMorphemesTip => HasLegacyMorphemes ? TraceText.Format(MotifAppStrings.TraceParserMorphemesTipFormat, LegacyMorphemes) : null;
    public IReadOnlyList<TraceMorphViewModel> Morphs { get; }

    /// <summary>The analysis's place among the distinct analyses, from 1, once duplicates are merged.</summary>
    public int? Position { get; private init; }

    /// <summary>How many times the search recorded this analysis, without asserting distinct derivations.</summary>
    public int RecordCount { get; private init; } = 1;

    public bool HasRepeatedRecords => RecordCount > 1;

    /// <summary>"Recorded twice" for an analysis recorded more than once, otherwise empty.</summary>
    public string RecordedCountText => HasRepeatedRecords
        ? PluralText.Format(MotifAppStrings.TraceRecordedTimesPattern, RecordCount) : string.Empty;

    public string Label => Position is { } position ? TraceText.Format(MotifAppStrings.TraceAnalysisNumberFormat, position)
        : Index is { } index ? TraceText.Format(MotifAppStrings.TraceAnalysisNumberFormat, index + 1)
        : AnalysisId is { Length: > 0 } id ? TraceText.Format(MotifAppStrings.TraceAnalysisNumberFormat, id) : MotifAppStrings.TraceRecordedAnalysis;
    public bool HasProjectionError => ProjectionError is { Length: > 0 };

    /// <summary>
    /// Why the analysis has no FieldWorks morphemes, in plain words; the parser's own account is the tooltip.
    /// </summary>
    public string ProjectionErrorText => MotifAppStrings.TraceProjectionErrorText;
}

/// <summary>One rich morph record. Missing fields are named as unavailable rather than inferred from another field.</summary>
public sealed class TraceMorphViewModel
{
    public TraceMorphViewModel(TraceMorph morph, bool allowLiveLink, IReadOnlyDictionary<string, TraceWritingSystem>? directions = null)
    {
        ArgumentNullException.ThrowIfNull(morph);
        FormWritingSystem = morph.FormWritingSystem;
        HeadwordWritingSystem = morph.HeadwordWritingSystem;
        GlossWritingSystem = morph.GlossWritingSystem;
        RawDetails = morph.RawJson ?? MotifAppStrings.TraceRawMorphNotRecorded;
        MsaDetails = FormatMsaDetails(morph.RawJson);
        Form = ValueOrUnavailable(morph.Form, MotifAppStrings.TraceMorphFormLabel);
        Headword = ValueOrUnavailable(morph.Headword, MotifAppStrings.TraceMorphHeadwordLabel);
        Gloss = ValueOrUnavailable(morph.Gloss, MotifAppStrings.TraceMorphGlossLabel);
        Category = ValueOrUnavailable(morph.CategoryName ?? morph.Category, MotifAppStrings.TraceMorphCategoryLabel);
        Slot = ValueOrUnavailable(morph.Slot, MotifAppStrings.TraceMorphSlotLabel);
        InflectionClass = ValueOrUnavailable(morph.InflectionClassName ?? morph.InflectionClass, MotifAppStrings.TraceMorphInflectionClassLabel);
        Features = ValueOrUnavailable(morph.Features, MotifAppStrings.TraceMorphFeaturesLabel);
        GuessedString = ValueOrUnavailable(morph.GuessedString, MotifAppStrings.TraceMorphGuessedStringLabel);
        Identity = ValueOrUnavailable(morph.Identity, MotifAppStrings.TraceMorphIdentityLabel);
        IdentityQuality = ValueOrUnavailable(morph.IdentityQuality, MotifAppStrings.TraceMorphIdentityQualityLabel);
        FormId = ValueOrUnavailable(morph.FormId, MotifAppStrings.TraceMorphFormIdLabel);
        EntryId = ValueOrUnavailable(morph.EntryId, MotifAppStrings.TraceMorphEntryIdLabel);
        MsaId = ValueOrUnavailable(morph.MsaId, MotifAppStrings.TraceMorphMsaIdLabel);
        InflTypeId = ValueOrUnavailable(morph.InflTypeId, MotifAppStrings.TraceMorphInflTypeIdLabel);
        WritingSystems = string.Join("; ", new[] {
            FormatWs(MotifAppStrings.TraceWsRoleForm, morph.FormWritingSystem),
            FormatWs(MotifAppStrings.TraceWsRoleHeadword, morph.HeadwordWritingSystem),
            FormatWs(MotifAppStrings.TraceWsRoleGloss, morph.GlossWritingSystem),
        }.Where(value => value is not null)!);
        Details = string.Join(" · ", new[] {
            LabelValue(MotifAppStrings.TraceMorphSlotLabel, Slot), LabelValue(MotifAppStrings.TraceMorphInflectionClassLabel, InflectionClass),
            LabelValue(MotifAppStrings.TraceMorphFeaturesLabel, Features), LabelValue(MotifAppStrings.TraceMorphGuessedLabel, GuessedString),
            LabelValue(MotifAppStrings.TraceMorphFormIdLabel, FormId), LabelValue(MotifAppStrings.TraceMorphEntryIdLabel, EntryId),
            LabelValue(MotifAppStrings.TraceMorphMsaIdLabel, MsaId), LabelValue(MotifAppStrings.TraceMorphInflTypeIdLabel, InflTypeId),
            LabelValue(MotifAppStrings.TraceMorphWritingSystemsLabel, WritingSystems.Length == 0 ? MotifAppStrings.TraceValueNotRecorded : WritingSystems),
        });
        var subject = InspectorSubject.Morpheme(morph.FormId, morph.MsaId, morph.Form ?? morph.Headword, morph.Gloss);
        InspectSubject = subject is null ? null : subject with { IdentityQuality = morph.IdentityQuality ?? MotifAppStrings.TraceIdentityQualityUnknown };
        Link = allowLiveLink && Uri.TryCreate(morph.FieldWorksLink, UriKind.Absolute, out var link) &&
               string.Equals(link.Scheme, "silfw", StringComparison.OrdinalIgnoreCase) ? link : null;
        var toolName = FieldWorksLinks.ToolNameOf(morph.FieldWorksLink);
        LinkName = string.IsNullOrWhiteSpace(morph.Headword)
            ? TraceText.Format(MotifAppStrings.TraceOpenEntryFormat, Form, toolName)
            : TraceText.Format(MotifAppStrings.TraceOpenHeadwordFormat, morph.Headword, toolName);
    }

    private static string ValueOrUnavailable(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? TraceText.Format(MotifAppStrings.TraceFieldNotRecordedFormat, label) : value;

    private static string LabelValue(string label, string value) => TraceText.Format(MotifAppStrings.TraceLabelValueFormat, label, value);

    private static string FormatMsaDetails(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return MotifAppStrings.TraceMsaNotRecorded;
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return MotifAppStrings.TraceMsaNotRecorded;
            var fields = new (string Label, string Name)[]
            {
                (MotifAppStrings.TraceMsaKindLabel, "kind"),
                (MotifAppStrings.TraceMsaFromCategoryLabel, "fromCategory"),
                (MotifAppStrings.TraceMsaToCategoryLabel, "toCategory"),
                (MotifAppStrings.TraceMsaFromInflectionClassLabel, "fromInflectionClass"),
                (MotifAppStrings.TraceMsaToInflectionClassLabel, "toInflectionClass"),
                (MotifAppStrings.TraceMsaFromFeaturesLabel, "fromFeatures"),
                (MotifAppStrings.TraceMsaToFeaturesLabel, "toFeatures"),
                (MotifAppStrings.TraceMsaAttachesToLabel, "attachesTo"),
                (MotifAppStrings.TraceMsaSlotsLabel, "slots"),
                (MotifAppStrings.TraceMorphInflectionClassLabel, "inflectionClass"),
            };
            var values = new List<string>();
            foreach (var field in fields)
            {
                if (TryDisplay(root, field.Name, out var value) || (root.TryGetProperty("msa", out var msa) &&
                    TryDisplay(msa, field.Name, out value)))
                    values.Add(LabelValue(field.Label, value));
            }

            return values.Count == 0 ? MotifAppStrings.TraceMsaNotRecorded : string.Join(" · ", values);
        }
        catch (JsonException)
        {
            return MotifAppStrings.TraceMsaUnavailable;
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
        JsonValueKind.Null => MotifAppStrings.TraceValueNotRecorded,
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(DisplayValue)),
        JsonValueKind.Object when value.TryGetProperty("text", out var text) => DisplayValue(text),
        JsonValueKind.Object when value.TryGetProperty("form", out var form) => DisplayValue(form),
        JsonValueKind.Object when value.TryGetProperty("name", out var name) => DisplayValue(name) +
            (value.TryGetProperty("optional", out var optional) ? TraceText.Format(MotifAppStrings.TraceMsaOptionalFormat, DisplayValue(optional)) : ""),
        JsonValueKind.Object => string.Join("; ", value.EnumerateObject().Select(field => LabelValue(field.Name, DisplayValue(field.Value)))),
        _ => value.GetRawText(),
    };
    public string? FormWritingSystem { get; }
    public string? HeadwordWritingSystem { get; }
    public string? GlossWritingSystem { get; }

    private static string? FormatWs(string role, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TraceText.Format(MotifAppStrings.TraceWsRoleFormat, role, value);

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

    /// <summary>The morph as the inspector looks it up, by identity; <see langword="null"/> when the trace names no id.</summary>
    public InspectorSubject? InspectSubject { get; }

    /// <summary>What the trace recorded about the morph, for the inspector to show apart from the Baseline's facts.</summary>
    public IReadOnlyList<InspectorDetail> Captured => InspectorDetail.Recorded((MotifAppStrings.TraceMorphFormLabel, Form), (MotifAppStrings.TraceMorphHeadwordLabel, Headword),
        (MotifAppStrings.TraceMorphGlossLabel, Gloss), (MotifAppStrings.TraceMorphCategoryLabel, Category),
        (MotifAppStrings.TraceMorphSlotLabel, Slot), (MotifAppStrings.TraceMorphFeaturesLabel, Features));
    public bool CanInspect => InspectSubject is not null;
    public bool CannotInspect => InspectSubject is null;
    public string Details { get; }
    public string WritingSystems { get; }
    public Uri? Link { get; }
    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;
    public string LinkName { get; }

    /// <summary>The link's own text: what opens, and the FieldWorks tool it opens in.</summary>
    public string LinkText => $"{LinkName} ↗";
}

/// <summary>
/// Recorded stopped attempts sharing an explicit stopping ref and reason, or one unattributed terminal outcome.
/// </summary>
public sealed partial class TraceStopGroupViewModel : ObservableObject
{
    public TraceStopGroupViewModel(string? rule, string? reasonCode, string? explanation, int count,
        string? ruleId = null, IReadOnlySet<string>? attemptIds = null)
    {
        Rule = rule;
        RuleId = ruleId;
        _attemptIds = attemptIds;
        ReasonCode = reasonCode;
        Explanation = explanation;
        Count = count;
        RuleText = rule is { Length: > 0 } named ? named : MotifAppStrings.TraceRecordedRefusal;
        ReasonText = explanation is { Length: > 0 } sentence ? sentence
            : reasonCode is { Length: > 0 } code ? TraceStepKinds.ExplainReason(code)
            : MotifAppStrings.TraceDidNotRecordWhy;
        CountText = PluralText.Format(MotifAppStrings.TraceAttemptsPattern, count);
    }

    /// <summary>The captured FieldWorks name or producer label; null when no stopping rule was recorded.</summary>
    public string? Rule { get; }
    public string? RuleId { get; }

    /// <summary>The parser's own reason code, kept for matching the group to its attempts.</summary>
    public string? ReasonCode { get; }

    public string? Explanation { get; }

    /// <summary>The recorded rule name, or a neutral heading when no rule was attributed.</summary>
    public string RuleText { get; }

    /// <summary>Why the attempts stopped, in the plain language FieldWorks uses where there is one.</summary>
    public string ReasonText { get; }

    public int Count { get; }

    public string CountText { get; }

    public bool HasRule => Rule is { Length: > 0 };

    /// <summary>How long this group's bar is: 1 for the largest group.</summary>
    public double Share { get; private set; }
    public bool ShowsShare => Share > 0;

    /// <summary>Whether the attempt list is filtered to this group.</summary>
    [ObservableProperty]
    private bool _isSelected;

    internal void SetShare(int largest)
    {
        Share = largest <= 0 ? 0 : (double)Count / largest;
        OnPropertyChanged(nameof(Share));
        OnPropertyChanged(nameof(ShowsShare));
    }

    private readonly IReadOnlySet<string>? _attemptIds;

    internal bool Matches(TraceCandidateViewModel candidate) =>
        candidate.AttemptId is { } id && _attemptIds?.Contains(id) == true;
}

/// <summary>One candidate attempt, kept separate from recorded analyses.</summary>
public sealed class TraceCandidateViewModel : ObservableObject
{
    public TraceCandidateViewModel(TraceCandidate candidate, bool allowLiveLinks = false,
        IReadOnlyDictionary<string, TraceWritingSystem>? directions = null, TraceDisplayLabels? labels = null,
        TraceStep? root = null, IReadOnlyDictionary<string, TraceRef>? refs = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Morphs = candidate.Morphs
            .Where(morph => !string.IsNullOrWhiteSpace(morph.Form) && morph.Form.Trim() != "?")
            .Select(ParserReadingMorphViewModel.ForTrace).ToArray();
        RichMorphs = candidate.RichMorphs.Select(morph => new TraceMorphViewModel(morph, allowLiveLinks, directions)).ToArray();
        Succeeded = candidate.Succeeded;
        AttemptId = candidate.AttemptId;
        FailureReason = candidate.FailureReason;
        Explanation = candidate.ExplanationAvailability == TraceEvidenceAvailability.Recorded ? candidate.Explanation : null;
        ContextualFailure = candidate.ContextualFailure;
        FailureRequired = candidate.FailureRequired;
        FailureActual = candidate.FailureActual;
        FailureEnvironment = candidate.FailureEnvironment;
        IsBlocked = candidate.OutcomeStatus == "blocked" || candidate.Steps.LastOrDefault()?.Type == "Blocked";
        IsFailure = !candidate.Succeeded && !IsBlocked && (candidate.OutcomeStatus is "failed" or "failure" || candidate.FailureReason is { Length: > 0 } || candidate.ContextualFailure is { Length: > 0 } || candidate.Steps.Any(step => step.FailureReason is { Length: > 0 }));
        SourceIdentity = candidate.SourceIdentityId is { Length: > 0 }
            ? TraceText.Format(MotifAppStrings.TraceSourceIdentityFormat, candidate.SourceIdentityKind ?? MotifAppStrings.TraceSourceKindLower,
                candidate.SourceIdentityId, candidate.SourceIdentityQuality ?? MotifAppStrings.TraceQualityNotRecorded)
            : MotifAppStrings.TraceSourceIdentityNotRecorded;
        HasTreeContext = candidate.TreeContext.Count > 0;
        _loadContext = () => root is null ? [] : TraceTreeContextRange.Resolve(root, candidate.TreeContext)
            .Select(step => new TraceStepViewModel(step, null, directions, labels, refs)).ToArray();
        Steps = candidate.Steps.Select(step => new TraceStepViewModel(step, deepestStepId: null, directions, labels, refs)).ToArray();
        Text = RichMorphs.Count > 0 ? string.Join(" + ", RichMorphs.Select(morph => morph.Form)) : Morphs.Count > 0 ? string.Join(" + ", Morphs.Select(morph => morph.Form)) : Steps.LastOrDefault()?.Source ?? MotifAppStrings.TraceRecordedAttempt;
        Gloss = string.Join(" + ", Morphs.Select(morph => morph.GlossOrPlaceholder));
        Surface = candidate.Surface;
        StoppedByRule = labels?.Resolve(candidate.StoppedByRefId, candidate.StoppedByRule) ?? candidate.StoppedByRule;
        StoppedByRuleId = candidate.StoppedByRuleId;
        StopHeadline = Succeeded ? MotifAppStrings.TraceKindBuiltTheWord
            : StoppedByRule is { Length: > 0 } rule ? TraceText.Format(MotifAppStrings.TraceRefusedByFormat, rule)
            : MotifAppStrings.TraceKindNoAnalysisFound;
        StopReason = Explanation is { Length: > 0 } explanation ? explanation
            : FailureReason is { Length: > 0 } code ? TraceFailureSentences.Explain(code, StoppedByRule, FailureRequired)
            : MotifAppStrings.TraceReasonNotRecorded;
    }

    /// <summary>The parser's own reason code, kept out of the sentence and shown only among the steps.</summary>
    public string? ParserCode => FailureReason;

    public bool HasParserCode => FailureReason is { Length: > 0 };

    /// <summary>The form the attempt had built when it ended.</summary>
    public string? Surface { get; }

    public bool HasSurface => Surface is { Length: > 0 };

    /// <summary>The explicitly linked stopping rule, by its captured FieldWorks name when known.</summary>
    public string? StoppedByRule { get; }
    public string? StoppedByRuleId { get; }

    /// <summary>What ended the attempt, in a few words: which rule, or that the attempt simply stopped.</summary>
    public string StopHeadline { get; }

    /// <summary>Why, in the plain language FieldWorks uses.</summary>
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
    public bool IsBlocked { get; }
    public string SourceIdentity { get; }
    public string FailureContext => string.Join(" · ", new[] { ContextualFailure, FailureReason, Explanation }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text
        ? text
        : MotifAppStrings.TraceFailureContextNotRecorded;
    public IReadOnlyList<TraceStepViewModel> Steps { get; }
    private readonly Func<IReadOnlyList<TraceStepViewModel>> _loadContext;
    private IReadOnlyList<TraceStepViewModel>? _context;
    private bool _isTreeContextExpanded;
    public bool HasTreeContext { get; }
    public bool IsTreeContextExpanded
    {
        get => _isTreeContextExpanded;
        set
        {
            if (SetProperty(ref _isTreeContextExpanded, value)) OnPropertyChanged(nameof(RecordedTreeContext));
        }
    }
    public IReadOnlyList<TraceStepViewModel> RecordedTreeContext => !IsTreeContextExpanded ? [] : _context ??= _loadContext();
    public string Text { get; }
    public string Gloss { get; }
    public string StatusText => IsBlocked ? MotifAppStrings.TraceKindBlocked : Succeeded ? MotifAppStrings.TraceStatusBuiltTheWord
        : IsFailure ? MotifAppStrings.TraceStepRefused : MotifAppStrings.TraceStepTried;

    /// <summary>How this attempt ended: it built the word, a rule refused it, or it was only tried.</summary>
    public Mark StepMark => Mark.Of(Succeeded ? TraceStepMark.Built : IsFailure ? TraceStepMark.Refused : TraceStepMark.Tried);
}

/// <summary>One row of aggregate parser effort, explicitly separated from selected-step details.</summary>
public sealed class TraceEffortViewModel
{
    public TraceEffortViewModel(TraceEffort effort) : this(effort, effort) { }

    private TraceEffortViewModel(TraceEffort effort, TraceEffort largest)
    {
        ArgumentNullException.ThrowIfNull(effort);
        Kind = effort.Kind == "Unattributed" ? MotifAppStrings.TimingNotAttributedLabel : effort.Kind;
        Work = effort.Work.ToString("N0");
        Uses = effort.Uses.ToString("N0");
        Tried = effort.Attempts.ToString("N0");
        Produced = effort.Outputs.ToString("N0");
        var misses = new List<string>();
        if (effort.NotApplied > 0) misses.Add(TraceText.Format(MotifAppStrings.TraceEffortDidNotApplyFormat, effort.NotApplied.ToString("N0")));
        if (effort.NoRoot > 0) misses.Add(TraceText.Format(MotifAppStrings.TraceEffortNoRootFormat, effort.NoRoot.ToString("N0")));
        if (effort.SurfaceMismatch > 0) misses.Add(TraceText.Format(MotifAppStrings.TraceEffortSurfaceMismatchFormat, effort.SurfaceMismatch.ToString("N0")));
        Missed = misses.Count == 0 ? "—" : string.Join(", ", misses);
        Time = effort.WorkSteps is { } steps ? PluralText.Format(MotifAppStrings.WorkStepsPattern, steps) : MotifAppStrings.WorkStepsNotRecorded;

        WorkHeat = Shade(effort.Work, largest.Work);
        UsesHeat = Shade(effort.Uses, largest.Uses);
        TriedHeat = Shade(effort.Attempts, largest.Attempts);
        ProducedHeat = Shade(effort.Outputs, largest.Outputs);
        MissedHeat = Shade(effort.NotApplied + effort.NoRoot + effort.SurfaceMismatch,
            largest.NotApplied + largest.NoRoot + largest.SurfaceMismatch);
        TimeHeat = Shade(effort.WorkSteps ?? 0, largest.WorkSteps ?? 0);
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
            WorkSteps = rows.Max(row => row.WorkSteps ?? 0),
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

/// <summary>One recorded derivation event, with its own children and shared row selection state.</summary>
public sealed class TraceStepViewModel : ObservableObject
{
    private bool _isExpanded;
    /// <summary>Remembers expansion while an off-screen tree node has no container.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    private bool _isSeparatePlainEvent;
    public bool IsSeparatePlainEvent
    {
        get => _isSeparatePlainEvent;
        set => SetProperty(ref _isSeparatePlainEvent, value);
    }
    public bool CanInspectBuilding => CanInspect && !Type.Contains("Analysis", StringComparison.Ordinal);
    public bool HasUnlinkedBuildingSource => HasUnlinkedSource && !Type.Contains("Analysis", StringComparison.Ordinal);

    public string BuildingText => Type.Contains("Analysis", StringComparison.Ordinal) && Output is { Length: > 0 }
            ? TraceText.Format(MotifAppStrings.TraceBuildingStemFormFormat, Output)
        : Type == "StratumSynthesisInput" ? TraceText.Format(MotifAppStrings.TraceBuildingStemUsedFormat, Input)
        : Type == "LexicalLookup" && RecordedStep.EventEvidence?.LookupResult is { MatchCount: > 0 }
            ? TraceText.Format(MotifAppStrings.TraceBuildingStemFoundFormat, Input)
        : Type == "Successful" ? MotifAppStrings.TraceKindBuiltTheWord
        : Type == "Failed" ? MotifAppStrings.TraceKindNoAnalysisFound
        : Type.Contains("MorphologicalRule", StringComparison.Ordinal) ? MotifAppStrings.TraceBuildingAffix
        : Type.Contains("PhonologicalRule", StringComparison.Ordinal) ? MotifAppStrings.TraceBuildingSoundRule : KindText;

    public bool ShowsBuildingOutcome => Type.Contains("Synthesis", StringComparison.Ordinal) && Type != "StratumSynthesisInput";

    public string PlainLabel => string.Join(" · ", new[] { RecordedLabel, Source, RecordedOutcomeText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    public string ShapeText => Input is { Length: > 0 } input && Output is { Length: > 0 } output
        ? $"{input} → {output}" : Output ?? Input ?? string.Empty;
    public string ReadableText => IsFailure ? PlainRefusalText : $"{PlainLabel}.";

    public TraceStep RecordedStep { get; private init; } = null!;
    public TraceRef? Reference { get; private init; }
    public string? ProducerSourceText => RecordedStep.Source is { Length: > 0 } source ? TraceText.Format(MotifAppStrings.TraceProducerFormat, source) : null;
    public string? CapturedSourceText => Reference?.CapturedFieldWorksLabel is { Length: > 0 } captured &&
        !string.Equals(captured, RecordedStep.Source, StringComparison.Ordinal) ? TraceText.Format(MotifAppStrings.TraceCapturedFieldWorksFormat, captured) : null;
    public string? SourceLabel => Source;

    /// <summary>The inspector subject from this event's recorded typed key; absent without that key.</summary>
    public InspectorSubject? InspectSubject => Reference is { TimingKey: { Identity: not null } key } reference
        ? InspectorSubject.Rule(key, Source, reference.IdentityQuality) : null;

    /// <summary>This event's recorded details, shown apart from the inspector's current Baseline facts.</summary>
    public IReadOnlyList<InspectorDetail> Captured => InspectorDetail.Recorded((MotifAppStrings.TraceCapturedKindLabel, Type),
        (MotifAppStrings.TraceCapturedProducerLabel, RecordedStep.Source), (MotifAppStrings.TraceCapturedFieldWorksLabel, Reference?.CapturedFieldWorksLabel),
        (MotifAppStrings.TraceCapturedProducerEventIdLabel, RecordedStep.EventEvidence?.ProducerStepId), (MotifAppStrings.TraceCapturedTreeAddressLabel, RecordedStep.StepId),
        (MotifAppStrings.TraceCapturedSubruleLabel, Subrule?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        (MotifAppStrings.TraceCapturedInputLabel, Input), (MotifAppStrings.TraceCapturedOutputLabel, Output),
        (MotifAppStrings.TraceCapturedOutcomeLabel, OutcomeStatus), (MotifAppStrings.TraceCapturedEventLabel, OutcomeEventType),
        (MotifAppStrings.TraceCapturedReasonLabel, FailureReason),
        (MotifAppStrings.TraceCapturedExplanationLabel, RecordedStep.ExplanationAvailability == TraceEvidenceAvailability.Recorded
            ? RecordedStep.ReasonExplanation : null), (MotifAppStrings.TraceCapturedEvaluatorDetailsLabel, RecordedRejectionText));
    public bool CanInspect => InspectSubject is not null;
    public bool HasUnlinkedSource => !CanInspect && SourceLabel is { Length: > 0 };

    public string RecordedOutcomeText => IsFailure ? MotifAppStrings.TraceOutcomeRefused : OutcomeStatus?.ToLowerInvariant() switch
    {
        "attempted" => MotifAppStrings.TraceOutcomeTried,
        "success" or "succeeded" or "successful" => MotifAppStrings.TraceOutcomeApplied,
        "failed" or "failure" or "error" => MotifAppStrings.TraceOutcomeRefused,
        "blocked" => MotifAppStrings.TraceKindBlocked,
        null or "" => MotifAppStrings.TraceOutcomeNotRecorded,
        _ => OutcomeStatus,
    };
    public string RecordedLabel => Type == "Blocked" ? MotifAppStrings.TraceKindBlocked : KindText;
    public string Notation => string.Join(" · ", new[] { Type, OutcomeStatus, OutcomeEventType }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    /// <summary>The event's recorded classification without repeating its event name.</summary>
    public string ExpertClassificationText => string.Join(" · ", new[] { OutcomeStatus,
        string.Equals(OutcomeEventType, Type, StringComparison.Ordinal) ? null : OutcomeEventType }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    /// <summary>Whether this event has a separate recorded classification to show.</summary>
    public bool HasExpertClassification => ExpertClassificationText.Length > 0;
    /// <summary>The event's recorded input, or why its empty expert token has no value.</summary>
    public string InputTip => Input is null ? MotifAppStrings.TraceInputNotRecorded : MotifAppStrings.TraceInputShapeTip;
    /// <summary>The event's recorded output, or why its empty expert token has no value.</summary>
    public string OutputTip => Output is null ? MotifAppStrings.TraceOutputNotRecorded : MotifAppStrings.TraceOutputShapeTip;
    /// <summary>This event's address in Motif's unchanged saved tree, independently of its producer event ID.</summary>
    public string RecordedEventAddress => RecordedStep.StepId is { Length: > 0 } address
        ? TraceText.Format(MotifAppStrings.TraceRecordedEventFormat, address) : MotifAppStrings.TraceEventAddressNotRecorded;

    /// <summary>The producer's captured event ID, shown separately from Motif's tree address; never synthesized.</summary>
    public string RecordedProducerEventAddress => RecordedStep.EventEvidence?.ProducerStepId is { Length: > 0 } producer
        ? RecordedStep.StepId is { Length: > 0 } treeAddress
            ? TraceText.Format(MotifAppStrings.TraceProducerEventWithTreeFormat, producer, treeAddress)
            : TraceText.Format(MotifAppStrings.TraceProducerEventNoTreeFormat, producer)
        : RecordedStep.StepId is { Length: > 0 } address ? TraceText.Format(MotifAppStrings.TraceTreeAddressOnlyFormat, address)
            : MotifAppStrings.TraceProducerEventNotRecorded;
    public string RecordedReasonText => RecordedStep.ReasonAvailability == TraceEvidenceAvailability.Recorded
        ? RecordedStep.FailureReason! : MotifAppStrings.TraceReasonNotRecorded;
    public string RecordedExplanationText => RecordedStep.ExplanationAvailability == TraceEvidenceAvailability.Recorded
        ? RecordedStep.ReasonExplanation! : TraceFailureSentences.Explain(FailureReason, Source, FailureRequired);
    public string RecordedRejectionText => RecordedStep.RejectionDetailsAvailability == TraceEvidenceAvailability.NotRecorded &&
        TraceEvidenceDisplay.Details(RecordedStep).Count == 0
        ? MotifAppStrings.TraceRejectionDetailsNotRecorded : string.Join("\n", new[]
    {
        FailureRequired is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceRequiredFormat, FailureRequired) : null,
        FailureActual is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceActualFormat, FailureActual) : null,
        FailureEnvironment is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceEnvironmentFormat, FailureEnvironment) : null,
        RecordedStep.FailureEvidence is { Status: "available" or "recorded" or "captured" } ? MotifAppStrings.TraceRejectionDetailsRecorded : null,
        RecordedStep.FailureEvidence?.Status is { Length: > 0 } status ? TraceText.Format(MotifAppStrings.TraceEvidenceStatusFormat, status) : null,
        RecordedStep.FailureEvidence?.Kind is { Length: > 0 } kind ? TraceText.Format(MotifAppStrings.TraceEvidenceKindFormat, kind) : null,
        RecordedStep.FailureEvidence?.Source is { Length: > 0 } source ? TraceText.Format(MotifAppStrings.TraceEvidenceSourceFormat, source) : null,
        RecordedStep.FailureEvidence?.ReasonCode is { Length: > 0 } code ? TraceText.Format(MotifAppStrings.TraceEvidenceReasonCodeFormat, code) : null,
        RecordedStep.FailureEvidence?.UnavailableReason is { Length: > 0 } unavailable ? TraceText.Format(MotifAppStrings.TraceEvidenceUnavailableReasonFormat, unavailable) : null,
        RecordedStep.FailureEvidence?.Reason is { Length: > 0 } reason ? TraceText.Format(MotifAppStrings.TraceEvidenceReasonFormat, reason) : null,
        RecordedStep.FailureEvidence?.Required is { Length: > 0 } required ? TraceText.Format(MotifAppStrings.TraceEvidenceRequiredFormat, required) : null,
        RecordedStep.FailureEvidence?.Actual is { Length: > 0 } actual ? TraceText.Format(MotifAppStrings.TraceEvidenceActualFormat, actual) : null,
        RecordedStep.FailureEvidence?.Environment is { Length: > 0 } environment ? TraceText.Format(MotifAppStrings.TraceEvidenceEnvironmentFormat, environment) : null,
    }.Where(value => value is not null).Concat(TraceEvidenceDisplay.Details(RecordedStep))) is { Length: > 0 } text
        ? text : MotifAppStrings.TraceRejectionDetailsNotRecorded;
    /// <summary>A known refusal explanation in the window's words, without parser codes or evidence details.</summary>
    public string PlainRefusalText
    {
        get
        {
            if (!IsFailure) return string.Empty;
            return RecordedStep.ExplanationAvailability == TraceEvidenceAvailability.Recorded
                ? RecordedStep.ReasonExplanation!
                : TraceFailureSentences.Explain(FailureReason, Source, FailureRequired);
        }
    }
    public TraceStepViewModel(TraceStep step, string? deepestStepId,
        IReadOnlyDictionary<string, TraceWritingSystem>? directions = null, TraceDisplayLabels? labels = null, IReadOnlyDictionary<string, TraceRef>? refs = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        RecordedStep = step;
        Reference = step.RefId is { } refId && refs is not null && refs.TryGetValue(refId, out var reference) ? reference : null;
        Type = step.Type;
        Source = labels?.Resolve(step.RefId, step.Source) ?? step.Source;
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
        IsDeepest = deepestStepId is not null && step.StepId.Length > 0 && string.Equals(step.StepId, deepestStepId, StringComparison.Ordinal);
        Children = step.Children.Select(child => new TraceStepViewModel(child, deepestStepId, directions, labels, refs)).ToArray();
        _directions = directions;
        Label = Source is { Length: > 0 } ? $"{TraceStepKinds.Describe(Type)}: {Source}" : TraceStepKinds.Describe(Type);
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
    public string SourceIdentity => TraceText.Format(MotifAppStrings.TraceSourceIdentityFormat, SourceIdentityKind ?? MotifAppStrings.TraceSourceKindTitle,
        SourceIdentityId ?? MotifAppStrings.TraceValueNotRecorded, SourceIdentityQuality ?? MotifAppStrings.TraceQualityNotRecorded);
    public IReadOnlyList<TraceMorphViewModel> AttemptedMorphs { get; }
    public bool HasAttemptedMorphs => AttemptedMorphs.Count > 0;
    public bool HasFailureReason => FailureReason is { Length: > 0 };

    public bool IsBlocked => Type == "Blocked" || IsStatus(OutcomeStatus, "blocked");

    public bool IsFailure => !IsBlocked && (HasFailureReason ||
        IsStatus(OutcomeStatus, "failure", "failed", "error") ||
        Type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("blocked", StringComparison.OrdinalIgnoreCase));

    public bool IsSuccessful => IsStatus(OutcomeStatus, "success", "succeeded", "successful") ||
        Type.Contains("successful", StringComparison.OrdinalIgnoreCase) ||
        Type.Contains("success", StringComparison.OrdinalIgnoreCase);

    /// <summary>What happened at the step, in the words a linguist uses: applied, refused, or only tried.</summary>
    public string StatusText => IsBlocked ? MotifAppStrings.TraceKindBlocked : IsFailure ? MotifAppStrings.TraceStepRefused
        : IsSuccessful ? MotifAppStrings.TraceStepApplied : MotifAppStrings.TraceStepTried;

    /// <summary>The step's kind in plain words, such as "Affix rule".</summary>
    public string KindText => TraceStepKinds.Describe(Type);

    /// <summary>Whether this event recorded a positive subrule index.</summary>
    public bool HasSubrule => Subrule is > 0;
    /// <summary>The recorded subrule index, when it has one.</summary>
    public string SubruleText => HasSubrule ? TraceText.Format(MotifAppStrings.TraceSubruleFormat, Subrule) : string.Empty;

    public string ContextText => string.Join("\n", new[] {
        FailureReason is null ? MotifAppStrings.TraceReasonNotRecorded : RecordedStep.ExplanationAvailability == TraceEvidenceAvailability.Recorded
            ? RecordedStep.ReasonExplanation : TraceStepKinds.ExplainReason(FailureReason),
        RecordedStep?.RejectionDetailsAvailability == TraceEvidenceAvailability.Recorded ? null : MotifAppStrings.TraceRejectionDetailsNotRecorded,
        ContextualFailure,
        FailureRequired is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceRequiredFormat, FailureRequired) : null,
        FailureActual is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceActualFormat, FailureActual) : null,
        FailureEnvironment is { Length: > 0 } ? TraceText.Format(MotifAppStrings.TraceEnvironmentFormat, FailureEnvironment) : null,
    }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } context ? context : MotifAppStrings.TraceContextualFailureNotRecorded;

    public string MorphSearchText => string.Join("\n", AttemptedMorphs.SelectMany(morph =>
        new[] { morph.Form, morph.Headword, morph.Gloss, morph.Category, morph.Identity, morph.FormId, morph.EntryId }));

    private static bool IsStatus(string? status, params string[] values) =>
        status is not null && values.Any(value => string.Equals(status, value, StringComparison.OrdinalIgnoreCase));

    public TraceStepViewModel WithChildren(IReadOnlyList<TraceStepViewModel> children, bool expandForFilter = false) =>
        new(Type, Source, Input, Output, FailureReason, Subrule, OutcomeStatus, OutcomeEventType,
            ContextualFailure, FailureRequired, FailureActual, FailureEnvironment, SourceIdentityKind, SourceIdentityId, SourceIdentityQuality,
            AttemptedMorphs, _directions, children, IsDeepest, expandForFilter) { RecordedStep = RecordedStep, Reference = Reference };

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
        _isExpanded = expandForFilter;
        Children = children;
        Label = Source is { Length: > 0 } ? $"{TraceStepKinds.Describe(Type)}: {Source}" : TraceStepKinds.Describe(Type);
    }

    public bool IsDeepest { get; }
    public bool ExpandForFilter { get; }
    public IReadOnlyList<TraceStepViewModel> Children { get; }
    public string Label { get; }
}

/// <summary>Formats a localized trace sentence with the current culture, as an interpolated string would.</summary>
internal static class TraceText
{
    public static string Format(string format, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);
}
