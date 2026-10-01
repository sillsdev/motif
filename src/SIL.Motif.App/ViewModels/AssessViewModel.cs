using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Adapts the Assessment request and response to the shared cancellable command-run module. The module
/// owns lifecycle, cancellation, progress, and refusal classification; this adapter owns Selection input.
/// Grammar findings are not read here — <see cref="GrammarViewModel"/> reads them independently of any run.
/// </summary>
public sealed partial class AssessViewModel : CommandRunViewModel<AssessCommandResponse>
{
    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;
    private readonly TimeProvider _timeProvider;
    private bool _keepShownThroughRun;

    public AssessViewModel(ICommandClient commandClient, SelectionViewModel selection, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        Trace = new TraceWordViewModel(commandClient);
        PropertyChanged += OnResultChanged;
        Words.PropertyChanged += OnWordsPropertyChanged;
        Compare.Rerun = RerunAsync;
        Compare.ChosenCellsChanged += (_, _) => Words.ShowOnly(Compare.ChosenWords);
    }

    private IReadOnlyList<string>? _rerunWords;
    private int? _rerunLimitMs;
    private StepCap? _rerunStepLimit;
    private AssessCommandResponse? _mergeInto;
    private bool _runDefaultSelection;
    private int? _defaultPerWordLimitMs;
    private StepCap? _defaultStepLimit;

    /// <summary>Runs the project's stored default Selection with the limits chosen during setup.</summary>
    public Task RunDefaultSelectionAsync(int? perWordLimitMs, StepCap? perWordStepLimit)
    {
        if (perWordLimitMs is <= 0) throw new ArgumentOutOfRangeException(nameof(perWordLimitMs));
        _runDefaultSelection = true;
        _defaultPerWordLimitMs = perWordLimitMs;
        _defaultStepLimit = perWordStepLimit;
        return RunCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Runs <paramref name="words"/> again with <paramref name="limitMs"/> per word, and folds each fresh answer into
    /// the current Assessment in place of the old one, so the rest of the result is kept.
    /// </summary>
    public Task RerunAsync(IReadOnlyList<string> words, int limitMs) => RerunAsync(words, limitMs, null);

    /// <summary>Runs chosen words again with an optional step cap for this Selection.</summary>
    public Task RerunAsync(IReadOnlyList<string> words, int limitMs, StepCap? stepLimit)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count == 0) return Task.CompletedTask;
        _rerunWords = words;
        _rerunLimitMs = limitMs;
        _rerunStepLimit = stepLimit;
        _rerunDescription = $"{words.Count:N0} word{(words.Count == 1 ? string.Empty : "s")} again at {limitMs / 1000.0:0.#} s each";
        return RunCommand.ExecuteAsync(null);
    }

    /// <summary>The current result with each word <paramref name="rerun"/> answered replaced by its new answer.</summary>
    public static AssessCommandResponse Merge(AssessCommandResponse into, AssessCommandResponse rerun)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(rerun);
        var fresh = rerun.Words.ToDictionary(word => word.Word, StringComparer.Ordinal);
        var existing = into.Words.Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
        return into with
        {
            Words = into.Words.Select(word => fresh.GetValueOrDefault(word.Word) ?? word)
                .Concat(rerun.Words.Where(word => !existing.Contains(word.Word))).ToArray(),
            TimingOverrideAssessmentIds = into.TimingOverrideAssessmentIds.Concat(
                rerun.Measurements.Where(measurement => measurement.Kind == AssessmentKinds.ParseTime)
                    .Select(measurement => measurement.AssessmentId)).ToArray(),
        };
    }

    internal void Restore(WorkspaceEvidence evidence)
    {
        _keepShownThroughRun = false;
        _previous = null;
        _previousAt = null;
        _rerunDescription = null;
        Result = evidence.Assessment;
        CompletedAt = evidence.CompletedAt;
        State = RunState.Completed;
    }

    // The result is cleared as a run starts, so the one a re-run folds into is kept here first.
    protected override Task<bool> PrepareRunAsync()
    {
        _keepShownThroughRun = true;
        _mergeInto = _rerunWords is not null ? Result : null;
        if (Result is not null) (_previous, _previousAt) = (Result, CompletedAt);
        LastRunWasRerun = _rerunWords is not null;
        if (!LastRunWasRerun) _rerunDescription = null;
        return Task.FromResult(true);
    }

    protected override void OnRunStarting() => _keepShownThroughRun = false;

    protected override void OnReset()
    {
        _keepShownThroughRun = false;
        (_previous, _previousAt, _rerunDescription) = (null, null, null);
        Words.Load(null, null);
        Compare.Load(null);
        OnPropertyChanged(nameof(ShowsEarlierResults));
        Difference.Load(null, null, string.Empty, string.Empty);
    }

    /// <summary>The project this Assessment measures, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    partial void OnProjectPathChanged(string? value)
    {
        RunCommand.NotifyCanExecuteChanged();
        Trace.SetProjectPath(value);
    }

    /// <summary>
    /// The Texts stage's word data, for looking up how often a Results word occurs in the chosen Texts.
    /// <see langword="null"/> shows no occurrence count rather than guessing one.
    /// </summary>
    public TextWordsViewModel? TextWords { get; set; }

    /// <summary>The last successful Assessment's words, as a sortable, searchable table.</summary>
    public AssessWordsViewModel Words { get; } = new();

    /// <summary>The same words in the matrix of what the project held against what the parser did.</summary>
    public CompareViewModel Compare { get; } = new();

    /// <summary>What changed since the run before this one, when there was one.</summary>
    public DifferenceViewModel Difference { get; } = new();

    /// <summary>Whether the latest run gave some words more time rather than measuring the whole Selection again.</summary>
    public bool LastRunWasRerun { get; private set; }

    private AssessCommandResponse? _previous;
    private DateTimeOffset? _previousAt;
    private string? _rerunDescription;

    /// <summary>Traces one word on demand against the current Baseline's grammar, for Try a Word.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>Opens Try a Word on a word and traces it; set by whoever hosts Try a Word.</summary>
    public Action<string>? OpenTryWord { get; set; }

    /// <summary>
    /// Selects <paramref name="word"/> in <see cref="Words"/> with every filter cleared, so the word is certain to
    /// be listed; a word the Assessment did not answer also clears the matrix's chosen cells.
    /// </summary>
    public void SelectWord(string word)
    {
        Words.WordFilter = string.Empty;
        Words.SelectedFilter = ResultsWordFilter.All;
        if (Words.Rows.All(row => row.Word != word)) Compare.ClearSelectionCommand.Execute(null);
        Words.SelectedRow = Words.Rows.FirstOrDefault(row => row.Word == word) ?? Words.SelectedRow;
    }

    protected override bool CanStartCore() => ProjectPath is not null &&
        (_rerunWords is not null || _runDefaultSelection || _selection.CanAssess);

    protected override IDisposable BeginUsageAction()
    {
        var shapes = new List<string> { UsageArgumentShape.Text("fwDataPath") };
        if (_rerunWords is { } words)
        {
            shapes.Add(UsageArgumentShape.List("words", words.Count));
            shapes.Add(UsageArgumentShape.Number("perWordLimitMs"));
            if (_rerunStepLimit is not null) shapes.Add(UsageArgumentShape.Number("perWordStepLimit"));
        }
        else if (_runDefaultSelection)
        {
            shapes.Add(UsageArgumentShape.Flag("defaultSelection"));
            if (_defaultPerWordLimitMs is not null) shapes.Add(UsageArgumentShape.Number("perWordLimitMs"));
            if (_defaultStepLimit is not null) shapes.Add(UsageArgumentShape.Number("perWordStepLimit"));
        }
        else
        {
            shapes.Add(UsageArgumentShape.Object("selection"));
            if (_selection.PerWordTimeLimitSeconds is not null)
                shapes.Add(UsageArgumentShape.Number("perWordTimeLimitSeconds"));
            if (_selection.PerWordStepLimit is not null)
                shapes.Add(UsageArgumentShape.Number("perWordStepLimit"));
        }

        return _commandClient.BeginUsageAction("assess", [.. shapes]);
    }

    /// <summary>When the last Assessment finished, so an older Handoff can say it is out of date.</summary>
    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    /// <summary>
    /// Whether the words and the Matrix still show the last finished parse because a newer one is running or was
    /// refused, so the page can dim them and say they are from before.
    /// </summary>
    public bool ShowsEarlierResults => Result is null && Compare.HasWords && (IsActive || Refusal is not null);

    /// <summary>The line above results kept from before, saying why they are not this parse's.</summary>
    public string EarlierResultsNote => IsActive
        ? "These are the results from before. The new ones replace them when parsing finishes."
        : "These are the results from before this parse.";

    /// <summary>Whether the last parse was refused rather than cancelled, so the page offers Report a problem.</summary>
    public bool OffersProblemReport => !IsActive && Refusal is { Reason: not FailureReason.Cancelled };

    private void OnResultChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Result) or nameof(State) or nameof(Refusal))
        {
            OnPropertyChanged(nameof(ShowsEarlierResults));
            OnPropertyChanged(nameof(EarlierResultsNote));
            OnPropertyChanged(nameof(OffersProblemReport));
        }
        if (e.PropertyName != nameof(Result)) return;
        if (Result is not null) CompletedAt = _timeProvider.GetLocalNow();
        // A run clears the result as it starts; the words and the Matrix wait for the new result rather than empty.
        if (Result is null && _keepShownThroughRun) return;
        Words.Load(Result?.Words, TextWords is { } textWords ? word => LookUpOccurrences(textWords, word) : null);
        Compare.Load(Result is null ? null : Words.AllRows);
        if (Result is null) return;
        var before = _previous?.Words.Select(word => new AssessWordRowViewModel(word)).ToArray();
        Difference.Load(before, before is null ? null : Words.AllRows,
            _previousAt is { } at ? $"Run of {at.ToLocalTime():t}" : "The run before",
            _rerunDescription is { } rerun ? $"This run: {rerun}" : "This run");
    }

    private static int? LookUpOccurrences(TextWordsViewModel textWords, string word) =>
        textWords.Rows.FirstOrDefault(row => row.Form == word)?.OccurrenceCount;

    // Choosing a Results word primes Try a Word with it, without starting a trace the person did not ask for.
    private void OnWordsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssessWordsViewModel.SelectedRow)) return;
        Trace.Reset();
        if (Words.SelectedRow is not { } row) return;
        Trace.SetWord(row.Word);
        Trace.SetExpected(row.Word, row.ExpectedAnalysis?.Morphs);
        // A word with no readings and nothing missed has no analyses to show, so Try a Word takes the width.
        Trace.IsFocused = !row.HasReadings && row.MissedApproved.Count == 0;
    }

    protected override async Task<CommandOutcome<AssessCommandResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var (words, limitMs, stepLimit, into) = (_rerunWords, _rerunLimitMs, _rerunStepLimit, _mergeInto);
        (_rerunWords, _rerunLimitMs, _rerunStepLimit, _mergeInto) = (null, null, null, null);
        if (words is null)
        {
            var runDefault = _runDefaultSelection;
            var timeLimitMs = runDefault ? _defaultPerWordLimitMs
                : _selection.PerWordTimeLimitSeconds is > 0 and var seconds ? (int)(seconds * 1000) : null;
            var requestedStepLimit = runDefault ? _defaultStepLimit : null;
            _runDefaultSelection = false;
            _defaultPerWordLimitMs = null;
            _defaultStepLimit = null;
            var request = new AssessRequest(ProjectPath!, runDefault ? null : _selection.BuildRequest(),
                timeLimitMs, requestedStepLimit);
            return await _commandClient.AssessAsync(request, this, cancellationToken).ConfigureAwait(true);
        }

        var rerun = new AssessRequest(ProjectPath!,
            new SelectionRequest(false, [], words, false, null,
                PerWordStepLimit: stepLimit ?? _selection.BuildRequest().PerWordStepLimit), limitMs);
        var outcome = await _commandClient.AssessAsync(rerun, this, cancellationToken).ConfigureAwait(true);
        return outcome.Succeeded && into is not null
            ? CommandOutcome<AssessCommandResponse>.Success(Merge(into, outcome.Value!))
            : outcome;
    }

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.CanAssess)) RunCommand.NotifyCanExecuteChanged();
    }
}
