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
/// is fed from that same completed Assessment's own rendered summary.
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

        Project.ProjectChosen += OnProjectChosen;
        Baseline.Refreshed += OnBaselineRefreshed;
        Baseline.OfferRerun += OnOfferRerun;
        Assess.PropertyChanged += OnAssessPropertyChanged;

        AcceptRerunCommand = new AsyncRelayCommand(AcceptRerunAsync, () => RerunOffered);
        DismissRerunCommand = new RelayCommand(() => RerunOffered = false, () => RerunOffered);
    }

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
    private bool _hasEverAssessed;

    /// <summary>The negation <see cref="HasEverAssessed"/> binds against, so a view never composes <c>!</c> itself.</summary>
    public bool NotYetAssessed => !HasEverAssessed;

    public IAsyncRelayCommand AcceptRerunCommand { get; }

    public IRelayCommand DismissRerunCommand { get; }

    /// <summary>Whether Project and Selection controls should accept input right now.</summary>
    public bool ProjectAndSelectionEnabled => !Assess.IsActive;

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

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssessViewModel.IsActive))
            OnPropertyChanged(nameof(ProjectAndSelectionEnabled));

        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == AssessRunState.Completed)
        {
            Baseline.HasAssessment = true;
            Statistics.Reset();
            Statistics.SummaryMarkdown = Assess.Result?.SummaryMarkdown;
            Statistics.AssessmentId = Assess.Result?.Measurements
                .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
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

        Assess.State = AssessRunState.Idle;
        Assess.Progress = null;
        Assess.Result = null;
        Assess.Refusal = null;

        Statistics.Reset();

        Handoff.State = HandoffRunState.Idle;
        Handoff.Progress = null;
        Handoff.Result = null;
        Handoff.Refusal = null;
        Handoff.OutputDirectory = null;
        Handoff.Files.Clear();
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Handoff.DisposeAsync().ConfigureAwait(true);
    }
}
