using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

/// <summary>The two ways Try a Word shows one trace: the candidates FieldWorks' own dialog lists, or the parser's full derivation tree.</summary>
public enum TraceView
{
    Candidates,
    FullDerivation,
}

/// <summary>
/// Traces one word against the current Baseline's grammar on demand, for the Results stage's Try a Word
/// panel. Holds no state of its own between words: choosing a word or typing one and pressing Try replaces
/// whatever trace was shown before.
/// </summary>
public sealed partial class TraceWordViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private string? _projectPath;
    private int _generation;
    private CancellationTokenSource? _running;
    private IReadOnlyList<TraceCandidateViewModel> _candidates = [];

    public TraceWordViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        TryCommand = new AsyncRelayCommand(TryAsync, () => _projectPath is not null && WordToTry.Trim().Length > 0);
        SetViewCommand = new RelayCommand<TraceView>(view => View = view);
        CancelCommand = new RelayCommand(CancelRunning, () => IsLoading);
    }

    /// <summary>The candidate a person is looking at, in either view; <see langword="null"/> selects none.</summary>
    [ObservableProperty]
    private TraceCandidateViewModel? _selectedCandidate;

    /// <summary>Whether Try a Word has widened to the detail area's full width, sharing it with nothing.</summary>
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

    /// <summary>The trace's candidates, built once per result so a selection keeps its identity across rebinds.</summary>
    public IReadOnlyList<TraceCandidateViewModel> Candidates => _candidates;

    partial void OnResultChanged(WordTraceResponse? value) =>
        _candidates = value?.Candidates.Select(candidate => new TraceCandidateViewModel(candidate)).ToArray() ?? [];

    public TraceStepViewModel? Root =>
        Result is { } result ? new TraceStepViewModel(result.Root, result.DeepestRule) : null;

    /// <summary>The full derivation tree's own root, wrapped as a single-item list for a <c>TreeView</c>.</summary>
    public IReadOnlyList<TraceStepViewModel> Roots => Root is { } root ? [root] : [];

    public bool ShowCandidates => View == TraceView.Candidates;
    public bool ShowFullDerivation => View == TraceView.FullDerivation;

    /// <summary>What the focus toggle reads, naming the move it is about to make rather than the state itself.</summary>
    public string FocusToggleLabel => IsFocused ? "Show Analyses" : "Focus this word";

    public IAsyncRelayCommand TryCommand { get; }

    /// <summary>Stops the trace in progress; a trace can run for minutes.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Chooses one of the two trace views, replacing whichever was chosen before.</summary>
    public IRelayCommand<TraceView> SetViewCommand { get; }

    public void SetProjectPath(string? fwDataPath)
    {
        _projectPath = fwDataPath;
        TryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Fills in the word to try, from a chosen Results row, without starting a trace.</summary>
    public void SetWord(string word) => WordToTry = word;

    /// <summary>Clears whatever trace was shown, for a newly chosen word or project.</summary>
    public void Reset()
    {
        CancelRunning();
        _generation++;
        IsLoading = false;
        Refusal = null;
        Result = null;
        SelectedStep = null;
        SelectedCandidate = null;
        View = TraceView.Candidates;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));
    }

    private async Task TryAsync()
    {
        if (_projectPath is not { } path) return;
        var word = WordToTry.Trim();
        if (word.Length == 0) return;

        CancelRunning();
        using var running = new CancellationTokenSource();
        _running = running;
        var generation = ++_generation;
        IsLoading = true;
        Refusal = null;
        Result = null;
        SelectedStep = null;
        SelectedCandidate = null;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));

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
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(Roots));
    }

    private void CancelRunning()
    {
        _running?.Cancel();
        _running = null;
        IsLoading = false;
    }
}

/// <summary>One candidate morph sequence, as FieldWorks' Try a Word dialog lists it.</summary>
public sealed class TraceCandidateViewModel
{
    public TraceCandidateViewModel(TraceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Morphs = candidate.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Succeeded = candidate.Succeeded;
        Explanation = candidate.Explanation;
        Steps = candidate.Steps.Select(step => new TraceStepViewModel(step, deepestRule: null)).ToArray();
        Text = string.Join(" + ", Morphs.Select(morph => morph.Form));
        Gloss = string.Join(" + ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public bool Succeeded { get; }
    public string? Explanation { get; }
    public IReadOnlyList<TraceStepViewModel> Steps { get; }
    public string Text { get; }
    public string Gloss { get; }
}

/// <summary>One node of the derivation tree, wrapped for a <c>TreeView</c> with its own children.</summary>
public sealed class TraceStepViewModel
{
    public TraceStepViewModel(TraceStep step, string? deepestRule)
    {
        ArgumentNullException.ThrowIfNull(step);
        Type = step.Type;
        Source = step.Source;
        Input = step.Input;
        Output = step.Output;
        FailureReason = step.FailureReason;
        IsDeepest = deepestRule is not null && string.Equals(step.Source, deepestRule, StringComparison.Ordinal);
        Children = step.Children.Select(child => new TraceStepViewModel(child, deepestRule)).ToArray();
        Label = Source is { Length: > 0 } ? $"{Type}: {Source}" : Type;
    }

    public string Type { get; }
    public string? Source { get; }
    public string? Input { get; }
    public string? Output { get; }
    public string? FailureReason { get; }
    public bool HasFailureReason => FailureReason is { Length: > 0 };

    /// <summary>Whether this step passed, the same fact <see cref="FailureReason"/> carries the other way.</summary>
    public bool Passed => !HasFailureReason;

    public bool IsDeepest { get; }
    public IReadOnlyList<TraceStepViewModel> Children { get; }

    /// <summary>The tree row's own text: the step's type, and its rule or stratum when it names one.</summary>
    public string Label { get; }
}
