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
    private bool _isSaving;
    private bool _awaitingFirstRun;
    private int _setupGeneration;
    private Task? _configurationLoadTask;
    private SelectionSnapshot? _snapshot;
    private ParserWorkLimit _configuredParserWorkLimit = ParserWorkLimit.Default;
    private ParserAttemptRate _parserStepRate = ParserWorkLimitEstimator.TypicalMachineRate;
    private string _runningParserWorkLimitText = string.Empty;
    private bool _syncingSelectionLimits;
    private bool _parserWorkLimitErrorShown;

    public SetupViewModel(WorkspaceContext context, TextWordsViewModel words)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(words);
        _context = context;
        _words = words;
        Selection = context.Selection;
        Indicators = Enumerable.Range(0, 4).Select(step => new SetupStepIndicator(step, step == 0)).ToArray();
        SkipCommand = new AsyncRelayCommand(SkipAsync);
        CancelCommand = new RelayCommand(Cancel, () => !_isSaving);
        BackCommand = new RelayCommand(() => Step--, () => Step > 0);
        NextCommand = new RelayCommand(Next, () => Step < 3);
        FinishCommand = new AsyncRelayCommand(FinishAsync, CanFinish);
        Selection.PropertyChanged += OnSelectionChanged;
        context.Assess.PropertyChanged += OnAssessmentChanged;
        words.PropertyChanged += OnWordsChanged;
    }

    public SelectionViewModel Selection { get; }

    /// <summary>Whether the saved Default Selection can be run from the shell.</summary>
    /// <summary>The saved Default Selection's name, or <see langword="null"/> before one is saved.</summary>
    internal string? SavedSelectionName => _savedSelection?.Name;

    public bool CanRunDefaultSelection => ProjectPath is not null && _savedSelection is not null;

    /// <summary>Whether the saved Default Selection contains every current project Text.</summary>
    internal bool SavedSelectionContainsAllTexts => _savedSelection is { } saved &&
        saved.TextIds.ToHashSet().SetEquals(Selection.AllTextIds);

    /// <summary>The accepted parsing policy shared by Setup, Settings, and Analysis options.</summary>
    public SelectionParsingLimits CurrentSelectionLimits => _savedSelection?.Limits ??
        SelectionLimitPolicy.Estimated(_configuredParserWorkLimit);

    /// <summary>Runs the saved Default Selection with the limits loaded for the project.</summary>
    public Task RunDefaultSelectionAsync()
    {
        if (!CanRunDefaultSelection) return Task.CompletedTask;
        var resolved = SelectionLimitPolicy.Resolve(_savedSelection!.Limits, _parserStepRate);
        var limitMs = ExplicitTimeLimitMs(_savedSelection.Limits);
        var parserWorkLimit = resolved.PerWordParserWorkLimit;
        RunningParserWorkLimitText = parserWorkLimit.IsUnbounded
            ? MotifAppStrings.RunningNoParserWorkLimit
            : string.Format(MotifAppStrings.RunningParserWorkLimitFormat, parserWorkLimit.Work ?? ParserWorkLimit.DefaultWork);
        return _context.Assess.RunDefaultSelectionAsync(limitMs, parserWorkLimit);
    }

    /// <summary>The time limit the saved Selection chose itself; an estimated time is left to the command.</summary>
    internal int? SavedExplicitTimeLimitMs => ExplicitTimeLimitMs(CurrentSelectionLimits);

    // An estimated time is left to the command, which sizes the same safety net the CLI uses.
    private static int? ExplicitTimeLimitMs(SelectionParsingLimits limits) =>
        limits.TimeMode == SelectionTimeLimitMode.Explicit ? limits.ExplicitPerWordLimitMs : null;

    /// <summary>The parser work limit captured by the parse running now.</summary>
    public string RunningParserWorkLimitText
    {
        get => _runningParserWorkLimitText;
        private set => SetProperty(ref _runningParserWorkLimitText, value);
    }

    public IReadOnlyList<SetupStepIndicator> Indicators { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    [NotifyPropertyChangedFor(nameof(StepCaption))]
    private string? _projectPath;

    public string ProjectName => ProjectPath is null ? string.Empty : Path.GetFileName(ProjectPath);

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
        1 => MotifAppStrings.SetupNextLimits,
        2 => IsEditingExistingSelection ? MotifAppStrings.SetupNextUseSelection : MotifAppStrings.SetupNextFirstRun,
        _ => MotifAppStrings.SetupNextTexts,
    };

    public string StepCaption => string.Format(MotifAppStrings.SetupStepCaptionFormat,
        ProjectName, Step + 1);

    public string ParserWorkLimitEstimateText
    {
        get
        {
            if (!IsParserWorkLimitValid) return ParserWorkLimitValidationMessage ?? string.Empty;
            if (IsParserWorkLimitUnbounded) return MotifAppStrings.SetupNoParserWorkLimitCaption;
            return string.Format(MotifAppStrings.SetupParserWorkLimitFormat,
                PluralText.Format(MotifAppStrings.ParserWorkLimitPattern, (long)ParserWorkLimitValue!.Value));
        }
    }

    /// <summary>The estimated time limit for the current parser work limit, or <see langword="null"/>.</summary>
    public int? EstimatedPerWordLimitMs => CurrentParserWorkLimitEstimate?.PerWordTimeLimitMs is { } milliseconds &&
        milliseconds is > 0 and <= int.MaxValue ? (int)milliseconds : null;

    /// <summary>Refreshes the setup fields from the shared Selection without editing it.</summary>
    public void SyncParsingLimitsFromSelection()
    {
        var parserWorkLimit = Selection.PerWordParserWorkLimitUnbounded ? ParserWorkLimit.Unbounded
            : Selection.PerWordParserWorkLimit is { } steps && steps is > 0 and <= long.MaxValue &&
              decimal.Truncate(steps) == steps ? new ParserWorkLimit(decimal.ToInt64(steps)) : _configuredParserWorkLimit;
        _syncingSelectionLimits = true;
        _parserWorkLimitErrorShown = false;
        try
        {
            IsParserWorkLimitUnbounded = parserWorkLimit.IsUnbounded;
            ParserWorkLimitValue = parserWorkLimit.Work ?? ParserWorkLimit.DefaultWork;
            _configuredParserWorkLimit = parserWorkLimit;
        }
        finally
        {
            _syncingSelectionLimits = false;
        }
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
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
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
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
    [NotifyPropertyChangedFor(nameof(ParserWorkLimitValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ParserWorkLimitEstimateText))]
    private decimal? _parserWorkLimitValue = ParserWorkLimit.DefaultWork;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParserWorkLimitValidationMessage))]
    [NotifyPropertyChangedFor(nameof(ParserWorkLimitEstimateText))]
    private bool _isParserWorkLimitUnbounded;

    // Shown on leave or confirm; cleared once the input is valid.
    public string? ParserWorkLimitValidationMessage => _parserWorkLimitErrorShown && !IsParserWorkLimitValid
        ? MotifAppStrings.EnterPositiveWholeNumberOfParserWork
        : null;

    /// <summary>Checks the parser work limit and shows its error when invalid.</summary>
    /// <returns>Whether the limit is valid.</returns>
    public bool ValidateParserWorkLimit()
    {
        var valid = IsParserWorkLimitValid;
        _parserWorkLimitErrorShown = !valid;
        OnPropertyChanged(nameof(ParserWorkLimitValidationMessage));
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
        return valid;
    }

    private void Next()
    {
        if (IsLimitsStep && !ValidateParserWorkLimit()) return;
        Step++;
    }

    private bool IsParserWorkLimitValid => IsParserWorkLimitUnbounded ||
        ParserWorkLimitValue is > 0 and var steps && decimal.Truncate(steps) == steps && steps <= long.MaxValue;

    private ParserWorkLimitEstimate? CurrentParserWorkLimitEstimate => IsParserWorkLimitUnbounded || !IsParserWorkLimitValid
        ? null
        : ToEstimate(SelectionLimitPolicy.Resolve(
            SelectionLimitPolicy.Estimated(new ParserWorkLimit((long)ParserWorkLimitValue!.Value)), _parserStepRate));

    private static ParserWorkLimitEstimate? ToEstimate(ResolvedSelectionLimits limits) =>
        limits.EstimatedMilliseconds is { } milliseconds
            ? new ParserWorkLimitEstimate(milliseconds, limits.PerWordTimeLimitMs, limits.IsTypicalMachine)
            : null;

    private void OpenFirstTimeSetup()
    {
        if (IsOpen) return;
        CaptureSnapshot();
        Step = 0;
        _setupGeneration++;
        IsOpen = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(FinishButtonText))]
    [NotifyPropertyChangedFor(nameof(FinishTitle))]
    [NotifyPropertyChangedFor(nameof(FinishDescription))]
    private bool _isEditingExistingSelection;

    public string AddedWordsText => Selection.PastedWordEntries.Count == 0
        ? MotifAppStrings.NoAddedWords
        : string.Join(", ", Selection.PastedWordEntries);

    public string RunSummary => _words.SummaryText;

    public string FinishButtonText => IsEditingExistingSelection ? MotifAppStrings.UseThisSelection : MotifAppStrings.StartFirstRun;

    public string FinishTitle => IsEditingExistingSelection
        ? MotifAppStrings.SetupUseSelectionTitle : MotifAppStrings.SetupFirstRunTitle;

    public string FinishDescription => IsEditingExistingSelection
        ? MotifAppStrings.SetupUseSelectionDescription
        : MotifAppStrings.SetupFirstRunDescription;

    public IAsyncRelayCommand SkipCommand { get; }

    /// <summary>Closes the dialog without saving or recording a skip, so first-time setup is offered again later.</summary>
    public IRelayCommand CancelCommand { get; }
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
        var parserWorkLimit = _savedSelection?.Limits.PerWordParserWorkLimit ?? scope?.PerWordParserWorkLimit ?? ParserWorkLimit.Default;
        _configuredParserWorkLimit = parserWorkLimit;
        Selection.PerWordParserWorkLimit = parserWorkLimit.Work;
        Selection.PerWordParserWorkLimitUnbounded = parserWorkLimit.IsUnbounded;
        SetParserWorkLimit(parserWorkLimit);

        var rate = await _context.Commands.ReadParserAttemptRateAsync(projectPath, cancellationToken)
            .ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        _parserStepRate = rate.Succeeded ? rate.Value! : ParserWorkLimitEstimator.TypicalMachineRate;
        ApplyAcceptedLimits(_savedSelection?.Limits ?? SelectionLimitPolicy.Estimated(parserWorkLimit));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));

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
        _configuredParserWorkLimit = ParserWorkLimit.Default;
        _parserStepRate = ParserWorkLimitEstimator.TypicalMachineRate;
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
            Selection.PerWordParserWorkLimit = _configuredParserWorkLimit.Work;
            Selection.PerWordParserWorkLimitUnbounded = _configuredParserWorkLimit.IsUnbounded;
            SetParserWorkLimit(_configuredParserWorkLimit);
            ApplyAcceptedLimits(SelectionLimitPolicy.Estimated(_configuredParserWorkLimit));
        }
        IsEditingExistingSelection = _savedSelection is not null;
        CaptureSnapshot();
        Step = 0;
        _setupGeneration++;
        IsOpen = true;
        _configurationLoadTask = RefreshParserAttemptRateAsync(projectPath);
    }

    /// <summary>Persists all project Texts as the Selection and updates Setup's shared choices.</summary>
    public async Task<WindowRefusal?> SelectAllTextsAsync()
    {
        if (ProjectPath is not { } projectPath || _savedSelection is not { } saved ||
            SavedSelectionContainsAllTexts) return null;

        var generation = _loadGeneration;
        Selection.SelectAllTexts();
        var result = await _context.Commands.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            projectPath, saved.Name, Selection.ChosenTextIds, saved.AddedWords, saved.Limits, saved.Revision),
            CancellationToken.None).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            if (result.Refusal!.Code == "selection.revision-conflict")
                await ReloadSelectionAfterConflictAsync(projectPath, generation).ConfigureAwait(true);
            else if (generation == _loadGeneration && ProjectPath == projectPath)
                Selection.ApplyDefaultSelection(saved);
            return WindowRefusal.From(result.Refusal!);
        }

        if (generation != _loadGeneration || ProjectPath != projectPath) return null;
        _savedSelection = result.Value!.Selection;
        Selection.ApplyDefaultSelection(_savedSelection);
        OnPropertyChanged(nameof(CanRunDefaultSelection));
        OnPropertyChanged(nameof(CurrentSelectionLimits));
        CaptureSnapshot();
        return null;
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
        if (IsParserWorkLimitUnbounded || ParserWorkLimitValue is not null)
            shapes.Add(UsageArgumentShape.Number("perWordParserWorkLimit"));
        if (Selection.PerWordTimeLimitSeconds is not null)
            shapes.Add(UsageArgumentShape.Number("perWordTimeLimitSeconds"));
        using var usageAction = _context.Commands.BeginUsageAction("selection set-default", [.. shapes]);
        var runFirstAssessment = !IsEditingExistingSelection;
        if (!ValidateParserWorkLimit()) return;
        if (!Selection.CanAssess)
        {
            ShownRefusal = WindowRefusal.Plain(MotifAppStrings.SetupChooseTextOrWord);
            return;
        }

        var parserWorkLimit = IsParserWorkLimitUnbounded ? ParserWorkLimit.Unbounded : new ParserWorkLimit((long)ParserWorkLimitValue!.Value);
        var previousLimits = _savedSelection?.Limits;
        var limits = parserWorkLimit.IsUnbounded
            ? SelectionLimitPolicy.Estimated(parserWorkLimit)
            : previousLimits is { TimeMode: SelectionTimeLimitMode.Explicit, ExplicitPerWordLimitMs: > 0 } explicitLimits
                ? SelectionLimitPolicy.Explicit(parserWorkLimit, explicitLimits.ExplicitPerWordLimitMs!.Value)
                : SelectionLimitPolicy.Estimated(parserWorkLimit);
        limits = limits with { AssessBudgetSeconds = previousLimits?.AssessBudgetSeconds ?? SelectionParsingLimits.DefaultAssessBudgetSeconds };
        var timeLimitMs = SelectionLimitPolicy.Resolve(limits, _parserStepRate).PerWordTimeLimitMs;
        Selection.PerWordParserWorkLimit = parserWorkLimit.Work;
        Selection.PerWordParserWorkLimitUnbounded = parserWorkLimit.IsUnbounded;
        var loadGeneration = _loadGeneration;
        var saved = await WhileSavingAsync(() => _context.Commands.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            projectPath, _savedSelection?.Name ?? DefaultSelectionName,
            Selection.ChosenTextIds, Selection.PastedWordEntries,
            limits, _savedSelection?.Revision),
            CancellationToken.None)).ConfigureAwait(true);
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
        _configuredParserWorkLimit = parserWorkLimit;
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
            await _context.Assess.RunDefaultSelectionAsync(ExplicitTimeLimitMs(limits), parserWorkLimit).ConfigureAwait(true);
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

    private bool CanFinish() => IsFirstRunStep && IsParserWorkLimitValid &&
        Selection.CanAssess && !_context.Assess.IsActive;

    // Cancel and Esc wait out the Selection save; afterwards Cancel closes like Skip and the run carries on.
    private async Task<T> WhileSavingAsync<T>(Func<Task<T>> save)
    {
        _isSaving = true;
        CancelCommand.NotifyCanExecuteChanged();
        try
        {
            return await save().ConfigureAwait(true);
        }
        finally
        {
            _isSaving = false;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private void Cancel()
    {
        if (!IsOpen || _isSaving) return;
        if (_snapshot is not null) RestoreSnapshot(_snapshot);
        _setupGeneration++;
        IsOpen = false;
        ShownRefusal = null;
    }

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
        var parserWorkLimit = resolved.PerWordParserWorkLimit;
        _configuredParserWorkLimit = parserWorkLimit;
        Selection.PerWordTimeLimitSeconds = resolved.PerWordTimeLimitMs is { } limitMs ? limitMs / 1000m : null;
        Selection.PerWordParserWorkLimit = parserWorkLimit.Work;
        Selection.PerWordParserWorkLimitUnbounded = parserWorkLimit.IsUnbounded;
        SetParserWorkLimit(parserWorkLimit);
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
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
        OnPropertyChanged(nameof(EstimatedPerWordLimitMs));
    }

    private void SetParserWorkLimit(ParserWorkLimit parserWorkLimit)
    {
        _parserWorkLimitErrorShown = false;
        IsParserWorkLimitUnbounded = parserWorkLimit.IsUnbounded;
        ParserWorkLimitValue = parserWorkLimit.Work ?? ParserWorkLimitValue ?? ParserWorkLimit.DefaultWork;
    }

    private void CaptureSnapshot() => _snapshot = new SelectionSnapshot(
        Selection.ChosenTextIds.ToArray(), Selection.PastedWords, Selection.AllWordforms,
        Selection.RetryFailed, Selection.RetryWorkOver, Selection.PerWordTimeLimitSeconds,
        Selection.PerWordParserWorkLimitUnbounded ? ParserWorkLimit.Unbounded
            : Selection.PerWordParserWorkLimit is { } steps ? new ParserWorkLimit(decimal.ToInt64(steps)) : ParserWorkLimit.Default);

    private void RestoreSnapshot(SelectionSnapshot snapshot)
    {
        Selection.ApplyDefaultSelection(new NamedSelectionProjection(
            DefaultSelectionName, snapshot.TextIds, SplitAddedWords(snapshot.PastedWords), string.Empty, string.Empty));
        Selection.AllWordforms = snapshot.AllWordforms;
        Selection.RetryFailed = snapshot.RetryFailed;
        Selection.RetryWorkOver = snapshot.RetryWorkOver;
        Selection.PerWordTimeLimitSeconds = snapshot.PerWordTimeLimitSeconds;
        Selection.PerWordParserWorkLimit = snapshot.PerWordParserWorkLimit.Work;
        Selection.PerWordParserWorkLimitUnbounded = snapshot.PerWordParserWorkLimit.IsUnbounded;
        SetParserWorkLimit(snapshot.PerWordParserWorkLimit);
    }

    partial void OnStepChanged(int value)
    {
        foreach (var indicator in Indicators) indicator.IsCurrent = indicator.Step == value;
        OnPropertyChanged(nameof(NextButtonText));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        FinishCommand.NotifyCanExecuteChanged();
    }

    partial void OnParserWorkLimitValueChanged(decimal? value)
    {
        if (!_syncingSelectionLimits) UpdateParserWorkLimit();
    }

    partial void OnIsParserWorkLimitUnboundedChanged(bool value)
    {
        if (_syncingSelectionLimits) return;
        UpdateParserWorkLimit();
        // A changed tick is not a leave or a confirm, so a stale error goes and a new one waits.
        _parserWorkLimitErrorShown = false;
        OnPropertyChanged(nameof(ParserWorkLimitValidationMessage));
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
    }

    private void UpdateParserWorkLimit()
    {
        Selection.PerWordParserWorkLimitUnbounded = IsParserWorkLimitUnbounded;
        Selection.PerWordParserWorkLimit = !IsParserWorkLimitUnbounded && IsParserWorkLimitValid ? ParserWorkLimitValue : null;
        OnPropertyChanged(nameof(ParserWorkLimitValidationMessage));
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
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

    private async Task RefreshParserAttemptRateAsync(string projectPath)
    {
        var rate = await _context.Commands.ReadParserAttemptRateAsync(projectPath, CancellationToken.None)
            .ConfigureAwait(true);
        if (ProjectPath != projectPath || !IsOpen) return;
        _parserStepRate = rate.Succeeded ? rate.Value! : ParserWorkLimitEstimator.TypicalMachineRate;
        OnPropertyChanged(nameof(ParserWorkLimitEstimateText));
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
        decimal? RetryWorkOver, decimal? PerWordTimeLimitSeconds, ParserWorkLimit PerWordParserWorkLimit);
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
