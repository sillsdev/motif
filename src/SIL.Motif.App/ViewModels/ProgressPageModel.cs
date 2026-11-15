using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads stored next actions for Progress and facts for the shared health strip.</summary>
public sealed partial class ProgressPageModel : PageModel
{
    private int _generation;
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource? _activeRead;
    private readonly List<Task> _reads = [];
    private IReadOnlyList<Guid> _readTexts = [];
    private string _readWords = string.Empty;

    public ProgressPageModel(WorkspaceContext context) : base(context)
    {
        TryAgainCommand = new AsyncRelayCommand(RefreshCurrentAsync, () => Context.HasProject && !IsReading);
        context.Selection.PropertyChanged += OnSelectionChanged;
        context.Evidence.PropertyChanged += OnEvidenceChanged;
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceContext.HasProject)) TryAgainCommand.NotifyCanExecuteChanged();
            if (e.PropertyName == nameof(WorkspaceContext.CurrentPage) &&
                context.CurrentPage == WorkspacePage.Progress && !context.IsOpeningProject)
                _ = RefreshCurrentAsync(CancellationToken.None);
        };
    }

    /// <summary>The health facts shared with the shell.</summary>
    public HealthStripViewModel Health => Context.Health;

    /// <summary>The complete waiting count, or null when not read or unreadable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaitingProposals), nameof(WaitingProposalsText))]
    private int? _waitingProposals;

    partial void OnWaitingProposalsChanged(int? value) => RebuildRows();

    public bool HasWaitingProposals => WaitingProposals > 0;

    public string WaitingProposalsText => WaitingProposals is { } count
        ? PluralText.Format(MotifAppStrings.ProgressWaitingProposalsPattern, count) : string.Empty;

    /// <summary>The total open Requests, independent of the bounded listing page.</summary>
    [ObservableProperty]
    private int? _openRequests;

    partial void OnOpenRequestsChanged(int? value) => RebuildRows();

    /// <summary>Only stored facts with a live destination become rows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextRows), nameof(HasNextSection))]
    private IReadOnlyList<ProgressNextRow> _nextRows = [];

    public bool HasNextRows => NextRows.Count > 0;
    public bool HasNextSection => HasNextRows || IsReading || HasUnavailableNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextSection))]
    private bool _isReading;

    partial void OnIsReadingChanged(bool value) => TryAgainCommand.NotifyCanExecuteChanged();

    /// <summary>A failed or partial read is distinct from a successful empty list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextSection))]
    private bool _hasUnavailableNext;

    public IAsyncRelayCommand TryAgainCommand { get; }

    protected override void OnProjectCleared()
    {
        Invalidate();
        Health.ClearProject();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        RefreshAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken) =>
        RefreshHealthAsync(cancellationToken);

    protected override Task OnGrammarCheckedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    protected override Task OnRequestsChangedAsync(CancellationToken cancellationToken) => RefreshCurrentAsync(cancellationToken);

    private Task RefreshHealthAsync(CancellationToken cancellationToken)
    {
        Health.Overview = null;
        return RefreshCurrentAsync(cancellationToken);
    }

    private Task RefreshCurrentAsync(CancellationToken cancellationToken) => Context.ProjectPath is { } project
        ? RefreshAsync(project, cancellationToken) : Task.CompletedTask;

    protected override async Task OnStopWorkAsync()
    {
        Invalidate();
        _stop.Cancel();
        await Task.WhenAll(_reads).ConfigureAwait(true);
        _reads.Clear();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
    }

    private void Invalidate()
    {
        ++_generation;
        var active = _activeRead;
        _activeRead = null;
        active?.Cancel();
        var previous = NextRows;
        WaitingProposals = null;
        OpenRequests = null;
        NextRows = [];
        HasUnavailableNext = false;
        IsReading = false;
        foreach (var row in previous) row.OpenCommand.NotifyCanExecuteChanged();
    }

    private Task RefreshAsync(string project, CancellationToken cancellationToken)
    {
        Invalidate();
        _readTexts = Context.Selection.ChosenTextIds.ToArray();
        _readWords = Context.Selection.PastedWords;
        HasUnavailableNext = Context.Evidence.IsStale;
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _activeRead = linked;
        IsReading = true;
        var read = ReadAsync(project, _generation, linked);
        _reads.Add(read);
        return read;
    }

    private async Task ReadAsync(string project, int generation, CancellationTokenSource linked)
    {
        try
        {
            await Task.WhenAll(ReadOverviewAsync(project, generation, linked.Token),
                ReadStandingsAsync(project, generation, linked.Token),
                ReadRequestsAsync(project, generation, linked.Token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            if (generation == _generation && linked.IsCancellationRequested) Invalidate();
            if (generation == _generation) IsReading = false;
            if (ReferenceEquals(_activeRead, linked)) _activeRead = null;
            linked.Dispose();
        }
    }

    private bool SelectionMatches() => Context.Selection.ChosenTextIds.SequenceEqual(_readTexts) &&
        Context.Selection.PastedWords == _readWords;

    private bool IsCurrent(string? project, int generation) => generation == _generation &&
        Context.ProjectPath == project && SelectionMatches();

    private async Task ReadOverviewAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.OverviewAsync(new OverviewRequest(project), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(project, generation) || cancellationToken.IsCancellationRequested) return;
        if (result.Value is { IsStale: true })
        {
            Invalidate();
            HasUnavailableNext = true;
        }
        Health.Overview = result.Succeeded ? result.Value : null;
    }

    private async Task ReadStandingsAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.ProposalStandingsAsync(new ProposalStandingsRequest(project), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrent(project, generation) || cancellationToken.IsCancellationRequested || Context.Evidence.IsStale) return;
        if (result.Succeeded && result.Value is { Unreadable.Count: 0 } value)
            WaitingProposals = value.Proposals.Count(card => card.Group == ProposalGroup.WaitingForYou);
        else HasUnavailableNext = true;
    }

    private async Task ReadRequestsAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.ReadProgressOpenRequestCountAsync(project, cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(project, generation) || cancellationToken.IsCancellationRequested || Context.Evidence.IsStale) return;
        if (result.Succeeded) OpenRequests = result.Value!.OpenCount;
        else HasUnavailableNext = true;
    }

    private void RebuildRows()
    {
        var project = Context.ProjectPath;
        var generation = _generation;
        IRelayCommand OpenProposals() => new RelayCommand(() =>
        {
            if (IsCurrent(project, generation) && !Context.Evidence.IsStale) Context.OpenPage(WorkspacePage.Proposals);
        }, () => IsCurrent(project, generation) && !Context.Evidence.IsStale);
        List<ProgressNextRow> rows = [];
        if (HasWaitingProposals)
            rows.Add(new(MotifAppStrings.BoardProposals, WaitingProposalsText, AutomationIds.ProgressNextProposals, OpenProposals()));
        if (OpenRequests is > 0)
            rows.Add(new(MotifAppStrings.ProgressRequestsLabel,
                PluralText.Format(MotifAppStrings.ProgressOpenRequestsPattern, OpenRequests.Value),
                AutomationIds.ProgressNextRequests, OpenProposals()));
        NextRows = rows;
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionViewModel.ChosenTextIds) or nameof(SelectionViewModel.PastedWords) &&
            !SelectionMatches()) Invalidate();
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectEvidence.Baseline) or nameof(ProjectEvidence.Set) ||
            e.PropertyName == nameof(ProjectEvidence.IsStale) && Context.Evidence.IsStale) Invalidate();
    }
}
