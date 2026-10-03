using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Lets a person choose a project: pick one of the machine's Known projects, or browse to a
/// <c>.fwdata</c> file. Raises <see cref="ProjectChosen"/> with the chosen path either way, for a
/// composing view model to load Baseline and Text state from.
/// </summary>
public sealed partial class ProjectViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private readonly IProjectPicker _projectPicker;
    private readonly string? _managedRoot;

    /// <summary>Lets a person browse for a project while reading Known projects from the optional machine root.</summary>
    /// <param name="commandClient">The command adapter used by the project chooser.</param>
    /// <param name="projectPicker">The dialog used to choose a FieldWorks project.</param>
    /// <param name="managedRoot">The machine-store root used for local recovery instructions, when known.</param>
    public ProjectViewModel(ICommandClient commandClient, IProjectPicker projectPicker, string? managedRoot = null)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(projectPicker);
        _commandClient = commandClient;
        _projectPicker = projectPicker;
        _managedRoot = managedRoot;
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
    }

    /// <summary>The machine's Known projects, most-recently-seen first.</summary>
    public ObservableCollection<KnownProjectSummary> KnownProjects { get; } = [];

    /// <summary>The local instructions shown when Motif's machine store has a refused shape.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMachineStoreRecovery))]
    private string? _machineStoreRecoveryMessage;

    [ObservableProperty]
    private ProblemReport? _machineStoreProblemReport;

    /// <summary>Whether the window has manual recovery instructions for the machine store.</summary>
    public bool HasMachineStoreRecovery => MachineStoreRecoveryMessage is not null;

    [ObservableProperty]
    private KnownProjectSummary? _selectedKnownProject;

    public IAsyncRelayCommand BrowseCommand { get; }

    /// <summary>Raised with a project's full <c>.fwdata</c> path, from either a Known-project pick or Browse.</summary>
    public event EventHandler<string>? ProjectChosen;

    /// <summary>(Re)loads the Known-project list, replacing whatever this view model held before.</summary>
    public async Task LoadKnownProjectsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnownProjectSummary> projects;
        try
        {
            projects = await _commandClient.ListKnownProjectsAsync(cancellationToken);
        }
        catch (Exception exception) when (_managedRoot is not null &&
            (exception is InvalidDataException or NotSupportedException or MotifStoreVersionException))
        {
            MachineStoreRecoveryMessage = MachineStoreRecovery.ForRefusedStore(_managedRoot);
            MachineStoreProblemReport = ProblemReport.ForMachineStoreFailure(exception);
            return;
        }

        MachineStoreRecoveryMessage = null;
        MachineStoreProblemReport = null;
        if (KnownProjects.Select(project => project.FullFwDataPath).SequenceEqual(
                projects.Select(project => project.FullFwDataPath), StringComparer.OrdinalIgnoreCase)) return;
        KnownProjects.Clear();
        foreach (var project in projects) KnownProjects.Add(project);
    }

    partial void OnSelectedKnownProjectChanged(KnownProjectSummary? value)
    {
        if (value is null || _showingChosen) return;
        using var usageAction = _commandClient.BeginUsageAction("project open",
            UsageArgumentShape.Text("fwDataPath"));
        ProjectChosen?.Invoke(this, value.FullFwDataPath);
    }

    private bool _showingChosen;

    /// <summary>The open project's path when no Known project holds it, for the picker to name.</summary>
    [ObservableProperty]
    private string? _openUnlistedPath;

    /// <summary>
    /// Shows <paramref name="fwDataPath"/> as the picker's choice without choosing it again. A listed project is
    /// selected; an unlisted one is named by <see cref="OpenUnlistedPath"/> so it can still be picked afresh.
    /// </summary>
    public void ShowChosen(string? fwDataPath)
    {
        var known = KnownProjects.FirstOrDefault(project =>
            string.Equals(project.FullFwDataPath, fwDataPath, StringComparison.OrdinalIgnoreCase));
        OpenUnlistedPath = known is null ? fwDataPath : null;
        _showingChosen = true;
        try { SelectedKnownProject = known; }
        finally { _showingChosen = false; }
    }

    private async Task BrowseAsync()
    {
        using var usageAction = _commandClient.BeginUsageAction("project browse",
            UsageArgumentShape.Text("fwDataPath"));
        var path = await _projectPicker.PickProjectFileAsync();
        if (path is not null) ProjectChosen?.Invoke(this, path);
    }
}
