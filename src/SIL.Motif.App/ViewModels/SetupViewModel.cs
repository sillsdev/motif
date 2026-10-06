using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;
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
    private bool _awaitingFirstRun;
    private int _setupGeneration;
    private Task? _configurationLoadTask;
    private SelectionSnapshot? _snapshot;
    private StepCap _configuredStepLimit = StepCap.Default;
    private ParserStepRate _parserStepRate = StepLimitEstimator.TypicalMachineRate;
    private string _runningStepLimitText = string.Empty;
    private bool _syncingSelectionLimits;

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

    /// <summary>Whether the saved Default Selection can be run from the shell.</summary>
    public bool CanRunDefaultSelection => ProjectPath is not null && _savedSelection is not null;

    /// <summary>The accepted parsing policy shared by Setup, Settings, and Analysis options.</summary>
    public SelectionParsingLimits CurrentSelectionLimits => _savedSelection?.Limits ??
        SelectionLimitPolicy.Estimated(_configuredStepLimit);

    /// <summary>Runs the saved Default Selection with the limits loaded for the project.</summary>
    public Task RunDefaultSelectionAsync()
    {
        if (!CanRunDefaultSelection) return Task.CompletedTask;
        var resolved = SelectionLimitPolicy.Resolve(_savedSelection!.Limits, _parserStepRate);
        var limitMs = resolved.PerWordTimeLimitMs;
        var stepLimit = resolved.PerWordStepLimit;
        RunningStepLimitText = stepLimit.IsUnbounded
            ? "The parse running now has no step limit."
            : $"The parse running now keeps {stepLimit.Steps ?? StepCap.DefaultSteps:N0} steps.";
        return _context.Assess.RunDefaultSelectionAsync(limitMs, stepLimit);
    }

    /// <summary>The step limit captured by the parse running now.</summary>
    public string RunningStepLimitText
    {
        get => _runningStepLimitText;
        private set => SetProperty(ref _runningStepLimitText, value);
    }

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

    public string StepLimitEstimateText
    {
        get
        {
            if (!IsStepLimitValid) return "Enter a positive whole number of analysis attempts, or choose no attempt limit.";
            var estimate = CurrentStepLimitEstimate;
            if (estimate is null) return "No analysis attempt limit. Motif will not apply a per-word time limit.";
            var source = estimate.IsTypicalMachine
                ? " The estimate uses a typical machine."
                : " The estimate uses the parser statistics from the last parse.";
            if (estimate.PerWordTimeLimitMs is not { } limitMs || limitMs > int.MaxValue)
                return $"At this limit a word takes up to about {FormatEstimate(estimate.EstimatedMilliseconds)}. " +
                    $"Motif will not apply a per-word time limit.{source}";
            return $"At this limit a word takes up to about {FormatEstimate(estimate.EstimatedMilliseconds)}. " +
                $"Motif stops a word after {FormatEstimate(limitMs)}.{source}";
        }
    }

    /// <summary>The estimated time limit for the current step limit, or <see langword="null"/>.</summary>
    public int? EstimatedPerWordLimitMs => CurrentStepLimitEstimate?.PerWordTimeLimitMs is { } milliseconds &&
        milliseconds is > 0 and <= int.MaxValue ? (int)milliseconds : null;

    /// <summary>Refreshes the setup fields from the shared Selection without editing it.</summary>
    public void SyncParsingLimitsFromSelection()
    {
        var stepLimit = Selection.PerWordStepLimitUnbounded ? StepCap.Unbounded
            : Selection.PerWordStepLimit is { } steps && steps is > 0 and <= long.MaxValue &&
              decimal.Truncate(steps) == steps ? new StepCap(decimal.ToInt64(steps)) : _configuredStepLimit;
        _syncingSelectionLimits = true;
        try
        {
            IsStepLimitUnbounded = stepLimit.IsUnbounded;
            StepLimitSteps = stepLimit.Steps ?? StepCap.DefaultSteps;
            _configuredStepLimit = stepLimit;
        }
        finally
        {
            _syncingSelectionLimits = false;
        }
        OnPropertyChanged(nameof(StepLimitEstimateText));
        OnPropertyChanged(nameof(EstimatedPerWordLimitMs));
    }

    /// <summary>Saves new parsing limits with the current project's existing Selection.</summary>
    /// <param name="limits">The complete policy to save with the Selection.</param>
    /// <returns>A refusal when the Selection could not be updated.</returns>
    public async Task<WindowRefusal?> SaveParsingLimitsAsync(SelectionParsingLimits limits)
    {
        if (ProjectPath is not { } projectPath || _savedSelection is not { } savedSelection) return null;
        if (limits.ValidationError() is { } invalid) return WindowRefusal.Plain(invalid);
        var generation = _loadGeneration;
        var saved = await _context.Commands.SetSelectionLimitsAsync(new SetSelectionLimitsRequest(
            projectPath, savedSelection.Name, savedSelection.Revision, limits), CancellationToken.None)
            .ConfigureAwait(true);
        if (!saved.Succeeded)
        {
            if (saved.Refusal!.Code == "selection.revision-conflict")
                await ReloadSelectionAfterConflictAsync(projectPath, generation).ConfigureAwait(true);
            return WindowRefusal.From(saved.Refusal);
        }
        if (generation != _loadGeneration || ProjectPath != projectPath) return null;

        _savedSelection = saved.Value;
        ApplyAcceptedLimits(limits);
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        OnPropertyChanged(nameof(CanRunDefaultSelection));
        OnPropertyChanged(nameof(StepLimitEstimateText));
        OnPropertyChanged(nameof(EstimatedPerWordLimitMs));
        return null;
    }

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Why the last step was refused, in the window's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    private WindowRefusal? _shownRefusal;

    public bool HasRefusal => ShownRefusal is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLimitValidationMessage))]
    [NotifyPropertyChangedFor(nameof(StepLimitEstimateText))]
    private decimal? _stepLimitSteps = StepCap.DefaultSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLimitValidationMessage))]
    [NotifyPropertyChangedFor(nameof(StepLimitEstimateText))]
    private bool _isStepLimitUnbounded;

    public string? StepLimitValidationMessage => IsStepLimitUnbounded || IsStepLimitValid
        ? null
        : "Enter a positive whole number of analysis attempts, or choose no attempt limit.";

    private bool IsStepLimitValid => IsStepLimitUnbounded ||
        StepLimitSteps is > 0 and var steps && decimal.Truncate(steps) == steps && steps <= long.MaxValue;

    private StepLimitEstimate? CurrentStepLimitEstimate => IsStepLimitUnbounded || !IsStepLimitValid
        ? null
        : ToEstimate(SelectionLimitPolicy.Resolve(
            SelectionLimitPolicy.Estimated(new StepCap((long)StepLimitSteps!.Value)), _parserStepRate));

    private static StepLimitEstimate? ToEstimate(ResolvedSelectionLimits limits) =>
        limits.EstimatedMilliseconds is { } milliseconds
            ? new StepLimitEstimate(milliseconds, limits.PerWordTimeLimitMs, limits.IsTypicalMachine)
            : null;

    private static string FormatEstimate(decimal milliseconds) => milliseconds switch
    {
        < 1m => "less than 1 ms",
        < 1000m => $"{decimal.Ceiling(milliseconds):N0} ms",
        _ => $"{milliseconds / 1000m:0.#} s",
    };

    private void OpenFirstTimeSetup()
    {
        if (IsOpen) return;
        CaptureSnapshot();
        Step = 0;
        _setupGeneration++;
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
        ? "This will be the project default. Parse all words will use it next time."
        : "Motif will save this Selection with the project, then parse all its words.";

    public IAsyncRelayCommand SkipCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand NextCommand { get; }
    public IAsyncRelayCommand FinishCommand { get; }
    internal Task? ConfigurationLoadTask => _configurationLoadTask;

    /// <summary>Loads the project's stored Selection and resolved per-word limits.</summary>
    public async Task ProjectOpenedAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var generation = ++_loadGeneration;
        IsOpen = false;
        ProjectPath = projectPath;
        Step = 0;
        ShownRefusal = null;

        var saved = await _context.Commands.ReadDefaultSelectionAsync(
            new ReadDefaultSelectionRequest(projectPath), cancellationToken).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        if (!saved.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(saved.Refusal!);
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
            ShownRefusal = WindowRefusal.From(config.Refusal!);
            return;
        }

        var scope = config.Value!.Scopes.FirstOrDefault(candidate => candidate.Name == "default")
            ?? config.Value.Scopes.FirstOrDefault();
        var stepLimit = _savedSelection?.Limits.PerWordStepLimit ?? scope?.PerWordStepLimit ?? StepCap.Default;
        _configuredStepLimit = stepLimit;
        Selection.PerWordStepLimit = stepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = stepLimit.IsUnbounded;
        SetStepLimit(stepLimit);

        var rate = await _context.Commands.ReadParserStepRateAsync(projectPath, cancellationToken)
            .ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        _parserStepRate = rate.Succeeded ? rate.Value! : StepLimitEstimator.TypicalMachineRate;
        ApplyAcceptedLimits(_savedSelection?.Limits ?? SelectionLimitPolicy.Estimated(stepLimit));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        OnPropertyChanged(nameof(StepLimitEstimateText));

        IsEditingExistingSelection = _savedSelection is not null;
        OnPropertyChanged(nameof(CanRunDefaultSelection));
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
        ++_setupGeneration;
        IsOpen = false;
        ProjectPath = null;
        _savedSelection = null;
        _setupSkipped = false;
        _snapshot = null;
        _configuredStepLimit = StepCap.Default;
        _parserStepRate = StepLimitEstimator.TypicalMachineRate;
        IsEditingExistingSelection = false;
        OnPropertyChanged(nameof(CanRunDefaultSelection));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        ShownRefusal = null;
    }

    void IProjectStateParticipant.ClearProject() => ProjectCleared();

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Setup;

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        ProjectOpenedAsync(projectPath, cancellationToken);

    /// <summary>Reopens setup from the project menu at its first step and with the saved values.</summary>
    public void OpenForConfiguration()
    {
        if (ProjectPath is null || _context.Baseline?.HasBaseline != true) return;
        var projectPath = ProjectPath;
        _awaitingFirstRun = false;
        ShownRefusal = null;
        if (_savedSelection is { } saved)
        {
            Selection.ApplyDefaultSelection(saved);
            ApplyAcceptedLimits(saved.Limits);
        }
        else
        {
            Selection.PerWordStepLimit = _configuredStepLimit.Steps;
            Selection.PerWordStepLimitUnbounded = _configuredStepLimit.IsUnbounded;
            SetStepLimit(_configuredStepLimit);
            ApplyAcceptedLimits(SelectionLimitPolicy.Estimated(_configuredStepLimit));
        }
        IsEditingExistingSelection = _savedSelection is not null;
        CaptureSnapshot();
        Step = 0;
        _setupGeneration++;
        IsOpen = true;
        _configurationLoadTask = RefreshParserStepRateAsync(projectPath);
    }

    private async Task FinishAsync()
    {
        if (!IsFirstRunStep || ProjectPath is not { } projectPath) return;
        var shapes = new List<string>
        {
            UsageArgumentShape.Text("fwDataPath"),
            UsageArgumentShape.List("textIds", Selection.ChosenTextIds.Count),
            UsageArgumentShape.List("addedWords", Selection.PastedWordEntries.Count),
        };
        if (IsStepLimitUnbounded || StepLimitSteps is not null)
            shapes.Add(UsageArgumentShape.Number("perWordStepLimit"));
        if (Selection.PerWordTimeLimitSeconds is not null)
            shapes.Add(UsageArgumentShape.Number("perWordTimeLimitSeconds"));
        using var usageAction = _context.Commands.BeginUsageAction("selection set-default", [.. shapes]);
        var runFirstAssessment = !IsEditingExistingSelection;
        if (!IsStepLimitValid)
        {
            OnPropertyChanged(nameof(StepLimitValidationMessage));
            return;
        }
        if (!Selection.CanAssess)
        {
            ShownRefusal = WindowRefusal.Plain("Choose at least one text or add a word before continuing.");
            return;
        }

        var stepLimit = IsStepLimitUnbounded ? StepCap.Unbounded : new StepCap((long)StepLimitSteps!.Value);
        var previousLimits = _savedSelection?.Limits;
        var limits = stepLimit.IsUnbounded
            ? SelectionLimitPolicy.Estimated(stepLimit)
            : previousLimits is { TimeMode: SelectionTimeLimitMode.Explicit, ExplicitPerWordLimitMs: > 0 } explicitLimits
                ? SelectionLimitPolicy.Explicit(stepLimit, explicitLimits.ExplicitPerWordLimitMs!.Value)
                : SelectionLimitPolicy.Estimated(stepLimit);
        var timeLimitMs = SelectionLimitPolicy.Resolve(limits, _parserStepRate).PerWordTimeLimitMs;
        Selection.PerWordStepLimit = stepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = stepLimit.IsUnbounded;
        var loadGeneration = _loadGeneration;
        var saved = await _context.Commands.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            projectPath, _savedSelection?.Name ?? DefaultSelectionName,
            Selection.ChosenTextIds, Selection.PastedWordEntries,
            limits, _savedSelection?.Revision),
            CancellationToken.None).ConfigureAwait(true);
        if (!saved.Succeeded)
        {
            if (saved.Refusal!.Code == "selection.revision-conflict")
                await ReloadSelectionAfterConflictAsync(projectPath, loadGeneration).ConfigureAwait(true);
            ShownRefusal = WindowRefusal.From(saved.Refusal!);
            return;
        }

        _savedSelection = saved.Value!.Selection;
        OnPropertyChanged(nameof(CanRunDefaultSelection));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        _setupSkipped = false;
        _configuredStepLimit = stepLimit;
        Selection.PerWordTimeLimitSeconds = timeLimitMs is { } savedLimitMs ? savedLimitMs / 1000m : null;
        CaptureSnapshot();

        if (!runFirstAssessment)
        {
            IsEditingExistingSelection = true;
            IsOpen = false;
            return;
        }

        var setupGeneration = _setupGeneration;
        _awaitingFirstRun = true;
        try
        {
            await _context.Assess.RunDefaultSelectionAsync(timeLimitMs, stepLimit).ConfigureAwait(true);
        }
        finally
        {
            _awaitingFirstRun = false;
        }
        if (!IsOpen || _setupGeneration != setupGeneration) return;
        if (_context.Assess.State == RunState.Completed)
        {
            IsEditingExistingSelection = true;
            IsOpen = false;
            ShownRefusal = null;
        }
        else
        {
            IsEditingExistingSelection = false;
            ShownRefusal = _context.Assess.ShownRefusal;
        }
    }

    private bool CanFinish() => IsFirstRunStep && IsStepLimitValid &&
        Selection.CanAssess && !_context.Assess.IsActive;

    private async Task SkipAsync()
    {
        if (ProjectPath is not { } projectPath) return;
        using var usageAction = _context.Commands.BeginUsageAction("setup skip",
            UsageArgumentShape.Text("fwDataPath"));
        var result = await _context.Commands.SkipSetupAsync(
            new SkipSetupRequest(projectPath), CancellationToken.None).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            ShownRefusal = WindowRefusal.From(result.Refusal!);
            return;
        }
        _setupSkipped = result.Value!.SetupSkipped;
        if (_snapshot is not null) RestoreSnapshot(_snapshot);
        _setupGeneration++;
        IsOpen = false;
        ShownRefusal = null;
    }

    private void ApplyAcceptedLimits(SelectionParsingLimits limits)
    {
        var resolved = SelectionLimitPolicy.Resolve(limits, _parserStepRate);
        var stepLimit = resolved.PerWordStepLimit;
        _configuredStepLimit = stepLimit;
        Selection.PerWordTimeLimitSeconds = resolved.PerWordTimeLimitMs is { } limitMs ? limitMs / 1000m : null;
        Selection.PerWordStepLimit = stepLimit.Steps;
        Selection.PerWordStepLimitUnbounded = stepLimit.IsUnbounded;
        SetStepLimit(stepLimit);
    }

    private async Task ReloadSelectionAfterConflictAsync(string projectPath, int generation)
    {
        var current = await _context.Commands.ReadDefaultSelectionAsync(
            new ReadDefaultSelectionRequest(projectPath), CancellationToken.None).ConfigureAwait(true);
        if (generation != _loadGeneration || ProjectPath != projectPath || !current.Succeeded) return;
        _savedSelection = current.Value!.Selection;
        if (_savedSelection is { } saved)
        {
            Selection.ApplyDefaultSelection(saved);
            ApplyAcceptedLimits(saved.Limits);
        }
        OnPropertyChanged(nameof(CanRunDefaultSelection));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        OnPropertyChanged(nameof(StepLimitEstimateText));
        OnPropertyChanged(nameof(EstimatedPerWordLimitMs));
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

    partial void OnStepLimitStepsChanged(decimal? value)
    {
        if (!_syncingSelectionLimits) UpdateStepLimit();
    }

    partial void OnIsStepLimitUnboundedChanged(bool value)
    {
        if (!_syncingSelectionLimits) UpdateStepLimit();
    }

    private void UpdateStepLimit()
    {
        Selection.PerWordStepLimitUnbounded = IsStepLimitUnbounded;
        Selection.PerWordStepLimit = !IsStepLimitUnbounded && IsStepLimitValid ? StepLimitSteps : null;
        OnPropertyChanged(nameof(StepLimitValidationMessage));
        OnPropertyChanged(nameof(StepLimitEstimateText));
        FinishCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionViewModel.CanAssess) or nameof(SelectionViewModel.PastedWords))
        {
            OnPropertyChanged(nameof(AddedWordsText));
            OnPropertyChanged(nameof(RunSummary));
            FinishCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnAssessmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssessViewModel.IsActive)) FinishCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(AssessViewModel.Progress) && _awaitingFirstRun &&
            _context.Assess.Progress is not null)
        {
            _awaitingFirstRun = false;
            IsEditingExistingSelection = true;
            _setupGeneration++;
            IsOpen = false;
            ShownRefusal = null;
        }
    }

    private async Task RefreshParserStepRateAsync(string projectPath)
    {
        var rate = await _context.Commands.ReadParserStepRateAsync(projectPath, CancellationToken.None)
            .ConfigureAwait(true);
        if (ProjectPath != projectPath || !IsOpen) return;
        _parserStepRate = rate.Succeeded ? rate.Value! : StepLimitEstimator.TypicalMachineRate;
        OnPropertyChanged(nameof(StepLimitEstimateText));
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
