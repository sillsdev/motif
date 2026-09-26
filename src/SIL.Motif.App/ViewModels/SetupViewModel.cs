using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>The shell's four-step first-run setup and project configuration dialog.</summary>
public sealed partial class SetupViewModel : ObservableObject, IProjectStateParticipant
{
    private const string DefaultSelectionName = "Default";
    private readonly WorkspaceContext _context;
    private readonly TextWordsViewModel _words;
    private int _loadGeneration;
    private NamedSelectionProjection? _savedSelection;
    private bool _setupSkipped;
    private SelectionSnapshot? _snapshot;
    private StepCap _configuredStepLimit = StepCap.Default;

    public SetupViewModel(WorkspaceContext context, TextWordsViewModel words)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(words);
        _context = context;
        _words = words;
        Selection = context.Selection;
        Indicators = Enumerable.Range(0, 4).Select(step => new SetupStepIndicator(step, step == 0)).ToArray();
        SkipCommand = new AsyncRelayCommand(SkipAsync);
        BackCommand = new RelayCommand(() => Step--, () => Step > 0);
        NextCommand = new RelayCommand(() => Step++, () => Step < 3);
        FinishCommand = new AsyncRelayCommand(FinishAsync, CanFinish);
        Selection.PropertyChanged += OnSelectionChanged;
        context.Assess.PropertyChanged += OnAssessmentChanged;
        words.PropertyChanged += OnWordsChanged;
    }

    public SelectionViewModel Selection { get; }

    public IReadOnlyList<SetupStepIndicator> Indicators { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    [NotifyPropertyChangedFor(nameof(StepCaption))]
    private string? _projectPath;

    public string ProjectName => ProjectPath is null ? string.Empty : Path.GetFileNameWithoutExtension(ProjectPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectStep))]
    [NotifyPropertyChangedFor(nameof(IsSelectionStep))]
    [NotifyPropertyChangedFor(nameof(IsLimitsStep))]
    [NotifyPropertyChangedFor(nameof(IsFirstRunStep))]
    [NotifyPropertyChangedFor(nameof(IsBackVisible))]
    [NotifyPropertyChangedFor(nameof(IsNextVisible))]
    [NotifyPropertyChangedFor(nameof(FinishTitle))]
    [NotifyPropertyChangedFor(nameof(FinishDescription))]
    [NotifyPropertyChangedFor(nameof(StepCaption))]
    private int _step;

    public bool IsProjectStep => Step == 0;
    public bool IsSelectionStep => Step == 1;
    public bool IsLimitsStep => Step == 2;
    public bool IsFirstRunStep => Step == 3;
    public bool IsBackVisible => Step > 0;
    public bool IsNextVisible => Step < 3;

    public string NextButtonText => Step switch
    {
        1 => "Next: limits",
        2 => "Next: first run",
        _ => "Next: texts",
    };

    public string StepCaption => $"Setting up {ProjectName} · step {Step + 1} of 4";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    private string? _refusalMessage;

    public bool HasRefusal => !string.IsNullOrWhiteSpace(RefusalMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLimitValidationMessage))]
    private decimal? _stepLimitSteps = StepCap.DefaultSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLimitValidationMessage))]
    private bool _isStepLimitUnbounded;

    public string? StepLimitValidationMessage => IsStepLimitUnbounded || IsStepLimitValid
        ? null
        : "Enter a positive whole number of steps, or choose no limit.";

    public string? TimeLimitValidationMessage => IsTimeLimitValid
        ? null
        : "Enter a positive time limit no greater than 2,147,483 seconds.";

    private bool IsStepLimitValid => IsStepLimitUnbounded ||
        StepLimitSteps is > 0 and var steps && decimal.Truncate(steps) == steps && steps <= long.MaxValue;

    private bool IsTimeLimitValid =>
        Selection.PerWordTimeLimitSeconds is > 0 and <= int.MaxValue / 1000m;

    private void OpenFirstTimeSetup()
    {
        if (IsOpen) return;
        CaptureSnapshot();
        Step = 0;
        IsOpen = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishButtonText))]
    [NotifyPropertyChangedFor(nameof(FinishTitle))]
    [NotifyPropertyChangedFor(nameof(FinishDescription))]
    private bool _isEditingExistingSelection;

    public string AddedWordsText => Selection.PastedWordEntries.Count == 0
        ? "No added words"
        : string.Join(", ", Selection.PastedWordEntries);

    public string RunSummary => _words.SummaryText;

    public string FinishButtonText => IsEditingExistingSelection ? "Use this Selection" : "Start first run";

    public string FinishTitle => IsEditingExistingSelection
        ? "Use this Selection as the project default" : "Ready for the first run?";

    public string FinishDescription => IsEditingExistingSelection
        ? "This will be the project default. The next Assessment will use it."
        : "Motif will save this Selection with the project, then assess the stored Default Selection.";

    public IAsyncRelayCommand SkipCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand NextCommand { get; }
    public IAsyncRelayCommand FinishCommand { get; }

    /// <summary>Loads the project's stored Selection and resolved per-word limits.</summary>
    public async Task ProjectOpenedAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var generation = ++_loadGeneration;
        IsOpen = false;
        ProjectPath = projectPath;
        Step = 0;
        RefusalMessage = null;

        var saved = await _context.Commands.ReadDefaultSelectionAsync(
            new ReadDefaultSelectionRequest(projectPath), cancellationToken).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        if (!saved.Succeeded)
        {
            RefusalMessage = saved.Refusal!.Message;
            return;
        }

        _savedSelection = saved.Value!.Selection;
        _setupSkipped = saved.Value.SetupSkipped;
        Selection.ApplyDefaultSelection(_savedSelection);

        var config = await _context.Commands.ShowConfigAsync(
            new SIL.Motif.Commands.Requests.ShowConfigRequest(projectPath, MotifProductVersion.CurrentText),
            cancellationToken).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        if (!config.Succeeded)
        {
            RefusalMessage = config.Refusal!.Message;
            return;
        }

        var scope = config.Value!.Scopes.FirstOrDefault(candidate => candidate.Name == "default")
            ?? config.Value.Scopes.FirstOrDefault();
        var timeLimitMs = _savedSelection?.PerWordLimitMs ?? scope?.PerWordLimitMs ?? 1000;
        var stepLimit = _savedSelection?.PerWordStepLimit ?? scope?.PerWordStepLimit ?? StepCap.Default;
        ApplyLimits(timeLimitMs, stepLimit);

        IsEditingExistingSelection = _savedSelection is not null;
        CaptureSnapshot();
        if (_savedSelection is null && !_setupSkipped && _context.Baseline?.HasBaseline == true)
            OpenFirstTimeSetup();
    }

    /// <summary>Opens first-time setup after the project receives its first Baseline.</summary>
    public Task BaselineCapturedAsync()
    {
        if (ProjectPath is not null && _context.Baseline?.HasBaseline == true &&
            _savedSelection is null && !_setupSkipped)
            OpenFirstTimeSetup();
        return Task.CompletedTask;
    }

    /// <summary>Closes the dialog and drops the project values when the shell clears its project.</summary>
    internal void ProjectCleared()
    {
        ++_loadGeneration;
        IsOpen = false;
        ProjectPath = null;
        _savedSelection = null;
        _setupSkipped = false;
        _snapshot = null;
        _configuredStepLimit = StepCap.Default;
        IsEditingExistingSelection = false;
        RefusalMessage = null;
    }

    void IProjectStateParticipant.ClearProject() => ProjectCleared();

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Setup;

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        ProjectOpenedAsync(projectPath, cancellationToken);

    /// <summary>Reopens setup from the project menu at its first step and with the saved values.</summary>
    public void OpenForConfiguration()
    {
        if (ProjectPath is null || _context.Baseline?.HasBaseline != true) return;
        RefusalMessage = null;
        if (_savedSelection is { } saved)
        {
            Selection.ApplyDefaultSelection(saved);
            ApplyLimits(saved.PerWordLimitMs, saved.PerWordStepLimit ?? _configuredStepLimit);
        }
        else
        {
            Selection.PerWordStepLimit = _configuredStepLimit.Steps;
            Selection.PerWordStepLimitUnbounded = _configuredStepLimit.IsUnbounded;
            SetStepLimit(_configuredStepLimit);
        }
        IsEditingExistingSelection = _savedSelection is not null;
        CaptureSnapshot();
        Step = 0;
        IsOpen = true;
    }

    private async Task FinishAsync()
    {
        if (!IsFirstRunStep || ProjectPath is not { } projectPath) return;
        var runFirstAssessment = !IsEditingExistingSelection;
        if (!IsStepLimitValid)
        {
            OnPropertyChanged(nameof(StepLimitValidationMessage));
            return;
        }
        if (!IsTimeLimitValid)
        {
            OnPropertyChanged(nameof(TimeLimitValidationMessage));
            return;
        }
        if (!Selection.CanAssess)
        {
            RefusalMessage = "Choose at least one text or add a word before continuing.";
            return;
        }

        var stepLimit = IsStepLimitUnbounded ? StepCap.Unbounded : new StepCap((long)StepLimitSteps!.Value);
        var timeLimitMs = decimal.ToInt32(decimal.Round(
            Selection.PerWordTimeLimitSeconds!.Value * 1000m, 0, MidpointRounding.AwayFromZero));
        Selection.PerWordStepLimit = stepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = stepLimit.IsUnbounded;
        var saved = await _context.Commands.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            projectPath, DefaultSelectionName, Selection.ChosenTextIds, Selection.PastedWordEntries,
            timeLimitMs, stepLimit),
            CancellationToken.None).ConfigureAwait(true);
        if (!saved.Succeeded)
        {
            RefusalMessage = saved.Refusal!.Message;
            return;
        }

        _savedSelection = saved.Value!.Selection;
        _setupSkipped = false;
        _configuredStepLimit = stepLimit;
        Selection.PerWordTimeLimitSeconds = timeLimitMs / 1000m;
        CaptureSnapshot();

        if (!runFirstAssessment)
        {
            IsEditingExistingSelection = true;
            IsOpen = false;
            return;
        }

        await _context.Assess.RunDefaultSelectionAsync(timeLimitMs, stepLimit).ConfigureAwait(true);
        if (_context.Assess.State == RunState.Completed)
        {
            IsEditingExistingSelection = true;
            IsOpen = false;
            RefusalMessage = null;
        }
        else
        {
            IsEditingExistingSelection = false;
            RefusalMessage = _context.Assess.Refusal?.Message;
        }
    }

    private bool CanFinish() => IsFirstRunStep && IsStepLimitValid && IsTimeLimitValid &&
        Selection.CanAssess && !_context.Assess.IsActive;

    private async Task SkipAsync()
    {
        if (ProjectPath is not { } projectPath) return;
        var result = await _context.Commands.SkipSetupAsync(
            new SkipSetupRequest(projectPath), CancellationToken.None).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            RefusalMessage = result.Refusal!.Message;
            return;
        }
        _setupSkipped = result.Value!.SetupSkipped;
        if (_snapshot is not null) RestoreSnapshot(_snapshot);
        IsOpen = false;
        RefusalMessage = null;
    }

    private void ApplyLimits(long timeLimitMs, StepCap stepLimit)
    {
        Selection.PerWordTimeLimitSeconds = timeLimitMs / 1000m;
        _configuredStepLimit = stepLimit;
        Selection.PerWordStepLimit = stepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = stepLimit.IsUnbounded;
        SetStepLimit(stepLimit);
    }

    private void SetStepLimit(StepCap stepLimit)
    {
        IsStepLimitUnbounded = stepLimit.IsUnbounded;
        StepLimitSteps = stepLimit.Steps ?? StepLimitSteps ?? StepCap.DefaultSteps;
    }

    private void CaptureSnapshot() => _snapshot = new SelectionSnapshot(
        Selection.ChosenTextIds.ToArray(), Selection.PastedWords, Selection.AllWordforms,
        Selection.RetryFailed, Selection.RetrySlowerThanMilliseconds, Selection.PerWordTimeLimitSeconds,
        Selection.PerWordStepLimitUnbounded ? StepCap.Unbounded
            : Selection.PerWordStepLimit is { } steps ? new StepCap(decimal.ToInt64(steps)) : StepCap.Default);

    private void RestoreSnapshot(SelectionSnapshot snapshot)
    {
        Selection.ApplyDefaultSelection(new NamedSelectionProjection(
            DefaultSelectionName, snapshot.TextIds, SplitAddedWords(snapshot.PastedWords), string.Empty, string.Empty));
        Selection.AllWordforms = snapshot.AllWordforms;
        Selection.RetryFailed = snapshot.RetryFailed;
        Selection.RetrySlowerThanMilliseconds = snapshot.RetrySlowerThanMilliseconds;
        Selection.PerWordTimeLimitSeconds = snapshot.PerWordTimeLimitSeconds;
        Selection.PerWordStepLimit = snapshot.PerWordStepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = snapshot.PerWordStepLimit.IsUnbounded;
        SetStepLimit(snapshot.PerWordStepLimit);
    }

    partial void OnStepChanged(int value)
    {
        foreach (var indicator in Indicators) indicator.IsCurrent = indicator.Step == value;
        OnPropertyChanged(nameof(NextButtonText));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        FinishCommand.NotifyCanExecuteChanged();
    }

    partial void OnStepLimitStepsChanged(decimal? value) => UpdateStepLimit();
    partial void OnIsStepLimitUnboundedChanged(bool value) => UpdateStepLimit();

    private void UpdateStepLimit()
    {
        Selection.PerWordStepLimitUnbounded = IsStepLimitUnbounded;
        Selection.PerWordStepLimit = !IsStepLimitUnbounded && IsStepLimitValid ? StepLimitSteps : null;
        OnPropertyChanged(nameof(StepLimitValidationMessage));
        FinishCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionViewModel.CanAssess) or nameof(SelectionViewModel.PastedWords)
            or nameof(SelectionViewModel.PerWordTimeLimitSeconds))
        {
            OnPropertyChanged(nameof(AddedWordsText));
            OnPropertyChanged(nameof(RunSummary));
            OnPropertyChanged(nameof(TimeLimitValidationMessage));
            FinishCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnAssessmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssessViewModel.IsActive)) FinishCommand.NotifyCanExecuteChanged();
    }

    private void OnWordsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextWordsViewModel.SummaryText)) OnPropertyChanged(nameof(RunSummary));
    }

    private static IReadOnlyList<string> SplitAddedWords(string words) => words
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record SelectionSnapshot(
        IReadOnlyList<Guid> TextIds, string PastedWords, bool AllWordforms, bool RetryFailed,
        decimal? RetrySlowerThanMilliseconds, decimal? PerWordTimeLimitSeconds, StepCap PerWordStepLimit);
}

public sealed partial class SetupStepIndicator : ObservableObject
{
    public SetupStepIndicator(int step, bool isCurrent)
    {
        Step = step;
        IsCurrent = isCurrent;
    }

    public int Step { get; }

    [ObservableProperty]
    private bool _isCurrent;
}
