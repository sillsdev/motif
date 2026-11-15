using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>Project-owned Requests for the Claude panel, without a conversation or inferred connection state.</summary>
public sealed partial class ClaudePanelViewModel : ObservableObject
{
    private readonly HashSet<CancellationTokenSource> _reads = [];
    private readonly HashSet<Task> _pending = [];
    private int _readGeneration;
    private int _projectGeneration;

    private WorkspaceContext Context { get; }

    public ClaudePanelViewModel(WorkspaceContext context) =>
        Context = context ?? throw new ArgumentNullException(nameof(context));

    public OpenRequestsViewModel Requests { get; } = new();
    public bool HasProject => Context.ProjectPath is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    private string _statusMessage = string.Empty;

    public bool ShowsEmpty => HasProject && !IsLoading && StatusMessage.Length == 0 && !Requests.HasRequests;

    [RelayCommand]
    private Task RefreshAsync() => ReadAsync(append: false);

    [RelayCommand]
    private Task ShowMoreAsync() => ReadAsync(append: true);

    private async Task ReadAsync(bool append, CancellationToken cancellationToken = default)
    {
        if (Context.ProjectPath is not { } path) return;
        foreach (var previous in _reads.ToArray()) previous.Cancel();
        var generation = ++_readGeneration;
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reads.Add(read);
        IsLoading = true;
        StatusMessage = string.Empty;
        var task = LoadAsync(path, generation, append, read.Token);
        _pending.Add(task);
        try { await task.ConfigureAwait(true); }
        finally
        {
            _pending.Remove(task);
            _reads.Remove(read);
            if (generation == _readGeneration) IsLoading = false;
        }
    }

    private async Task LoadAsync(string path, int generation, bool append, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Context.Commands.ListRequestsAsync(new ListRequestsRequest(path,
                MotifProductVersion.CurrentText, "open", append ? Requests.Rows.Count : 0, 200), cancellationToken)
                .ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || generation != _readGeneration || path != Context.ProjectPath) return;
            if (!result.Succeeded)
            {
                StatusMessage = WindowRefusal.From(result.Refusal!).Sentence;
                return;
            }
            Requests.Show(result.Value!.Requests, Context.Clock.GetUtcNow(), append, result.Value.Total);
            OnPropertyChanged(nameof(ShowsEmpty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    [RelayCommand]
    private async Task WithdrawAsync(OpenRequestRowViewModel? row)
    {
        if (row is null || !Requests.Rows.Contains(row) || Context.ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var result = await Context.Commands.WithdrawRequestAsync(new WithdrawRequestRequest(path,
            MotifProductVersion.CurrentText, row.RequestId), CancellationToken.None).ConfigureAwait(true);
        if (generation != _projectGeneration || path != Context.ProjectPath) return;
        if (!result.Succeeded) StatusMessage = WindowRefusal.From(result.Refusal!).Sentence;
        else await Context.PublishRequestsChangedAsync().ConfigureAwait(true);
    }

    internal void ProjectCleared()
    {
        ++_projectGeneration;
        ++_readGeneration;
        foreach (var read in _reads.ToArray()) read.Cancel();
        Requests.Show([], Context.Clock.GetUtcNow());
        StatusMessage = string.Empty;
        IsLoading = false;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ShowsEmpty));
    }

    internal async Task StopWorkAsync()
    {
        ProjectCleared();
        await Task.WhenAll(_pending.ToArray()).ConfigureAwait(true);
    }

    internal Task ProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        OnPropertyChanged(nameof(HasProject));
        return ReadAsync(false, cancellationToken);
    }

    internal Task RequestsChangedAsync(CancellationToken cancellationToken) => ReadAsync(false, cancellationToken);
}
