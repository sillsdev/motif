using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Shows what Motif has done with a project — Baselines captured, words parsed, AI Handoffs written —
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
    public ObservableCollection<ProjectHistoryRow> Entries { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Why the last read or write was refused, in the window's words.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Sets the project to read history for and immediately loads it.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        _projectPath = fwDataPath;
        _generation++;
        Entries.Clear();
        ShownRefusal = null;
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
            ShownRefusal = WindowRefusal.From(outcome.Refusal!);
            return;
        }

        ShownRefusal = null;
        Entries.Clear();
        foreach (var entry in outcome.Value!.Entries) Entries.Add(ProjectHistoryRow.From(entry));
        OnPropertyChanged(nameof(HasEntries));
    }
}

/// <summary>One event in a project's history as the window shows it, titled in the window's words.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Title">What happened: a Baseline, a Parse all words, or an AI Handoff.</param>
/// <param name="Summary">The event's one-line summary, as the store wrote it.</param>
public sealed record ProjectHistoryRow(DateTimeOffset At, string Title, string Summary)
{
    /// <summary>The row for a stored history entry.</summary>
    public static ProjectHistoryRow From(ProjectHistoryEntry entry) =>
        new(entry.At, entry.Kind switch
        {
            ProjectHistoryKind.Assessment => "Parse all words",
            ProjectHistoryKind.Handoff => "AI Handoff",
            ProjectHistoryKind.Baseline => "Baseline",
            _ => entry.Kind.ToString(),
        }, entry.Summary);
}
