using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;

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

    public ProjectViewModel(ICommandClient commandClient, IProjectPicker projectPicker)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(projectPicker);
        _commandClient = commandClient;
        _projectPicker = projectPicker;
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
    }

    /// <summary>The machine's Known projects, most-recently-seen first.</summary>
    public ObservableCollection<KnownProjectSummary> KnownProjects { get; } = [];

    [ObservableProperty]
    private KnownProjectSummary? _selectedKnownProject;

    public IAsyncRelayCommand BrowseCommand { get; }

    /// <summary>Raised with a project's full <c>.fwdata</c> path, from either a Known-project pick or Browse.</summary>
    public event EventHandler<string>? ProjectChosen;

    /// <summary>(Re)loads the Known-project list, replacing whatever this view model held before.</summary>
    public async Task LoadKnownProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _commandClient.ListKnownProjectsAsync(cancellationToken);
        KnownProjects.Clear();
        foreach (var project in projects) KnownProjects.Add(project);
    }

    partial void OnSelectedKnownProjectChanged(KnownProjectSummary? value)
    {
        if (value is not null && !_showingChosen) ProjectChosen?.Invoke(this, value.FullFwDataPath);
    }

    private bool _showingChosen;

    /// <summary>The open project's path when no Known project holds it, for the picker to name.</summary>
    [ObservableProperty]
    private string? _openUnlistedPath;

    /// <summary>
    /// Shows <paramref name="fwDataPath"/> as the picker's choice without choosing it again. A listed project is
    /// selected; an unlisted one is named by <see cref="OpenUnlistedPath"/> so it can still be picked afresh.
    /// </summary>
    public void ShowChosen(string fwDataPath)
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
        var path = await _projectPicker.PickProjectFileAsync();
        if (path is not null) ProjectChosen?.Invoke(this, path);
    }
}
