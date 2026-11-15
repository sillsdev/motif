namespace SIL.Motif.App.ViewModels;

/// <summary>The two recorded diagnostic views.</summary>
public enum DiagnosticsTab
{
    Warnings,
    SpeedByRule,
}

/// <summary>Opens one Diagnostics tab when Diagnostics is enabled.</summary>
public sealed record OpenDiagnosticsRequest(DiagnosticsTab Tab) : PageRequest(WorkspacePage.Diagnostics);

/// <summary>The recorded warnings and timing detail shown on Diagnostics.</summary>
public sealed class DiagnosticsPageModel : PageModel
{
    private CancellationTokenSource _stop = new();
    private readonly List<Task> _reads = [];
    private int _selectedTabIndex;

    public DiagnosticsPageModel(WorkspaceContext context) : base(context)
    {
        Warnings = new WarningsDiagnosticsViewModel(context);
        SpeedByRule = new SpeedByRuleViewModel(context);
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(WorkspaceContext.Diagnostics)) return;
            if (context.Diagnostics) _ = RefreshCurrentAsync(CancellationToken.None);
            else
            {
                Warnings.Clear();
                SpeedByRule.Clear();
                _stop.Cancel();
                _stop.Dispose();
                _stop = new CancellationTokenSource();
            }
        };
    }

    /// <summary>Recorded grammar findings.</summary>
    public WarningsDiagnosticsViewModel Warnings { get; }

    /// <summary>Recorded timing grouped by rule.</summary>
    public SpeedByRuleViewModel SpeedByRule { get; }

    /// <summary>The selected diagnostic tab.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is OpenDiagnosticsRequest diagnostics) SelectedTabIndex = (int)diagnostics.Tab;
    }

    protected override void OnProjectCleared()
    {
        Warnings.Clear();
        SpeedByRule.Clear();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        RefreshAsync(projectPath, cancellationToken);

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken) =>
        RefreshCurrentAsync(cancellationToken);

    protected override Task OnGrammarCheckedAsync(CancellationToken cancellationToken) => RefreshCurrentAsync(cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) => RefreshCurrentAsync(cancellationToken);

    protected override async Task OnStopWorkAsync()
    {
        Warnings.Clear();
        SpeedByRule.Clear();
        _stop.Cancel();
        await Task.WhenAll(_reads).ConfigureAwait(true);
        _reads.Clear();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
    }

    private Task RefreshCurrentAsync(CancellationToken cancellationToken) => Context.Diagnostics && Context.ProjectPath is { } project
        ? RefreshAsync(project, cancellationToken) : Task.CompletedTask;

    private Task RefreshAsync(string project, CancellationToken cancellationToken)
    {
        if (!Context.Diagnostics) return Task.CompletedTask;
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var read = ReadAsync(project, linked);
        _reads.Add(read);
        return read;
    }

    private async Task ReadAsync(string project, CancellationTokenSource linked)
    {
        try
        {
            await Task.WhenAll(Warnings.ReadAsync(project, linked.Token), SpeedByRule.ReadAsync(project, linked.Token))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { linked.Dispose(); }
    }
}
