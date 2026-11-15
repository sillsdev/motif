using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads Grammar Design Records from the open project's Baseline.</summary>
public sealed partial class GrammarGuidePageModel : PageModel
{
    private int _generation;
    private CancellationTokenSource? _loadCancellation;
    private readonly List<Task> _loadTasks = [];
    private CancellationTokenSource? _showCancellation;
    private readonly List<Task> _showTasks = [];

    public GrammarGuidePageModel(WorkspaceContext context) : base(context) { }

    /// <summary>The records in their stored order.</summary>
    public ObservableCollection<GdrSummary> Records { get; } = [];

    /// <summary>The record selected for reading.</summary>
    [ObservableProperty]
    private GdrSummary? _selectedRecord;

    /// <summary>The selected record and its discussion, including writing-system information.</summary>
    [ObservableProperty]
    private GdrShowResponse? _shownRecord;

    partial void OnSelectedRecordChanged(GdrSummary? value)
    {
        _showCancellation?.Cancel();
        _showCancellation = null;
        ShownRecord = null;
        var generation = ++_generation;
        if (value is null || Context.ProjectPath is not { } project) return;
        var cancellation = new CancellationTokenSource();
        _showCancellation = cancellation;
        _showTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        _showTasks.Add(ShowAsync(project, value.Id, generation, cancellation));
    }

    protected override void OnProjectCleared()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        SelectedRecord = null;
        Records.Clear();
        ShownRecord = null;
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        LoadAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } project ? LoadAsync(project, cancellationToken) : Task.CompletedTask;

    protected override async Task OnStopWorkAsync()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        await Task.WhenAll(_loadTasks.Concat(_showTasks)).ConfigureAwait(true);
        _loadTasks.Clear();
        _showTasks.Clear();
    }

    private Task LoadAsync(string project, CancellationToken cancellationToken)
    {
        _loadCancellation?.Cancel();
        _showCancellation?.Cancel();
        SelectedRecord = null;
        ShownRecord = null;
        Records.Clear();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        var generation = ++_generation;
        _loadTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        var read = ReadListAsync(project, generation, cancellation);
        _loadTasks.Add(read);
        return read;
    }

    private async Task ReadListAsync(string project, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var result = await Context.Commands.ListGrammarGuideAsync(new GdrListRequest(project), token)
                .ConfigureAwait(true);
            if (generation != _generation || token.IsCancellationRequested || Context.ProjectPath != project) return;
            Records.Clear();
            SelectedRecord = null;
            if (!result.Succeeded || result.Value is not { } value) return;
            foreach (var record in value.Records) Records.Add(record);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ShowAsync(string project, string id, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var result = await Context.Commands.ShowGrammarGuideAsync(new GdrShowRequest(project, id), token)
                .ConfigureAwait(true);
            if (generation != _generation || token.IsCancellationRequested || Context.ProjectPath != project) return;
            if (result.Succeeded) ShownRecord = result.Value;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_showCancellation, cancellation)) _showCancellation = null;
            cancellation.Dispose();
        }
    }
}
