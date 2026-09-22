using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Shows what Motif has done with a project — Baselines captured, Assessments run, Handoffs written —
/// loaded when a project is chosen and again after every successful Baseline refresh.
/// </summary>
public sealed partial class ProjectHistoryViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private string? _projectPath;
    private int _generation;

    public ProjectHistoryViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
    }

    /// <summary>The project's history, newest first.</summary>
    public ObservableCollection<ProjectHistoryEntry> Entries { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _refusalMessage;

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Sets the project to read history for and immediately loads it.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        _projectPath = fwDataPath;
        _generation++;
        Entries.Clear();
        RefusalMessage = null;
        IsLoading = false;
        OnPropertyChanged(nameof(HasEntries));
        if (fwDataPath is not null) await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_projectPath is not { } path) return;

        var generation = ++_generation;
        IsLoading = true;

        var outcome = await _commandClient.GetProjectHistoryAsync(new ProjectHistoryRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _generation) return;

        IsLoading = false;
        if (!outcome.Succeeded)
        {
            RefusalMessage = outcome.Refusal!.Message;
            return;
        }

        RefusalMessage = null;
        Entries.Clear();
        foreach (var entry in outcome.Value!.Entries) Entries.Add(entry);
        OnPropertyChanged(nameof(HasEntries));
    }
}
