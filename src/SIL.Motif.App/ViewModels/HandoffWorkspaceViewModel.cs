using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Composes the six child view models of the first Motif window into one workflow: choosing a project
/// cancels whatever Assessment or Handoff run is active, clears the state the previous project displayed,
/// and loads the new project's Baseline and Text state. This is also where the two loose ends the child
/// view models left for composition get wired: <see cref="ViewModels.BaselineViewModel.HasAssessment"/>
/// is set once an Assessment actually completes, and <see cref="ViewModels.StatisticsViewModel.SummaryMarkdown"/>
/// is fed from that same completed Assessment's own rendered summary. It also owns which of the five
/// <see cref="WorkflowStage"/>s the window shows, and what each stage's stepper entry says.
/// </summary>
public sealed partial class HandoffWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private string? _projectPath;

    public HandoffWorkspaceViewModel(
        ProjectViewModel project, BaselineViewModel baseline, SelectionViewModel selection,
        AssessViewModel assess, StatisticsViewModel statistics, HandoffViewModel handoff)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(handoff);

        Project = project;
        Baseline = baseline;
        Selection = selection;
        Assess = assess;
        Statistics = statistics;
        Handoff = handoff;

        Stages =
        [
            new StageViewModel(WorkflowStage.Project, "Project"),
            new StageViewModel(WorkflowStage.Grammar, "Grammar"),
            new StageViewModel(WorkflowStage.Texts, "Texts"),
            new StageViewModel(WorkflowStage.Results, "Results"),
            new StageViewModel(WorkflowStage.Handoff, "Handoff"),
        ];
        ShowStageCommand = new RelayCommand<WorkflowStage>(stage => CurrentStage = stage);
        ShowResultsViewCommand = new RelayCommand<ResultsView>(view => ResultsView = view);

        Project.ProjectChosen += OnProjectChosen;
        Baseline.Refreshed += OnBaselineRefreshed;
        Baseline.OfferRerun += OnOfferRerun;
        Baseline.PropertyChanged += OnChildPropertyChanged;
        Selection.PropertyChanged += OnChildPropertyChanged;
        Handoff.PropertyChanged += OnChildPropertyChanged;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        Assess.GrammarWarnings.PropertyChanged += OnChildPropertyChanged;

        AcceptRerunCommand = new AsyncRelayCommand(AcceptRerunAsync, () => RerunOffered);
        DismissRerunCommand = new RelayCommand(() => RerunOffered = false, () => RerunOffered);
        RefreshStages();
    }

    /// <summary>The stage stepper's entries, in workflow order.</summary>
    public IReadOnlyList<StageViewModel> Stages { get; }

    /// <summary>The stage the window is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedStage))]
    [NotifyPropertyChangedFor(nameof(IsProjectStage))]
    [NotifyPropertyChangedFor(nameof(IsGrammarStage))]
    [NotifyPropertyChangedFor(nameof(IsTextsStage))]
    [NotifyPropertyChangedFor(nameof(IsResultsStage))]
    [NotifyPropertyChangedFor(nameof(IsHandoffStage))]
    private WorkflowStage _currentStage;

    /// <summary>The stepper entry for <see cref="CurrentStage"/>; setting it opens that stage.</summary>
    public StageViewModel SelectedStage
    {
        get => Stages[(int)CurrentStage];
        set
        {
            if (value is not null) CurrentStage = value.Stage;
        }
    }

    public bool IsProjectStage => CurrentStage == WorkflowStage.Project;

    public bool IsGrammarStage => CurrentStage == WorkflowStage.Grammar;

    public bool IsTextsStage => CurrentStage == WorkflowStage.Texts;

    public bool IsResultsStage => CurrentStage == WorkflowStage.Results;

    public bool IsHandoffStage => CurrentStage == WorkflowStage.Handoff;

    /// <summary>Opens the stage passed as the command parameter.</summary>
    public IRelayCommand<WorkflowStage> ShowStageCommand { get; }

    /// <summary>Which view of a finished Assessment the Results stage is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultsWords))]
    [NotifyPropertyChangedFor(nameof(ShowResultsStatistics))]
    private ResultsView _resultsView;

    public bool ShowResultsWords => ResultsView == ResultsView.Words;

    public bool ShowResultsStatistics => ResultsView == ResultsView.Statistics;

    /// <summary>Opens the Results view passed as the command parameter.</summary>
    public IRelayCommand<ResultsView> ShowResultsViewCommand { get; }

    /// <summary>The chosen project's file name for the top bar, or a prompt before one is chosen.</summary>
    public string ProjectName => _projectPath is null ? "No project chosen" : Path.GetFileName(_projectPath);

    /// <summary>Whether a project has been chosen in this window.</summary>
    public bool HasProject => _projectPath is not null;

    /// <summary>The Baseline's captured time for the project menu.</summary>
    public string BaselineHeaderText => $"Baseline: {Baseline.CapturedTimeText}";

    /// <summary>What the Handoff stage's action reads: the first write, or a rewrite.</summary>
    public string HandoffActionText => Handoff.HasCompletedFiles ? "Write Handoff again" : "Write Handoff";

    /// <summary>Where the Handoff stands, for the stepper and the Handoff stage.</summary>
    public string HandoffStatusText => Handoff.HasCompletedFiles
        ? $"Written: {Handoff.Files.Count} file(s)"
        : "Not written yet";

    partial void OnCurrentStageChanged(WorkflowStage value) => RefreshStages();

    public ProjectViewModel Project { get; }

    public BaselineViewModel Baseline { get; }

    public SelectionViewModel Selection { get; }

    public AssessViewModel Assess { get; }

    public StatisticsViewModel Statistics { get; }

    public HandoffViewModel Handoff { get; }

    /// <summary>Whether a successful Refresh replaced a Baseline an Assessment already covered.</summary>
    [ObservableProperty]
    private bool _rerunOffered;

    /// <summary>Whether the current project has completed at least one Assessment since it was chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotYetAssessed))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyResults))]
    private bool _hasEverAssessed;

    /// <summary>The negation <see cref="HasEverAssessed"/> binds against, so a view never composes <c>!</c> itself.</summary>
    public bool NotYetAssessed => !HasEverAssessed;

    /// <summary>Whether the Words view has nothing to show and nothing to explain yet: no run, no refusal.</summary>
    public bool ShowEmptyResults => NotYetAssessed && !Assess.IsActive && Assess.Refusal is null;

    public IAsyncRelayCommand AcceptRerunCommand { get; }

    public IRelayCommand DismissRerunCommand { get; }

    /// <summary>Whether Project and Selection controls should accept input right now.</summary>
    public bool ProjectAndSelectionEnabled => !Assess.IsActive;

    partial void OnHasEverAssessedChanged(bool value) => RefreshStages();

    partial void OnRerunOfferedChanged(bool value)
    {
        AcceptRerunCommand.NotifyCanExecuteChanged();
        DismissRerunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels any active Assessment or Handoff run, clears whatever the previous project displayed, and
    /// loads the newly chosen project's Baseline and Text state.
    /// </summary>
    public async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);

        await CancelActiveWorkAsync().ConfigureAwait(true);
        ClearProjectBoundState();
        _projectPath = fwDataPath;
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(HasProject));
        CurrentStage = WorkflowStage.Project;
        RefreshStages();

        await Baseline.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        await Selection.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);

        Assess.ProjectPath = fwDataPath;
        Statistics.ProjectPath = fwDataPath;
        Handoff.ProjectPath = fwDataPath;
    }

    private async void OnProjectChosen(object? sender, string fwDataPath) =>
        await SetProjectAsync(fwDataPath).ConfigureAwait(true);

    // The Text list belongs to the Baseline just captured, not to the one (or none) shown before Refresh.
    private async void OnBaselineRefreshed(object? sender, EventArgs e)
    {
        if (_projectPath is { } path) await Selection.LoadTextsAsync(path).ConfigureAwait(true);
    }

    private void OnOfferRerun(object? sender, EventArgs e) => RerunOffered = true;

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(BaselineHeaderText));
        OnPropertyChanged(nameof(HandoffActionText));
        OnPropertyChanged(nameof(HandoffStatusText));
        RefreshStages();
    }

    private void RefreshStages()
    {
        var project = Stages[(int)WorkflowStage.Project];
        project.Summary = !HasProject ? "Choose a project" : Baseline.HasBaseline ? "Baseline captured" : "No Baseline yet";
        project.IsDone = Baseline.HasBaseline;

        var grammar = Stages[(int)WorkflowStage.Grammar];
        var findings = Assess.GrammarWarnings.TotalCount;
        grammar.Summary = Assess.Result is null ? "Findings appear after the first Assessment"
            : findings == 0 ? "No findings" : $"{findings} finding(s)";
        grammar.Badge = findings > 0 ? findings.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        grammar.IsDone = Assess.Result is not null;

        var texts = Stages[(int)WorkflowStage.Texts];
        texts.Summary = Selection.SummaryText;
        texts.IsDone = Selection.CanAssess;

        var results = Stages[(int)WorkflowStage.Results];
        results.Summary = Assess.IsActive ? "Running..." : HasEverAssessed
            ? Assess.Result?.CompletionSummary is { Length: > 0 } summary ? summary : "Completed"
            : "Not run yet";
        results.IsDone = HasEverAssessed;

        var handoff = Stages[(int)WorkflowStage.Handoff];
        handoff.Summary = HandoffStatusText;
        handoff.IsDone = Handoff.HasCompletedFiles;

        foreach (var stage in Stages) stage.IsCurrent = stage.Stage == CurrentStage;
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyResults));

        if (e.PropertyName == nameof(AssessViewModel.IsActive))
        {
            OnPropertyChanged(nameof(ProjectAndSelectionEnabled));
            if (Assess.IsActive)
            {
                ResultsView = ResultsView.Words;
                CurrentStage = WorkflowStage.Results;
            }
        }

        RefreshStages();

        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == RunState.Completed)
        {
            Baseline.HasAssessment = true;
            Statistics.Reset();
            Statistics.SummaryMarkdown = Assess.Result?.SummaryMarkdown;
            Statistics.AssessmentId = Assess.Result?.Measurements
                .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
            Handoff.InvocationId = Assess.Result?.InvocationId;
            HasEverAssessed = true;
        }
    }

    private async Task AcceptRerunAsync()
    {
        RerunOffered = false;
        if (Assess.RunCommand.CanExecute(null)) await Assess.RunCommand.ExecuteAsync(null);
    }

    // Awaits each command's own unwind rather than disposing it: the workspace outlives one project.
    private async Task CancelActiveWorkAsync()
    {
        if (Assess.IsActive)
        {
            Assess.CancelCommand.Execute(null);
            if (Assess.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }

        if (Handoff.IsActive)
        {
            Handoff.CancelCommand.Execute(null);
            if (Handoff.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }
    }

    private void ClearProjectBoundState()
    {
        RerunOffered = false;
        HasEverAssessed = false;

        Assess.Reset();

        Statistics.Reset();

        Handoff.Reset();
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Handoff.DisposeAsync().ConfigureAwait(true);
    }
}
